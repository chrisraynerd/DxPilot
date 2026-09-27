namespace JtdxAutoResume.V3.Services;

// Scheduling only: no radio commands, scoring of wanted targets, or TX control.
public sealed class ScavengerSearchPolicy
{
    public static Models.DxTarget? SelectTarget(IEnumerable<Models.DxTarget> candidates, Func<Models.DxTarget, int> wantedPriority) =>
        candidates.OrderBy(wantedPriority)
            .ThenByDescending(t => t.Decode.IsLotwUser)
            .ThenByDescending(t => t.Ranking.AdjustedDxValueScore).FirstOrDefault();

    private readonly Dictionary<string, int> _eligibleRound = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    public int Round { get; private set; } = 1;

    public string? Next(IReadOnlyList<string> enabled)
    {
        if (enabled.Count == 0) return null;
        var next = enabled.FirstOrDefault(b => !_visited.Contains(b) && SkipsRemaining(b) == 0);
        if (next != null) return next;
        // Exactly one round per completed pass. If all bands are resting, give
        // the earliest-due band a real listening visit; never spin virtual rounds.
        Round++;
        _visited.Clear();
        return enabled.FirstOrDefault(b => SkipsRemaining(b) == 0)
            ?? enabled.OrderBy(b => _eligibleRound.GetValueOrDefault(b)).First();
    }

    public void Complete(string band, int skipRounds)
    {
        _visited.Add(band);
        _eligibleRound[band] = Round + Math.Clamp(skipRounds, 0, 12) + 1;
    }

    public int SkipsRemaining(string band) => Math.Max(0, _eligibleRound.GetValueOrDefault(band) - Round);

    public static int Classify(int stations, int locatedStations, double farthestMiles, int deadRounds, int quietRounds)
    {
        if (stations == 0) return Math.Clamp(deadRounds, 1, 12);
        // Unknown locations must not be called local. A single distant station
        // keeps the band in rotation even when total traffic is very low.
        return stations <= 5 && locatedStations == stations && farthestMiles <= 1500
            ? Math.Clamp(quietRounds, 1, 12) : 0;
    }

    public static DateTime SampleEnd(DateTime fullSlotStart, int minutes, TimeSpan period)
    {
        var seconds = period.TotalSeconds > 0 ? period.TotalSeconds : 15;
        var slots = Math.Ceiling(Math.Clamp(minutes, 1, 5) * 60d / seconds);
        return fullSlotStart.AddSeconds(slots * seconds + 3); // final decode delivery
    }

    public static DateTime ReturnWatchEnd(DateTime now) => now.AddMinutes(5);

    // Status messages need not arrive every second between decode periods.
    // This is listening continuity only; movement and TX still use the existing
    // stricter fresh-status check immediately before issuing any command.
    public static bool HasReceiveEvidence(Models.JtdxStatusMessage? status, DateTime now, TimeSpan period)
    {
        if (status == null || status.TxEnabled || status.Transmitting) return false;
        var age = now - status.ReceivedAt;
        return age >= TimeSpan.FromSeconds(-1)
            && age <= TimeSpan.FromSeconds(Math.Max(20, period.TotalSeconds * 2 + 5));
    }
}
