using System.Net;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxSelectionController
{
    private readonly JtdxUdpReplySelector _udpReplySelector;
    private readonly JtdxGuiGridSelector _guiGridSelector;
    private readonly JtdxVisibleRowModel _visibleRowModel;
    private readonly Func<string> _getCurrentDxCall;
    private readonly Func<TimeSpan, Task> _delayAsync;

    public JtdxSelectionController(
        JtdxUdpReplySelector udpReplySelector,
        JtdxGuiGridSelector guiGridSelector,
        JtdxVisibleRowModel visibleRowModel,
        Func<string> getCurrentDxCall,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        _udpReplySelector = udpReplySelector;
        _guiGridSelector = guiGridSelector;
        _visibleRowModel = visibleRowModel;
        _getCurrentDxCall = getCurrentDxCall;
        _delayAsync = delayAsync ?? (delay => Task.Delay(delay));
    }

    public async Task<SelectionResult> SelectTargetAsync(
        DxTarget target,
        AppSettings settings,
        IPEndPoint endpoint,
        IPEndPoint fallbackEndpoint,
        string appId,
        bool sendFallback,
        CancellationToken cancellationToken = default)
    {
        var expectedCall = target.Callsign;
        var before = _getCurrentDxCall();
        var calibration = JtdxBandActivityGridCalibration.FromSettings(settings);
        SelectionResult result;

        if (ShouldUseUdpReply(target.Decode))
        {
            result = await _udpReplySelector.SelectAsync(target.Decode, expectedCall, endpoint, fallbackEndpoint, appId, before, sendFallback, cancellationToken);
        }
        else
        {
            result = _guiGridSelector.Select(target.Decode, expectedCall, settings, _visibleRowModel, calibration, before);
        }

        if (result.FailureReason != SelectionFailureReason.None)
            return result;

        return await ConfirmAsync(result, expectedCall, TimeSpan.FromSeconds(Math.Max(1, settings.ReplyConfirmSeconds)), cancellationToken);
    }

    public async Task<SelectionResult> SelectTargetByGridForTestAsync(
        DxTarget target,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var expectedCall = target.Callsign;
        var before = _getCurrentDxCall();
        var calibration = JtdxBandActivityGridCalibration.FromSettings(settings);
        var result = _guiGridSelector.Select(target.Decode, expectedCall, settings, _visibleRowModel, calibration, before);
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

    private async Task<SelectionResult> ConfirmAsync(SelectionResult result, string expectedCall, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.Now + timeout;
        while (DateTime.Now < until && !cancellationToken.IsCancellationRequested)
        {
            var current = _getCurrentDxCall();
            result.JtdxDxCallAfter = current;
            if (current.Equals(expectedCall, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.ConfirmationTime = DateTime.Now;
                return result;
            }

            await _delayAsync(TimeSpan.FromMilliseconds(150));
        }

        var after = _getCurrentDxCall();
        result.JtdxDxCallAfter = after;
        result.FailureReason = string.IsNullOrWhiteSpace(after)
            ? SelectionFailureReason.ConfirmationTimedOut
            : SelectionFailureReason.JtdxSelectedWrongCall;
        result.FailureDetail = string.IsNullOrWhiteSpace(after)
            ? $"Timed out waiting for JTDX DX Call '{expectedCall}'."
            : $"JTDX DX Call is '{after}', expected '{expectedCall}'.";
        return result;
    }
}
