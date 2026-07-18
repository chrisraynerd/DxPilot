namespace JtdxAutoResume.V3.Models;

public sealed class DxCandidateRow
{
    public string JtdxRow { get; set; } = "";
    public int Rank { get; set; }
    public string Call { get; set; } = "";
    public string Country { get; set; } = "";
    public string Dxcc { get; set; } = "";
    public string Tier { get; set; } = "";
    public string WantedReason { get; set; } = "";
    public string DxccStatus { get; set; } = "";
    public int? RarityRank { get; set; }
    public int RarityScore { get; set; }
    public string Grid { get; set; } = "";
    public string GridStatus { get; set; } = "";
    public string State { get; set; } = "";
    public string StateStatus { get; set; } = "";
    public string Rarity { get; set; } = "";
    public double? DistanceMiles { get; set; }
    public string Age { get; set; } = "";
    public int Snr { get; set; }
    public string SourceType { get; set; } = "";
    public int Score { get; set; }
    public string TargetStatus { get; set; } = "";
    public string PriorityClass { get; set; } = "";
    public string Details { get; set; } = "";
    public DxTarget Target { get; set; } = new();
}
