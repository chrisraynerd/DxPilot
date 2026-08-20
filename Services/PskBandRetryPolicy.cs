namespace JtdxAutoResume.V3.Services;

public static class PskBandRetryPolicy
{
    // A band may be measured again only when an automatic analysis has clear
    // evidence that no CQ left the radio. Ambiguous or partial transmission
    // evidence is deliberately treated as on-air activity.
    public static bool CanRetryIncompleteBand(
        bool automatic,
        bool retryAlreadyUsed,
        int verifiedCqTransmissions,
        bool transmissionDefinitelyAbsent) =>
        automatic
        && !retryAlreadyUsed
        && verifiedCqTransmissions == 0
        && transmissionDefinitelyAbsent;
}
