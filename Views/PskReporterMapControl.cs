using System.Windows;
using System.Windows.Controls;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;
using MapBrush = Mapsui.Styles.Brush;
using MapColor = Mapsui.Styles.Color;
using MapPen = Mapsui.Styles.Pen;

namespace JtdxAutoResume.V3.Views;

public sealed class PskReporterMapControl : MapControl
{
    private const string UserAgent = "DXPilot-for-JTDX-G1CEC/3.9.0 (PSK propagation map)";
    private readonly MemoryLayer _reportLayer;
    private readonly MemoryLayer _homeLayer;
    private PskReporterMapViewModel? _subscribedModel;
    private DateTime _lastHoverCheckUtc = DateTime.MinValue;
    private string _hoveredReceiver = "";

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(PskReporterMapViewModel), typeof(PskReporterMapControl),
        new FrameworkPropertyMetadata(null, ModelChanged));

    public PskReporterMapControl()
    {
        Map = new Mapsui.Map { BackColor = MapColor.FromString("#DCE8EE") };
        Map.Layers.Add(OpenStreetMap.CreateTileLayer(UserAgent));
        _homeLayer = new MemoryLayer("Survey station") { Style = null, Features = Array.Empty<IFeature>() };
        _reportLayer = new MemoryLayer("PSK Reporter receivers") { Style = null, Features = Array.Empty<IFeature>() };
        Map.Layers.Add(_homeLayer);
        Map.Layers.Add(_reportLayer);
        MapTapped += OnMapTapped;
        MapPointerMoved += OnMapPointerMoved;
        Loaded += (_, _) =>
        {
            Map.Navigator.ZoomToLevel(1);
            RefreshMap();
        };
        ToolTipService.SetInitialShowDelay(this, 120);
        ToolTipService.SetShowDuration(this, 15000);
    }

    public PskReporterMapViewModel? Model
    {
        get => (PskReporterMapViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void ModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PskReporterMapControl control)
            return;
        if (control._subscribedModel != null)
            control._subscribedModel.MapChanged -= control.OnMapChanged;
        control._subscribedModel = args.NewValue as PskReporterMapViewModel;
        if (control._subscribedModel != null)
            control._subscribedModel.MapChanged += control.OnMapChanged;
        control.RefreshMap();
    }

    private void OnMapChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.CheckAccess())
            RefreshMap();
        else
            Dispatcher.BeginInvoke(RefreshMap);
    }

    private void RefreshMap()
    {
        if (Model == null)
            return;
        _reportLayer.Features = Model.Reports.Select(CreateReportFeature).ToList();
        _homeLayer.Features = CreateHomeFeature(Model.HomeGrid);
        _reportLayer.DataHasChanged();
        _homeLayer.DataHasChanged();
        Map.RefreshGraphics();
    }

    private IFeature CreateReportFeature(PskReporterMapPoint report)
    {
        var feature = new PointFeature(SphericalMercator.FromLonLat(report.Longitude, report.Latitude).ToMPoint())
        {
            Data = report
        };
        var selected = ReferenceEquals(report, Model?.SelectedReport);
        feature.Styles.Add(new SymbolStyle
        {
            Fill = new MapBrush(MapColor.FromString(report.Colour)),
            Outline = new MapPen(selected ? MapColor.White : MapColor.FromString("#172331"), selected ? 3 : 1.25f),
            SymbolScale = selected ? 0.75 : 0.58,
            Opacity = 0.92f
        });
        return feature;
    }

    private static IEnumerable<IFeature> CreateHomeFeature(string homeGrid)
    {
        if (!MaidenheadGrid.TryGetCentre(homeGrid, out var latitude, out var longitude))
            return Array.Empty<IFeature>();
        var feature = new PointFeature(SphericalMercator.FromLonLat(longitude, latitude).ToMPoint());
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Triangle,
            Fill = new MapBrush(MapColor.FromString("#FACC15")),
            Outline = new MapPen(MapColor.FromString("#172331"), 1.5f),
            SymbolScale = 0.78
        });
        return [feature];
    }

    private void OnMapTapped(object? sender, MapEventArgs args)
    {
        var mapInfo = GetMapInfo(args.ScreenPosition, [_reportLayer]);
        if (mapInfo?.Feature?.Data is PskReporterMapPoint report && Model != null)
            Model.SelectedReport = report;
    }

    private void OnMapPointerMoved(object? sender, MapEventArgs args)
    {
        var now = DateTime.UtcNow;
        if (now - _lastHoverCheckUtc < TimeSpan.FromMilliseconds(90))
            return;
        _lastHoverCheckUtc = now;
        var mapInfo = GetMapInfo(args.ScreenPosition, [_reportLayer]);
        var report = mapInfo?.Feature?.Data as PskReporterMapPoint;
        var key = report == null ? "" : $"{report.Band}|{report.ReceiverCallsign}";
        if (key.Equals(_hoveredReceiver, StringComparison.OrdinalIgnoreCase))
            return;
        _hoveredReceiver = key;
        ToolTip = report?.Detail;
    }
}
