namespace JtdxAutoResume.V3.Services;

public static class BandSurveyTiming
{
    public static DateTime FirstEligibleSlotStart(DateTime confirmedAt, TimeSpan receivePeriod)
    {
        var safePeriod = SafePeriod(receivePeriod);
        var confirmedUtc = confirmedAt.ToUniversalTime();
        var elapsedTicks = confirmedUtc.TimeOfDay.Ticks;
        var completedPeriods = elapsedTicks / safePeriod.Ticks;
        var nextTicks = (completedPeriods + 1) * safePeriod.Ticks;
        var nextUtc = confirmedUtc.Date.AddTicks(nextTicks);
        return nextUtc.ToLocalTime();
    }

    public static DateTime SilentBandFallbackAt(DateTime confirmedAt, TimeSpan receivePeriod)
    {
        var safePeriod = SafePeriod(receivePeriod);
        var processingDelay = TimeSpan.FromSeconds(Math.Clamp(safePeriod.TotalSeconds / 5d, 1.2, 3));
        return FirstEligibleSlotStart(confirmedAt, safePeriod) + safePeriod + processingDelay;
    }

    private static TimeSpan SafePeriod(TimeSpan receivePeriod)
    {
        return receivePeriod > TimeSpan.Zero ? receivePeriod : TimeSpan.FromSeconds(15);
    }
}
