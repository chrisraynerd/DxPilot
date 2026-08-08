using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class MapStationViewModel : ObservableObject
{
    private string _grid = "";
    private string _country = "";
    private string _band = "";
    private string _mode = "";
    private int _snr;
    private DateTime _lastHeard;
    private int _heardCount;
    private bool _isNewDxcc;
    private bool _isUnconfirmedDxcc;
    private bool _isNewGrid;
    private bool _isNewState;
    private bool _isContactable;
    private string _locationSource = "Grid";

    public required string Callsign { get; init; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public MapOpportunityProfile OpportunityProfile { get; set; }

    public string Grid { get => _grid; set => SetProperty(ref _grid, value); }
    public string Country { get => _country; set => SetProperty(ref _country, value); }
    public string Band { get => _band; set => SetProperty(ref _band, value); }
    public string Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public int Snr { get => _snr; set => SetProperty(ref _snr, value); }
    public DateTime LastHeard { get => _lastHeard; set { if (SetProperty(ref _lastHeard, value)) OnPropertyChanged(nameof(Age)); } }
    public int HeardCount { get => _heardCount; set => SetProperty(ref _heardCount, value); }
    public bool IsNewDxcc { get => _isNewDxcc; set { if (SetProperty(ref _isNewDxcc, value)) OnPropertyChanged(nameof(OpportunityClass)); } }
    public bool IsUnconfirmedDxcc { get => _isUnconfirmedDxcc; set { if (SetProperty(ref _isUnconfirmedDxcc, value)) OnPropertyChanged(nameof(OpportunityClass)); } }
    public bool IsNewGrid { get => _isNewGrid; set { if (SetProperty(ref _isNewGrid, value)) OnPropertyChanged(nameof(OpportunityClass)); } }
    public bool IsNewState { get => _isNewState; set { if (SetProperty(ref _isNewState, value)) OnPropertyChanged(nameof(OpportunityClass)); } }
    public bool IsContactable
    {
        get => _isContactable;
        set
        {
            if (SetProperty(ref _isContactable, value))
            {
                OnPropertyChanged(nameof(ContactabilityText));
                OnPropertyChanged(nameof(ContactActionText));
                OnPropertyChanged(nameof(ActionStateClass));
            }
        }
    }
    public string ContactabilityText => IsContactable ? "Ready" : "No";
    public string ContactActionText => IsContactable ? "CALL NOW" : "UNABLE TO CONTACT";
    public string OpportunityClass => IsNewDxcc ? "NewDxcc"
        : IsUnconfirmedDxcc ? "UnconfirmedDxcc"
        : IsNewGrid ? "NewGrid"
        : IsNewState ? "NewState"
        : "";
    public string ActionStateClass => IsContactable ? "" : "NotContactable";
    public string LocationSource { get => _locationSource; set => SetProperty(ref _locationSource, value); }
    public string Age => FormatAge(DateTime.Now - LastHeard);

    public void RefreshAge() => OnPropertyChanged(nameof(Age));

    public void ApplyOpportunityColours(WantedScope scope, bool showDxcc, bool showGrid, bool showState)
    {
        var flags = OpportunityProfile.ForScope(scope);
        IsNewDxcc = showDxcc && flags.IsNewDxcc;
        IsUnconfirmedDxcc = showDxcc && flags.IsUnconfirmedDxcc;
        IsNewGrid = showGrid && flags.IsNewGrid;
        IsNewState = showState && flags.IsNewState;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60)
            return $"{Math.Max(0, (int)age.TotalSeconds)}s";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalHours}h";
    }
}

public sealed class MapViewModel : ObservableObject, IDisposable
{
    private const int MaximumStations = 500;
    private readonly Dictionary<string, MapStationViewModel> _byCall = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _ageTimer;
    private string _homeGrid;
    private string _status = "Waiting for a JTDX decode containing a grid square.";
    private string _activeCallsign = "";
    private MapStationViewModel? _selectedStation;
    private bool _showPaths = true;
    private bool _showLabels = true;
    private bool _showGridSquares;
    private bool _showLotwConfirmedGrids;
    private double _lotwConfirmedGridOpacityPercent = 25;
    private WantedScope _lotwConfirmedGridScope = WantedScope.Overall;
    private readonly HashSet<string> _lotwConfirmedGrids = new(StringComparer.OrdinalIgnoreCase);
    private int _confirmedGridVersion;
    private double _ageLimitMinutes = 2;
    private WantedScope _colourScope = WantedScope.Overall;
    private bool _colourDxcc = true;
    private bool _colourGrid = true;
    private bool _colourState = true;

