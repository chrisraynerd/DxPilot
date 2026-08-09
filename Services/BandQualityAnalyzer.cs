using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class BandQualityAnalyzer
{
    private const double DistantMiles = 2_500;
    private const double LongDxMiles = 5_000;

    private static readonly IReadOnlyDictionary<string, string> ContinentNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AF"] = "Africa",
            ["AN"] = "Antarctica",
            ["AS"] = "Asia",
            ["EU"] = "Europe",
            ["NA"] = "North America",
            ["OC"] = "Oceania",
            ["SA"] = "South America"
        };

    public BandQualitySnapshot Analyze(string band, IEnumerable<DecodeMessage> source)
    {
        var decodes = source.Where(d => d.Band.Equals(band, StringComparison.OrdinalIgnoreCase)).ToList();
        var stations = decodes
            .Select(d => new { Decode = d, Call = ObservationCall(d) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Call))
            .GroupBy(item => item.Call, StringComparer.OrdinalIgnoreCase)
            .Select(group => Representative(group.Select(item => item.Decode)))
            .ToList();

        var distances = stations
            .Where(d => d.DistanceMiles.HasValue)
            .Select(d => d.DistanceMiles!.Value)
            .OrderBy(value => value)
            .ToList();
        var continents = stations
            .Where(d => !string.IsNullOrWhiteSpace(d.Continent))
            .GroupBy(d => d.Continent.Trim().ToUpperInvariant())
            .OrderByDescending(group => group.Count())
            .ToList();

        var uniqueCount = stations.Count;
        var cqCount = stations.Count(d => d.IsCq);
        var distantCount = stations.Count(d => d.DistanceMiles >= DistantMiles);
        var longDxCount = stations.Count(d => d.DistanceMiles >= LongDxMiles);
        var wantedCount = stations.Count(IsWanted);
        var newDxccCount = stations.Count(d => d.IsNewDxcc);
        var p80 = Percentile(distances, 0.80);
        double? farthest = distances.Count == 0 ? null : distances[^1];
        var medianSnr = Median(stations.Select(d => (double)d.Snr).OrderBy(value => value).ToList());
        var mainArea = MainArea(continents, uniqueCount);
        var activityScore = Math.Clamp((int)Math.Round(100 * (1 - Math.Exp(-uniqueCount / 18d))), 0, 100);
        var dxShare = uniqueCount == 0 ? 0 : distantCount / (double)uniqueCount;
        var distanceScore = Math.Min(60, (p80 ?? 0) / 6_000d * 60);
        var shareScore = dxShare * 20;
        var diversityScore = Math.Min(15, Math.Max(0, continents.Count - 1) * 5);
        var longDxScore = Math.Min(5, longDxCount);
        var dxReachScore = Math.Clamp((int)Math.Round(distanceScore + shareScore + diversityScore + longDxScore), 0, 100);
        var assessment = Assessment(uniqueCount, distantCount, longDxCount, continents.Count, p80);

        return new BandQualitySnapshot
        {
            Band = band,
            TotalDecodes = decodes.Count,
            UniqueStations = uniqueCount,
            CqCallers = cqCount,
            NewDxccStations = newDxccCount,
            DistantStations = distantCount,
            LongDxStations = longDxCount,
            WantedStations = wantedCount,
            ContinentCount = continents.Count,
            MainArea = mainArea,
            MedianSnr = medianSnr,
            EightiethPercentileDistanceMiles = p80,
            FarthestDistanceMiles = farthest,
            ActivityScore = activityScore,
            DxReachScore = dxReachScore,
            Assessment = assessment,
            Detail = BuildDetail(band, uniqueCount, cqCount, distantCount, longDxCount, wantedCount, continents.Count,
                mainArea, medianSnr, p80, farthest, assessment)
        };
    }

    private static string ObservationCall(DecodeMessage decode)
    {
        return FirstNonBlank(decode.ContactableCall, decode.Callsign, decode.HeardCall, decode.PrimaryDisplayCall)
            .Trim()
            .ToUpperInvariant();
    }

    private static DecodeMessage Representative(IEnumerable<DecodeMessage> observations)
    {
        return observations
            .OrderByDescending(d => d.DistanceMiles.HasValue)
            .ThenByDescending(d => d.DistanceMiles)
            .ThenByDescending(d => d.IsCq)
            .ThenByDescending(d => d.ReceivedAt)
            .First();
    }

    private static bool IsWanted(DecodeMessage decode)
    {
        return decode.IsNewDxcc || decode.IsUnconfirmedDxcc || decode.IsNewGrid || decode.IsNewState;
    }

    private static string MainArea(IReadOnlyList<IGrouping<string, DecodeMessage>> continents, int stationCount)
    {
        if (continents.Count == 0)
            return "No location data";

        var first = continents[0];
        var firstName = ContinentName(first.Key);
        if (continents.Count == 1 || first.Count() >= Math.Max(1, stationCount) * 0.55)
            return firstName;

        return $"{firstName} / {ContinentName(continents[1].Key)}";
    }

    private static string ContinentName(string code)
    {
        return ContinentNames.TryGetValue(code, out var name) ? name : code;
    }

    private static string Assessment(int unique, int distant, int longDx, int continents, double? p80)
    {
        if (unique == 0)
            return "No activity";
        if (unique <= 2)
            return "Very quiet";
        if (longDx >= 3 && continents >= 3 && p80 >= LongDxMiles)
            return "Strong multi-region opening";
        if (distant >= 3 && p80 >= 3_000)
            return "Good DX opening";
        if (distant >= 1 || p80 >= DistantMiles)
            return "DX promising";
        if (unique >= 20)
            return "Busy regional";
        return "Regional activity";
    }

    private static string BuildDetail(
        string band,
        int unique,
        int cq,
        int distant,
        int longDx,
        int wanted,
        int continents,
        string mainArea,
        double? medianSnr,
        double? p80,
        double? farthest,
        string assessment)
    {
        return $"{band}: {assessment}. {unique} unique stations and {cq} CQ callers; "
            + $"{distant} beyond {DistantMiles:0} miles and {longDx} beyond {LongDxMiles:0} miles. "
            + $"Main activity: {mainArea}; {continents} continent{(continents == 1 ? "" : "s")}. "
            + $"80th-percentile distance: {DistanceText(p80)}; farthest: {DistanceText(farthest)}; "
            + $"median SNR: {(medianSnr.HasValue ? $"{medianSnr.Value:+0;-0;0} dB" : "unknown")}; "
            + $"wanted stations: {wanted}.";
    }

    private static string DistanceText(double? value) => value.HasValue ? $"{value.Value:N0} mi" : "unknown";

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static double? Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return null;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static string FirstNonBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
