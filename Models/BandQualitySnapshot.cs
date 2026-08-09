namespace JtdxAutoResume.V3.Models;

public sealed class BandQualitySnapshot
{
    public string Band { get; init; } = "";
    public int TotalDecodes { get; init; }
    public int UniqueStations { get; init; }
    public int CqCallers { get; init; }
    public int NewDxccStations { get; init; }
    public int DistantStations { get; init; }
    public int LongDxStations { get; init; }
    public int WantedStations { get; init; }
    public int ContinentCount { get; init; }
    public string MainArea { get; init; } = "No location data";
    public double? MedianSnr { get; init; }
    public double? EightiethPercentileDistanceMiles { get; init; }
    public double? FarthestDistanceMiles { get; init; }
    public int ActivityScore { get; init; }
    public int DxReachScore { get; init; }
    public string Assessment { get; init; } = "Insufficient sample";
    public string Detail { get; init; } = "No observations yet.";
}
