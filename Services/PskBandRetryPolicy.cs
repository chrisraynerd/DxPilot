namespace JtdxAutoResume.V3.Services;

public static class PskBandRetryPolicy
{
    // This is a new, deliberate measurement attempt after the previous attempt
    // has ended and JTDX has been positively restored to receive-only/Tx1. It is
    // not permission to repeat an uncertain toggle click. A completed two-CQ
    // window is retained instead, and every genuinely incomplete band gets at
    // most one fresh probe-pair attempt.
    public static bool CanRetryIncompleteBand(
        bool retryAlreadyUsed,
        bool safeStateRestored,
        bool completedProbeAvailable) =>
        !retryAlreadyUsed
        && safeStateRestored
        && !completedProbeAvailable;
}
