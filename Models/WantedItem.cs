using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JtdxAutoResume.V3.Models;

public sealed class WantedItem : INotifyPropertyChanged
{
    private DateTime _lastSeenUtc = DateTime.UtcNow;

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
    public string Entity { get; set; } = "";
    public string DxccNumber { get; set; } = "";
    public string WantedDetail { get; set; } = "";
    public string WantedReason { get; set; } = "";
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
    public WantedActionabilityStatus ActionabilityStatus { get; set; } = WantedActionabilityStatus.Other;
    public bool IsActionable { get; set; }
    public string SelectionMethod { get; set; } = "NotSelectable";
    public string NotActionableReason { get; set; } = "";
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

    public void RefreshTimeFields()
    {
        OnPropertyChanged(nameof(AgeText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
