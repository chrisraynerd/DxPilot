namespace JtdxAutoResume.V3.Models;

public enum DxccCandidateStatus
{
    Unknown,
    NotWorked,
    WorkedUnconfirmed,
    Confirmed
}

public sealed class CandidateRanking
{
    public string Call { get; set; } = "";
    public string Entity { get; set; } = "";
    public string DxccNumber { get; set; } = "";
    public string Grid { get; set; } = "";
    public string State { get; set; } = "";
    public string Iota { get; set; } = "";
    public string Band { get; set; } = "";
    public string Mode { get; set; } = "";
    public WantedScope WantedScope { get; set; } = WantedScope.Overall;
    public NeedStatus NeedStatus { get; set; } = NeedStatus.Unknown;
    public int PriorityTier { get; set; } = 99;
    public string PriorityTierName { get; set; } = "Diagnostic";
    public string PrimaryWantedReason { get; set; } = "";
    public List<string> AllWantedReasons { get; } = new();
    public DxccCandidateStatus DxccStatus { get; set; } = DxccCandidateStatus.Unknown;
    public bool DxccWorked { get; set; }
    public bool DxccConfirmed { get; set; }
    public bool IsNewToCallsign { get; set; }
    public string AchievementProfileLabel { get; set; } = "All callsigns";
    public string DxccConfirmationMode { get; set; } = "";
    public string DxccConfirmationSource { get; set; } = "";
    public int? RarityRank { get; set; }
    public int RarityScore { get; set; }
    public double GlobalRarityScore { get; set; }
    public double UKDesirability { get; set; }
    public string DesirabilityBand { get; set; } = "";
    public string UKRegionBand { get; set; } = "";
    public double DistanceScore { get; set; }
    public double AdjustedDxValueScore { get; set; }
    public string RarityMatchSource { get; set; } = "";
    public string RarityMatchConfidence { get; set; } = "";
    public double? DistanceMiles { get; set; }
    public string DistanceSource { get; set; } = "";
    public int SignalScore { get; set; }
    public int FreshnessScore { get; set; }
    public int PenaltyScore { get; set; }
    public int FinalScore { get; set; }
    public bool IsSelectable { get; set; } = true;
    public string SelectabilityStatus { get; set; } = "Selectable";
    public string NotSelectedReason { get; set; } = "";
    public string EligibilityStatus { get; set; } = "Eligible";
    public string SuppressionStatus { get; set; } = "";
    public string SourceRawMessage { get; set; } = "";
    public DateTime SourceDecodeTime { get; set; }
    public double AgeSeconds { get; set; }
    public string SelectionExplanation { get; set; } = "";
    public string ScoreBreakdown { get; set; } = "";
}
