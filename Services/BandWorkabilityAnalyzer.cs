using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class BandWorkabilityAnalyzer
{
    private const double MilesPerKm = 0.621371;

    public BandWorkabilityMetrics Analyze(
        string band,
        string homeGrid,
        IReadOnlyList<DecodeMessage> decodes,
        IReadOnlyList<PskReporterSpot> matchedReports,
        BandQualitySnapshot quality,
        PskReporterMetrics psk,
        HuntingOperatingMode mode,
        BandPerformanceEvidence performance)
    {
        if (quality.NewDxccStations > 0)
        {
            return new BandWorkabilityMetrics
            {
                Calculated = true,
                Score = 10_000 + quality.NewDxccStations * 1_000,
                PskViabilityPercent = ViabilityPercent(psk),
                Assessment = "New DXCC priority",
                Detail = $"{band}: live New DXCC has absolute priority; outward PSK evidence does not suppress it."
            };
        }

        if (!psk.Measured)
        {
            return new BandWorkabilityMetrics
            {
                Calculated = false,
                Assessment = "PSK result unavailable",
                Detail = $"{band}: the CQ probes completed, but PSK Reporter could not be queried. DX Pilot will not infer two-way workability or select this result automatically."
            };
        }

        var viability = PskViability(psk);
        var receivers = ReceiverPaths(matchedReports);
        var opportunities = BuildOpportunities(homeGrid, decodes, receivers, viability);
        var ranked = opportunities
            .OrderByDescending(item => item.Value)
            .Take(5)
            .ToList();
        var diminishing = new[] { 1d, 0.72, 0.52, 0.38, 0.28 };
        var maximum = diminishing.Sum();
        var opportunityScore = ranked
            .Select((item, index) => item.Value * diminishing[index])
            .Sum() / maximum * 100d;
        var receivedPathMatch = ReceivedPathAlignment(homeGrid, decodes, receivers, viability);
        var wantedPathMatch = opportunities.Count == 0
            ? receivedPathMatch
            : opportunities.Average(item => item.PathMatch);
        // In Wanted Sniper the wanted paths are the strongest evidence. In the
        // other modes the general receive/outward corridor is more important.
        var pathMatch = mode == HuntingOperatingMode.WantedSniper && opportunities.Count > 0
            ? wantedPathMatch * 0.75 + receivedPathMatch * 0.25
            : receivedPathMatch;
        var persistence = opportunities.Count == 0
            ? 0
            : opportunities.Average(item => item.Persistence);
        var workable = opportunities.Count(item => item.PathMatch >= 0.60);

        var receiveScore = Math.Clamp(quality.DxReachScore * 0.65 + quality.ActivityScore * 0.35, 0, 100);
        var twoWayGate = viability * 0.55 + pathMatch * 0.45;
        var viableReceiveScore = receiveScore * twoWayGate;
        var productivity = ProductivityAdjustment(performance);
        var score = mode switch
        {
            HuntingOperatingMode.WantedSniper => opportunityScore * 0.65 + viableReceiveScore * 0.20 + psk.PropagationScore * 0.15,
            HuntingOperatingMode.LocationHunt => opportunityScore * 0.30 + viableReceiveScore * 0.45 + psk.PropagationScore * 0.25,
            _ => opportunityScore * 0.15 + viableReceiveScore * 0.60 + psk.PropagationScore * 0.25
        };
        score = Math.Clamp(score + productivity, 0, 100);

        var assessment = Assessment(score, viability, pathMatch, opportunities.Count, workable);
        var pathText = opportunities.Count == 0
            ? "no located wanted opportunities were available for direct path matching"
            : $"{workable} of {opportunities.Count} distinct wanted path{(opportunities.Count == 1 ? "" : "s")} had credible outward support";
        var performanceText = performance.CallingAttempts < 3
            ? "insufficient recent calling history for a productivity adjustment"
            : $"recent evidence: {performance.CallingAttempts} calls, {performance.ReplyOrProgressEvents} progress events and {performance.CompletedQsos} completed QSOs ({productivity:+0.0;-0.0;0.0} points)";

        return new BandWorkabilityMetrics
        {
            Calculated = true,
            Score = score,
            DistinctOpportunities = opportunities.Count,
            WorkableOpportunities = workable,
            PathMatchPercent = (int)Math.Round(pathMatch * 100),
            PskViabilityPercent = (int)Math.Round(viability * 100),
            PersistencePercent = (int)Math.Round(persistence * 100),
            ProductivityAdjustment = productivity,
            Assessment = assessment,
            Detail = $"{band}: {assessment}. Two-way workability {score:0}/100; PSK viability {viability * 100:0}%; "
                + $"geographical path match {pathMatch * 100:0}%; {pathText}. "
                + $"Opportunity score {opportunityScore:0}; viable received score {viableReceiveScore:0}; PSK score {psk.PropagationScore}; {performanceText}."
        };
    }

    public static double PskViability(PskReporterMetrics psk)
    {
        if (!psk.Measured)
            return 1d;
        if (psk.UniqueReceivers == 0)
            return 0.20;

        var measuredFactor = 0.20 + Math.Clamp(psk.PropagationScore, 0, 100) / 100d * 0.80;
        var confidence = 1 - Math.Exp(-psk.UniqueReceivers / 3d);
        return Math.Clamp(1 - confidence * (1 - measuredFactor), 0.20, 1d);
    }

    private static int ViabilityPercent(PskReporterMetrics psk) =>
        (int)Math.Round(PskViability(psk) * 100);

    private static double ProductivityAdjustment(BandPerformanceEvidence evidence)
    {
        if (evidence.CallingAttempts < 3)
            return 0;

        var useful = evidence.CompletedQsos + Math.Min(evidence.ReplyOrProgressEvents, evidence.CallingAttempts) * 0.30;
        var smoothedRate = (useful + 0.75) / (evidence.CallingAttempts + 5d);
        return Math.Clamp((smoothedRate - 0.20) * 25d, -5d, 8d);
    }

    private static string Assessment(double score, double viability, double pathMatch, int opportunities, int workable)
    {
        if (viability < 0.45)
            return "Poor outward workability";
        if (opportunities > 0 && workable == 0)
            return "Wanted paths unsupported";
        if (score >= 75 && pathMatch >= 0.70)
            return "Strong two-way opportunity";
        if (score >= 55)
            return "Good two-way prospects";
        if (score >= 35)
            return "Mixed workability";
        return "Low practical workability";
    }

    private static List<Opportunity> BuildOpportunities(
        string homeGrid,
        IReadOnlyList<DecodeMessage> decodes,
        IReadOnlyList<PathPoint> receivers,
        double viability)
    {
        var groupedStations = decodes
            .Select(decode => new { Decode = decode, Call = ObservationCall(decode) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Call) && IsWanted(item.Decode))
            .GroupBy(item => item.Call, StringComparer.OrdinalIgnoreCase);
        var opportunities = new Dictionary<string, Opportunity>(StringComparer.OrdinalIgnoreCase);

        foreach (var station in groupedStations)
        {
            var observations = station.Select(item => item.Decode).OrderByDescending(item => item.ReceivedAt).ToList();
            var representative = observations
                .OrderByDescending(item => HasLocation(item))
                .ThenByDescending(item => item.Snr)
                .First();
            var key = OpportunityKey(representative, station.Key);
            var pathMatch = PathMatch(homeGrid, representative, receivers, viability);
            var persistence = Math.Clamp(0.55 + Math.Min(3, observations.Count - 1) * 0.15, 0.55, 1d);
            var bestSnr = observations.Max(item => item.Snr);
            var signal = Math.Clamp((bestSnr + 30) / 25d, 0.45, 1d);
            var priority = OpportunityPriority(representative);
            var value = priority * pathMatch * persistence * signal;
            var candidate = new Opportunity(key, value, pathMatch, persistence);
            if (!opportunities.TryGetValue(key, out var existing) || candidate.Value > existing.Value)
                opportunities[key] = candidate;
        }

        return opportunities.Values.ToList();
    }

    private static bool IsWanted(DecodeMessage decode) =>
        decode.IsNewDxcc || decode.IsUnconfirmedDxcc || decode.IsNewGrid || decode.IsNewState;

    private static double OpportunityPriority(DecodeMessage decode) =>
        decode.IsNewDxcc ? 1d :
        decode.IsUnconfirmedDxcc ? 1d :
        decode.IsNewState ? 0.90 :
        decode.IsNewGrid ? 0.75 : 0.60;

    private static string OpportunityKey(DecodeMessage decode, string call)
    {
        if (decode.IsNewDxcc || decode.IsUnconfirmedDxcc)
            return $"DXCC:{FirstNonBlank(decode.Dxcc, decode.ContactableDxccNumber, decode.EntityName, call)}";
        if (decode.IsNewState && !string.IsNullOrWhiteSpace(decode.State))
            return $"STATE:{decode.State}";
        var grid = FirstGrid(decode);
        if (decode.IsNewGrid && MaidenheadGrid.Normalize(grid).IsValid)
            return $"GRID:{MaidenheadGrid.Normalize(grid).Grid4}";
        return $"CALL:{call}";
    }

    private static double PathMatch(
        string homeGrid,
        DecodeMessage target,
        IReadOnlyList<PathPoint> receivers,
        double viability)
    {
        if (receivers.Count == 0 || !TryTargetPoint(target, out var targetPoint))
            return viability;
        if (!MaidenheadGrid.TryGetCentre(homeGrid, out var homeLat, out var homeLon))
            return viability;

        var targetDistance = DistanceMiles(homeLat, homeLon, targetPoint.Latitude, targetPoint.Longitude);
        var targetBearing = Bearing(homeLat, homeLon, targetPoint.Latitude, targetPoint.Longitude);
        var best = 0d;
        foreach (var receiver in receivers)
        {
            var receiverDistance = DistanceMiles(homeLat, homeLon, receiver.Latitude, receiver.Longitude);
            var receiverBearing = Bearing(homeLat, homeLon, receiver.Latitude, receiver.Longitude);
            var targetSeparation = DistanceMiles(targetPoint.Latitude, targetPoint.Longitude, receiver.Latitude, receiver.Longitude);
            var bearingDifference = BearingDifference(targetBearing, receiverBearing);
            var coverage = targetDistance <= 1 ? 1 : receiverDistance / targetDistance;
            var match = targetSeparation switch
            {
                <= 500 => 1d,
                <= 1_000 => 0.90,
                _ when bearingDifference <= 20 && coverage >= 0.80 => 0.85,
                _ when bearingDifference <= 35 && coverage >= 0.65 => 0.72,
                _ when bearingDifference <= 55 && coverage >= 0.50 => 0.55,
                _ => Math.Min(0.45, viability)
            };
            best = Math.Max(best, match);
        }

        // A single geographically fortunate reporter must not completely hide
        // generally poor outward results; blend the path match with band-wide
        // viability while preserving genuine corridor evidence.
        return Math.Clamp(best * 0.75 + viability * 0.25, 0.20, 1d);
    }

    private static double ReceivedPathAlignment(
        string homeGrid,
        IReadOnlyList<DecodeMessage> decodes,
        IReadOnlyList<PathPoint> receivers,
        double viability)
    {
        var paths = decodes
            .Select(decode => new { Decode = decode, Call = ObservationCall(decode) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Call) && HasLocation(item.Decode))
            .GroupBy(item => item.Call, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var observations = group.Select(item => item.Decode).ToList();
                var representative = observations
                    .OrderByDescending(item => IsWanted(item))
                    .ThenByDescending(item => item.DistanceMiles ?? 0)
                    .ThenByDescending(item => item.Snr)
                    .First();
                var persistence = Math.Clamp(0.60 + Math.Min(4, observations.Count - 1) * 0.10, 0.60, 1d);
                var distanceWeight = Math.Clamp((representative.DistanceMiles ?? 0) / 2_500d, 0.35, 1.25);
                var wantedWeight = IsWanted(representative) ? 1.35 : 1d;
                var weight = persistence * distanceWeight * wantedWeight;
                return new PathEvidence(PathMatch(homeGrid, representative, receivers, viability), weight);
            })
            .OrderByDescending(item => item.Weight)
            .Take(12)
            .ToList();

        if (paths.Count == 0)
            return viability;
        var totalWeight = paths.Sum(item => item.Weight);
        return totalWeight <= 0
            ? viability
            : Math.Clamp(paths.Sum(item => item.Match * item.Weight) / totalWeight, 0.20, 1d);
    }

    private static List<PathPoint> ReceiverPaths(IReadOnlyList<PskReporterSpot> reports) =>
        reports
            .Where(report => MaidenheadGrid.TryGetCentre(report.ReceiverLocator, out _, out _))
            .GroupBy(report => report.ReceiverCallsign, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(report => MaidenheadGrid.TryGetCentre(report.ReceiverLocator, out var lat, out var lon)
                ? new PathPoint(lat, lon)
                : default)
            .Where(point => point != default)
            .ToList();

    private static bool HasLocation(DecodeMessage decode) =>
        TryTargetPoint(decode, out _);

    private static bool TryTargetPoint(DecodeMessage decode, out PathPoint point)
    {
        var grid = FirstGrid(decode);
        if (MaidenheadGrid.TryGetCentre(grid, out var latitude, out var longitude))
        {
            point = new PathPoint(latitude, longitude);
            return true;
        }
        if (decode.EntityLatitude.HasValue && decode.EntityLongitude.HasValue)
        {
            point = new PathPoint(decode.EntityLatitude.Value, decode.EntityLongitude.Value);
            return true;
        }
        point = default;
        return false;
    }

    private static string FirstGrid(DecodeMessage decode) =>
        new[] { decode.TransmittedGrid, decode.EffectiveGrid, decode.Grid, decode.AdifGrid, decode.QrzGrid }
            .FirstOrDefault(value => MaidenheadGrid.Normalize(value).IsValid) ?? "";

    private static string ObservationCall(DecodeMessage decode) =>
        FirstNonBlank(decode.ContactableCall, decode.Callsign, decode.HeardCall, decode.PrimaryDisplayCall)
            .Trim().ToUpperInvariant();

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static double DistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371d;
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var rLat1 = Radians(lat1);
        var rLat2 = Radians(lat2);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(rLat1) * Math.Cos(rLat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * MilesPerKm;
    }

    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var rLat1 = Radians(lat1);
        var rLat2 = Radians(lat2);
        var dLon = Radians(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(rLat2);
        var x = Math.Cos(rLat1) * Math.Sin(rLat2) - Math.Sin(rLat1) * Math.Cos(rLat2) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static double BearingDifference(double first, double second)
    {
        var difference = Math.Abs(first - second) % 360;
        return difference > 180 ? 360 - difference : difference;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;

    private readonly record struct PathPoint(double Latitude, double Longitude);
    private readonly record struct PathEvidence(double Match, double Weight);
    private sealed record Opportunity(string Key, double Value, double PathMatch, double Persistence);
}
