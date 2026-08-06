namespace JtdxAutoResume.V3.Models;

public sealed class JtdxOutgoingTransmission
{
    public DateTime LoggedAtUtc { get; init; }
    public DateTime ObservedAt { get; init; } = DateTime.Now;
    public bool IsRetransmitting { get; init; }
    public string Mode { get; init; } = "";
    public string Message { get; init; } = "";
    public string RawLine { get; init; } = "";
}

public enum JtdxOutgoingMessageDisposition
{
    Unknown,
    ExpectedTarget,
    Cq,
    WrongTarget
}

public sealed class JtdxOutgoingMessageAnalysis
{
    public JtdxOutgoingMessageDisposition Disposition { get; init; }
    public string ObservedTargetCall { get; init; } = "";
}
