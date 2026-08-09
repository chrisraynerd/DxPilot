namespace JtdxAutoResume.V3.Models;

public sealed record PskReporterSpot
{
    public long SequenceNumber { get; init; }
    public long FrequencyHz { get; init; }
    public string Band { get; init; } = "";
    public string Mode { get; init; } = "";
    public int? SignalReportDb { get; init; }
    public DateTime TransmissionTimeUtc { get; init; }
    public string SenderCallsign { get; init; } = "";
    public string SenderLocator { get; init; } = "";
    public string ReceiverCallsign { get; init; } = "";
    public string ReceiverLocator { get; init; } = "";
    public string ReceiverDxcc { get; init; } = "";
    public string ReceiverDxccCode { get; init; } = "";
    public string Source { get; init; } = "";
}

public sealed record PskProbeWindow(
    string Band,
    DateTime FirstCqUtc,
    DateTime SecondCqUtc)
{
    public bool Matches(PskReporterSpot spot, TimeSpan tolerance)
    {
        if (!Band.Equals(spot.Band, StringComparison.OrdinalIgnoreCase)
            || !spot.Mode.Equals("FT8", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Math.Abs((spot.TransmissionTimeUtc - FirstCqUtc).TotalSeconds) <= tolerance.TotalSeconds
            || Math.Abs((spot.TransmissionTimeUtc - SecondCqUtc).TotalSeconds) <= tolerance.TotalSeconds;
    }
}
