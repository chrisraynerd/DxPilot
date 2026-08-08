using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;
using JtdxAutoResume.V3.Views;
using Mapsui;
using BruTile.Web;
using Mapsui.Tiling.Layers;
using System.Reflection;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

Require(MaidenheadGrid.TryGetCentre("IO91wm", out var latitude, out var longitude), "Valid six-character grid was rejected.");
Require(latitude is > 51 and < 52 && longitude is > -1 and < 0, "Grid centre conversion produced the wrong location.");
Require(!MaidenheadGrid.TryGetCentre("ZZ99", out _, out _), "Invalid grid was accepted.");

var squareLabelFactory = typeof(GridMapControl).GetMethod("CreateVisibleSquareLabels", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Detailed Grid4 label factory was not found.");
var squareLabels = (IEnumerable<IFeature>)(squareLabelFactory.Invoke(null, [90, 91, 130, 131])
    ?? throw new InvalidOperationException("Detailed Grid4 label factory returned no result."));
var squareLabelTexts = squareLabels.Select(feature => feature.Data as string).Where(text => text != null).ToList();
Require(squareLabelTexts.Count == 4 && squareLabelTexts.Contains("JN00") && squareLabelTexts.Contains("JN11"), "Visible detailed-square Grid4 labels were not generated correctly.");

using (var defaults = new MapViewModel("IO91WM"))
{
    Require(defaults.AgeLimitMinutes == 2, "Map hide-if-not-heard default is not two minutes.");
    Require(defaults.BasemapId == "OpenStreetMap" && defaults.BasemapOptions.Count == 5,
        "Map basemap selector did not default to OpenStreetMap with all expected choices.");
}

using (var esriDefaults = new MapViewModel("IO91WM", basemapId: "esristreets"))
{
    Require(esriDefaults.BasemapId == "EsriStreets",
        "English Esri World Street selection was not retained by the map model.");
    esriDefaults.BasemapId = "unsupported";
    Require(esriDefaults.BasemapId == "OpenStreetMap", "Unsupported basemap did not fall back safely to OpenStreetMap.");
}

var streetOption = MapViewModel.AvailableBasemaps.Single(option => option.Id == "EsriStreets");
Require(streetOption.TileUrl != null
    && streetOption.TileUrl.Contains("World_Street_Map/MapServer/tile/{z}/{y}/{x}", StringComparison.Ordinal)
    && !streetOption.TileUrl.Contains("token", StringComparison.OrdinalIgnoreCase),
    "Esri World Street is not configured as an anonymous cached map service.");
var esriLayerFactory = typeof(GridMapControl).GetMethod("CreateEsriTileLayer", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Esri tile layer factory was not found.");
using (var esriLayer = (TileLayer)(esriLayerFactory.Invoke(null, [streetOption])
    ?? throw new InvalidOperationException("Esri tile layer factory returned no layer.")))
{
    var esriSource = (HttpTileSource)esriLayer.TileSource;
    Require(esriSource.Schema.GetTileWidth(0) == 256
        && esriSource.Schema.Resolutions.Count >= 20,
        "Esri cached basemap does not use the expected Web Mercator tile schema.");
}

var scopedIndexes = new WorkedStatusIndexes();
var workedGrid = new SimpleWorkedStatus { Id = "JN02", WorkedAny = true, ConfirmedAny = true, LoTWConfirmedAny = true };
workedGrid.WorkedBands.Add("20m");
workedGrid.WorkedModes.Add("FT8");
workedGrid.WorkedBandModes.Add("20M|FT8");
workedGrid.ConfirmedBands.Add("20m");
workedGrid.ConfirmedModes.Add("FT8");
workedGrid.ConfirmedBandModes.Add("20M|FT8");
workedGrid.LoTWConfirmedBands.Add("20m");
workedGrid.LoTWConfirmedModes.Add("FT8");
workedGrid.LoTWConfirmedBandModes.Add("20M|FT8");
scopedIndexes.Grids["JN02"] = workedGrid;
var ft4Grid = new SimpleWorkedStatus { Id = "FN20", WorkedAny = true, ConfirmedAny = true, LoTWConfirmedAny = true };
ft4Grid.LoTWConfirmedBands.Add("15m");
ft4Grid.LoTWConfirmedModes.Add("FT4");
scopedIndexes.Grids["FN20"] = ft4Grid;
Require(MapOpportunityClassifier.LotwConfirmedGridsForScope(scopedIndexes, WantedScope.Overall, "20m", "FT8").Count == 2,
    "All-time LoTW grid-fill scope did not include every confirmed Grid4.");
Require(MapOpportunityClassifier.LotwConfirmedGridsForScope(scopedIndexes, WantedScope.CurrentBand, "20m", "FT4").SequenceEqual(["JN02"]),
    "Current-band LoTW grid-fill scope included a grid confirmed only on another band.");
Require(MapOpportunityClassifier.LotwConfirmedGridsForScope(scopedIndexes, WantedScope.CurrentMode, "15m", "FT4").SequenceEqual(["FN20"]),
    "Current-mode LoTW grid-fill scope included a grid confirmed only in another mode.");
var scopedDecode = new DecodeMessage { ContactableCall = "C31TEST", Grid = "JN02TM", Band = "17m", Mode = "FT8", ReceivedAt = DateTime.Now };
var scopedProfile = MapOpportunityClassifier.Classify(scopedDecode, scopedIndexes);
Require(!scopedProfile.Overall.IsNewGrid, "Grid6 was not matched to its worked Grid4 parent.");
Require(scopedProfile.CurrentBand.IsNewGrid, "Current-band grid need was not identified.");
Require(!scopedProfile.CurrentMode.IsNewGrid, "Worked current-mode grid was incorrectly marked needed.");
Require(scopedProfile.CurrentBandMode.IsNewGrid, "Unworked band-and-mode grid slot was not identified.");

var noLocationProfile = MapOpportunityClassifier.Classify(
    new DecodeMessage
    {
        ContactableCall = "NOGRID",
        Band = "20m",
        Mode = "FT8",
        ReceivedAt = DateTime.Now
    },
    scopedIndexes);
Require(!noLocationProfile.Overall.IsNewGrid,
    "A decode without a locator was not classified safely.");

var japanIndexes = new WorkedStatusIndexes();
japanIndexes.Grids["PM43"] = new SimpleWorkedStatus
{
    Id = "PM43",
    WorkedAny = true,
    PaperConfirmedAny = true,
    ConfirmedAny = true,
    LoTWConfirmedAny = false
};
var japanNewGridProfile = MapOpportunityClassifier.Classify(
    new DecodeMessage
    {
        ContactableCall = "JA6LCJ",
        Grid = "PM52",
        Band = "20m",
        Mode = "FT8",
        ReceivedAt = DateTime.Now
    },
    japanIndexes);
var japanUnconfirmedEffectiveGridProfile = MapOpportunityClassifier.Classify(
    new DecodeMessage
    {
        ContactableCall = "JE6WOQ",
        EffectiveGrid = "PM43",
        QrzGrid = "PM43AB",
        Band = "20m",
        Mode = "FT8",
        ReceivedAt = DateTime.Now
    },
    japanIndexes);
Require(japanNewGridProfile.Overall.IsNewGrid
    && japanUnconfirmedEffectiveGridProfile.Overall.IsNewGrid,
    "New PM52 and non-LoTW-confirmed PM43 did not both retain blue map classification when the second locator came from an effective/QRZ source.");

using (var txMap = new MapViewModel("IO91WM"))
{
    txMap.ObserveDecode(
        new DecodeMessage { ContactableCall = "DXCC1", Grid = "FN20", ReceivedAt = DateTime.Now },
        MapOpportunityProfile.FromOverall(new MapOpportunityFlags(true, false, false, false)));
    txMap.ObserveDecode(
        new DecodeMessage { ContactableCall = "GRID1", Grid = "JN02", ReceivedAt = DateTime.Now },
        MapOpportunityProfile.FromOverall(new MapOpportunityFlags(false, false, true, false)));
    txMap.ObserveDecode(
        new DecodeMessage { ContactableCall = "STATE1", Grid = "EM10", ReceivedAt = DateTime.Now },
        MapOpportunityProfile.FromOverall(new MapOpportunityFlags(false, false, false, true)));
    txMap.ActiveCallsign = "GRID1";
    txMap.SetContactableCallsigns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GRID1" });
    Require(txMap.Stations.Single(station => station.Callsign == "DXCC1").IsNewDxcc,
        "Starting a call cleared another station's DXCC colour.");
    Require(txMap.Stations.Single(station => station.Callsign == "GRID1").IsNewGrid,
        "The red active-call override cleared the target's stored grid classification.");
    Require(txMap.Stations.Single(station => station.Callsign == "STATE1").IsNewState,
        "TX contactability refresh cleared another station's state colour.");

    txMap.Clear();
    Require(txMap.ActiveCallsign == "GRID1" && txMap.Stations.Count == 0,
        "Clearing live map stations incorrectly released the active hunting/QSO target.");
    txMap.ObserveDecode(
        new DecodeMessage { ContactableCall = "GRID1", Grid = "JN02", ReceivedAt = DateTime.Now },
        MapOpportunityProfile.FromOverall(new MapOpportunityFlags(false, false, true, false)));
    Require(txMap.ActiveCallsign == "GRID1" && txMap.Stations.Single().Callsign == "GRID1",
        "The active target was not retained when its station dot was replotted after a map clear.");
}

var markerColourResolver = typeof(GridMapControl).GetMethod("MarkerColourHex", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Semantic marker colour resolver was not found.");
Require((string?)markerColourResolver.Invoke(null, [new MapOpportunityFlags(true, false, false, false)]) == "#A855F7"
    && (string?)markerColourResolver.Invoke(null, [new MapOpportunityFlags(false, false, true, false)]) == "#2563EB"
    && (string?)markerColourResolver.Invoke(null, [new MapOpportunityFlags(false, false, false, true)]) == "#0D9488",
    "Semantic marker colour resolution changed during the TX regression test.");

using var map = new MapViewModel("IO91WM", 15);
Require(map.AgeLimitMinutes == 12, "Map display limit was not clamped to 12 minutes.");
Require(!map.ShowLotwConfirmedGrids, "LoTW confirmed-grid overlay was not opt-in by default.");
Require(map.LotwConfirmedGridScope == WantedScope.Overall && map.LotwGridScopeOptions.Count == 3,
    "LoTW grid-fill scope did not default to the three-choice all-time/band/mode selector.");
Require(map.LotwConfirmedGridOpacityPercent == 25, "LoTW confirmed-grid fill did not use the more visible 25% default.");
map.LotwConfirmedGridOpacityPercent = 1;
Require(map.LotwConfirmedGridOpacityPercent == 5, "LoTW grid-fill opacity did not enforce its 5% minimum.");
map.LotwConfirmedGridOpacityPercent = 99;
Require(map.LotwConfirmedGridOpacityPercent == 50, "LoTW grid-fill opacity did not enforce its 50% maximum.");
map.LotwConfirmedGridOpacityPercent = 25;
map.SetLotwConfirmedGrids(["JN02", "JN02AB", "FN20", "INVALID"]);
Require(map.LotwConfirmedGridCount == 2 && map.LotwConfirmedGrids.SetEquals(["JN02", "FN20"]),
    "LoTW confirmed-grid overlay did not normalize and deduplicate Grid4 locators.");
map.ShowLotwConfirmedGrids = true;
var createConfirmedGridFeatures = typeof(GridMapControl).GetMethod(
    "CreateVisibleConfirmedGridFeatures", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Confirmed Grid4 polygon builder was not found.");
var jn02Features = (System.Collections.ICollection)createConfirmedGridFeatures.Invoke(
    null, [map.LotwConfirmedGrids, 90, 90, 132, 132])!;
Require(jn02Features.Count == 1, "Viewport-limited LoTW overlay did not render exactly the visible confirmed Grid4 square.");
var worldConfirmedFeatures = (System.Collections.ICollection)createConfirmedGridFeatures.Invoke(
    null, [map.LotwConfirmedGrids, 0, 179, 0, 179])!;
Require(worldConfirmedFeatures.Count == map.LotwConfirmedGridCount,
    "World-scale LoTW cache did not retain every confirmed Grid4 polygon.");
var confirmedRasterField = typeof(GridMapControl).GetField("_confirmedGridRasterLayer", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("Raster-cached LoTW confirmation layer was not found.");
Require(confirmedRasterField.FieldType == typeof(Mapsui.Layers.RasterizingLayer),
    "LoTW confirmations are not isolated in the cached raster display layer.");
map.AgeLimitMinutes = 0;
Require(map.AgeLimitMinutes == 1, "Map display limit was not clamped to 1 minute.");
map.AgeLimitMinutes = 12;
map.ObserveDecode(scopedDecode, scopedProfile);
Require(!map.Stations.Single().IsNewGrid, "Overall map scope did not use overall grid status.");
map.ColourScope = WantedScope.CurrentBand;
Require(map.Stations.Single().IsNewGrid && map.VisibleNeededGridCount == 1, "Current-band map scope did not colour the needed Grid4.");
map.ColourGrid = false;
Require(!map.Stations.Single().IsNewGrid && map.VisibleNeededGridCount == 0, "Grid colour switch did not suppress blue status.");
map.ColourGrid = true;
map.ColourScope = WantedScope.Overall;
map.Clear();
map.ObserveDecode(new DecodeMessage
{
    ContactableCall = "G1ABC",
    Grid = "FN20",
    EffectiveGrid = "FN20",
    Band = "20m",
    Mode = "FT8",
    Snr = -12,
    ReceivedAt = DateTime.Now,
    IsNewGrid = true
});
Require(map.StationCount == 1, "First gridded station was not plotted.");
Require(map.Stations[0].Grid == "FN20", "Plotted grid was not normalized.");
Require(map.Stations[0].IsNewGrid, "New-grid classification was not retained.");

var firstLatitude = map.Stations[0].Latitude;
var firstLongitude = map.Stations[0].Longitude;
map.ObserveDecode(new DecodeMessage
{
    ContactableCall = "G1ABC",
    Grid = "FN20",
    EffectiveGrid = "FN20",
    QrzGrid = "FN20ab",
    Band = "20m",
    Mode = "FT8",
    Snr = -10,
    ReceivedAt = DateTime.Now,
    IsNewGrid = true
});
Require(map.Stations[0].Grid == "FN20AB", "A compatible QRZ six-character grid did not refine the plotted four-character grid.");
Require(map.Stations[0].Latitude != firstLatitude || map.Stations[0].Longitude != firstLongitude, "QRZ refinement did not improve map coordinates.");

map.ObserveDecode(new DecodeMessage
{
    ContactableCall = "C31ABC",
    Grid = "JN02",
    TransmittedGrid = "JN02",
    EffectiveGrid = "JN02",
    QrzGrid = "JN02TM",
    QrzLatitude = 42.506,
    QrzLongitude = 1.522,
    QrzGeoLocationSource = "dxcc",
    EntityName = "Andorra",
    ReceivedAt = DateTime.Now
});
var andorra = map.Stations.Single(station => station.Callsign == "C31ABC");
Require(andorra.Latitude == 42.506 && andorra.Longitude == 1.522, "Compatible QRZ coordinates were not used for the map point.");
Require(andorra.LocationSource == "QRZ dxcc", "QRZ coordinate source was not disclosed on the map station.");

map.ObserveDecode(new DecodeMessage
{
    ContactableCall = "EA1ABC",
    Grid = "IN73",
    TransmittedGrid = "IN73",
    EffectiveGrid = "IN73",
    QrzGrid = "JN02TM",
    QrzLatitude = 42.506,
    QrzLongitude = 1.522,
    QrzGeoLocationSource = "user",
    ReceivedAt = DateTime.Now
});
var conflicting = map.Stations.Single(station => station.Callsign == "EA1ABC");
Require(conflicting.Grid == "IN73" && (conflicting.Latitude != 42.506 || conflicting.Longitude != 1.522), "Conflicting QRZ coordinates overrode the transmitted locator.");

var refinedStation = map.Stations.Single(station => station.Callsign == "G1ABC");
var refinedLatitude = refinedStation.Latitude;
var refinedLongitude = refinedStation.Longitude;
map.ObserveDecode(new DecodeMessage
{
    ContactableCall = "G1ABC",
    EntityLatitude = 0,
    EntityLongitude = 0,
    Band = "20m",
    Mode = "FT8",
    Snr = -8,
    ReceivedAt = DateTime.Now,
    IsNewDxcc = true
});
var repeated = map.Stations.Single(station => station.Callsign == "G1ABC");
Require(map.StationCount == 3 && repeated.HeardCount == 3, "Repeated station was duplicated instead of updated.");
Require(repeated.Latitude == refinedLatitude && repeated.Longitude == refinedLongitude, "Precise grid was replaced by an approximate DXCC centre.");
Require(repeated.IsNewGrid && repeated.IsNewDxcc, "Current DXCC status or prior grid status was not preserved across a gridless report.");

map.SetContactableCallsigns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "G1ABC" });
Require(repeated.IsContactable && repeated.ContactabilityText == "Ready" && repeated.ContactActionText == "CALL NOW", "Contactable station was not marked ready for CALL NOW.");
map.SetContactableCallsigns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
Require(!repeated.IsContactable && repeated.ActionStateClass == "NotContactable" && repeated.ContactActionText == "UNABLE TO CONTACT", "Unavailable station was not marked non-contactable with a disabled action label.");
map.ReportContactUnavailable(repeated);
Require(map.Status.Contains("Unable to contact G1ABC", StringComparison.Ordinal), "A rejected map double-click did not expose a clear unable-to-contact status.");

map.ObserveDecode(new DecodeMessage
{
    ContactableCall = "W1OLD",
    Grid = "FN31",
    EffectiveGrid = "FN31",
    ReceivedAt = DateTime.Now.AddMinutes(-20)
});
Require(map.StationCount == 4, "Session history did not retain a stale station.");
Require(map.VisibleStationCount == 3, "Stale station was not hidden from the live layer.");
map.ActiveCallsign = "W1OLD";
Require(map.VisibleStationCount == 4, "Currently called station was hidden by the map display timer.");
map.ActiveCallsign = "";
Require(map.VisibleStationCount == 3, "Released stale target remained visible as an active call.");

map.Clear();
Require(map.StationCount == 0 && map.Stations.Count == 0, "Clear map did not clear session stations.");

Console.WriteLine("PASS: Grid4-normalized map/Wanted status, world-scale raster-cached LoTW confirmations, colour scopes, two-minute default, QRZ refinement, TX-safe semantic colours, contactability, active-call retention, stale filtering and session clearing.");
