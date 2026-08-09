using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class PskReporterMapPoint
{
    public required string Band { get; init; }
    public required string ReceiverCallsign { get; init; }
    public string ReceiverLocator { get; init; } = "";
    public string ReceiverCountry { get; init; } = "";
    public int? SignalReportDb { get; init; }
    public DateTime TransmissionTimeUtc { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Colour { get; init; } = "#64748B";
    public string SignalDisplay => SignalReportDb.HasValue ? $"{SignalReportDb.Value:+0;-0;0} dB" : "SNR unknown";
    public string Detail => $"{ReceiverCallsign} · {ReceiverLocator} · {Band} · {SignalDisplay} · {TransmissionTimeUtc:HH:mm:ss} UTC"
        + (string.IsNullOrWhiteSpace(ReceiverCountry) ? "" : $" · {ReceiverCountry}");
}

public sealed class PskReporterBandLegendItem
{
    public required string Band { get; init; }
    public required string Colour { get; init; }
    public int UniqueReceivers { get; init; }
    public int PropagationScore { get; init; }
    public string Assessment { get; init; } = "";
    public string Label => $"{Band}: {UniqueReceivers} receiver{(UniqueReceivers == 1 ? "" : "s")} · score {PropagationScore}";
}

public sealed class PskReporterMapViewModel : ObservableObject
{
    private PskReporterMapPoint? _selectedReport;
    private string _status = "Run a PSK propagation survey to plot outward reception reports.";
    private string _homeGrid = "";

    public ObservableCollection<PskReporterMapPoint> Reports { get; } = [];
    public ObservableCollection<PskReporterBandLegendItem> Bands { get; } = [];
    public event EventHandler? MapChanged;

    public PskReporterMapPoint? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetProperty(ref _selectedReport, value))
                MapChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string HomeGrid { get => _homeGrid; private set => SetProperty(ref _homeGrid, value); }
    public bool HasReports => Reports.Count > 0;

    public void Apply(
        IEnumerable<PskReporterSpot> reports,
        IEnumerable<BandAnalysisBandViewModel> bandRows,
        string homeGrid)
    {
        Reports.Clear();
        Bands.Clear();
        HomeGrid = homeGrid?.Trim().ToUpperInvariant() ?? "";

        var rows = bandRows.Where(row => row.PskMeasured).ToList();
        foreach (var row in rows.OrderBy(row => BandOrder(row.Band)))
        {
            Bands.Add(new PskReporterBandLegendItem
            {
                Band = row.Band,
                Colour = BandColour(row.Band),
                UniqueReceivers = row.PskUniqueReceivers,
                PropagationScore = row.PskMetrics.PropagationScore,
                Assessment = row.PskAssessment
            });
        }

        var points = reports
            .Where(report => MaidenheadGrid.TryGetCentre(report.ReceiverLocator, out _, out _))
            .GroupBy(report => $"{report.Band}|{report.ReceiverCallsign}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.SignalReportDb).First())
            .Select(report => ToPoint(report))
            .Where(point => point != null)
            .Cast<PskReporterMapPoint>()
            .OrderBy(point => BandOrder(point.Band))
            .ThenBy(point => point.ReceiverCallsign)
            .ToList();
        foreach (var point in points)
            Reports.Add(point);

        SelectedReport = Reports.FirstOrDefault();
        Status = Reports.Count == 0
            ? "No locator-bearing PSK Reporter reports matched this survey."
            : $"Plotted {Reports.Count} unique receiver-and-band points. Colours identify bands; select a dot for its receiver details.";
        OnPropertyChanged(nameof(HasReports));
        MapChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Reports.Clear();
        Bands.Clear();
        SelectedReport = null;
        Status = "PSK propagation survey in progress; the map will appear after reports are matched.";
        OnPropertyChanged(nameof(HasReports));
        MapChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginSurvey()
    {
        Status = Reports.Count == 0
            ? "PSK propagation survey in progress; the map will appear after reports are matched."
            : $"PSK propagation survey in progress; retaining the previous {Reports.Count}-point map until a newer result is available.";
    }

    public void RetainAfterEmptySurvey()
    {
        Status = Reports.Count == 0
            ? "No locator-bearing PSK Reporter reports have yet been available to plot."
            : $"The latest survey produced no locator-bearing matches; retained the previous {Reports.Count}-point map.";
    }

    public void MarkRestored(DateTime observedAtUtc)
    {
        if (Reports.Count == 0)
            return;
        Status = $"Restored the latest saved PSK map: {Reports.Count} unique receiver-and-band points from {observedAtUtc.ToLocalTime():dd MMM HH:mm}.";
    }

    public static string BandColour(string band) => band.Trim().ToLowerInvariant() switch
    {
        "160m" => "#7C3AED",
        "80m" => "#A855F7",
        "60m" => "#6366F1",
        "40m" => "#2563EB",
        "30m" => "#0891B2",
        "20m" => "#16A34A",
        "17m" => "#65A30D",
        "15m" => "#D97706",
        "12m" => "#EA580C",
        "10m" => "#DC2626",
        "6m" => "#DB2777",
        "2m" => "#475569",
        _ => "#64748B"
    };

    public static int BandOrder(string band)
    {
        for (var index = 0; index < BandAnalysisViewModel.BandButtons.Length; index++)
        {
            if (BandAnalysisViewModel.BandButtons[index].Band.Equals(band, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return int.MaxValue;
    }

    private static PskReporterMapPoint? ToPoint(PskReporterSpot report)
    {
        if (!MaidenheadGrid.TryGetCentre(report.ReceiverLocator, out var latitude, out var longitude))
            return null;
        var index = Math.Max(0, BandOrder(report.Band));
        var angle = index * Math.PI * 2 / Math.Max(1, BandAnalysisViewModel.BandButtons.Length);
        // A tiny display-only offset keeps receivers heard on several bands visible
        // as separate coloured dots at world scale. The displayed locator remains exact.
        longitude += Math.Cos(angle) * 0.12;
        latitude += Math.Sin(angle) * 0.08;
        return new PskReporterMapPoint
        {
            Band = report.Band,
            ReceiverCallsign = report.ReceiverCallsign,
            ReceiverLocator = report.ReceiverLocator,
            ReceiverCountry = report.ReceiverDxcc,
            SignalReportDb = report.SignalReportDb,
            TransmissionTimeUtc = report.TransmissionTimeUtc,
            Latitude = latitude,
            Longitude = longitude,
            Colour = BandColour(report.Band)
        };
    }
}
