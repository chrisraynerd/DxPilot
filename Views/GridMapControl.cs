using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Wpf;
using NetTopologySuite.Geometries;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;
using MapBrush = Mapsui.Styles.Brush;
using MapColor = Mapsui.Styles.Color;
using MapPen = Mapsui.Styles.Pen;

namespace JtdxAutoResume.V3.Views;

public sealed class MapStationDoubleClickedEventArgs : EventArgs
{
    public MapStationDoubleClickedEventArgs(MapStationViewModel station)
    {
        Station = station;
    }

    public MapStationViewModel Station { get; }
}

/// <summary>
/// Retained-layer world map. OpenStreetMap tiles, Maidenhead geometry and live
/// stations are separate layers so a decode update never rebuilds the base map.
/// </summary>
public sealed class GridMapControl : MapControl
{
    private const string UserAgent = "DXPilot-for-JTDX-G1CEC/3.4.3 (amateur-radio companion)";
    private const string OsmAttribution = "© OpenStreetMap contributors";
    private const string EsriAttribution = "Esri, HERE, Garmin, USGS, Intermap, INCREMENT P, NRCan, Esri Japan, METI, © OpenStreetMap contributors, and the GIS User Community";
    private ILayer _baseLayer;
    private readonly MemoryLayer _fieldGridLayer;
    private readonly MemoryLayer _squareGridLayer;
    private readonly MemoryLayer _confirmedGridLayer;
    private readonly RasterizingLayer _confirmedGridRasterLayer;
    private readonly MemoryLayer _squareLabelLayer;
    private readonly MemoryLayer _fieldLabelLayer;
    private readonly MemoryLayer _pathLayer;
    private readonly MemoryLayer _stationLayer;
    private readonly MemoryLayer _homeLayer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _squareLabelRefreshTimer;
    private MapViewModel? _subscribedModel;
    private bool _stationRefreshPending;
    private bool _lastShowSquares;
    private bool _lastShowLotwConfirmedGrids;
    private int _lastConfirmedGridVersion = -1;
    private int _confirmedGridCacheVersion = -1;
    private double _lastConfirmedGridOpacityPercent = -1;
    private bool _initialViewApplied;
    private DateTime _lastHoverCheckUtc = DateTime.MinValue;
    private string _hoveredCallsign = "";
    private string _lastRequestedBasemapId = "";

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MapViewModel), typeof(GridMapControl),
        new FrameworkPropertyMetadata(null, ModelChanged));

    public event EventHandler<MapStationDoubleClickedEventArgs>? StationDoubleClicked;

    public GridMapControl()
    {
        Map = new Mapsui.Map { BackColor = MapColor.FromString("#DCE8EE") };
        _baseLayer = OpenStreetMap.CreateTileLayer(UserAgent);
        Map.Layers.Add(_baseLayer);

        _confirmedGridLayer = new MemoryLayer("LoTW confirmed Grid4 squares")
        {
            Style = new VectorStyle
            {
                Fill = new MapBrush(MapColor.FromString("#EF4444")),
                Outline = null,
                Opacity = 0.25f
            },
            Features = Array.Empty<IFeature>(),
            Enabled = true
        };
        _confirmedGridRasterLayer = new RasterizingLayer(_confirmedGridLayer, delayBeforeRasterize: 160)
        {
            Name = "Cached LoTW confirmed Grid4 squares",
            Enabled = false
        };
        _fieldGridLayer = CreateLineLayer("Maidenhead fields", CreateGridLines(20, 10), "#27657E", 1.15f, 0.78f);
        _squareGridLayer = CreateLineLayer("Maidenhead squares", CreateGridLines(2, 1), "#4A7890", 0.55f, 0.43f);
        _squareGridLayer.Enabled = false;
        _squareLabelLayer = new MemoryLayer("Maidenhead square names")
        {
            Style = null,
            Features = Array.Empty<IFeature>(),
            Enabled = false
        };
        _fieldLabelLayer = CreateFieldLabelLayer();
        _pathLayer = new MemoryLayer("Radio paths") { Style = null, Features = Array.Empty<IFeature>() };
        _homeLayer = new MemoryLayer("Home") { Style = null, Features = Array.Empty<IFeature>() };
        _stationLayer = new MemoryLayer("Stations")
        {
            Style = null,
            Features = Array.Empty<IFeature>()
        };

        // The confirmation wash sits immediately above the map tiles. Grid
        // lines, names, radio paths and station dots remain fully legible above it.
        Map.Layers.Add(_confirmedGridRasterLayer);
        Map.Layers.Add(_squareGridLayer);
        Map.Layers.Add(_fieldGridLayer);
        Map.Layers.Add(_fieldLabelLayer);
        Map.Layers.Add(_squareLabelLayer);
        Map.Layers.Add(_pathLayer);
        Map.Layers.Add(_homeLayer);
        Map.Layers.Add(_stationLayer);

        MapTapped += OnMapTapped;
        MapPointerMoved += OnMapPointerMoved;
        PreviewMouseLeftButtonDown += OnMapMouseLeftButtonDown;
        PreviewMouseWheel += (_, _) => ScheduleSquareLabelRefresh();
        SizeChanged += (_, _) => ScheduleSquareLabelRefresh();
        ToolTipService.SetInitialShowDelay(this, 150);
        ToolTipService.SetShowDuration(this, 12000);
        ToolTipService.SetBetweenShowDelay(this, 50);
        Loaded += (_, _) => ApplyInitialView();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _stationRefreshPending)
                ScheduleRefresh();
        };
        _refreshTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _refreshTimer.Tick += (_, _) => FlushPendingRefresh();
        _squareLabelRefreshTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _squareLabelRefreshTimer.Tick += (_, _) => RefreshVisibleSquareLabels();
    }

    public MapViewModel? Model
    {
        get => (MapViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public void ShowWorld()
    {
        Map.Navigator.ZoomToLevel(1);
        ScheduleSquareLabelRefresh();
    }

    private static void ModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not GridMapControl control)
            return;
        if (control._subscribedModel != null)
            control._subscribedModel.MapChanged -= control.OnModelMapChanged;
        control._subscribedModel = args.NewValue as MapViewModel;
        if (control._subscribedModel != null)
            control._subscribedModel.MapChanged += control.OnModelMapChanged;
        control.ScheduleRefresh();
        control.ApplyInitialView();
    }

    private void OnModelMapChanged(object? sender, EventArgs e)
    {
        // Decode batches can contain dozens of messages. Coalesce them into one
        // retained-layer replacement rather than blocking the UDP/UI dispatcher.
        if (Dispatcher.CheckAccess())
            ScheduleRefresh();
        else
            Dispatcher.BeginInvoke(ScheduleRefresh);
    }

    private void ScheduleRefresh()
    {
        _stationRefreshPending = true;
        if (IsVisible && !_refreshTimer.IsEnabled)
            _refreshTimer.Start();
    }

    private void FlushPendingRefresh()
    {
        _refreshTimer.Stop();
        if (!_stationRefreshPending || Model == null)
            return;

        _stationRefreshPending = false;
        try
        {
            FlushPendingRefreshCore();
        }
        catch (Exception ex)
        {
            Model.ReportMapError(ex.GetBaseException().Message);
            System.Diagnostics.Debug.WriteLine($"Map layer refresh failed: {ex}");
        }
    }

    private void FlushPendingRefreshCore()
    {
        if (Model == null)
            return;

        RefreshBasemap(Model);

        var showSquares = Model.ShowGridSquares;
        if (showSquares != _lastShowSquares)
        {
            _squareGridLayer.Enabled = showSquares;
            _squareLabelLayer.Enabled = showSquares;
            _lastShowSquares = showSquares;
            ScheduleSquareLabelRefresh();
        }

        var showConfirmedGrids = Model.ShowLotwConfirmedGrids;
        if (showConfirmedGrids != _lastShowLotwConfirmedGrids)
        {
            _confirmedGridRasterLayer.Enabled = showConfirmedGrids;
            _lastShowLotwConfirmedGrids = showConfirmedGrids;
            if (showConfirmedGrids)
                ScheduleConfirmedGridCacheRefresh(Model);
            Map.Refresh();
        }
        if (_lastConfirmedGridVersion != Model.ConfirmedGridVersion)
        {
            _lastConfirmedGridVersion = Model.ConfirmedGridVersion;
            if (showConfirmedGrids)
                ScheduleConfirmedGridCacheRefresh(Model);
        }
        if (Math.Abs(_lastConfirmedGridOpacityPercent - Model.LotwConfirmedGridOpacityPercent) > 0.01)
        {
            _lastConfirmedGridOpacityPercent = Model.LotwConfirmedGridOpacityPercent;
            if (_confirmedGridLayer.Style is VectorStyle confirmedStyle)
                confirmedStyle.Opacity = (float)(Model.LotwConfirmedGridOpacityPercent / 100d);
            _confirmedGridLayer.DataHasChanged();
        }

        var cutoff = DateTime.Now.AddMinutes(-Model.AgeLimitMinutes);
        var visible = Model.Stations
            .Where(station => station.LastHeard >= cutoff
                || station.Callsign.Equals(Model.ActiveCallsign, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(station => station.LastHeard)
            .ToList();

        // Mapsui draws later features on top. Render ordinary markers first and
        // semantic priorities last so a New Grid cannot be hidden by an orange
        // station sharing the same four-character Maidenhead locator.
        _stationLayer.Features = visible
            .OrderBy(MarkerPriority)
            .ThenBy(station => station.LastHeard)
            .Select(CreateStationFeature)
            .ToList();
        _homeLayer.Features = CreateHomeFeatures(Model.HomeGrid);
        _pathLayer.Features = Model.ShowPaths
            ? CreatePathFeatures(
                Model.HomeGrid,
                visible.Take(20)
                    .Concat(visible.Where(station => station.Callsign.Equals(Model.ActiveCallsign, StringComparison.OrdinalIgnoreCase)))
                    .DistinctBy(station => station.Callsign),
                Model.ActiveCallsign)
            : Array.Empty<IFeature>();

        _stationLayer.DataHasChanged();
        _homeLayer.DataHasChanged();
        _pathLayer.DataHasChanged();
        Map.RefreshGraphics();
    }

    private void RefreshBasemap(MapViewModel model)
    {
        var requestedId = MapViewModel.NormalizeBasemapId(model.BasemapId);
        if (requestedId.Equals(_lastRequestedBasemapId, StringComparison.Ordinal))
            return;

        _lastRequestedBasemapId = requestedId;
        var option = MapViewModel.AvailableBasemaps.First(item => item.Id.Equals(requestedId, StringComparison.Ordinal));

        ILayer replacement;
        string status;
        string attribution;
        if (string.IsNullOrWhiteSpace(option.TileUrl))
        {
            replacement = OpenStreetMap.CreateTileLayer(UserAgent);
            status = option.Label;
            attribution = OsmAttribution;
        }
        else
        {
            try
            {
                var esriLayer = CreateEsriTileLayer(option);
                esriLayer.DataChanged += (_, args) =>
                {
                    if (args.Error != null)
                        Dispatcher.BeginInvoke(() => FallBackAfterEsriTileError(esriLayer, option.Label));
                };
                replacement = esriLayer;
                status = $"{option.Label} selected.";
                attribution = EsriAttribution;
            }
            catch
            {
                replacement = OpenStreetMap.CreateTileLayer(UserAgent);
                status = $"Could not start {option.Label}; showing OpenStreetMap.";
                attribution = OsmAttribution;
            }
        }

        var oldLayer = _baseLayer;
        Map.Layers.Remove(oldLayer);
        Map.Layers.Insert(0, replacement);
        _baseLayer = replacement;
        if (oldLayer is IDisposable disposable)
            disposable.Dispose();
        model.ReportBasemapState(status, attribution);
    }

    private void FallBackAfterEsriTileError(TileLayer failedLayer, string label)
    {
        if (!ReferenceEquals(_baseLayer, failedLayer))
            return;

        var fallback = OpenStreetMap.CreateTileLayer(UserAgent);
        Map.Layers.Remove(failedLayer);
        Map.Layers.Insert(0, fallback);
        _baseLayer = fallback;
        failedLayer.Dispose();
        Model?.ReportBasemapState($"{label} could not load. Check the internet connection; showing OpenStreetMap.", OsmAttribution);
        Map.RefreshGraphics();
    }

    private static TileLayer CreateEsriTileLayer(MapBasemapOption option)
    {
        var source = new HttpTileSource(
            new GlobalSphericalMercator(),
            option.TileUrl!,
            name: option.Label,
            attribution: new Attribution(EsriAttribution, "https://www.esri.com/en-us/legal/terms/full-master-agreement"),
            configureHttpRequestMessage: request => request.Headers.TryAddWithoutValidation("User-Agent", UserAgent));
        return new TileLayer(source) { Name = option.Label };
    }

    private IFeature CreateStationFeature(MapStationViewModel station)
    {
        var point = SphericalMercator.FromLonLat(station.Longitude, station.Latitude).ToMPoint();
        var feature = new PointFeature(point) { Data = station };
        var selected = ReferenceEquals(station, Model?.SelectedStation);
        var active = station.Callsign.Equals(Model?.ActiveCallsign, StringComparison.OrdinalIgnoreCase);
        var flags = MarkerFlags(station);
        feature.Styles.Add(new SymbolStyle
        {
            Fill = new MapBrush(MapColor.FromString(active ? "#EF4444" : MarkerColourHex(flags))),
            Outline = new MapPen(selected ? MapColor.White : MapColor.FromString("#172331"), selected ? 3 : 1.25f),
            SymbolScale = active ? 0.78 : selected ? 0.72 : flags.IsNewDxcc ? 0.62 : 0.52,
            Opacity = AgeOpacity(station)
        });

        if (!station.IsContactable)
        {
            feature.Styles.Add(new SymbolStyle
            {
                Fill = new MapBrush(MapColor.FromString("#05080C")),
                Outline = null,
                SymbolScale = selected ? 0.35 : 0.27,
                Opacity = AgeOpacity(station)
            });
        }

        if (Model?.ShowLabels == true || selected)
        {
            var label = new LabelStyle
            {
                Text = station.Callsign,
                ForeColor = MapColor.FromString("#172331"),
                BackColor = new MapBrush(MapColor.White),
                BorderColor = MapColor.FromString("#475B6B"),
                BorderThickness = 1,
                CornerRounding = 3,
                Offset = new Offset(12, -10),
                CollisionDetection = true
            };
            if (!selected)
                label.MaxVisible = 5000;
            feature.Styles.Add(label);
        }
        return feature;
    }

    private float AgeOpacity(MapStationViewModel station)
    {
        var ageMinutes = Math.Max(0, (DateTime.Now - station.LastHeard).TotalMinutes);
        return (float)Math.Clamp(1 - ageMinutes / Math.Max(1, Model?.AgeLimitMinutes ?? 3) * 0.65, 0.35, 1);
    }

    private static IEnumerable<IFeature> CreateHomeFeatures(string homeGrid)
    {
        if (!MaidenheadGrid.TryGetCentre(homeGrid, out var latitude, out var longitude))
            return Array.Empty<IFeature>();
        var point = SphericalMercator.FromLonLat(longitude, latitude).ToMPoint();
        var feature = new PointFeature(point);
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Triangle,
            Fill = new MapBrush(MapColor.FromString("#FACC15")),
            Outline = new MapPen(MapColor.FromString("#172331"), 1.5f),
            SymbolScale = 0.7
        });
        return [feature];
    }

    private static IEnumerable<IFeature> CreatePathFeatures(
        string homeGrid,
        IEnumerable<MapStationViewModel> stations,
        string activeCallsign)
    {
        if (!MaidenheadGrid.TryGetCentre(homeGrid, out var homeLat, out var homeLon))
            return Array.Empty<IFeature>();
        var home = SphericalMercator.FromLonLat(homeLon, homeLat);
        var features = new List<IFeature>();
        foreach (var station in stations.OrderBy(station =>
                     station.Callsign.Equals(activeCallsign, StringComparison.OrdinalIgnoreCase)))
        {
            var active = station.Callsign.Equals(activeCallsign, StringComparison.OrdinalIgnoreCase);
            var target = SphericalMercator.FromLonLat(station.Longitude, station.Latitude);
            var geometry = new LineString([
                new Coordinate(home.x, home.y),
                new Coordinate(target.x, target.y)
            ]);
            var feature = new GeometryFeature { Geometry = geometry };
            feature.Styles.Add(new VectorStyle
            {
                Opacity = active ? 0.92f : 0.35f,
                Line = new MapPen(MapColor.FromString(active ? "#EF4444" : "#83C5E8"), active ? 3 : 1)
            });
            features.Add(feature);
        }
        return features;
    }

    private static MemoryLayer CreateLineLayer(string name, IEnumerable<IFeature> features, string colour, float width, float opacity)
    {
        return new MemoryLayer(name)
        {
            Style = new VectorStyle
            {
                Opacity = opacity,
                Line = new MapPen(MapColor.FromString(colour), width)
            },
            Features = features.ToList()
        };
    }

    private static IEnumerable<IFeature> CreateGridLines(int longitudeStep, int latitudeStep)
    {
        var features = new List<IFeature>();
        for (var longitude = -180 + longitudeStep; longitude < 180; longitude += longitudeStep)
            features.Add(CreateGeoLine(longitude, -85.0, longitude, 85.0));
        for (var latitude = -80; latitude <= 80; latitude += latitudeStep)
            features.Add(CreateGeoLine(-180.0, latitude, 180.0, latitude));
        return features;
    }

    private static IFeature CreateGeoLine(double lon1, double lat1, double lon2, double lat2)
    {
        var from = SphericalMercator.FromLonLat(lon1, lat1);
        var to = SphericalMercator.FromLonLat(lon2, lat2);
        return new GeometryFeature
        {
            Geometry = new LineString([
                new Coordinate(from.x, from.y),
                new Coordinate(to.x, to.y)
            ])
        };
    }

    private static MemoryLayer CreateFieldLabelLayer()
    {
        var features = new List<IFeature>();
        for (var x = 0; x < 18; x++)
        for (var y = 1; y < 17; y++)
        {
            var centre = SphericalMercator.FromLonLat(-170 + x * 20, -85 + y * 10).ToMPoint();
            var field = new PointFeature(centre);
            field.Styles.Add(new LabelStyle
            {
                Text = $"{(char)('A' + x)}{(char)('A' + y)}",
                ForeColor = MapColor.FromString("#345365"),
                BackColor = null,
                CollisionDetection = false
            });
            features.Add(field);
        }
        return new MemoryLayer("Maidenhead field names")
        {
            Style = null,
            Features = features,
            MaxVisible = 30000
        };
    }

    private void ScheduleSquareLabelRefresh()
    {
        if (Model is not { } model
            || !model.ShowGridSquares
            || !IsVisible)
            return;
        _squareLabelRefreshTimer.Stop();
        _squareLabelRefreshTimer.Start();
    }

    private void RefreshVisibleSquareLabels()
    {
        _squareLabelRefreshTimer.Stop();
        if (Model == null
            || !Model.ShowGridSquares
            || Map?.Navigator?.Viewport == null)
            return;

        try
        {
            var extent = Map.Navigator.Viewport.ToExtent();
            var southWest = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
            var northEast = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);
            var minimumLongitude = Math.Clamp(Math.Min(southWest.Item1, northEast.Item1), -180, 180);
            var maximumLongitude = Math.Clamp(Math.Max(southWest.Item1, northEast.Item1), -180, 180);
            var minimumLatitude = Math.Clamp(Math.Min(southWest.Item2, northEast.Item2), -90, 90);
            var maximumLatitude = Math.Clamp(Math.Max(southWest.Item2, northEast.Item2), -90, 90);

            var minimumLongitudeCell = Math.Clamp((int)Math.Floor((minimumLongitude + 180) / 2), 0, 179);
            var maximumLongitudeCell = Math.Clamp((int)Math.Floor((maximumLongitude + 180) / 2), 0, 179);
            var minimumLatitudeCell = Math.Clamp((int)Math.Floor(minimumLatitude + 90), 0, 179);
            var maximumLatitudeCell = Math.Clamp((int)Math.Floor(maximumLatitude + 90), 0, 179);
            var cellCount = (maximumLongitudeCell - minimumLongitudeCell + 1)
                * (maximumLatitudeCell - minimumLatitudeCell + 1);

            // At a world-scale view the labels would be unreadable. Keeping a
            // hard visible-cell ceiling prevents a pan/zoom from constructing
            // the complete 32,400-label Maidenhead grid.
            var features = !Model.ShowGridSquares || cellCount > 1200
                ? new List<IFeature>()
                : CreateVisibleSquareLabels(
                    minimumLongitudeCell, maximumLongitudeCell,
                    minimumLatitudeCell, maximumLatitudeCell);
            _squareLabelLayer.Features = features;
            _squareLabelLayer.DataHasChanged();

            Map.RefreshGraphics();
        }
        catch (Exception ex)
        {
            Model.ReportMapError(ex.GetBaseException().Message);
        }
    }

    private void ScheduleConfirmedGridCacheRefresh(MapViewModel model)
    {
        var version = model.ConfirmedGridVersion;
        if (_confirmedGridCacheVersion == version)
            return;

        _confirmedGridCacheVersion = version;
        var grids = model.LotwConfirmedGrids.ToArray();
        _ = BuildConfirmedGridCacheAsync(grids, version);
    }

    private async Task BuildConfirmedGridCacheAsync(IReadOnlyCollection<string> grids, int version)
    {
        try
        {
            var features = await Task.Run(() => CreateVisibleConfirmedGridFeatures(
                grids.ToHashSet(StringComparer.OrdinalIgnoreCase),
                0, 179, 0, 179));
            await Dispatcher.InvokeAsync(() =>
            {
                if (Model?.ConfirmedGridVersion != version || _confirmedGridCacheVersion != version)
                    return;

                _confirmedGridLayer.Features = features;
                _confirmedGridLayer.DataHasChanged();
                Map.Refresh();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_confirmedGridCacheVersion == version)
                    _confirmedGridCacheVersion = -1;
                Model?.ReportMapError(ex.GetBaseException().Message);
            });
        }
    }

    private static List<IFeature> CreateVisibleSquareLabels(
        int minimumLongitudeCell,
        int maximumLongitudeCell,
        int minimumLatitudeCell,
        int maximumLatitudeCell)
    {
        var features = new List<IFeature>();
        for (var longitudeCell = minimumLongitudeCell; longitudeCell <= maximumLongitudeCell; longitudeCell++)
        for (var latitudeCell = minimumLatitudeCell; latitudeCell <= maximumLatitudeCell; latitudeCell++)
        {
            var fieldLongitude = longitudeCell / 10;
            var fieldLatitude = latitudeCell / 10;
            var longitudeDigit = longitudeCell % 10;
            var latitudeDigit = latitudeCell % 10;
            var label = $"{(char)('A' + fieldLongitude)}{(char)('A' + fieldLatitude)}{longitudeDigit}{latitudeDigit}";
            var centreLongitude = -179 + longitudeCell * 2;
            var centreLatitude = -89.5 + latitudeCell;
            var point = new PointFeature(SphericalMercator.FromLonLat(centreLongitude, centreLatitude).ToMPoint())
            {
                Data = label
            };
            point.Styles.Add(new LabelStyle
            {
                Text = label,
                ForeColor = MapColor.FromString("#526C7A"),
                BackColor = null,
                CollisionDetection = false,
                Opacity = 0.48f
            });
            features.Add(point);
        }
        return features;
    }

    private static List<IFeature> CreateVisibleConfirmedGridFeatures(
        IReadOnlySet<string> confirmedGrids,
        int minimumLongitudeCell,
        int maximumLongitudeCell,
        int minimumLatitudeCell,
        int maximumLatitudeCell)
    {
        const double mercatorLatitudeLimit = 85.05112878;
        var features = new List<IFeature>();
        foreach (var value in confirmedGrids)
        {
            var grid = MaidenheadGrid.Normalize(value).Grid4;
            if (grid.Length != 4)
                continue;

            var longitudeCell = (grid[0] - 'A') * 10 + (grid[2] - '0');
            var latitudeCell = (grid[1] - 'A') * 10 + (grid[3] - '0');
            if (longitudeCell < minimumLongitudeCell || longitudeCell > maximumLongitudeCell
                || latitudeCell < minimumLatitudeCell || latitudeCell > maximumLatitudeCell)
            {
                continue;
            }

            var west = -180 + longitudeCell * 2;
            var east = west + 2;
            var rawSouth = -90 + latitudeCell;
            var rawNorth = rawSouth + 1;
            var south = Math.Clamp(rawSouth, -mercatorLatitudeLimit, mercatorLatitudeLimit);
            var north = Math.Clamp(rawNorth, -mercatorLatitudeLimit, mercatorLatitudeLimit);
            if (north <= south)
                continue;

            var southWest = SphericalMercator.FromLonLat(west, south);
            var southEast = SphericalMercator.FromLonLat(east, south);
            var northEast = SphericalMercator.FromLonLat(east, north);
            var northWest = SphericalMercator.FromLonLat(west, north);
            features.Add(new GeometryFeature
            {
                Data = grid,
                Geometry = new Polygon(new LinearRing([
                    new Coordinate(southWest.x, southWest.y),
                    new Coordinate(southEast.x, southEast.y),
                    new Coordinate(northEast.x, northEast.y),
                    new Coordinate(northWest.x, northWest.y),
                    new Coordinate(southWest.x, southWest.y)
                ]))
            });
        }

        return features;
    }

    private void ApplyInitialView()
    {
        if (_initialViewApplied || !IsLoaded || Map?.Navigator == null)
            return;
        _initialViewApplied = true;
        if (Model != null && MaidenheadGrid.TryGetCentre(Model.HomeGrid, out var latitude, out var longitude))
        {
            var home = SphericalMercator.FromLonLat(longitude, latitude).ToMPoint();
            Map.Navigator.CenterOnAndZoomTo(home, Map.Navigator.Resolutions[3]);
        }
        else
        {
            Map.Navigator.ZoomToLevel(1);
        }
    }

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        var mapInfo = GetMapInfo(e.ScreenPosition, [_stationLayer]);
        if (mapInfo?.Feature?.Data is MapStationViewModel station && Model != null)
            Model.SelectedStation = station;
    }

    private void OnMapMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        var position = e.GetPosition(this);
        var mapInfo = GetMapInfo(new ScreenPosition(position.X, position.Y), [_stationLayer]);
        if (mapInfo?.Feature?.Data is not MapStationViewModel station || Model == null)
            return;

        Model.SelectedStation = station;
        StationDoubleClicked?.Invoke(this, new MapStationDoubleClickedEventArgs(station));
        e.Handled = true;
    }

    private void OnMapPointerMoved(object? sender, MapEventArgs e)
    {
        ScheduleSquareLabelRefresh();
        var now = DateTime.UtcNow;
        if (now - _lastHoverCheckUtc < TimeSpan.FromMilliseconds(90))
            return;
        _lastHoverCheckUtc = now;

        var mapInfo = GetMapInfo(e.ScreenPosition, [_stationLayer]);
        var station = mapInfo?.Feature?.Data as MapStationViewModel;
        var callsign = station?.Callsign ?? "";
        if (callsign.Equals(_hoveredCallsign, StringComparison.OrdinalIgnoreCase))
            return;
        _hoveredCallsign = callsign;
        ToolTip = station == null
            ? null
            : $"{station.Callsign}{(station.Callsign.Equals(Model?.ActiveCallsign, StringComparison.OrdinalIgnoreCase) ? " — CURRENTLY CALLING" : "")}\nDXCC: {(string.IsNullOrWhiteSpace(station.Country) ? "Unknown" : station.Country)}\n{station.Grid} · {station.Band} {station.Mode} · {station.Snr:+0;-0;0} dB\n{(station.IsContactable ? "Contactable now" : "Not currently contactable")}";
    }

    private int MarkerPriority(MapStationViewModel station)
    {
        if (station.Callsign.Equals(Model?.ActiveCallsign, StringComparison.OrdinalIgnoreCase)) return 20;
        if (ReferenceEquals(station, Model?.SelectedStation)) return 10;
        var flags = MarkerFlags(station);
        if (flags.IsNewDxcc) return 5;
        if (flags.IsUnconfirmedDxcc) return 4;
        if (flags.IsNewGrid) return 3;
        if (flags.IsNewState) return 2;
        return 1;
    }

    private MapOpportunityFlags MarkerFlags(MapStationViewModel station)
    {
        var flags = station.OpportunityProfile.ForScope(Model?.ColourScope ?? WantedScope.Overall);
        return flags with
        {
            IsNewDxcc = Model?.ColourDxcc == true && flags.IsNewDxcc,
            IsUnconfirmedDxcc = Model?.ColourDxcc == true && flags.IsUnconfirmedDxcc,
            IsNewGrid = Model?.ColourGrid == true && flags.IsNewGrid,
            IsNewState = Model?.ColourState == true && flags.IsNewState
        };
    }

    private static string MarkerColourHex(MapOpportunityFlags flags) => flags switch
    {
        { IsNewDxcc: true } => "#A855F7",
        { IsUnconfirmedDxcc: true } => "#D8B4FE",
        { IsNewGrid: true } => "#2563EB",
        { IsNewState: true } => "#0D9488",
        _ => "#F59E0B"
    };
}
