namespace JtdxAutoResume.V3.Models;

public sealed class DxccRarityInfo
{
    public string DxccNumber { get; set; } = "";
    public string CtyEntityName { get; set; } = "";
    public string ClubLogEntityName { get; set; } = "";
    public int? RarityRank { get; set; }
    public int? ClubLogRank
    {
        get => RarityRank;
        set => RarityRank = value;
    }
    public int RarityScore { get; set; } = 1000;
    public double GlobalRarityScore { get; set; }
    public double UKDesirability { get; set; }
    public string DesirabilityBand { get; set; } = "";
    public string UKRegionBand { get; set; } = "";
    public string SuggestedUse { get; set; } = "";
    public string MatchConfidence { get; set; } = "Low";
    public string MatchSource { get; set; } = "Default";
    public string Notes { get; set; } = "";
}

public sealed class DxccRarityDiagnostics
{
    public string FilePath { get; set; } = "";
    public bool Loaded { get; set; }
    public int RowsLoaded { get; set; }
    public int MatchedToDxcc { get; set; }
    public int MatchedByExactName { get; set; }
    public int MatchedByAlias { get; set; }
    public int Unmatched { get; set; }
    public DateTime? LastLoadedAt { get; set; }
    public string LoadError { get; set; } = "";
    public List<string> UnmatchedRows { get; } = new();

    public string Summary =>
        Loaded
            ? $"DXCC rarity file: {FilePath}; loaded yes; rows {RowsLoaded}; matched {MatchedToDxcc}; exact {MatchedByExactName}; alias {MatchedByAlias}; unmatched {Unmatched}; loaded at {LastLoadedAt:yyyy-MM-dd HH:mm:ss}; errors {(string.IsNullOrWhiteSpace(LoadError) ? "none" : LoadError)}"
            : $"DXCC rarity file: {FilePath}; loaded no; No DXCC rarity file loaded; using default rarity scores.";
}
