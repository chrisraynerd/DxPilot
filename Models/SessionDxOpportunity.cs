namespace JtdxAutoResume.V3.Models;

public sealed class SessionDxOpportunity
{
    public string SessionId { get; set; } = "";
    public DateTime SessionStartedUtc { get; set; }
    public string OpportunityId { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public double LastSeenAgeSeconds => Math.Max(0, (DateTime.UtcNow - LastSeenUtc).TotalSeconds);
    public string Call { get; set; } = "";
    public bool WasCallWorkedBefore { get; set; }
    public bool WasCallWorkedInSelectedProfile { get; set; }
    public bool WasCallWorkedUnderAnotherProfileOnly { get; set; }
    public string WorkedCallToolTip { get; set; } = "";
    public int? UniversalRank { get; set; }
    public string RankText { get; set; } = "";
    public string JtdxRow { get; set; } = "";
    public bool IsPermanentlySuppressed { get; set; }
    public string Entity { get; set; } = "";
    public string DxccNumber { get; set; } = "";
    public string DxccStatus { get; set; } = "";
    public string Category { get; set; } = "";
    public string Need { get; set; } = "";
    public string GridNeed { get; set; } = "";
    public string StateNeed { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Band { get; set; } = "";
    public string Mode { get; set; } = "";
    public ulong DialFrequencyHz { get; set; }
    public int? RarityRank { get; set; }
    public int RarityScore { get; set; }
    public int PriorityTier { get; set; } = 99;
    public string PriorityTierName { get; set; } = "";
    public string PrimaryReason { get; set; } = "";
    public int BestSnr { get; set; } = int.MinValue;
    public int LastSnr { get; set; }
    public double? BestDistance { get; set; }
    public string Grid { get; set; } = "";
    public string State { get; set; } = "";
    public string GridSource { get; set; } = "";
    public string SourceRawMessage { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string LastCountedObservationId { get; set; } = "";
    public int SeenCount { get; set; }
    public int DirectlyHeardCount { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public bool WasAutoSelected { get; set; }
    public bool WasManuallySelected { get; set; }
    public bool WasCalled { get; set; }
    public bool WasWorked { get; set; }
    public DateTime? WorkedUtc { get; set; }
    public string WorkedSource { get; set; } = "";
    public string Outcome { get; set; } = "Seen only";
    public string OutcomeReason { get; set; } = "";
    public DateTime? SuppressedUntilUtc { get; set; }
    public string Notes { get; set; } = "";
    public List<string> RawMessages { get; set; } = new();
    public List<string> Timeline { get; set; } = new();

    public string SessionDateText => SessionStartedUtc == DateTime.MinValue ? "" : SessionStartedUtc.ToLocalTime().ToString("dd MMM yy");
    public string FirstSeenText => FirstSeenUtc.ToLocalTime().ToString("HH:mm:ss");
    public string LastSeenText => LastSeenUtc.ToLocalTime().ToString("HH:mm:ss");
    public string AgeText => LastSeenAgeSeconds < 60 ? $"{(int)LastSeenAgeSeconds}s" : $"{(int)(LastSeenAgeSeconds / 60)}m {((int)LastSeenAgeSeconds % 60):00}s";
    public string SeenForText
    {
        get
        {
            var span = LastSeenUtc - FirstSeenUtc;
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;
            return span.TotalSeconds < 60 ? $"{(int)span.TotalSeconds}s" : $"{(int)span.TotalMinutes}m {span.Seconds:00}s";
        }
    }
    public string TimesSeenText => Math.Max(1, SeenCount).ToString();
    public string CalledText => WasCalled ? "Yes" : "No";
    public string WorkedText => WasWorked ? "Yes" : "No";
    public string GridStateText => !string.IsNullOrWhiteSpace(State) ? State : Grid;
    public string SourceText => WasWorked ? WorkedSource : SourceType;
    public string BestSnrText => BestSnr == int.MinValue ? "" : BestSnr.ToString();
    public string RarityRankText => RarityRank.HasValue ? $"#{RarityRank}" : "";
    public string SelectionText => WasManuallySelected ? "Manual" : WasAutoSelected ? "Auto" : "";
    public string OutcomeClass =>
        WasWorked ? "Worked" :
        Outcome.Contains("Missed", StringComparison.OrdinalIgnoreCase) || Outcome.Contains("TX mismatch", StringComparison.OrdinalIgnoreCase) ? "Missed" :
        Outcome.Contains("Suppressed", StringComparison.OrdinalIgnoreCase) ? "Suppressed" :
        Outcome.Contains("progress", StringComparison.OrdinalIgnoreCase) ? "InProgress" :
        DxccStatus is "New DXCC" or "Worked unconfirmed" ? "WantedDxcc" :
        RarityRank.HasValue ? "Rare" : "";
    public string OpportunityClass =>
        DxccStatus == "New DXCC" ? "NewDxcc" :
        DxccStatus == "Worked unconfirmed" ? "UnconfirmedDxcc" :
        GridNeed is "New" or "Unconfirmed" ? "NewGrid" :
        StateNeed is "New" or "Unconfirmed" ? "NewState" :
        Category.Equals("Rare confirmed DXCC", StringComparison.OrdinalIgnoreCase) ? "RareDxcc" :
        PriorityTier == 60 ? "BandMode" : "Heard";
    public string ActionStateClass =>
        IsPermanentlySuppressed ? "PermanentlySuppressed" :
        Outcome.Contains("Suppressed", StringComparison.OrdinalIgnoreCase) ? "Suppressed" :
        Outcome.Contains("Missed", StringComparison.OrdinalIgnoreCase)
            || Outcome.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
            || Outcome.Contains("Failed", StringComparison.OrdinalIgnoreCase) ? "Failed" :
        Outcome.Equals("In progress", StringComparison.OrdinalIgnoreCase) ? "InProgress" :
        Outcome.Equals("Called", StringComparison.OrdinalIgnoreCase) ? "Calling" :
        WasWorked ? "Worked" : "";
    public string WantedReasonDisplay =>
        DxccStatus == "New DXCC" ? "New DXCC" :
        DxccStatus == "Worked unconfirmed" ? "Unconfirmed DXCC" :
        GridNeed is "New" or "Unconfirmed" ? $"{GridNeed} grid {Grid}".Trim() :
        StateNeed is "New" or "Unconfirmed" ? $"{StateNeed} state {State}".Trim() :
        Category.Equals("Rare confirmed DXCC", StringComparison.OrdinalIgnoreCase) ? "Rare country (already confirmed)" :
        Category.Equals("Band/mode", StringComparison.OrdinalIgnoreCase) ? PrimaryReason :
        "Heard / general";
    public string StationStatusDisplay => Outcome;

    public string Details =>
        $"{Call} - {Entity}\n"
        + "\nWanted reason:\n"
        + $"{PrimaryReason}\n\n"
        + "Seen:\n"
        + $"First seen: {FirstSeenText}\n"
        + $"Last seen: {LastSeenText}\n"
        + $"Seen for: {SeenForText}\n"
        + $"Last heard: {AgeText} ago\n"
        + $"Best SNR: {BestSnrText}\n"
        + $"Times seen: {TimesSeenText}\n\n"
        + "Action:\n"
        + $"Called by DX Pilot: {CalledText}\n"
        + $"Worked/logged: {WorkedText}\n"
        + $"Outcome: {Outcome}\n"
        + $"Outcome reason: {OutcomeReason}\n\n"
        + "Timeline\n"
        + string.Join("\n", Timeline.TakeLast(12))
        + "\n\nAdvanced/debug\n"
        + $"- DXCC: {DxccNumber}  Status: {DxccStatus}\n"
        + $"- Category: {Category}  Need: {Need}  Scope: {Scope}\n"
        + $"- Grid need: {GridNeed}  State need: {StateNeed}\n"
        + $"- Radio: {Band} {Mode}  Dial frequency: {(DialFrequencyHz == 0 ? "Unknown" : $"{DialFrequencyHz / 1_000_000d:0.000000} MHz")}\n"
        + $"- Tier: {PriorityTierName}  Rarity: {RarityRankText}  Score: {RarityScore}\n"
        + $"- Grid/state: {GridStateText}  Grid source: {GridSource}\n"
        + $"- Last SNR: {LastSnr}  Attempts: {AttemptCount}  Auto: {WasAutoSelected}  Manual: {WasManuallySelected}\n"
        + $"- Source type: {SourceType}\n"
        + $"- Source decode: {SourceRawMessage}\n\n"
        + "Recent raw messages\n"
        + string.Join("\n", RawMessages.TakeLast(8));

    public SessionDxOpportunity Snapshot()
    {
        var copy = (SessionDxOpportunity)MemberwiseClone();
        copy.RawMessages = new List<string>(RawMessages);
        copy.Timeline = new List<string>(Timeline);
        return copy;
    }
}
