using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class ScavengerBandRow : ObservableObject
{
    public ScavengerBandRow(BandAnalysisBandViewModel band) => BandChoice = band;
    public BandAnalysisBandViewModel BandChoice { get; }
    public string Band => BandChoice.Band;
    private string _status = "Not visited";
    private string _activity = "—";
    private string _revisit = "Next round";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Activity { get => _activity; set { if (SetProperty(ref _activity, value)) OnPropertyChanged(nameof(HeardCount)); } }
    public string HeardCount => int.TryParse(Activity.Split(' ')[0], out var count) ? $"{count} heard" : "";
    public string Revisit { get => _revisit; set => SetProperty(ref _revisit, value); }
}

public sealed class ScavengerViewModel : ObservableObject
{
    private readonly Func<AppSettings> _getSettings;
    private AppSettings Settings => _getSettings();
    private string _status = "Ready to search for new and unconfirmed DXCCs.";
    private string _phase = "Stopped";
    private int _round = 1;
    private string _restingTargets = "";
    private string _targetSummary = "No Scavenger target selected.";
    private string _filterSummary = "";
    public string TargetSummary { get => _targetSummary; set => SetProperty(ref _targetSummary, value); }
    public string FilterSummary { get => _filterSummary; set => SetProperty(ref _filterSummary, value); }
    public ScavengerOpportunityPanel[] OpportunityPanels { get; } =
        [new("Wanted DXCC", "DXCC"), new("Wanted Grids", "grid"), new("Wanted States", "state")];
    public void ClearOpportunities()
    {
        foreach (var panel in OpportunityPanels) panel.Rows.Clear();
        TargetSummary = "No Scavenger target selected.";
    }
    public ScavengerViewModel(Func<AppSettings> settings, IEnumerable<BandAnalysisBandViewModel> bands)
    {
        _getSettings = settings;
        Bands = new(bands.Select(b => new ScavengerBandRow(b)));
    }
    public ObservableCollection<ScavengerBandRow> Bands { get; }
    public bool WantedDxcc { get => Settings.ScavengerWantedDxcc; set { Settings.ScavengerWantedDxcc = value; OnPropertyChanged(); } }
    public bool WantedGrids { get => Settings.ScavengerWantedGrids; set { Settings.ScavengerWantedGrids = value; OnPropertyChanged(); } }
    public bool WantedStates { get => Settings.ScavengerWantedStates; set { Settings.ScavengerWantedStates = value; OnPropertyChanged(); } }
    public bool IncludeBand { get => Settings.ScavengerIncludeBand; set { Settings.ScavengerIncludeBand = value; OnPropertyChanged(); } }
    public bool IncludeMode { get => Settings.ScavengerIncludeMode; set { Settings.ScavengerIncludeMode = value; OnPropertyChanged(); } }
    public bool IncludeBandMode { get => Settings.ScavengerIncludeBandMode; set { Settings.ScavengerIncludeBandMode = value; OnPropertyChanged(); } }
    public int[] MinuteChoices { get; } = [1, 2, 3, 4, 5];
    public int[] CallingMinuteChoices { get; } = Enumerable.Range(1, 60).ToArray();
    public int[] TargetRestRoundChoices { get; } = Enumerable.Range(1, 12).ToArray();
    public string RestingTargets { get => _restingTargets; set => SetProperty(ref _restingTargets, value); }
    public int MaxCallingMinutes { get => Math.Clamp(Settings.ScavengerMaxCallingMinutes, 1, 60); set { Settings.ScavengerMaxCallingMinutes = Math.Clamp(value, 1, 60); OnPropertyChanged(); } }
    public int TargetRestRounds { get => Math.Clamp(Settings.ScavengerTargetRestRounds, 1, 12); set { Settings.ScavengerTargetRestRounds = Math.Clamp(value, 1, 12); OnPropertyChanged(); } }
    public int[] RoundChoices { get; } = [1, 2, 3, 4, 5, 6, 8, 10, 12];
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Phase { get => _phase; set => SetProperty(ref _phase, value); }
    public int Round { get => _round; set => SetProperty(ref _round, value); }
    public int ListenMinutes { get => Math.Clamp(Settings.ScavengerListenMinutes, 1, 5); set { Settings.ScavengerListenMinutes = Math.Clamp(value, 1, 5); OnPropertyChanged(); } }
    public int DeadSkipRounds { get => Math.Clamp(Settings.ScavengerDeadSkipRounds, 1, 12); set { Settings.ScavengerDeadSkipRounds = Math.Clamp(value, 1, 12); OnPropertyChanged(); } }
    public int QuietSkipRounds { get => Math.Clamp(Settings.ScavengerQuietSkipRounds, 1, 12); set { Settings.ScavengerQuietSkipRounds = Math.Clamp(value, 1, 12); OnPropertyChanged(); } }
}