    public MapViewModel(
        string homeGrid,
        double ageLimitMinutes = 2,
        bool showPaths = true,
        bool showLabels = true,
        bool showGridSquares = false,
        string colourScope = "Overall",
        bool colourDxcc = true,
        bool colourGrid = true,
        bool colourState = true,
        bool showLotwConfirmedGrids = false,
        double lotwConfirmedGridOpacityPercent = 25,
        string lotwConfirmedGridScope = "Overall")
    {
        _homeGrid = homeGrid?.Trim().ToUpperInvariant() ?? "";
        _ageLimitMinutes = Math.Clamp(Math.Round(ageLimitMinutes), 1, 12);
        _showPaths = showPaths;
        _showLabels = showLabels;
        _showGridSquares = showGridSquares;
        _colourScope = Enum.TryParse<WantedScope>(colourScope, true, out var parsedScope)
            ? parsedScope
            : WantedScope.Overall;
        _colourDxcc = colourDxcc;
        _colourGrid = colourGrid;
        _colourState = colourState;
        _showLotwConfirmedGrids = showLotwConfirmedGrids;
        _lotwConfirmedGridOpacityPercent = Math.Clamp(Math.Round(lotwConfirmedGridOpacityPercent / 5) * 5, 5, 50);
        _lotwConfirmedGridScope = Enum.TryParse<WantedScope>(lotwConfirmedGridScope, true, out var parsedLotwScope)
            && parsedLotwScope is WantedScope.Overall or WantedScope.CurrentBand or WantedScope.CurrentMode
                ? parsedLotwScope
                : WantedScope.Overall;
        ClearCommand = new RelayCommand(Clear);
        _ageTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _ageTimer.Tick += (_, _) =>
        {
            foreach (var station in Stations.Take(300))
                station.RefreshAge();
            OnPropertyChanged(nameof(VisibleStationCount));
            OnPropertyChanged(nameof(VisibleNeededGridCount));
            MapChanged?.Invoke(this, EventArgs.Empty);
        };
        _ageTimer.Start();
    }

    public ObservableCollection<MapStationViewModel> Stations { get; } = new();
    public IReadOnlyList<MapColourScopeOption> ColourScopeOptions { get; } =
    [
        new(WantedScope.Overall, "Overall"),
        new(WantedScope.CurrentBand, "Current band"),
        new(WantedScope.CurrentMode, "Current mode"),
        new(WantedScope.CurrentBandMode, "Band + mode")
    ];
    public IReadOnlyList<MapColourScopeOption> LotwGridScopeOptions { get; } =
    [
        new(WantedScope.Overall, "All time"),
        new(WantedScope.CurrentBand, "On current band"),
        new(WantedScope.CurrentMode, "On current mode")
    ];
    public ICommand ClearCommand { get; }
    public event EventHandler? MapChanged;

    public string HomeGrid
    {
        get => _homeGrid;
        set
        {
            var normalized = value?.Trim().ToUpperInvariant() ?? "";
            if (SetProperty(ref _homeGrid, normalized))
                RaiseMapChanged();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public int StationCount => Stations.Count;
    public int VisibleStationCount => Stations.Count(station =>
        DateTime.Now - station.LastHeard <= TimeSpan.FromMinutes(AgeLimitMinutes)
        || station.Callsign.Equals(ActiveCallsign, StringComparison.OrdinalIgnoreCase));
    public int VisibleNeededGridCount => Stations
        .Where(IsVisible)
        .Where(station => station.IsNewGrid)
        .Select(station => MaidenheadGrid.Normalize(station.Grid).Grid4)
        .Where(grid => !string.IsNullOrWhiteSpace(grid))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    public IReadOnlySet<string> LotwConfirmedGrids => _lotwConfirmedGrids;
    public int LotwConfirmedGridCount => _lotwConfirmedGrids.Count;
    public int ConfirmedGridVersion => _confirmedGridVersion;

    public WantedScope ColourScope
    {
        get => _colourScope;
        set
        {
            if (!SetProperty(ref _colourScope, value))
                return;
            RefreshOpportunityColours();
        }
    }

    public bool ColourDxcc
    {
        get => _colourDxcc;
        set { if (SetProperty(ref _colourDxcc, value)) RefreshOpportunityColours(); }
    }

    public bool ColourGrid
    {
        get => _colourGrid;
        set { if (SetProperty(ref _colourGrid, value)) RefreshOpportunityColours(); }
    }

    public bool ColourState
    {
        get => _colourState;
        set { if (SetProperty(ref _colourState, value)) RefreshOpportunityColours(); }
    }

    public string ActiveCallsign
    {
        get => _activeCallsign;
        set
        {
            var normalized = value?.Trim().ToUpperInvariant() ?? "";
            if (!SetProperty(ref _activeCallsign, normalized))
                return;
            // A target lock changes only the active marker override. Reapply the
            // stored opportunity profile so no transient TX/contactability state
            // can leak into the other markers or table rows.
            foreach (var station in Stations)
                station.ApplyOpportunityColours(ColourScope, ColourDxcc, ColourGrid, ColourState);
            OnPropertyChanged(nameof(VisibleStationCount));
            OnPropertyChanged(nameof(VisibleNeededGridCount));
            RaiseMapChanged();
        }
    }

    public MapStationViewModel? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (SetProperty(ref _selectedStation, value))
                RaiseMapChanged();
        }
    }

