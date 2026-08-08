using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class DxTargetScorer
{
    private readonly DxccResolver _dxccResolver;
    private readonly DxccRarityService _rarityService;
    private readonly GridDistanceCalculator _distanceCalculator;

    public DxTargetScorer(DxccResolver dxccResolver, DxccRarityService rarityService, GridDistanceCalculator distanceCalculator)
    {
        _dxccResolver = dxccResolver;
        _rarityService = rarityService;
        _distanceCalculator = distanceCalculator;
    }

    public DxTarget Score(
        DecodeMessage decode,
        IReadOnlyCollection<AdifQso> logbook,
        WorkedStatusIndexes indexes,
        IEnumerable<DecodeMessage> recentDecodes,
        AppSettings settings)
    {
        EnrichDecode(decode, logbook, indexes, settings);

        var ranking = BuildRanking(decode, logbook, indexes, recentDecodes, settings);
        var target = new DxTarget
        {
            Decode = decode,
            Ranking = ranking,
            Score = ranking.FinalScore
        };
        target.Reasons.Add(ranking.PrimaryWantedReason);
        foreach (var reason in ranking.AllWantedReasons.Where(r => !r.Equals(ranking.PrimaryWantedReason, StringComparison.OrdinalIgnoreCase)))
            target.Reasons.Add(reason);
        target.Reasons.Add(ranking.SelectionExplanation);
        return target;
    }

    public void EnrichDecode(DecodeMessage decode, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings)
    {
        var addressed = ResolveEntity(decode.AddressedCall);
        var heard = ResolveEntity(string.IsNullOrWhiteSpace(decode.ContactableCall) ? decode.HeardCall : decode.ContactableCall);
        decode.Call1Entity = ResolveName(decode.Call1);
        decode.AddressedEntity = addressed?.Name ?? "";
        decode.AddressedDxccNumber = addressed?.Code ?? "";
        decode.Call2Entity = ResolveName(decode.Call2);
        decode.ContactableEntity = heard?.Name ?? "";
        decode.ContactableDxccNumber = heard?.Code ?? "";
        decode.GridEntity = ResolveName(decode.GridOwnerCall);

        var lookupCall = !string.IsNullOrWhiteSpace(decode.ContactableCall)
            ? decode.ContactableCall
            : decode.Callsign;
        var entity = _dxccResolver.Resolve(lookupCall);
        if (entity != null && !string.IsNullOrWhiteSpace(entity.Code))
        {
            decode.Callsign = lookupCall;
            decode.Dxcc = entity.Code;
            decode.EntityName = entity.Name;
            decode.Continent = entity.Continent;
            decode.PrimaryDisplayEntity = entity.Name;
            decode.EntitySource = entity.Source;
            decode.EntityConfidence = entity.Confidence;
            decode.EntityReason = entity.Reason;
            decode.LookupPrefix = entity.LookupPrefix;
            decode.EntityLatitude = entity.Latitude;
            decode.EntityLongitude = entity.Longitude;
        }
        else if (string.IsNullOrWhiteSpace(decode.EntityName))
        {
            var worked = logbook.FirstOrDefault(q => q.Call.Equals(lookupCall, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(q.Dxcc) || !string.IsNullOrWhiteSpace(q.Country)));
            if (worked != null)
            {
                decode.Dxcc = worked.Dxcc;
                decode.EntityName = worked.Country;
                decode.PrimaryDisplayEntity = worked.Country;
                decode.EntitySource = "ADIF history";
                decode.EntityConfidence = "Medium";
                decode.EntityReason = "Resolved from previous logged QSO";
            }
            else
            {
                decode.EntityName = "Unknown";
                decode.PrimaryDisplayEntity = "Unknown";
                decode.EntitySource = "Unknown";
                decode.EntityConfidence = "Low";
                decode.EntityReason = entity?.Reason ?? "No CTY prefix match";
            }
        }

        if (!decode.DistanceKm.HasValue
            && !decode.GridSource.Equals("QRZ", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.HomeGrid)
            && !string.IsNullOrWhiteSpace(decode.Grid))
        {
            decode.DistanceKm = _distanceCalculator.DistanceKm(settings.HomeGrid, decode.Grid);
            decode.DistanceSource = "Grid";
        }
        else if (!decode.DistanceKm.HasValue && decode.EntityLatitude.HasValue && decode.EntityLongitude.HasValue)
        {
            decode.DistanceSource = "EntityApprox";
        }
        else if (!decode.DistanceKm.HasValue)
        {
            decode.DistanceSource = "None";
        }

        decode.State = WasStateEligibility.IsEligible(decode)
            ? WasStateEligibility.NormalizeState(decode.State, settings.IncludeDistrictOfColumbia)
            : "";

        var dxccStatus = DetermineDxccStatus(decode, indexes);
        decode.IsNewDxcc = dxccStatus == DxccCandidateStatus.NotWorked;
        decode.IsUnconfirmedDxcc = dxccStatus == DxccCandidateStatus.WorkedUnconfirmed;
        var normalizedGrid = MaidenheadGrid.Normalize(decode.Grid);
        decode.IsNewGrid = normalizedGrid.IsValid
            && (!indexes.Grids.TryGetValue(normalizedGrid.Grid4, out var gridStatus) || !gridStatus.ConfirmedAny);
        decode.IsNewState = !string.IsNullOrWhiteSpace(decode.State)
            && WasStateEligibility.IsEligible(decode)
            && (!indexes.States.TryGetValue(decode.State, out var stateStatus) || !stateStatus.ConfirmedAny);
    }

    private CandidateRanking BuildRanking(
        DecodeMessage decode,
        IReadOnlyCollection<AdifQso> logbook,
        WorkedStatusIndexes indexes,
        IEnumerable<DecodeMessage> recentDecodes,
        AppSettings settings)
    {
        var ranking = new CandidateRanking
        {
            Call = decode.Callsign,
            Entity = decode.EntityName,
            DxccNumber = decode.Dxcc,
            Grid = decode.Grid,
            State = decode.State,
            Band = decode.Band,
            Mode = decode.Mode,
            DxccConfirmationMode = settings.DxccConfirmationMode,
            SourceRawMessage = decode.RawText,
            SourceDecodeTime = decode.ReceivedAt,
            AgeSeconds = Math.Max(0, (DateTime.Now - decode.ReceivedAt).TotalSeconds),
            DistanceMiles = decode.DistanceMiles,
            DistanceSource = decode.DistanceSource
        };

        ranking.DxccStatus = DetermineDxccStatus(decode, indexes);
        ranking.DxccWorked = ranking.DxccStatus is DxccCandidateStatus.WorkedUnconfirmed or DxccCandidateStatus.Confirmed;
        ranking.DxccConfirmed = ranking.DxccStatus == DxccCandidateStatus.Confirmed;
        if (!string.IsNullOrWhiteSpace(decode.Dxcc) && indexes.Dxcc.TryGetValue(decode.Dxcc, out var dxccWorked))
            ranking.DxccConfirmationSource = dxccWorked.Source;

        var rarity = _rarityService.Get(decode.Dxcc, decode.EntityName);
        ranking.RarityRank = rarity.RarityRank;
        ranking.RarityScore = rarity.RarityScore;
        ranking.GlobalRarityScore = rarity.GlobalRarityScore;
        ranking.UKDesirability = rarity.UKDesirability;
        ranking.DesirabilityBand = rarity.DesirabilityBand;
        ranking.UKRegionBand = rarity.UKRegionBand;
        ranking.DistanceScore = DistanceScore(ranking.DistanceMiles);
        ranking.AdjustedDxValueScore = AdjustedDxValueScore(
            ranking.GlobalRarityScore,
            ranking.UKDesirability,
            ranking.DistanceScore,
            settings);
        ranking.RarityMatchSource = rarity.MatchSource;
        ranking.RarityMatchConfidence = rarity.MatchConfidence;
        ranking.SignalScore = SignalScore(decode.Snr);
        ranking.FreshnessScore = FreshnessScore(ranking.AgeSeconds, decode, settings);
        ranking.PenaltyScore = PenaltyScore(decode, logbook, recentDecodes, settings);

        AssignTier(ranking, decode, logbook, indexes, settings);
        ranking.PrimaryWantedReason = ranking.AllWantedReasons.FirstOrDefault() ?? ranking.PriorityTierName;
        ranking.FinalScore = (int)Math.Round(ranking.AdjustedDxValueScore * 100) + ranking.FreshnessScore + ranking.SignalScore - ranking.PenaltyScore;
        ranking.ScoreBreakdown = $"tier {ranking.PriorityTierName}; adjusted DX {ranking.AdjustedDxValueScore:0.0}; global rarity {ranking.GlobalRarityScore:0.0}; UK desirability {ranking.UKDesirability:0.0}; distance {ranking.DistanceScore:0.0}; freshness {ranking.FreshnessScore}; signal {ranking.SignalScore}; penalty -{ranking.PenaltyScore}";
        ranking.SelectionExplanation = ExplainSelection(ranking);
        return ranking;
    }

    private static void AssignTier(CandidateRanking ranking, DecodeMessage decode, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings)
    {
        if (ranking.DxccStatus == DxccCandidateStatus.Unknown)
        {
            ranking.PriorityTier = 99;
            ranking.PriorityTierName = "Diagnostic: Unknown DXCC";
            ranking.AllWantedReasons.Add("Unknown DXCC; not auto-selectable");
            return;
        }

        if (ranking.DxccStatus is DxccCandidateStatus.NotWorked or DxccCandidateStatus.WorkedUnconfirmed)
        {
            ranking.PriorityTier = 10;
            ranking.PriorityTierName = ranking.DxccStatus == DxccCandidateStatus.NotWorked
                ? "Tier 1A: New DXCC, never worked"
                : "Tier 1A: Unconfirmed DXCC";
            ranking.WantedScope = WantedScope.Overall;
            ranking.NeedStatus = ranking.DxccStatus == DxccCandidateStatus.NotWorked
                ? NeedStatus.NeverWorked
                : NeedStatus.WorkedNotLoTWConfirmed;
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatDxcc(ranking.DxccStatus, decode.EntityName));
            return;
        }

        if (!string.IsNullOrWhiteSpace(decode.Dxcc)
            && indexes.Dxcc.TryGetValue(decode.Dxcc, out var scopedDxcc)
            && TryBestEnabledScopedNeed(
                scopedDxcc.WorkedBands,
                scopedDxcc.WorkedModes,
                scopedDxcc.WorkedBandModes,
                scopedDxcc.LoTWConfirmedBands,
                scopedDxcc.LoTWConfirmedModes,
                scopedDxcc.LoTWConfirmedBandModes,
                decode.Band,
                decode.Mode,
                settings,
                out var dxccScope,
                out var dxccNeed))
        {
            ranking.PriorityTier = dxccScope switch
            {
                WantedScope.CurrentBand => 12,
                WantedScope.CurrentMode => 13,
                _ => 14
            };
            var needLabel = dxccNeed == NeedStatus.NeverWorked ? "new" : "unconfirmed";
            ranking.PriorityTierName = dxccScope switch
            {
                WantedScope.CurrentBand => $"Tier 1C: DXCC {needLabel} on current band",
                WantedScope.CurrentMode => $"Tier 1D: DXCC {needLabel} on current mode",
                _ => $"Tier 1E: DXCC {needLabel} on current band and mode"
            };
            ranking.WantedScope = dxccScope;
            ranking.NeedStatus = dxccNeed;
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatWantedReason(
                "DXCC", dxccNeed, dxccScope, decode.EntityName, decode.Band, decode.Mode));
            return;
        }

        if (settings.ChaseRareConfirmedDxcc
            && ranking.DxccStatus == DxccCandidateStatus.Confirmed
            && ranking.RarityRank.HasValue
            && ranking.RarityRank.Value <= Math.Max(1, settings.RareDxccRankThreshold))
        {
            ranking.PriorityTier = 20;
            ranking.PriorityTierName = "Tier 2: Rare country already confirmed";
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatRareConfirmedDxcc(decode.EntityName));
            return;
        }

        var rankingGrid = MaidenheadGrid.Normalize(decode.Grid);
        indexes.Grids.TryGetValue(rankingGrid.Grid4, out var gridStatus);
        if (settings.PrioritizeNewGridsInDxAssist
            && !string.IsNullOrWhiteSpace(decode.Grid)
            && (gridStatus == null || !gridStatus.WorkedAny))
        {
            ranking.PriorityTier = 30;
            ranking.PriorityTierName = "Tier 3: New grid priority enabled";
            ranking.WantedScope = WantedScope.Overall;
            ranking.NeedStatus = NeedStatus.NeverWorked;
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatGrid(gridStatus, decode.Grid));
            return;
        }

        indexes.States.TryGetValue(decode.State, out var stateStatus);
        if (WasStateEligibility.IsEligible(decode)
            && !string.IsNullOrWhiteSpace(decode.State)
            && stateStatus == null
            && settings.PrioritizeNewUsStates)
        {
            ranking.PriorityTier = 40;
            ranking.PriorityTierName = "Tier 4: New USA state";
            ranking.WantedScope = WantedScope.Overall;
            ranking.NeedStatus = NeedStatus.NeverWorked;
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatState(stateStatus, decode.State));
            return;
        }

        if (WasStateEligibility.IsEligible(decode)
            && !string.IsNullOrWhiteSpace(decode.State)
            && stateStatus != null
            && TryBestEnabledScopedNeed(
                stateStatus.WorkedBands,
                stateStatus.WorkedModes,
                stateStatus.WorkedBandModes,
                stateStatus.LoTWConfirmedBands,
                stateStatus.LoTWConfirmedModes,
                stateStatus.LoTWConfirmedBandModes,
                decode.Band,
                decode.Mode,
                settings,
                out var stateScope,
                out var stateNeed))
        {
            ranking.PriorityTier = stateScope switch
            {
                WantedScope.CurrentBand => 41,
                WantedScope.CurrentMode => 42,
                _ => 43
            };
            var needLabel = stateNeed == NeedStatus.NeverWorked ? "new" : "unconfirmed";
            ranking.PriorityTierName = stateScope switch
            {
                WantedScope.CurrentBand => $"Tier 4B: State {needLabel} on current band",
                WantedScope.CurrentMode => $"Tier 4C: State {needLabel} on current mode",
                _ => $"Tier 4D: State {needLabel} on current band and mode"
            };
            ranking.WantedScope = stateScope;
            ranking.NeedStatus = stateNeed;
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatWantedReason(
                "USA State", stateNeed, stateScope, decode.State, decode.Band, decode.Mode));
            return;
        }

        if (WasStateEligibility.IsEligible(decode)
            && !string.IsNullOrWhiteSpace(decode.State)
            && stateStatus != null
            && !stateStatus.LoTWConfirmedAny
            && settings.PrioritizeUnconfirmedUsStates)
        {
            ranking.PriorityTier = 44;
            ranking.PriorityTierName = "Tier 4E: Worked but unconfirmed USA state";
            ranking.WantedScope = WantedScope.Overall;
            ranking.NeedStatus = NeedStatus.WorkedNotLoTWConfirmed;
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatState(stateStatus, decode.State));
            return;
        }

        var workedBandMode = logbook.Any(q =>
            q.Call.Equals(decode.Callsign, StringComparison.OrdinalIgnoreCase)
            && Matches(q.Band, decode.Band)
            && Matches(q.Mode, decode.Mode));
        if (settings.IncludeBandModeWanted
            && !workedBandMode
            && HasRealBandMode(decode.Band, decode.Mode))
        {
            ranking.PriorityTier = 60;
            ranking.PriorityTierName = "Tier 6: New band/mode slot";
            ranking.AllWantedReasons.Add(TargetReasonFormatter.FormatBandModeSlot(decode.Callsign, decode.Band, decode.Mode));
            return;
        }

        ranking.PriorityTier = 80;
        ranking.PriorityTierName = "Tier 8: Distance/general DX";
        ranking.AllWantedReasons.Add("General DX / distance");
    }

    private static bool TryBestEnabledScopedNeed(
        HashSet<string> workedBands,
        HashSet<string> workedModes,
        HashSet<string> workedBandModes,
        HashSet<string> confirmedBands,
        HashSet<string> confirmedModes,
        HashSet<string> confirmedBandModes,
        string band,
        string mode,
        AppSettings settings,
        out WantedScope scope,
        out NeedStatus need)
    {
        scope = WantedScope.Overall;
        need = NeedStatus.Unknown;
        var candidates = new List<(WantedScope Scope, bool Enabled, bool HasValue, bool Worked, bool Confirmed)>
        {
            (WantedScope.CurrentBand, settings.IncludeBandWanted, !string.IsNullOrWhiteSpace(band), workedBands.Contains(band), confirmedBands.Contains(band)),
            (WantedScope.CurrentMode, settings.IncludeModeWanted, !string.IsNullOrWhiteSpace(mode), workedModes.Contains(mode), confirmedModes.Contains(mode)),
            (WantedScope.CurrentBandMode, settings.IncludeBandModeWanted,
                !string.IsNullOrWhiteSpace(band) && !string.IsNullOrWhiteSpace(mode),
                workedBandModes.Contains(BandModeKey(band, mode)),
                confirmedBandModes.Contains(BandModeKey(band, mode)))
        };

        foreach (var candidate in candidates.Where(candidate => candidate.Enabled && candidate.HasValue && !candidate.Worked))
        {
            scope = candidate.Scope;
            need = NeedStatus.NeverWorked;
            return true;
        }

        foreach (var candidate in candidates.Where(candidate => candidate.Enabled && candidate.HasValue && candidate.Worked && !candidate.Confirmed))
        {
            scope = candidate.Scope;
            need = NeedStatus.WorkedNotLoTWConfirmed;
            return true;
        }

        return false;
    }

    private static string BandModeKey(string band, string mode)
    {
        return $"{band.Trim().ToUpperInvariant()}|{mode.Trim().ToUpperInvariant()}";
    }

    private static DxccCandidateStatus DetermineDxccStatus(DecodeMessage decode, WorkedStatusIndexes indexes)
    {
        if (string.IsNullOrWhiteSpace(decode.Dxcc))
            return DxccCandidateStatus.Unknown;

        if (!indexes.Dxcc.TryGetValue(decode.Dxcc, out var status) || !status.WorkedAny)
            return DxccCandidateStatus.NotWorked;

        return status.ConfirmedAny ? DxccCandidateStatus.Confirmed : DxccCandidateStatus.WorkedUnconfirmed;
    }

    private static int FreshnessScore(double ageSeconds, DecodeMessage decode, AppSettings settings)
    {
        var maxAge = StaleWindowSeconds(decode, settings);
        if (ageSeconds >= maxAge)
            return 0;
        return Math.Clamp((int)Math.Round((maxAge - ageSeconds) * 5), 0, 450);
    }

    private static int SignalScore(int snr)
    {
        return snr switch
        {
            >= -10 => 80,
            >= -16 => 55,
            >= -22 => 30,
            _ => 5
        };
    }

    private static double DistanceScore(double? distanceMiles)
    {
        return distanceMiles.HasValue
            ? Math.Clamp(distanceMiles.Value / 12000.0 * 100.0, 0, 100)
            : 0;
    }

    private static double AdjustedDxValueScore(double globalRarityScore, double ukDesirabilityScore, double distanceScore, AppSettings settings)
    {
        var globalWeight = Math.Max(0, settings.GlobalRarityWeight);
        var ukWeight = Math.Max(0, settings.UkDesirabilityWeight);
        var distanceWeight = Math.Max(0, settings.DistanceWeight);
        var total = globalWeight + ukWeight + distanceWeight;
        if (total <= 0)
        {
            globalWeight = 0.50;
            ukWeight = 0.35;
            distanceWeight = 0.15;
            total = 1.0;
        }

        return (Math.Clamp(globalRarityScore, 0, 100) * globalWeight
            + Math.Clamp(ukDesirabilityScore, 0, 100) * ukWeight
            + Math.Clamp(distanceScore, 0, 100) * distanceWeight) / total;
    }

    private static int PenaltyScore(DecodeMessage decode, IReadOnlyCollection<AdifQso> logbook, IEnumerable<DecodeMessage> recentDecodes, AppSettings settings)
    {
        var penalty = 0;
        if (decode.ParseConfidence == ParseConfidence.Low)
            penalty += 5000;
        if (decode.AgeSeconds() > StaleWindowSeconds(decode, settings))
            penalty += 5000;
        if (recentDecodes.Count(d => d.Callsign.Equals(decode.Callsign, StringComparison.OrdinalIgnoreCase) && d.ReceivedAt > DateTime.Now.AddMinutes(-5)) > 3)
            penalty += 30;
        if (logbook.Any(q => q.Call.Equals(decode.Callsign, StringComparison.OrdinalIgnoreCase) && q.QsoDate.HasValue && q.QsoDate.Value > DateTime.Today.AddDays(-30)))
            penalty += 120;
        return penalty;
    }

    private static int StaleWindowSeconds(DecodeMessage decode, AppSettings settings)
    {
        var normal = Math.Max(30, settings.CandidateMaxAgeSeconds);
        return decode.IsNewDxcc ? Math.Max(normal, settings.NewDxccStaleSeconds) : normal;
    }

    private static string ExplainSelection(CandidateRanking ranking)
    {
        return ranking.PriorityTier switch
        {
            10 => $"{ranking.PriorityTierName} beats worked-but-unconfirmed DXCC, grid, state, band, callsign and general DX needs; adjusted DX value breaks ties.",
            12 or 13 or 14 => $"{ranking.PriorityTierName} is enabled as an optional scoped DXCC target and follows only a globally new DXCC.",
            15 => $"{ranking.PriorityTierName} beats grid, state, band, callsign and general DX needs under the selected confirmation mode.",
            20 => "Rare-country repeat chasing is enabled; no needed DXCC is higher in this candidate set.",
            30 => "New-grid priority is enabled; this grid follows needed DXCC and precedes normal DX ranking. Normal DX ranking resumes when no new grid is available.",
            40 => "USA state need considered after DXCC/grid priorities.",
            _ => "Lower-tier candidate ranked after DXCC need, adjusted DX value, global rarity, UK desirability, distance, freshness and signal."
        };
    }

    private string ResolveName(string callsign)
    {
        return string.IsNullOrWhiteSpace(callsign) ? "" : _dxccResolver.Resolve(callsign)?.Name ?? "";
    }

    private DxccEntity? ResolveEntity(string callsign)
    {
        return string.IsNullOrWhiteSpace(callsign) ? null : _dxccResolver.Resolve(callsign);
    }

    private static bool Matches(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRealBandMode(string band, string mode)
    {
        return HasRealValue(band) && HasRealValue(mode);
    }

    private static bool HasRealValue(string value)
    {
        value = (value ?? "").Trim();
        return !string.IsNullOrWhiteSpace(value)
            && !value.Equals("Current", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("~", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("Current ~", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DecodeMessageAgeExtensions
{
    public static double AgeSeconds(this DecodeMessage decode) => Math.Max(0, (DateTime.Now - decode.ReceivedAt).TotalSeconds);
}
