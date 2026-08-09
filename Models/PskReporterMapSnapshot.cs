namespace JtdxAutoResume.V3.Models;

public sealed record PskReporterMapSnapshot
{
    public DateTime ObservedAtUtc { get; init; }
    public string HomeGrid { get; init; } = "";
    public List<PskReporterSpot> Reports { get; init; } = [];
}
