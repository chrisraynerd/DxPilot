namespace JtdxAutoResume.V3.Services;

public static class PskTxTransitionPolicy
{
    public static TimeSpan OffConfirmationTimeout(TimeSpan? requested = null)
    {
        var minimum = TimeSpan.FromSeconds(18);
        return requested.HasValue && requested.Value > minimum
            ? requested.Value
            : minimum;
    }

    public static bool CanSafelyRetryArm(
        bool startedFreshlyOff,
        bool latestReportsActive,
        bool freshOffReportedAfterClick,
        int greyPercent,
        int activePercent,
        int configuredMinimumGreyPercent,
        int configuredMaximumActivePercent)
    {
        if (!startedFreshlyOff || latestReportsActive)
            return false;

        if (freshOffReportedAfterClick)
            return true;

        // A missed physical click does not necessarily make JTDX emit a new UDP
        // Status packet. Permit one bounded re-click only when the calibrated
        // button sample remains much more conclusively grey than the user's
        // ordinary off threshold. An active or visually ambiguous toggle is
        // never clicked again.
        var strongGreyThreshold = Math.Max(configuredMinimumGreyPercent, 80);
        var strongActiveThreshold = Math.Min(configuredMaximumActivePercent, 10);
        return greyPercent >= strongGreyThreshold
            && activePercent <= strongActiveThreshold;
    }
}
