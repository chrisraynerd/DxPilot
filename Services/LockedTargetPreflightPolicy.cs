using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class LockedTargetPreflightPolicy
{
    public static bool ShouldRefreshQueuedInitialCall(
        JtdxStatusMessage? status,
        string? lockedCall,
        bool isInitialCallingStage,
        bool sourceSupportsUdpReply,
        bool selectionAlreadyInProgress)
    {
        if (status == null
            || string.IsNullOrWhiteSpace(lockedCall)
            || !isInitialCallingStage
            || !sourceSupportsUdpReply
            || selectionAlreadyInProgress
            || status.Transmitting)
        {
            return false;
        }

        var message = status.TxMessage.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return true;

        var upper = message.ToUpperInvariant();
        return upper.Equals("CQ", StringComparison.Ordinal)
            || upper.StartsWith("CQ ", StringComparison.Ordinal);
    }

    public static long SlotKey(DateTime receivedAt, uint trPeriodSeconds)
    {
        var periodSeconds = Math.Max(1L, trPeriodSeconds == 0 ? 15L : trPeriodSeconds);
        return new DateTimeOffset(receivedAt.ToUniversalTime()).ToUnixTimeSeconds() / periodSeconds;
    }
}
