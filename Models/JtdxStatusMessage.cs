namespace JtdxAutoResume.V3.Models;

public sealed class JtdxStatusMessage
{
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
    public string SourceAppId { get; set; } = "";
    public ulong DialFrequencyHz { get; set; }
    public string Band { get; set; } = "";
    public string Mode { get; set; } = "";
    public string TxMode { get; set; } = "";
    public uint TrPeriodSeconds { get; set; }
    public string DxCall { get; set; } = "";
    public string TxMessage { get; set; } = "";
    public bool TxEnabled { get; set; }
    public bool Transmitting { get; set; }
    public bool Decoding { get; set; }
}
