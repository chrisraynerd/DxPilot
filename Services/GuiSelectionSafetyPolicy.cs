using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class GuiSelectionSafetyPolicy
{
    public static bool RequiresReceiveOnlyBarrier(SelectionResult selection)
    {
        return !selection.Success
            && selection.SelectionMethod == JtdxSelectionMethod.GuiGridDoubleClick
            && selection.SelectionActionAt.HasValue
            && selection.FailureReason is SelectionFailureReason.ConfirmationTimedOut
                or SelectionFailureReason.JtdxSelectedWrongCall;
    }

    public static bool IsConfirmedReceiveOnly(
        JtdxStatusMessage? status,
        DateTime now,
        DateTime? statusMustFollow = null)
    {
        if (status == null)
            return false;

        var age = now - status.ReceivedAt;
        if (age < TimeSpan.FromSeconds(-1) || age > TimeSpan.FromSeconds(3))
            return false;

        if (statusMustFollow.HasValue
            && status.ReceivedAt < statusMustFollow.Value)
        {
            return false;
        }

        return !status.TxEnabled && !status.Transmitting;
    }
}
