using System.Collections.ObjectModel;
using System.Windows.Input;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class SessionHistoryViewModel : ObservableObject
{
    private SessionDxOpportunity? _selectedOpportunity;
    private string _status = "Session DX history will track meaningful DX opportunities.";
    private bool _showNewUnconfirmed = true;
    private bool _showRareConfirmed = true;
    private bool _showDxcc = true;
    private bool _showGrids = true;
    private bool _showUsaStates = true;
    private bool _showCalledOnly = true;
    private bool _showWorked = true;
    private bool _showMissed = true;
    private bool _showSeenOnly = true;
    private bool _showSuppressed = true;
    private bool _showFailedMismatch = true;
    private bool _showCurrentSessionOnly = true;

    public ObservableCollection<SessionDxOpportunity> AllOpportunities { get; } = new();
    public ObservableCollection<SessionDxOpportunity> Opportunities { get; } = new();

    public SessionDxOpportunity? SelectedOpportunity
    {
        get => _selectedOpportunity;
        set => SetProperty(ref _selectedOpportunity, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool ShowNewUnconfirmed
    {
        get => _showNewUnconfirmed;
        set { if (SetProperty(ref _showNewUnconfirmed, value)) Refresh(); }
    }

    public bool ShowRareConfirmed
    {
        get => _showRareConfirmed;
        set { if (SetProperty(ref _showRareConfirmed, value)) Refresh(); }
    }

    public bool ShowDxcc
    {
        get => _showDxcc;
        set { if (SetProperty(ref _showDxcc, value)) Refresh(); }
    }

    public bool ShowGrids
    {
        get => _showGrids;
        set { if (SetProperty(ref _showGrids, value)) Refresh(); }
    }

    public bool ShowUsaStates
    {
        get => _showUsaStates;
        set { if (SetProperty(ref _showUsaStates, value)) Refresh(); }
    }

    public bool ShowCalledOnly
    {
        get => _showCalledOnly;
        set { if (SetProperty(ref _showCalledOnly, value)) Refresh(); }
    }

    public bool ShowWorked
    {
        get => _showWorked;
        set { if (SetProperty(ref _showWorked, value)) Refresh(); }
    }

    public bool ShowMissed
    {
        get => _showMissed;
        set { if (SetProperty(ref _showMissed, value)) Refresh(); }
    }

    public bool ShowSuppressed
    {
        get => _showSuppressed;
        set { if (SetProperty(ref _showSuppressed, value)) Refresh(); }
    }

    public bool ShowFailedMismatch
    {
        get => _showFailedMismatch;
        set { if (SetProperty(ref _showFailedMismatch, value)) Refresh(); }
    }

    public bool ShowSeenOnly
    {
        get => _showSeenOnly;
        set { if (SetProperty(ref _showSeenOnly, value)) Refresh(); }
    }

    public bool ShowCurrentSessionOnly
    {
        get => _showCurrentSessionOnly;
        set { if (SetProperty(ref _showCurrentSessionOnly, value)) Refresh(); }
    }

    public ICommand? ExportCommand { get; set; }
    public ICommand? ClearCommand { get; set; }

    public void Refresh()
    {
        var rows = AllOpportunities
            .Where(PassesFilter)
            .OrderBy(o => o.PriorityTier)
            .ThenBy(o => o.RarityRank ?? int.MaxValue)
            .ThenByDescending(o => o.LastSeenUtc)
            .ThenBy(o => OutcomeSort(o.Outcome))
            .ToList();

        Opportunities.Clear();
        foreach (var row in rows)
            Opportunities.Add(row);

        if (SelectedOpportunity == null || !Opportunities.Contains(SelectedOpportunity))
            SelectedOpportunity = Opportunities.FirstOrDefault();
        else
            OnPropertyChanged(nameof(SelectedOpportunity));

        Status = $"{Opportunities.Count} shown / {AllOpportunities.Count} tracked session DX opportunities.";
    }

    private bool PassesFilter(SessionDxOpportunity item)
    {
        var isDxcc = item.Category.Equals("DXCC", StringComparison.OrdinalIgnoreCase)
            || item.DxccStatus is "New DXCC" or "Worked unconfirmed";
        var isGrid = item.Category.Equals("Grid", StringComparison.OrdinalIgnoreCase);
        var isState = item.Category.Equals("USA State", StringComparison.OrdinalIgnoreCase);
        var isRareConfirmed = item.Category.Equals("Rare confirmed DXCC", StringComparison.OrdinalIgnoreCase)
            || item.DxccStatus == "Confirmed" && item.RarityRank.HasValue;
        var passesType = (ShowDxcc && isDxcc)
            || (ShowGrids && isGrid)
            || (ShowUsaStates && isState)
            || (ShowRareConfirmed && isRareConfirmed);

        var isMissed = item.Outcome.Contains("Missed", StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains("TX mismatch", StringComparison.OrdinalIgnoreCase);
        var isSuppressed = item.Outcome.Contains("Suppressed", StringComparison.OrdinalIgnoreCase);
        var isFailedMismatch = item.Outcome.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains("wrong target", StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains("Failed", StringComparison.OrdinalIgnoreCase);
        var isSeenOnly = item.Outcome == "Seen only";

        if (!passesType && !item.WasCalled && !item.WasWorked)
            return false;

        return (ShowWorked && item.WasWorked)
            || (ShowMissed && isMissed)
            || (ShowSuppressed && isSuppressed)
            || (ShowFailedMismatch && isFailedMismatch)
            || (ShowSeenOnly && isSeenOnly)
            || (ShowCalledOnly && item.WasCalled);
    }

    private static int OutcomeSort(string outcome)
    {
        if (outcome == "In progress")
            return 0;
        if (outcome == "Called")
            return 1;
        if (outcome == "Worked")
            return 2;
        if (outcome.StartsWith("Missed", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (outcome == "Suppressed")
            return 4;
        return 5;
    }
}
