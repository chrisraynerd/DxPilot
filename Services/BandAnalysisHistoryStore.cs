using System.IO;
using System.Text.Json;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class BandAnalysisHistoryStore
{
    private readonly string _path;
    private readonly string _csvPath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public BandAnalysisHistoryStore(string appFolder)
    {
        _path = Path.Combine(appFolder, "band_analysis_history.json");
        _csvPath = Path.Combine(appFolder, "band_analysis_history.csv");
    }

    public string HistoryFile => _csvPath;

    public List<BandAnalysisHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return [];

            return JsonSerializer.Deserialize<List<BandAnalysisHistoryEntry>>(File.ReadAllText(_path), _options)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<BandAnalysisHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var cutoff = DateTime.UtcNow.AddDays(-60);
            var bounded = entries
                .Where(entry => entry.ObservedAtUtc >= cutoff)
                .OrderByDescending(entry => entry.ObservedAtUtc)
                .Take(2_000)
                .OrderBy(entry => entry.ObservedAtUtc)
                .ToList();
            File.WriteAllText(_path, JsonSerializer.Serialize(bounded, _options));
            var lines = new List<string>
            {
                "SurveyId,ObservedAtUtc,Automatic,TriggerReason,StartingBand,SelectedBand,Decision,Band,SecondsObserved,UniqueStations,CqCallers,NewDxccStations,WantedStations,ActivityScore,DxReachScore,Reach80Miles,MainArea,Assessment,PskMeasured,PskReports,PskUniqueReceivers,PskUniqueCountries,PskFarthestMiles,PskMedianSnr,PskPropagationScore,PskMainArea,PskAssessment,CompletedComparable,WorkabilityScore,PskViabilityPercent,PathMatchPercent,DistinctWantedOpportunities,WorkableWantedOpportunities,ProductivityAdjustment,WorkabilityAssessment,WorkabilityDetail"
            };
            lines.AddRange(bounded.Select(entry => string.Join(",",
                Csv(entry.SurveyId),
                Csv(entry.ObservedAtUtc.ToString("O")),
                entry.Automatic,
                Csv(entry.TriggerReason),
                Csv(entry.StartingBand),
                Csv(entry.SelectedBand),
                Csv(entry.Decision),
                Csv(entry.Band),
                entry.SecondsObserved,
                entry.UniqueStations,
                entry.CqCallers,
                entry.NewDxccStations,
                entry.WantedStations,
                entry.ActivityScore,
                entry.DxReachScore,
                entry.EightiethPercentileDistanceMiles?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                Csv(entry.MainArea),
                Csv(entry.Assessment),
                entry.PskMeasured,
                entry.PskReports,
                entry.PskUniqueReceivers,
                entry.PskUniqueCountries,
                entry.PskFarthestDistanceMiles?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                entry.PskMedianSnr?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                entry.PskPropagationScore,
                Csv(entry.PskMainArea),
                Csv(entry.PskAssessment),
                entry.CompletedComparableAnalysis,
                entry.WorkabilityScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                entry.PskViabilityPercent,
                entry.PathMatchPercent,
                entry.DistinctWantedOpportunities,
                entry.WorkableWantedOpportunities,
                entry.ProductivityAdjustment.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                Csv(entry.WorkabilityAssessment),
                Csv(entry.WorkabilityDetail))));
            File.WriteAllLines(_csvPath, lines);
        }
        catch
        {
        }
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