    public bool ShowPaths
    {
        get => _showPaths;
        set { if (SetProperty(ref _showPaths, value)) RaiseMapChanged(); }
    }

    public bool ShowLabels
    {
        get => _showLabels;
        set { if (SetProperty(ref _showLabels, value)) RaiseMapChanged(); }
    }

    public bool ShowGridSquares
    {
        get => _showGridSquares;
        set { if (SetProperty(ref _showGridSquares, value)) RaiseMapChanged(); }
    }

    public bool ShowLotwConfirmedGrids
    {
        get => _showLotwConfirmedGrids;
        set { if (SetProperty(ref _showLotwConfirmedGrids, value)) RaiseMapChanged(); }
    }

    public double LotwConfirmedGridOpacityPercent
    {
        get => _lotwConfirmedGridOpacityPercent;
        set
        {
            var limited = Math.Clamp(Math.Round(value / 5) * 5, 5, 50);
            if (SetProperty(ref _lotwConfirmedGridOpacityPercent, limited))
                RaiseMapChanged();
        }
    }

    public WantedScope LotwConfirmedGridScope
    {
        get => _lotwConfirmedGridScope;
        set
        {
            var supported = value is WantedScope.Overall or WantedScope.CurrentBand or WantedScope.CurrentMode
                ? value
                : WantedScope.Overall;
            SetProperty(ref _lotwConfirmedGridScope, supported);
        }
    }

    public double AgeLimitMinutes
    {
        get => _ageLimitMinutes;
        set
        {
            var limited = Math.Clamp(Math.Round(value), 1, 12);
            if (!SetProperty(ref _ageLimitMinutes, limited))
                return;
            OnPropertyChanged(nameof(VisibleStationCount));
            OnPropertyChanged(nameof(VisibleNeededGridCount));
            RaiseMapChanged();
        }
    }

