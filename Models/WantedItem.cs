using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JtdxAutoResume.V3.Models;

public sealed class WantedItem : INotifyPropertyChanged
{
    private DateTime _lastSeenUtc = DateTime.UtcNow;
    private WantedActionabilityStatus _actionabilityStatus = WantedActionabilityStatus.Other;
    private bool _isActionable;
    private string _selectionMethod = "NotSelectable";
    private string _notActionableReason = "";
    private bool _isPermanentlySuppressed;
    private string _rankText = "";
    private bool _wasCallWorkedBefore;
    private bool _wasCallWorkedInSelectedProfile;
    private bool _wasCallWorkedUnderAnotherProfileOnly;
    private string _workedCallToolTip = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; set; } = "";
    private string _jtdxRow = "";

    public string JtdxRow
    {
        get => _jtdxRow;
        set
        {
            if (_jtdxRow == value)
                return;

            _jtdxRow = value;
            OnPropertyChanged();
        }
    }

    public string Section { get; set; } = "";
    public string Block { get; set; } = "";
    public string Call { get; set; } = "";
    public string ContactableCall { get; set; } = "";
    public bool WasCallWorkedBefore
    {
        get => _wasCallWorkedBefore;
        set
        {
            if (_wasCallWorkedBefore == value)
                return;

            _wasCallWorkedBefore = value;
            OnPropertyChanged();
        }
    }
    public bool WasCallWorkedInSelectedProfile
    {
        get => _wasCallWorkedInSelectedProfile;
        set
        {
            if (_wasCallWorkedInSelectedProfile == value)
                return;

            _wasCallWorkedInSelectedProfile = value;
            OnPropertyChanged();
        }
    }
    public bool WasCallWorkedUnderAnotherProfileOnly
    {
        get => _wasCallWorkedUnderAnotherProfileOnly;
        set
        {
            if (_wasCallWorkedUnderAnotherProfileOnly == value)
                return;

            _wasCallWorkedUnderAnotherProfileOnly = value;
            OnPropertyChanged();
        }
    }
    public string WorkedCallToolTip
    {
        get => _workedCallToolTip;
        set
        {
            if (_workedCallToolTip == value)
                return;

            _workedCallToolTip = value;
            OnPropertyChanged();
        }
    }
    public string Entity { get; set; } = "";
    public string DxccNumber { get; set; } = "";
    public string WantedValue { get; set; } = "";
    public string WantedDetail { get; set; } = "";
    public string WantedReason { get; set; } = "";
    public bool IsNewToCallsign { get; set; }
    public string AchievementProfileLabel { get; set; } = "All callsigns";
    public NeedStatus NeedStatus { get; set; } = NeedStatus.Unknown;
    public string NeedStatusText => NeedStatus switch
    {
        NeedStatus.NeverWorked => "New",
        NeedStatus.WorkedNotLoTWConfirmed => "Unconfirmed",
        NeedStatus.LoTWConfirmed => "LoTW confirmed",
        _ => "Unknown"
    };
    public WantedScope WantedScope { get; set; } = WantedScope.Overall;
    public string WantedScopeText => WantedScope switch
    {
        WantedScope.CurrentBand => "Current Band",
        WantedScope.CurrentMode => "Current Mode",
        WantedScope.CurrentBandMode => "Current Band + Mode",
        _ => "Overall"
    };
    public string Grid { get; set; } = "";
    public string GridSource { get; set; } = "";
    public string NormalizedGrid4 { get; set; } = "";
    public string NormalizedGrid6 { get; set; } = "";
    public int MatchingWorkedQsoCount { get; set; }
    public int MatchingLoTWConfirmedQsoCount { get; set; }
    public string GridNeedStatus { get; set; } = "";
    public string GridDiagnosticReason { get; set; } = "";
    public string State { get; set; } = "";
    public string StateSource { get; set; } = "";
    public string QrzStatus { get; set; } = "";
    public bool IsPermanentlySuppressed
    {
        get => _isPermanentlySuppressed;
        set
        {
            if (_isPermanentlySuppressed == value)
                return;

            _isPermanentlySuppressed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionStateClass));
            OnPropertyChanged(nameof(StationStatusDisplay));
        }
    }
    public string Band { get; set; } = "";
    public string Mode { get; set; } = "";
    public int Snr { get; set; }
    public double Dt { get; set; }
    public int? Offset { get; set; }
    public string MessageType { get; set; } = "";
    public int? PriorityTier { get; set; }
    public double? AdjustedDxValueScore { get; set; }
    public int? ClubLogRank { get; set; }
    public double? UKDesirability { get; set; }
    public double? DistanceMiles { get; set; }
    public WantedActionabilityStatus ActionabilityStatus
    {
        get => _actionabilityStatus;
        set
        {
            if (_actionabilityStatus == value)
                return;

            _actionabilityStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionStateClass));
            OnPropertyChanged(nameof(StationStatusDisplay));
        }
    }

    public bool IsActionable
    {
        get => _isActionable;
        set
        {
            if (_isActionable == value)
                return;

            _isActionable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionabilityText));
            OnPropertyChanged(nameof(ActionStateClass));
            OnPropertyChanged(nameof(StationStatusDisplay));
        }
    }

    public string SelectionMethod
    {
        get => _selectionMethod;
        set
        {
            if (_selectionMethod == value)
                return;

            _selectionMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionabilityText));
        }
    }

    public string NotActionableReason
    {
        get => _notActionableReason;
        set
        {
            if (_notActionableReason == value)
                return;

            _notActionableReason = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionabilityText));
            OnPropertyChanged(nameof(StationStatusDisplay));
        }
    }
    public string ActionabilityText => IsActionable ? $"Yes ({SelectionMethod})" : $"No ({NotActionableReason})";
    public DateTime LastSeenUtc
    {
        get => _lastSeenUtc;
        set
        {
            if (_lastSeenUtc == value)
                return;

            _lastSeenUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AgeText));
        }
    }

    public string SourceRawMessage { get; set; } = "";
    public DecodeMessage SourceDecode { get; set; } = new();
    public string AgeText => $"{Math.Max(0, (int)(DateTime.UtcNow - LastSeenUtc).TotalSeconds)}s";
    public string RankText
    {
        get => _rankText;
        set
        {
            if (_rankText == value)
                return;

            _rankText = value;
            OnPropertyChanged();
        }
    }
    public string WantedReasonDisplay =>
        Section.Equals("DXCC", StringComparison.OrdinalIgnoreCase)
            ? IsNewToCallsign
                ? $"New to {AchievementProfileLabel}"
                : NeedStatus == NeedStatus.NeverWorked ? "New DXCC" : "Unconfirmed DXCC"
            : Section.Equals("Grid", StringComparison.OrdinalIgnoreCase)
                ? $"{(NeedStatus == NeedStatus.NeverWorked ? "New" : "Unconfirmed")} grid {WantedValue}".Trim()
                : Section.Equals("USA State", StringComparison.OrdinalIgnoreCase)
                    ? $"{(NeedStatus == NeedStatus.NeverWorked ? "New" : "Unconfirmed")} state {WantedValue}".Trim()
                    : WantedReason;
    public string StationStatusDisplay =>
        IsPermanentlySuppressed ? "Suppressed" :
        IsActionable ? "Candidate" :
        ActionabilityStatus == WantedActionabilityStatus.QsoInProgress ? "In QSO" :
        NotActionableReason.Contains("JTDX grid", StringComparison.OrdinalIgnoreCase) ? "Off JTDX grid" :
        string.IsNullOrWhiteSpace(NotActionableReason) ? "Not contactable" : NotActionableReason;
    public string OpportunityClass =>
        Section.Equals("DXCC", StringComparison.OrdinalIgnoreCase)
            ? NeedStatus == NeedStatus.NeverWorked ? "NewDxcc" : "UnconfirmedDxcc"
            : Section.Equals("Grid", StringComparison.OrdinalIgnoreCase)
                ? "NewGrid"
                : Section.Equals("USA State", StringComparison.OrdinalIgnoreCase)
                    ? "NewState"
                    : Section.Contains("Band", StringComparison.OrdinalIgnoreCase)
                        ? "BandMode"
                        : "";
    public string ActionStateClass =>
        IsPermanentlySuppressed ? "PermanentlySuppressed" :
        ActionabilityStatus == WantedActionabilityStatus.Suppressed ? "Suppressed" :
        ActionabilityStatus is WantedActionabilityStatus.FailedSource or WantedActionabilityStatus.InvalidParse ? "Failed" :
        ActionabilityStatus == WantedActionabilityStatus.QsoInProgress ? "InProgress" :
        IsActionable ? "Actionable" : "NotContactable";

    public void RefreshTimeFields()
    {
        OnPropertyChanged(nameof(AgeText));
    }

    public void RefreshVisualFields()
    {
        OnPropertyChanged(nameof(OpportunityClass));
        OnPropertyChanged(nameof(ActionStateClass));
        OnPropertyChanged(nameof(WantedReasonDisplay));
        OnPropertyChanged(nameof(StationStatusDisplay));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
