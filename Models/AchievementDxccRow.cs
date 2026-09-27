namespace JtdxAutoResume.V3.Models;

public sealed class AchievementDxccRow
{
    // State rows share the read-only totals and band presentation, not DXCC identity.
    public string StateCode { get; init; } = "";
    public IReadOnlyList<AchievementBandCell> BandCells { get; init; } = AchievementBandCell.Build([]);
    public string DxccNumber { get; init; } = "";
    public string EntityName { get; init; } = "";
    public int? ClubLogRank { get; init; }
    public string ClubLogRankDisplay => ClubLogRank?.ToString() ?? "—";
    public double UKDesirability { get; init; }
    public string UKDesirabilityDisplay => UKDesirability > 0 ? $"{UKDesirability:0}" : "—";
    public string DifficultyBand { get; init; } = "";
    public int SeenCount { get; init; }
    public int SeenCallCount { get; init; }
    public int QsoCount { get; init; }
    public int UnconfirmedQsoCount { get; init; }
    public int LotwConfirmedQsoCount { get; init; }
    public bool SeenButNeeded => QsoCount == 0 && SeenCount > 0;
    public DateTime? LastWorked { get; init; }
    public string LastWorkedDisplay => LastWorked?.ToString("dd MMM yyyy") ?? "—";
    public string Bands { get; init; } = "";
    public string Modes { get; init; } = "";
    public string StatusKey { get; init; } = "Needed";
    public string StatusDisplay => StatusKey switch
    {
        "LotwConfirmed" => "LoTW confirmed",
        "WorkedUnconfirmed" => "Worked — awaiting LoTW",
        _ => SeenButNeeded ? "Needed — seen by DX Pilot" : "Needed"
    };
}
