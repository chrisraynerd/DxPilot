using System.IO;
using System.Text.Json;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class PskReporterMapStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PskReporterMapStore(string appFolder)
    {
        _path = Path.Combine(appFolder, "latest_psk_report_map.json");
    }

    public string MapFile => _path;

    public PskReporterMapSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            var snapshot = JsonSerializer.Deserialize<PskReporterMapSnapshot>(File.ReadAllText(_path), _options);
            return snapshot is { Reports.Count: > 0 } ? snapshot : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string homeGrid, IEnumerable<PskReporterSpot> reports)
    {
        try
        {
            var retained = reports
                .Where(report => MaidenheadGrid.TryGetCentre(report.ReceiverLocator, out _, out _))
                .GroupBy(report => $"{report.Band}|{report.ReceiverCallsign}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(report => report.SignalReportDb).First())
                .OrderBy(report => report.Band, StringComparer.OrdinalIgnoreCase)
                .ThenBy(report => report.ReceiverCallsign, StringComparer.OrdinalIgnoreCase)
                .Take(2_000)
                .ToList();
            if (retained.Count == 0)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var snapshot = new PskReporterMapSnapshot
            {
                ObservedAtUtc = DateTime.UtcNow,
                HomeGrid = homeGrid?.Trim().ToUpperInvariant() ?? "",
                Reports = retained
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, _options));
        }
        catch
        {
        }
    }
}
