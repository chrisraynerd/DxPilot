using System.Net;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxSelectionController
{
    private readonly JtdxUdpReplySelector _udpReplySelector;
    private readonly JtdxGuiGridSelector _guiGridSelector;
    private readonly JtdxVisibleRowModel _visibleRowModel;
    private readonly Func<string> _getCurrentDxCall;
    private readonly Func<JtdxStatusMessage?> _getCurrentStatus;
    private readonly Func<TimeSpan, Task> _delayAsync;

    public JtdxSelectionController(
        JtdxUdpReplySelector udpReplySelector,
        JtdxGuiGridSelector guiGridSelector,
        JtdxVisibleRowModel visibleRowModel,
        Func<string> getCurrentDxCall,
        Func<JtdxStatusMessage?> getCurrentStatus,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        _udpReplySelector = udpReplySelector;
        _guiGridSelector = guiGridSelector;
        _visibleRowModel = visibleRowModel;
        _getCurrentDxCall = getCurrentDxCall;
        _getCurrentStatus = getCurrentStatus;
        _delayAsync = delayAsync ?? (delay => Task.Delay(delay));
    }

    public async Task<SelectionResult> SelectTargetAsync(
        DxTarget target,
        AppSettings settings,
        IPEndPoint endpoint,
        IPEndPoint fallbackEndpoint,
        string appId,
        bool sendFallback,
        CancellationToken cancellationToken = default,
        TimeSpan? confirmationTimeout = null,
        Action<SelectionResult>? selectionActionObserver = null,
        bool forceGuiGridClick = false)
    {
        var expectedCall = target.Callsign;
        var before = _getCurrentDxCall();
        var calibration = JtdxBandActivityGridCalibration.FromSettings(settings);
        SelectionResult result;

        if (!forceGuiGridClick && ShouldUseUdpReply(target.Decode))
        {
            result = await _udpReplySelector.SelectAsync(target.Decode, expectedCall, endpoint, fallbackEndpoint, appId, before, sendFallback, cancellationToken);
        }
        else
        {
            result = await _guiGridSelector.SelectAsync(
                target.Decode,
                expectedCall,
                settings,
                _visibleRowModel,
                calibration,
                before,
                _delayAsync);
        }

        if (result.SelectionActionAt.HasValue)
            selectionActionObserver?.Invoke(result);

        if (result.FailureReason != SelectionFailureReason.None)
            return result;

        return await ConfirmAsync(
            result,
            expectedCall,
            ConfirmationTimeoutFor(result, settings, confirmationTimeout),
            cancellationToken);
    }

    public async Task<SelectionResult> SelectTargetByGridForTestAsync(
        DxTarget target,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var expectedCall = target.Callsign;
        var before = _getCurrentDxCall();
        var calibration = JtdxBandActivityGridCalibration.FromSettings(settings);
        var result = await _guiGridSelector.SelectAsync(
            target.Decode,
            expectedCall,
            settings,
            _visibleRowModel,
            calibration,
            before,
            _delayAsync);
        if (result.FailureReason != SelectionFailureReason.None)
            return result;

        return await ConfirmAsync(result, expectedCall, TimeSpan.FromSeconds(Math.Max(1, settings.ReplyConfirmSeconds)), cancellationToken);
    }

    public static bool ShouldUseUdpReply(DecodeMessage decode)
    {
        return decode.MessageType == Ft8MessageType.Cq;
    }

    public bool ShouldUseGuiGridClick(DecodeMessage decode)
    {
        return !ShouldUseUdpReply(decode) && _visibleRowModel.FindDecode(decode) != null;
    }

    private static TimeSpan ConfirmationTimeoutFor(
        SelectionResult result,
        AppSettings settings,
        TimeSpan? requestedTimeout)
    {
        if (requestedTimeout.HasValue)
            return requestedTimeout.Value;

        // A GUI double-click changes JTDX's DX Call immediately. Waiting for the
        // general 30-second UDP timeout blocks CALL NOW and consumes whole FT8
        // cycles while every recovery request is rejected as "still selecting".
        return result.SelectionMethod == JtdxSelectionMethod.GuiGridDoubleClick
            ? TimeSpan.FromSeconds(4)
            : TimeSpan.FromSeconds(Math.Max(1, settings.ReplyConfirmSeconds));
    }

    private async Task<SelectionResult> ConfirmAsync(SelectionResult result, string expectedCall, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.Now + timeout;
        while (DateTime.Now < until && !cancellationToken.IsCancellationRequested)
        {
            var status = _getCurrentStatus();
            var current = status?.DxCall?.Trim() ?? _getCurrentDxCall();
            result.JtdxDxCallAfter = current;
            if (IsFreshMatchingStatus(status, expectedCall, result.SelectionActionAt))
            {
                result.Success = true;
                result.ConfirmationTime = DateTime.Now;
                result.ConfirmationStatusReceivedAt = status!.ReceivedAt;
                return result;
            }

            await _delayAsync(TimeSpan.FromMilliseconds(150));
        }

        var finalStatus = _getCurrentStatus();
        var after = finalStatus?.DxCall?.Trim() ?? _getCurrentDxCall();
        result.JtdxDxCallAfter = after;
        var onlyStaleExpectedStatus = after.Equals(expectedCall, StringComparison.OrdinalIgnoreCase)
            && !IsFreshMatchingStatus(finalStatus, expectedCall, result.SelectionActionAt);
        result.FailureReason = string.IsNullOrWhiteSpace(after) || onlyStaleExpectedStatus
            ? SelectionFailureReason.ConfirmationTimedOut
            : SelectionFailureReason.JtdxSelectedWrongCall;
        result.FailureDetail = onlyStaleExpectedStatus
            ? $"DX Call was already '{expectedCall}' before selection, but no fresh JTDX Status confirmed it after the selection action."
            : string.IsNullOrWhiteSpace(after)
                ? $"Timed out waiting for a fresh JTDX Status with DX Call '{expectedCall}'."
                : $"Fresh JTDX Status reports DX Call '{after}', expected '{expectedCall}'.";
        return result;
    }

    private static bool IsFreshMatchingStatus(
        JtdxStatusMessage? status,
        string expectedCall,
        DateTime? selectionActionAt)
    {
        return status != null
            && selectionActionAt.HasValue
            && status.ReceivedAt >= selectionActionAt.Value
            && status.DxCall.Trim().Equals(expectedCall, StringComparison.OrdinalIgnoreCase);
    }
}
