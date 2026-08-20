namespace JtdxAutoResume.V3.Models;

public sealed record BandWorkabilityMetrics
{
    public bool Calculated { get; init; }
    public double Score { get; init; }
    public int DistinctOpportunities { get; init; }
    public int WorkableOpportunities { get; init; }
    public int PathMatchPercent { get; init; }
    public int PskViabilityPercent { get; init; }
    public int PersistencePercent { get; init; }
    public double ProductivityAdjustment { get; init; }
    public string Assessment { get; init; } = "Not calculated";
    public string Detail { get; init; } = "Complete a transmitted Band Analysis to calculate two-way workability.";
}

public readonly record struct BandPerformanceEvidence(
    int CallingAttempts,
    int ReplyOrProgressEvents,
    int CompletedQsos);

