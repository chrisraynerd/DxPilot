namespace JtdxAutoResume.V3.Models;

public sealed class BandAnalysisHistoryEntry
{
    public string SurveyId { get; set; } = "";
    public DateTime ObservedAtUtc { get; set; }
    public string Band { get; set; } = "";
    public string TriggerReason { get; set; } = "Manual survey";
    public bool Automatic { get; set; }
    public int SecondsObserved { get; set; }
    public int UniqueStations { get; set; }
    public int CqCallers { get; set; }
    public int NewDxccStations { get; set; }
    public int WantedStations { get; set; }
    public int ActivityScore { get; set; }
    public int DxReachScore { get; set; }
    public double? EightiethPercentileDistanceMiles { get; set; }
    public string MainArea { get; set; } = "No location data";
    public string Assessment { get; set; } = "Insufficient sample";
    public bool PskMeasured { get; set; }
    public int PskReports { get; set; }
    public int PskUniqueReceivers { get; set; }
    public int PskUniqueCountries { get; set; }
    public double? PskFarthestDistanceMiles { get; set; }
    public double? PskMedianSnr { get; set; }
    public int PskPropagationScore { get; set; }
    public string PskMainArea { get; set; } = "";
    public string PskAssessment { get; set; } = "";
    public bool CompletedComparableAnalysis { get; set; }
    public double WorkabilityScore { get; set; }
    public int PskViabilityPercent { get; set; }
    public int PathMatchPercent { get; set; }
    public int DistinctWantedOpportunities { get; set; }
    public int WorkableWantedOpportunities { get; set; }
    public double ProductivityAdjustment { get; set; }
    public string WorkabilityAssessment { get; set; } = "";
    public string WorkabilityDetail { get; set; } = "";
    public string StartingBand { get; set; } = "";
    public string SelectedBand { get; set; } = "";
    public string Decision { get; set; } = "";
}
