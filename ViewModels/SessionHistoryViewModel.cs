using System.Collections.ObjectModel;
using System.Windows.Input;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class SessionHistoryViewModel : ObservableObject
{
    private readonly Dictionary<string, int> _archiveIndexes = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _showHeard = true;
    private bool _showCurrentSessionOnly = true;
    private bool _isViewingArchive;
    private string _searchText = "";
    private bool _isViewActive;
    private bool _refreshPending;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public SessionHistoryViewModel()
    {
        ToggleArchiveCommand = new RelayCommand(ToggleArchive);
    }

    public ObservableCollection<SessionDxOpportunity> AllOpportunities { get; } = new();
    public ObservableCollection<SessionDxOpportunity> ArchiveOpportunities { get; } = new();
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

    public bool ShowHeard
    {
        get => _showHeard;
        set { if (SetProperty(ref _showHeard, value)) Refresh(); }
    }

    public bool IsViewingArchive
    {
        get => _isViewingArchive;
        private set
        {
            if (!SetProperty(ref _isViewingArchive, value))
                return;
            OnPropertyChanged(nameof(ViewHeading));
            OnPropertyChanged(nameof(ViewSubtitle));
            OnPropertyChanged(nameof(ArchiveButtonText));
            Refresh();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? "")) Refresh(); }
    }

    public string ViewHeading => IsViewingArchive ? "Full Archive" : "Current Session";
    public string ViewSubtitle => IsViewingArchive
        ? "Permanently stored records from this and previous DX Pilot runs"
        : "Across every band and mode used during this DX Pilot run";
    public string ArchiveButtonText => IsViewingArchive ? "Current Session" : "Full Archive";

    public ICommand? ExportCommand { get; set; }
    public ICommand? ClearCommand { get; set; }
    public ICommand ToggleArchiveCommand { get; }

    public IReadOnlyList<SessionDxOpportunity> RowsForExport() =>
        (IsViewingArchive ? ArchiveOpportunities : AllOpportunities).ToList();

    public void LoadArchive(IEnumerable<SessionDxOpportunity> entries)
    {
        _archiveIndexes.Clear();
        ArchiveOpportunities.Clear();
        foreach (var entry in entries.OrderBy(entry => entry.SessionStartedUtc).ThenBy(entry => entry.FirstSeenUtc))
        {
            _archiveIndexes[ArchiveKey(entry)] = ArchiveOpportunities.Count;
            ArchiveOpportunities.Add(entry);
        }
        Refresh();
    }

    public void UpsertArchive(SessionDxOpportunity current)
    {
        var archiveId = ArchiveKey(current);
        var snapshot = current.Snapshot();
        if (!_archiveIndexes.TryGetValue(archiveId, out var index))
        {
            _archiveIndexes[archiveId] = ArchiveOpportunities.Count;
            ArchiveOpportunities.Add(snapshot);
        }
        else
            ArchiveOpportunities[index] = snapshot;

        if (IsViewingArchive)
            RequestRefresh();
    }

    public void SetViewActive(bool active)
    {
        if (_isViewActive == active)
            return;

        _isViewActive = active;
        if (active)
            Refresh();
    }

    public void RequestRefresh()
    {
        _refreshPending = true;
    }

    public void RefreshIfDue(DateTime utcNow, TimeSpan minimumInterval)
    {
        if (!_isViewActive || !_refreshPending)
            return;
        if (_lastRefreshUtc != DateTime.MinValue && utcNow - _lastRefreshUtc < minimumInterval)
            return;

        Refresh();
    }

    private void ToggleArchive()
    {
        IsViewingArchive = !IsViewingArchive;
    }

    private static string ArchiveKey(SessionDxOpportunity item) => $"{item.SessionId}|{item.OpportunityId}";

    public void Refresh()
    {
        _refreshPending = false;
        _lastRefreshUtc = DateTime.UtcNow;
        var source = IsViewingArchive ? ArchiveOpportunities : AllOpportunities;
        var rows = source
            .Where(PassesFilter)
            .Where(PassesSearch)
            .OrderBy(NewOrUnconfirmedDxccSort)
            .ThenBy(o => o.UniversalRank ?? int.MaxValue)
            .ThenBy(o => o.PriorityTier)
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

        Status = IsViewingArchive
            ? $"{Opportunities.Count} shown / {ArchiveOpportunities.Count} permanently archived records. Search by call, country, DXCC, grid, state or reason."
            : $"{Opportunities.Count} shown / {AllOpportunities.Count} stations heard or acted on this session. {ArchiveOpportunities.Count} records in Full Archive.";
    }

    private bool PassesFilter(SessionDxOpportunity item)
    {
        var isDxcc = item.DxccStatus is "New DXCC" or "Worked unconfirmed";
        var isGrid = item.Category.Equals("Grid", StringComparison.OrdinalIgnoreCase);
        var isState = item.Category.Equals("USA State", StringComparison.OrdinalIgnoreCase);
        var isRareConfirmed = item.Category.Equals("Rare confirmed DXCC", StringComparison.OrdinalIgnoreCase);
        var isHeard = item.Category is "Heard" or "General" or "Band/mode";
        var passesType = (ShowDxcc && isDxcc)
            || (ShowGrids && isGrid)
            || (ShowUsaStates && isState)
            || (ShowRareConfirmed && isRareConfirmed)
            || (ShowHeard && isHeard);

        var isMissed = item.Outcome.Contains("Missed", StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains("TX mismatch", StringComparison.OrdinalIgnoreCase);
        var isSuppressed = item.Outcome.Contains("Suppressed", StringComparison.OrdinalIgnoreCase);
        var isFailedMismatch = item.Outcome.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains("wrong target", StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains("Failed", StringComparison.OrdinalIgnoreCase);
        var isSeenOnly = item.Outcome == "Seen only";

        // Category filters are strict. Outcome filters are a separate dimension and
        // must not make called/worked records reappear when their category is hidden.
        if (!passesType)
            return false;

        return (ShowWorked && item.WasWorked)
            || (ShowMissed && isMissed)
            || (ShowSuppressed && isSuppressed)
            || (ShowFailedMismatch && isFailedMismatch)
            || (ShowSeenOnly && isSeenOnly)
            || (ShowCalledOnly && item.WasCalled);
    }

    private bool PassesSearch(SessionDxOpportunity item)
    {
        var search = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return item.Call.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Entity.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.DxccNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Grid.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.State.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.PrimaryReason.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Outcome.Contains(search, StringComparison.OrdinalIgnoreCase);
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

    private static int NewOrUnconfirmedDxccSort(SessionDxOpportunity item) =>
        item.DxccStatus is "New DXCC" or "Worked unconfirmed" ? 0 : 1;
}