    public void ObserveDecode(DecodeMessage decode, MapOpportunityProfile? opportunityProfile = null)
    {
        var call = FirstNonBlank(decode.ContactableCall, decode.Callsign, decode.GridOwnerCall, decode.HeardCall);
        if (string.IsNullOrWhiteSpace(call))
            return;

        var key = call.Trim().ToUpperInvariant();
        _byCall.TryGetValue(key, out var station);
        var existingProfile = station?.OpportunityProfile;
        var currentProfile = opportunityProfile ?? MapOpportunityProfile.FromOverall(new MapOpportunityFlags(
            decode.IsNewDxcc,
            decode.IsUnconfirmedDxcc,
            decode.IsNewGrid,
            decode.IsNewState));
        var hasCurrentGrid = MaidenheadGrid.Normalize(FirstValidGrid(
            decode.EffectiveGrid, decode.TransmittedGrid, decode.Grid, decode.AdifGrid, decode.QrzGrid)).IsValid;
        var hasCurrentState = !string.IsNullOrWhiteSpace(decode.State);
        var grid = BestGridForMap(decode, key);
        double latitude;
        double longitude;
        var source = "Grid";
        if (TryGetQrzPointForMap(decode, key, out latitude, out longitude, out source))
        {
            var qrzGrid = MaidenheadGrid.Normalize(decode.QrzGrid);
            if (qrzGrid.IsValid)
                grid = string.IsNullOrWhiteSpace(qrzGrid.Grid6) ? qrzGrid.Grid4 : qrzGrid.Grid6;
        }
        else if (!string.IsNullOrWhiteSpace(grid) && MaidenheadGrid.TryGetCentre(grid, out latitude, out longitude))
        {
            grid = string.IsNullOrWhiteSpace(MaidenheadGrid.Normalize(grid).Grid6)
                ? MaidenheadGrid.Normalize(grid).Grid4
                : MaidenheadGrid.Normalize(grid).Grid6;
        }
        else if (station != null && !string.IsNullOrWhiteSpace(station.Grid))
        {
            // Do not replace a previously heard precise grid with a later report
            // that can only be placed at the much less precise DXCC centre.
            latitude = station.Latitude;
            longitude = station.Longitude;
            grid = station.Grid;
            source = station.LocationSource;
        }
        else if (decode.EntityLatitude.HasValue && decode.EntityLongitude.HasValue)
        {
            latitude = decode.EntityLatitude.Value;
            longitude = decode.EntityLongitude.Value;
            grid = "";
            source = "DXCC centre";
        }
        else
        {
            return;
        }

        if (station == null)
        {
            station = new MapStationViewModel { Callsign = key };
            _byCall[key] = station;
            Stations.Insert(0, station);
        }

        station.Latitude = Math.Clamp(latitude, -90, 90);
        station.Longitude = NormalizeLongitude(longitude);
        station.Grid = grid;
        station.Country = FirstNonBlank(decode.CountryDisplay, decode.ContactableEntity, decode.PrimaryDisplayEntity);
        station.Band = decode.Band;
        station.Mode = decode.Mode;
        station.Snr = decode.Snr;
        station.LastHeard = decode.ReceivedAt;
        station.HeardCount++;
        station.OpportunityProfile = existingProfile.HasValue
            ? MapOpportunityProfile.MergeMissingLocationCategories(
                existingProfile.Value, currentProfile, hasCurrentGrid, hasCurrentState)
            : currentProfile;
        station.ApplyOpportunityColours(ColourScope, ColourDxcc, ColourGrid, ColourState);
        station.LocationSource = source;
        while (Stations.Count > MaximumStations)
        {
            var removed = Stations[^1];
            Stations.RemoveAt(Stations.Count - 1);
            _byCall.Remove(removed.Callsign);
        }

        Status = $"Last plotted {key} at {(string.IsNullOrWhiteSpace(grid) ? source : grid)} · {station.Band} {station.Mode} · {station.Snr:+0;-0;0} dB";
        OnPropertyChanged(nameof(StationCount));
        OnPropertyChanged(nameof(VisibleStationCount));
        OnPropertyChanged(nameof(VisibleNeededGridCount));
        RaiseMapChanged();
    }

    public void Clear()
    {
        ClearLiveStations("Map cleared. Waiting for new JTDX decodes.");
    }

    public void ClearForBandChange(string previousBand, string newBand)
    {
        ClearLiveStations($"Band changed from {previousBand} to {newBand}. Live map stations cleared; LoTW confirmed-grid shading retained.");
    }

    private void ClearLiveStations(string status)
    {
        _byCall.Clear();
        Stations.Clear();
        SelectedStation = null;
        ActiveCallsign = "";
        Status = status;
        OnPropertyChanged(nameof(StationCount));
        OnPropertyChanged(nameof(VisibleStationCount));
        OnPropertyChanged(nameof(VisibleNeededGridCount));
        RaiseMapChanged();
    }

    public void SetLotwConfirmedGrids(IEnumerable<string> grids)
    {
        var normalized = grids
            .Select(MaidenheadGrid.Normalize)
            .Where(grid => grid.IsValid)
            .Select(grid => grid.Grid4)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_lotwConfirmedGrids.SetEquals(normalized))
            return;

