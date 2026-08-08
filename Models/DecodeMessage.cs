namespace JtdxAutoResume.V3.Models;

public sealed class DecodeMessage
{
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
    public TimeSpan? DecodeTime { get; set; }
    public int Snr { get; set; }
    public double Dt { get; set; }
    public int? AudioOffset { get; set; }
    public string Mode { get; set; } = "";
    public string ProtocolMode { get; set; } = "";
    public long RadioContextGeneration { get; set; }
    public ulong DialFrequencyHz { get; set; }
    public string RawText { get; set; } = "";
    public string SourceAppId { get; set; } = "";
    public Ft8MessageType MessageType { get; set; } = Ft8MessageType.Unknown;
    public string MessageTypeText => MessageType switch
    {
        Ft8MessageType.Cq => "CQ",
        Ft8MessageType.DirectedGrid => "Directed Grid",
        Ft8MessageType.DirectedReport => "Directed Report",
        Ft8MessageType.DirectedRReport => "Directed R Report",
        Ft8MessageType.DirectedRrr => "Directed RRR",
        Ft8MessageType.DirectedRr73 => "Directed RR73",
        Ft8MessageType.Directed73 => "Directed 73",
        Ft8MessageType.HashedReport => "Hashed Report",
        Ft8MessageType.HashedRReport => "Hashed R Report",
        Ft8MessageType.HashedRr73 => "Hashed RR73",
        Ft8MessageType.Hashed73 => "Hashed 73",
        Ft8MessageType.HashedOther => "Hashed/Compound",
        Ft8MessageType.Invalid => "Unknown / Low Confidence",
        _ => "Unknown / Low Confidence"
    };
    public bool IsCq { get; set; }
    public string Call1 { get; set; } = "";
    public string AddressedCall { get; set; } = "";
    public string Call1Entity { get; set; } = "";
    public string AddressedEntity { get; set; } = "";
    public string AddressedDxccNumber { get; set; } = "";
    public string Call2 { get; set; } = "";
    public string HeardCall { get; set; } = "";
    public string Call2Entity { get; set; } = "";
    public string Payload { get; set; } = "";
    public string GridOwnerCall { get; set; } = "";
    public string GridEntity { get; set; } = "";
    public string PrimaryDisplayCall { get; set; } = "";
    public string PrimaryDisplayEntity { get; set; } = "";
    public string PossibleHuntCalls { get; set; } = "";
    public string ContactableCall { get; set; } = "";
    public string ContactableEntity { get; set; } = "";
    public string ContactableDxccNumber { get; set; } = "";
    public string HuntTarget { get; set; } = "";
    public string HuntTargetEntity { get; set; } = "";
    public string HuntTargetReason { get; set; } = "";
    public bool IsReport { get; set; }
    public bool IsRReport { get; set; }
    public bool IsRrr { get; set; }
    public bool IsRR73 { get; set; }
    public bool Is73 { get; set; }
    public bool IsHashedOrCompound { get; set; }
    public bool ContainsMyCall { get; set; }
    public bool Targetable { get; set; }
    public string TargetabilityReason { get; set; } = "";
    public string ParserReason { get; set; } = "";
    public string ParserInterpretation { get; set; } = "";
    public ParseConfidence ParseConfidence { get; set; } = ParseConfidence.Low;
    public string ParseConfidenceText => ParseConfidence.ToString();
    public string ParseDebugLine { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string Grid { get; set; } = "";
    public string TransmittedGrid { get; set; } = "";
    public string SessionObservedGrid { get; set; } = "";
    public string AdifGrid { get; set; } = "";
    public string QrzGrid { get; set; } = "";
    public double? QrzLatitude { get; set; }
    public double? QrzLongitude { get; set; }
    public string QrzGeoLocationSource { get; set; } = "";
    public string EffectiveGrid { get; set; } = "";
    public DecodeGridSource EffectiveGridSource { get; set; } = DecodeGridSource.Unknown;
    public CallsignLookupStatus CallsignLookupStatus { get; set; } = CallsignLookupStatus.Pending;
    public CallsignDataSource CallsignDataSource { get; set; } = CallsignDataSource.Unknown;
    public string CallsignLookupError { get; set; } = "";
    public string ReportedGrid
    {
        get => Grid;
        set => Grid = value;
    }
    public string GridSource { get; set; } = "";
    public string Dxcc { get; set; } = "";
    public string DxccNumber
    {
        get => Dxcc;
        set => Dxcc = value;
    }
    public string EntityName { get; set; } = "";
    public string Continent { get; set; } = "";
    public string Iota { get; set; } = "";
    public string EntitySource { get; set; } = "";
    public string EntityConfidence { get; set; } = "";
    public string EntityReason { get; set; } = "";
    public string LookupPrefix { get; set; } = "";
    public double? EntityLatitude { get; set; }
    public double? EntityLongitude { get; set; }
    public string DistanceSource { get; set; } = "";
    public string State { get; set; } = "";
    public string StateSource { get; set; } = "";
    public string Band { get; set; } = "";
    public double? DistanceKm { get; set; }
    public double? DistanceMiles => DistanceKm / 1.609344;
    public bool IsNewDxcc { get; set; }
    public bool IsUnconfirmedDxcc { get; set; }
    public bool IsNewGrid { get; set; }
    public bool IsNewState { get; set; }
    public bool IsPermanentlySuppressed { get; set; }
    public string PriorityClass => IsNewDxcc ? "NewDxcc" : IsUnconfirmedDxcc ? "UnconfirmedDxcc" : IsNewGrid ? "NewGrid" : IsNewState ? "NewState" : "";
    public string OpportunityClass => PriorityClass;
    public string ActionStateClass => IsPermanentlySuppressed ? "PermanentlySuppressed" : LowConfidence ? "Muted" : "";
    public string RankText { get; set; } = "";
    public string JtdxRow { get; set; } = "";
    public string AgeText { get; set; } = "";
    public string WantedReasonDisplay { get; set; } = "";
    public string StationStatusDisplay { get; set; } = "";
    public bool WasCallWorkedBefore { get; set; }
    public string WorkedCallToolTip { get; set; } = "";
    public string CountryDisplay => string.IsNullOrWhiteSpace(EntityName) ? PrimaryDisplayEntity : EntityName;
    public bool LowConfidence { get; set; }
    public string DisplayTime => DecodeTime?.ToString(@"hh\:mm\:ss") ?? ReceivedAt.ToString("HH:mm:ss");
}
