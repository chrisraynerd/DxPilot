namespace JtdxAutoResume.V3.Services;

public static class PskPropagationProbeTiming
{
    public const int CqPeriodsPerBand = 2;

    public static TimeSpan PassiveListenDuration(int configuredMinutes, TimeSpan period)
    {
        var minutes = Math.Clamp(configuredMinutes, 1, 5);
        var total = TimeSpan.FromMinutes(minutes);
        var probe = TimeSpan.FromTicks(period.Ticks * CqPeriodsPerBand);
        return total > probe ? total - probe : TimeSpan.Zero;
    }

    public static TimeSpan EstimatedBandOccupancy(int configuredMinutes, TimeSpan period) =>
        TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 1, 5));

    public static long SlotNumber(DateTime observedAt, TimeSpan period)
    {
        var seconds = Math.Max(1, (long)Math.Round(period.TotalSeconds));
        return new DateTimeOffset(observedAt.ToUniversalTime()).ToUnixTimeSeconds() / seconds;
    }

    public static bool AreImmediatelyConsecutive(long firstSlot, long secondSlot) =>
        secondSlot == firstSlot + 1;
}
