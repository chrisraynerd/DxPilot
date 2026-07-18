using System.Net;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxUdpReplySelector
{
    private readonly JtdxUdpClient _udpClient;

    public JtdxUdpReplySelector(JtdxUdpClient udpClient)
    {
        _udpClient = udpClient;
    }

    public async Task<SelectionResult> SelectAsync(
        DecodeMessage target,
        string expectedCall,
        IPEndPoint endpoint,
        IPEndPoint fallbackEndpoint,
        string appId,
        string dxCallBefore,
        bool sendFallback,
        CancellationToken cancellationToken = default)
    {
        var result = NewResult(target, expectedCall, dxCallBefore);
        try
        {
            await _udpClient.SendReplyAsync(target, appId, endpoint, cancellationToken);
            if (sendFallback)
                await _udpClient.SendReplyAsync(target, appId, fallbackEndpoint, cancellationToken);

            result.Details = sendFallback
                ? $"UDP Reply sent to {endpoint.Address}:{endpoint.Port} and fallback {fallbackEndpoint.Address}:{fallbackEndpoint.Port}."
                : $"UDP Reply sent to {endpoint.Address}:{endpoint.Port}.";
        }
        catch (Exception ex)
        {
            result.FailureReason = SelectionFailureReason.UdpReplyFailed;
            result.FailureDetail = ex.GetBaseException().Message;
        }

        return result;
    }

    private static SelectionResult NewResult(DecodeMessage target, string expectedCall, string dxCallBefore)
    {
        return new SelectionResult
        {
            SelectionMethod = JtdxSelectionMethod.UdpReply,
            ExpectedCall = expectedCall,
            JtdxDxCallBefore = dxCallBefore,
            TargetRawMessage = target.RawText,
            MessageType = target.MessageType
        };
    }
}
