using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed record ConditionsSearchTrigger(
    string Reason,
    bool FullSurvey,
    int Priority);

public sealed record ConditionsBandChoice(
    string Band,
    double Score,
    double CurrentScore,
    bool ShouldMove,
    string Explanation);

public sealed record PskSurveyBandCandidate(
    string Band,
    double CombinedScore,
    int NewDxccStations,
    int WantedStations,
    int PskPropagationScore,
    int DxReachScore,
    int ActivityScore);

public sealed record BandTrendResult(string Label, int Score, double? ChangePercent, DateTime? ComparedAtUtc);

public static class ConditionsSearchPolicy
{
    public static string SurveyDestinationBand(
        ConditionsBandChoice choice,
        string startingBand,
        bool automatic,
        bool automaticMovementEnabled)
    {
        if (automatic)
            return automaticMovementEnabled && choice.ShouldMove ? choice.Band : startingBand;

        // A manually initiated survey from an active assistance mode follows
        // any genuinely better result, but never hops on an empty/exact tie.
        return choice.Score > choice.CurrentScore ? choice.Band : startingBand;
    }

    public static PskSurveyBandCandidate? ChoosePskSurveyBand(
        IEnumerable<PskSurveyBandCandidate> candidates,
        string currentBand)
    {
        return candidates
            .OrderByDescending(candidate => candidate.NewDxccStations)
            .ThenByDescending(candidate => candidate.CombinedScore)
            .ThenByDescending(candidate => candidate.WantedStations)
            .ThenByDescending(candidate => candidate.PskPropagationScore)
            .ThenByDescending(candidate => candidate.DxReachScore)
            .ThenByDescending(candidate => candidate.ActivityScore)
            .ThenByDescending(candidate => candidate.Band.Equals(currentBand, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    public static ConditionsSearchTrigger? DetectTrigger(
        bool scheduledDue,
        bool startupDue,
        TimeSpan timeOnBand,
        TimeSpan sinceAnyDecode,
        TimeSpan sinceUsefulTarget,
        int uniqueStations,
        TimeSpan lowActivityDuration,
        int unansweredAttempts,
        int distinctAttemptedStations,
        AppSettings settings)
    {
        if (scheduledDue)
            return new ConditionsSearchTrigger("scheduled UTC conditions check", true, 100);
        if (startupDue)
            return new ConditionsSearchTrigger("startup conditions check", true, 90);
        if (sinceAnyDecode >= TimeSpan.FromMinutes(settings.ConditionsSearchSilentMinutes))
            return new ConditionsSearchTrigger($"no stations decoded for {settings.ConditionsSearchSilentMinutes} minutes", false, 80);

        if (timeOnBand < TimeSpan.FromMinutes(settings.ConditionsSearchMonitoringWindowMinutes))
            return null;

        if (unansweredAttempts >= settings.ConditionsSearchPoorReplyAttempts
            && distinctAttemptedStations >= settings.ConditionsSearchPoorReplyDistinctStations)
        {
            return new ConditionsSearchTrigger(
                $"{unansweredAttempts} unanswered calls across {distinctAttemptedStations} stations",
                false,
                70);
        }

        if (sinceUsefulTarget >= TimeSpan.FromMinutes(settings.ConditionsSearchNoUsefulTargetMinutes))
        {
            return new ConditionsSearchTrigger(
                $"no useful target for {settings.ConditionsSearchNoUsefulTargetMinutes} minutes",
                false,
                60);
        }

        if (uniqueStations < settings.ConditionsSearchLowStationThreshold
            && lowActivityDuration >= TimeSpan.FromMinutes(settings.ConditionsSearchLowActivityPersistMinutes))
        {
            return new ConditionsSearchTrigger(
                $"only {uniqueStations} unique stations in the rolling {settings.ConditionsSearchMonitoringWindowMinutes}-minute sample",
                false,
                50);
        }

        return null;
    }

    public static ConditionsSearchTrigger? DetectCompletedQsoTrigger(
        TimeSpan timeOnBand,
        TimeSpan sinceCompletedQso,
        int callingAttemptsSinceCompletedQso,
        int incompleteExchanges,
        AppSettings settings)
    {
        if (timeOnBand < TimeSpan.FromMinutes(settings.ConditionsSearchMonitoringWindowMinutes)
            || sinceCompletedQso < TimeSpan.FromMinutes(settings.ConditionsSearchNoCompletedQsoMinutes))
        {
            return null;
        }

        var enoughCallingEffort = callingAttemptsSinceCompletedQso >= settings.ConditionsSearchPoorReplyAttempts;
        var enoughIncompleteExchanges = incompleteExchanges >= settings.ConditionsSearchIncompleteQsoThreshold;
        if (!enoughCallingEffort && !enoughIncompleteExchanges)
            return null;

        var evidence = incompleteExchanges > 0
            ? $"{incompleteExchanges} incomplete exchange{(incompleteExchanges == 1 ? "" : "s")}, {callingAttemptsSinceCompletedQso} calling attempts"
            : $"{callingAttemptsSinceCompletedQso} calling attempts";
        return new ConditionsSearchTrigger(
            $"no completed QSO for {settings.ConditionsSearchNoCompletedQsoMinutes} minutes despite {evidence}",
            false,
            65);
    }

    public static double Score(BandQualitySnapshot snapshot, HuntingOperatingMode mode, int trendScore)
    {
        if (snapshot.NewDxccStations > 0)
            return 10_000 + snapshot.NewDxccStations * 1_000;

        var wantedWeight = mode == HuntingOperatingMode.WantedSniper ? 45d : 30d;
        var dxWeight = mode == HuntingOperatingMode.DxAssist ? 0.60 : 0.50;
        var activityWeight = mode == HuntingOperatingMode.LocationHunt ? 0.30 : 0.25;
        return snapshot.WantedStations * wantedWeight
            + snapshot.DxReachScore * dxWeight
            + snapshot.ActivityScore * activityWeight
            + Math.Clamp(trendScore, -10, 10);
    }

    public static ConditionsBandChoice ChooseBand(
        IReadOnlyList<(BandQualitySnapshot Snapshot, int TrendScore)> samples,
        string currentBand,
        HuntingOperatingMode mode,
        int requiredImprovementPercent)
    {
        if (samples.Count == 0)
            return new ConditionsBandChoice(currentBand, 0, 0, false, "No completed band samples were available.");

        var scored = samples
            .Select(item => (item.Snapshot, Score: Score(item.Snapshot, mode, item.TrendScore)))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Snapshot.DxReachScore)
            .ThenByDescending(item => item.Snapshot.ActivityScore)
            .ToList();
        var best = scored[0];
        var current = scored.FirstOrDefault(item => item.Snapshot.Band.Equals(currentBand, StringComparison.OrdinalIgnoreCase));
        var currentScore = current.Snapshot == null ? 0 : current.Score;
        var currentPoor = current.Snapshot == null || current.Snapshot.UniqueStations <= 2;
        var required = currentScore <= 0
            ? 0
            : currentScore * (1 + Math.Clamp(requiredImprovementPercent, 5, 100) / 100d);
        var differentBand = !best.Snapshot.Band.Equals(currentBand, StringComparison.OrdinalIgnoreCase);
        var shouldMove = differentBand && (currentPoor ? best.Score > currentScore : best.Score >= required);
        var explanation = !differentBand
            ? $"{currentBand} remains the strongest measured band."
            : shouldMove
                ? $"{best.Snapshot.Band} scored {best.Score:0}, compared with {currentBand} at {currentScore:0}."
                : $"{best.Snapshot.Band} was slightly stronger, but did not exceed the {requiredImprovementPercent}% movement margin.";
        return new ConditionsBandChoice(best.Snapshot.Band, best.Score, currentScore, shouldMove, explanation);
    }

    public static (string Label, int Score) Trend(
        string band,
        IReadOnlyList<BandAnalysisHistoryEntry> history)
    {
        var recent = history
            .Where(entry => entry.Band.Equals(band, StringComparison.OrdinalIgnoreCase)
                && entry.SecondsObserved > 0)
            .OrderByDescending(entry => entry.ObservedAtUtc)
            .Take(4)
            .ToList();
        if (recent.Count < 2)
            return ("Building history", 0);

        static double Value(BandAnalysisHistoryEntry entry) =>
            entry.NewDxccStations * 100
            + entry.WantedStations * 20
            + entry.DxReachScore * 0.60
            + entry.ActivityScore * 0.25;

        var latest = Value(recent[0]);
        var previous = recent.Skip(1).Average(Value);
        var delta = latest - previous;
        return delta switch
        {
            >= 15 => ("Emerging strongly", 10),
            >= 7 => ("Improving", 5),
            <= -15 => ("Declining", -10),
            <= -7 => ("Easing", -5),
            _ => ("Stable", 0)
        };
    }

    public static BandTrendResult RecentTrendAgainstCurrent(
        string band,
        double currentWorkabilityScore,
        IReadOnlyList<BandAnalysisHistoryEntry> history,
        DateTime nowUtc,
        int comparisonWindowHours)
    {
        var cutoff = nowUtc.AddHours(-Math.Clamp(comparisonWindowHours, 1, 6));
        var previous = ComparableHistory(band, history, cutoff, nowUtc)
            .OrderByDescending(entry => entry.ObservedAtUtc)
            .FirstOrDefault();
        if (previous == null)
            return new BandTrendResult("No recent comparison", 0, null, null);

        return DescribeRecentTrend(currentWorkabilityScore, previous.WorkabilityScore, previous.ObservedAtUtc, nowUtc);
    }

    public static BandTrendResult RecentHistoricalTrend(
        string band,
        IReadOnlyList<BandAnalysisHistoryEntry> history,
        DateTime nowUtc,
        int comparisonWindowHours)
    {
        var cutoff = nowUtc.AddHours(-Math.Clamp(comparisonWindowHours, 1, 6));
        var recent = ComparableHistory(band, history, cutoff, nowUtc)
            .OrderByDescending(entry => entry.ObservedAtUtc)
            .Take(2)
            .ToList();
        if (recent.Count < 2)
            return new BandTrendResult("No recent comparison", 0, null, null);

        return DescribeRecentTrend(recent[0].WorkabilityScore, recent[1].WorkabilityScore, recent[1].ObservedAtUtc, recent[0].ObservedAtUtc);
    }

    private static IEnumerable<BandAnalysisHistoryEntry> ComparableHistory(
        string band,
        IReadOnlyList<BandAnalysisHistoryEntry> history,
        DateTime cutoffUtc,
        DateTime upperBoundUtc) =>
        history.Where(entry => entry.Band.Equals(band, StringComparison.OrdinalIgnoreCase)
            && entry.CompletedComparableAnalysis
            && entry.PskMeasured
            && entry.WorkabilityScore > 0
            && entry.ObservedAtUtc >= cutoffUtc
            && entry.ObservedAtUtc < upperBoundUtc.AddMilliseconds(-1));

    private static BandTrendResult DescribeRecentTrend(
        double current,
        double previous,
        DateTime comparedAtUtc,
        DateTime observedAtUtc)
    {
        if (previous <= 0)
            return new BandTrendResult("No recent comparison", 0, null, null);

        var change = (current - previous) / previous * 100d;
        var (word, adjustment) = change switch
        {
            >= 15 => ("Improving strongly", 5),
            >= 7 => ("Improving", 2),
            <= -15 => ("Declining", -5),
            <= -7 => ("Easing", -2),
            _ => ("Stable", 0)
        };
        var age = observedAtUtc - comparedAtUtc;
        var ageText = age.TotalMinutes < 90
            ? $"{Math.Max(1, Math.Round(age.TotalMinutes)):0}m ago"
            : $"{age.TotalHours:0.0}h ago";
        return new BandTrendResult(
            $"{word} {change:+0;-0;0}% vs {comparedAtUtc.ToLocalTime():HH:mm} ({ageText})",
            adjustment,
            change,
            comparedAtUtc);
    }
}