        _lotwConfirmedGrids.Clear();
        _lotwConfirmedGrids.UnionWith(normalized);
        _confirmedGridVersion++;
        OnPropertyChanged(nameof(LotwConfirmedGridCount));
        RaiseMapChanged();
    }

    public void ReportMapError(string message)
    {
        Status = $"Map display issue: {message}";
    }

    public void ReportContactUnavailable(MapStationViewModel station)
    {
        SelectedStation = station;
        Status = $"Unable to contact {station.Callsign}: the station is stale or no longer has a live selectable JTDX row/UDP Reply source.";
    }

    public void SetContactableCallsigns(IReadOnlySet<string> callsigns)
    {
        var changed = false;
        foreach (var station in Stations)
        {
            var contactable = callsigns.Contains(station.Callsign);
            if (station.IsContactable == contactable)
                continue;
            station.IsContactable = contactable;
            changed = true;
        }

        if (changed)
            RaiseMapChanged();
    }

    public void Dispose() => _ageTimer.Stop();

    private void RaiseMapChanged() => MapChanged?.Invoke(this, EventArgs.Empty);

    private bool IsVisible(MapStationViewModel station) =>
        DateTime.Now - station.LastHeard <= TimeSpan.FromMinutes(AgeLimitMinutes)
        || station.Callsign.Equals(ActiveCallsign, StringComparison.OrdinalIgnoreCase);

    private void RefreshOpportunityColours()
    {
        foreach (var station in Stations)
            station.ApplyOpportunityColours(ColourScope, ColourDxcc, ColourGrid, ColourState);
        OnPropertyChanged(nameof(VisibleNeededGridCount));
        RaiseMapChanged();
    }

    private static string FirstValidGrid(params string[] values)
    {
        foreach (var value in values)
        {
            var normalized = MaidenheadGrid.Normalize(value ?? "");
            if (normalized.IsValid)
                return string.IsNullOrWhiteSpace(normalized.Grid6) ? normalized.Grid4 : normalized.Grid6;
        }
        return "";
    }

    private static string BestGridForMap(DecodeMessage decode, string callsign)
    {
        var primary = FirstValidGrid(decode.EffectiveGrid, decode.TransmittedGrid, decode.Grid, decode.AdifGrid);
        var primaryNormalized = MaidenheadGrid.Normalize(primary);
        var qrzNormalized = MaidenheadGrid.Normalize(decode.QrzGrid);

        // A QRZ six-character locator is a useful refinement of a four-character
        // FT8 locator when both agree on the same parent square. Do not use a
        // fixed QRZ address for portable/mobile calls, and never override a
        // six-character locator transmitted by the station.
        if (!CallsignNormalizer.IsPotentiallyPortable(callsign)
            && !string.IsNullOrWhiteSpace(qrzNormalized.Grid6)
            && string.IsNullOrWhiteSpace(primaryNormalized.Grid6)
            && (!primaryNormalized.IsValid || primaryNormalized.Grid4.Equals(qrzNormalized.Grid4, StringComparison.OrdinalIgnoreCase)))
        {
            return qrzNormalized.Grid6;
        }

        if (primaryNormalized.IsValid)
            return string.IsNullOrWhiteSpace(primaryNormalized.Grid6) ? primaryNormalized.Grid4 : primaryNormalized.Grid6;
        return qrzNormalized.IsValid
            ? string.IsNullOrWhiteSpace(qrzNormalized.Grid6) ? qrzNormalized.Grid4 : qrzNormalized.Grid6
            : "";
    }

    private static bool TryGetQrzPointForMap(
        DecodeMessage decode,
        string callsign,
        out double latitude,
        out double longitude,
        out string source)
    {
        latitude = 0;
        longitude = 0;
        source = "Grid";
        if (CallsignNormalizer.IsPotentiallyPortable(callsign)
            || decode.QrzLatitude is not double qrzLatitude
            || decode.QrzLongitude is not double qrzLongitude
            || !double.IsFinite(qrzLatitude)
            || !double.IsFinite(qrzLongitude)
            || qrzLatitude is < -90 or > 90
            || qrzLongitude is < -180 or > 180)
        {
            return false;
        }

        // Treat a transmitted/session/ADIF locator as the operational truth.
        // QRZ may refine a four-character square only when its own locator is
        // in the same parent square, and it never overrides a six-character
        // locator supplied over the air.
        var primary = FirstValidGrid(
            decode.TransmittedGrid,
            decode.EffectiveGridSource == DecodeGridSource.Qrz ? "" : decode.EffectiveGrid,
            decode.GridSource.Equals("QRZ", StringComparison.OrdinalIgnoreCase) ? "" : decode.Grid,
            decode.AdifGrid);
        var primaryGrid = MaidenheadGrid.Normalize(primary);
        var qrzGrid = MaidenheadGrid.Normalize(decode.QrzGrid);
        if (!string.IsNullOrWhiteSpace(primaryGrid.Grid6)
            || (primaryGrid.IsValid
                && (!qrzGrid.IsValid
                    || !primaryGrid.Grid4.Equals(qrzGrid.Grid4, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        latitude = qrzLatitude;
        longitude = qrzLongitude;
        source = string.IsNullOrWhiteSpace(decode.QrzGeoLocationSource)
            ? "QRZ coordinates"
            : $"QRZ {decode.QrzGeoLocationSource}";
        return true;
    }

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static double NormalizeLongitude(double longitude)
    {
        while (longitude > 180) longitude -= 360;
        while (longitude < -180) longitude += 360;
        return longitude;
    }
}
