using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class PskReporterAnalyzer
{
    private const double KmToMiles = 0.621371;
    private const double DistantMiles = 2_500;
    private const double LongDxMiles = 5_000;
    private readonly GridDistanceCalculator _distanceCalculator;
    private readonly DxccResolver _dxccResolver;

    public PskReporterAnalyzer(GridDistanceCalculator distanceCalculator, DxccResolver dxccResolver)
    {
        _distanceCalculator = distanceCalculator;
        _dxccResolver = dxccResolver;
    }

    public PskReporterMetrics Analyze(
        string band,
        string homeGrid,
        IReadOnlyList<PskReporterSpot> reports,
        bool measured)
    {
        if (!measured)
            return new PskReporterMetrics();

        var onBand = reports
            .Where(report => report.Band.Equals(band, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (onBand.Count == 0)
        {
            return new PskReporterMetrics
            {
                Measured = true,
                Assessment = "No reports yet",
                Detail = $"{band}: neither the live PSK Reporter feed nor the end-of-survey retrieval contained a receiver report matching either CQ probe. A late report may still appear on the PSK Reporter website."
            };
        }

        var effectiveHomeGrid = FirstNonBlank(homeGrid, onBand.Select(report => report.SenderLocator).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "");
        var receivers = onBand
            .GroupBy(report => report.ReceiverCallsign, StringComparer.OrdinalIgnoreCase)
            .Select(group => Representative(group, effectiveHomeGrid))
            .ToList();
        var distances = receivers.Where(item => item.DistanceMiles.HasValue).Select(item => item.DistanceMiles!.Value).OrderBy(value => value).ToList();
        var countries = receivers.Select(item => item.Country).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var continents = receivers
            .Where(item => item.Continent.Length > 0)
            .GroupBy(item => item.Continent, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ToList();
        var snrs = onBand.Where(report => report.SignalReportDb.HasValue).Select(report => report.SignalReportDb!.Value).OrderBy(value => value).ToList();
        var p80 = Percentile(distances, 0.80);
        var farthest = receivers.Where(item => item.DistanceMiles.HasValue).OrderByDescending(item => item.DistanceMiles).FirstOrDefault();
        var distant = receivers.Count(item => item.DistanceMiles >= DistantMiles);
        var longDx = receivers.Count(item => item.DistanceMiles >= LongDxMiles);
        var mainArea = MainArea(continents, receivers.Count);
        var score = Score(receivers.Count, distant, longDx, continents.Count, p80);
        var assessment = Assessment(receivers.Count, distant, longDx, continents.Count, p80);

        return new PskReporterMetrics
        {
            Measured = true,
            ReportCount = onBand.Count,
            UniqueReceivers = receivers.Count,
            UniqueCountries = countries.Count,
            ContinentCount = continents.Count,
            DistantReceivers = distant,
            LongDxReceivers = longDx,
            StrongestSnr = snrs.Count == 0 ? null : snrs[^1],
            MedianSnr = Median(snrs),
            EightiethPercentileDistanceMiles = p80,
            FarthestDistanceMiles = farthest?.DistanceMiles,
            FarthestReceiver = farthest?.Report.ReceiverCallsign ?? "",
            MainArea = mainArea,
            PropagationScore = score,
            Assessment = assessment,
            Detail = $"{band}: {assessment}. Heard by {receivers.Count} unique receiver{(receivers.Count == 1 ? "" : "s")} in {countries.Count} countr{(countries.Count == 1 ? "y" : "ies")} across {continents.Count} continent{(continents.Count == 1 ? "" : "s")}; "
                + $"main outward area: {mainArea}. {distant} receiver{(distant == 1 ? "" : "s")} beyond 2,500 miles and {longDx} beyond 5,000 miles. "
                + $"80th-percentile reach: {DistanceText(p80)}; farthest: {(farthest == null ? "unknown" : $"{farthest.Report.ReceiverCallsign} at {DistanceText(farthest.DistanceMiles)}")}; "
                + $"median report: {(snrs.Count == 0 ? "unknown" : $"{Median(snrs):+0;-0;0} dB")}; strongest: {(snrs.Count == 0 ? "unknown" : $"{snrs[^1]:+0;-0;0} dB")}."
        };
    }

    private ReceiverObservation Representative(IEnumerable<PskReporterSpot> source, string homeGrid)
    {
        var candidates = source.Select(report =>
        {
            var entity = _dxccResolver.Resolve(report.ReceiverCallsign);
            var distance = _distanceCalculator.DistanceKm(homeGrid, report.ReceiverLocator) * KmToMiles;
            return new ReceiverObservation(
                report,
                distance,
                FirstNonBlank(report.ReceiverDxcc, entity?.Name ?? ""),
                entity?.Continent ?? "");
        }).ToList();
        return candidates
            .OrderByDescending(item => item.DistanceMiles.HasValue)
            .ThenByDescending(item => item.DistanceMiles)
            .ThenByDescending(item => item.Report.SignalReportDb)
            .First();
    }

    private static int Score(int receivers, int distant, int longDx, int continents, double? p80)
    {
        var receiverScore = 30 * (1 - Math.Exp(-receivers / 10d));
        var distanceScore = Math.Min(45, (p80 ?? 0) / 6_000d * 45);
        var distantShare = receivers == 0 ? 0 : distant / (double)receivers;
        var diversityScore = Math.Min(15, Math.Max(0, continents - 1) * 5);
        var longDxScore = Math.Min(5, longDx) + distantShare * 5;
        return Math.Clamp((int)Math.Round(receiverScore + distanceScore + diversityScore + longDxScore), 0, 100);
    }

    private static string Assessment(int receivers, int distant, int longDx, int continents, double? p80)
    {
        if (receivers == 0)
            return "No reports yet";
        if (longDx >= 3 && continents >= 3 && p80 >= LongDxMiles)
            return "Strong outward multi-region opening";
        if (distant >= 3 && p80 >= 3_000)
            return "Good outward DX propagation";
        if (distant >= 1 || p80 >= DistantMiles)
            return "Outward DX promising";
        if (receivers >= 15)
            return "Strong regional coverage";
        if (receivers <= 2)
            return "Limited outward sample";
        return "Regional outward propagation";
    }

    private static string MainArea<T>(IReadOnlyList<IGrouping<string, T>> continents, int receiverCount)
    {
        if (continents.Count == 0)
            return "Unknown";
        var first = continents[0];
        if (continents.Count == 1 || first.Count() >= Math.Max(1, receiverCount) * 0.55)
            return ContinentName(first.Key);
        return $"{ContinentName(first.Key)} / {ContinentName(continents[1].Key)}";
    }

    private static string ContinentName(string code) => code.ToUpperInvariant() switch
    {
        "AF" => "Africa", "AN" => "Antarctica", "AS" => "Asia", "EU" => "Europe",
        "NA" => "North America", "OC" => "Oceania", "SA" => "South America", _ => code
    };

    private static double? Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return null;
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
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
        return lower == upper
            ? sortedValues[lower]
            : sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static string DistanceText(double? value) => value.HasValue ? $"{value.Value:N0} mi" : "unknown";
    private static string FirstNonBlank(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    private sealed record ReceiverObservation(PskReporterSpot Report, double? DistanceMiles, string Country, string Continent);
}
