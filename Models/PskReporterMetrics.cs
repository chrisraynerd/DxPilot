namespace JtdxAutoResume.V3.Models;

public sealed record PskReporterMetrics
{
    public bool Measured { get; init; }
    public int ReportCount { get; init; }
    public int UniqueReceivers { get; init; }
    public int UniqueCountries { get; init; }
    public int ContinentCount { get; init; }
    public int DistantReceivers { get; init; }
    public int LongDxReceivers { get; init; }
    public int? StrongestSnr { get; init; }
    public double? MedianSnr { get; init; }
    public double? EightiethPercentileDistanceMiles { get; init; }
    public double? FarthestDistanceMiles { get; init; }
    public string FarthestReceiver { get; init; } = "";
    public string MainArea { get; init; } = "No reports";
    public int PropagationScore { get; init; }
    public string Assessment { get; init; } = "Not measured";
    public string Detail { get; init; } = "No PSK Reporter measurement has been made.";
}
