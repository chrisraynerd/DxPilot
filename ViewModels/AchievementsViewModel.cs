using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class AchievementsViewModel : ObservableObject
{
    private readonly DxccAchievementCollator _collator = new();
    private IReadOnlyList<AdifQso> _allQsos = Array.Empty<AdifQso>();
    private IReadOnlyList<AdifQso> _profileQsos = Array.Empty<AdifQso>();
    private IReadOnlyList<SessionDxOpportunity> _history = Array.Empty<SessionDxOpportunity>();
    private IReadOnlyList<DxccEntityDefinition> _entities = Array.Empty<DxccEntityDefinition>();
    private DxccResolver? _resolver;
    private DxccRarityService? _rarityService;
    private string _selectedProfileKey = StationCallsignIdentity.AllCallsignsKey;
    private string _searchText = "";
    private string _selectedStatusFilter = "All entities";
    private int _totalEntities;
    private int _lotwConfirmedEntities;
    private int _workedUnconfirmedEntities;
    private int _neededEntities;
    private int _profileQsoCount;
    private DateTime _lastRefreshed;

    public ObservableCollection<CallsignLogProfile> Profiles { get; } = new();
    public ObservableCollection<AchievementDxccRow> DxccRows { get; } = new();
    public IReadOnlyList<string> StatusFilters { get; } =
    [
        "All entities", "Needed", "Worked — awaiting LoTW", "LoTW confirmed"
    ];

    public string SelectedProfileKey
    {
        get => _selectedProfileKey;
        set
        {
            var requested = string.IsNullOrWhiteSpace(value)
                ? StationCallsignIdentity.AllCallsignsKey
                : value.Trim().ToUpperInvariant();
            if (!SetProperty(ref _selectedProfileKey, requested))
                return;
            Refresh();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? ""))
                return;
            ApplyVisibleFilter();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (!SetProperty(ref _selectedStatusFilter, value ?? "All entities"))
                return;
            ApplyVisibleFilter();
        }
    }

    public int TotalEntities { get => _totalEntities; private set => SetProperty(ref _totalEntities, value); }
    public int LotwConfirmedEntities { get => _lotwConfirmedEntities; private set => SetProperty(ref _lotwConfirmedEntities, value); }
    public int WorkedUnconfirmedEntities { get => _workedUnconfirmedEntities; private set => SetProperty(ref _workedUnconfirmedEntities, value); }
    public int NeededEntities { get => _neededEntities; private set => SetProperty(ref _neededEntities, value); }
    public int ProfileQsoCount { get => _profileQsoCount; private set => SetProperty(ref _profileQsoCount, value); }
    public DateTime LastRefreshed { get => _lastRefreshed; private set { if (SetProperty(ref _lastRefreshed, value)) OnPropertyChanged(nameof(LastRefreshedDisplay)); } }
    public string LastRefreshedDisplay => LastRefreshed == DateTime.MinValue ? "Not refreshed" : $"Refreshed {LastRefreshed:dd MMM yyyy HH:mm:ss}";
    public string ProgressDisplay => TotalEntities == 0 ? "0%" : $"{LotwConfirmedEntities * 100d / TotalEntities:0.0}%";
    public string VisibleSummary => $"Showing {DxccRows.Count:N0} of {TotalEntities:N0} current DXCC entities";
    public string ProfileSummary
    {
        get
        {
            var label = Profiles.FirstOrDefault(profile => profile.Key.Equals(SelectedProfileKey, StringComparison.OrdinalIgnoreCase))?.DisplayLabel
                ?? SelectedProfileKey;
            return $"Display only: {label}. This selection does not change Wanted Sniper, DX Assist or TX behaviour.";
        }
    }

    public void UpdateData(
        IReadOnlyList<AdifQso> allQsos,
        IReadOnlyList<SessionDxOpportunity> history,
        IReadOnlyList<CallsignLogProfile> profiles,
        IReadOnlyList<DxccEntityDefinition> entities,
        DxccResolver resolver,
        DxccRarityService rarityService,
        string defaultProfileKey)
    {
        _allQsos = allQsos;
        _history = history;
        _entities = entities;
        _resolver = resolver;
        _rarityService = rarityService;

        var previousProfile = SelectedProfileKey;
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);

        var selected = Profiles.Any(profile => profile.Key.Equals(previousProfile, StringComparison.OrdinalIgnoreCase))
            ? previousProfile
            : Profiles.Any(profile => profile.Key.Equals(defaultProfileKey, StringComparison.OrdinalIgnoreCase))
                ? defaultProfileKey
                : StationCallsignIdentity.AllCallsignsKey;
        _selectedProfileKey = selected;
        OnPropertyChanged(nameof(SelectedProfileKey));
        Refresh();
    }

    public AchievementDxccDetailViewModel BuildQsoDetails(AchievementDxccRow row)
    {
        var profile = Profiles.FirstOrDefault(item => item.Key.Equals(SelectedProfileKey, StringComparison.OrdinalIgnoreCase));
        var profileDisplay = profile?.DisplayLabel ?? SelectedProfileKey;
        var qsos = _resolver == null
            ? Array.Empty<AchievementQsoDetail>()
            : _collator.BuildQsoDetails(row.DxccNumber, _profileQsos, _entities, _resolver);
        return new AchievementDxccDetailViewModel
        {
            Entity = row,
            Qsos = qsos,
            ProfileDisplay = profileDisplay
        };
    }

    private void Refresh()
    {
        if (_resolver == null || _rarityService == null)
            return;

        _profileQsos = SelectedProfileKey.Equals(StationCallsignIdentity.AllCallsignsKey, StringComparison.OrdinalIgnoreCase)
            ? _allQsos
            : _allQsos.Where(qso => StationCallsignIdentity.Matches(qso.StationCallsign, SelectedProfileKey)).ToList();
        var rows = _collator.Build(_profileQsos, _history, _entities, _resolver, _rarityService);
        _allRows = rows;
        TotalEntities = rows.Count;
        LotwConfirmedEntities = rows.Count(row => row.StatusKey == "LotwConfirmed");
        WorkedUnconfirmedEntities = rows.Count(row => row.StatusKey == "WorkedUnconfirmed");
        NeededEntities = rows.Count(row => row.StatusKey == "Needed");
        ProfileQsoCount = _profileQsos.Count;
        LastRefreshed = DateTime.Now;
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ProfileSummary));
        ApplyVisibleFilter();
    }

    private IReadOnlyList<AchievementDxccRow> _allRows = Array.Empty<AchievementDxccRow>();

    private void ApplyVisibleFilter()
    {
        var search = SearchText.Trim();
        var filtered = _allRows.Where(row => MatchesStatus(row) && MatchesSearch(row, search));
        DxccRows.Clear();
        foreach (var row in filtered)
            DxccRows.Add(row);
        OnPropertyChanged(nameof(VisibleSummary));
    }

    private bool MatchesStatus(AchievementDxccRow row)
    {
        return SelectedStatusFilter switch
        {
            "Needed" => row.StatusKey == "Needed",
            "Worked — awaiting LoTW" => row.StatusKey == "WorkedUnconfirmed",
            "LoTW confirmed" => row.StatusKey == "LotwConfirmed",
            _ => true
        };
    }

    private static bool MatchesSearch(AchievementDxccRow row, string search)
    {
        return string.IsNullOrWhiteSpace(search)
            || row.EntityName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.DxccNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.DifficultyBand.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
