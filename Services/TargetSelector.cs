using JtdxAutoResume.V3.Models;
using System.Text.RegularExpressions;

namespace JtdxAutoResume.V3.Services;

public sealed class TargetSelector
{
    private readonly DxTargetScorer _scorer;

    public TargetSelector(DxTargetScorer scorer)
    {
        _scorer = scorer;
    }

    public DxTarget? SelectBest(IEnumerable<DecodeMessage> decodes, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings)
    {
        return SelectRanked(decodes, logbook, indexes, settings, 1, includeActiveQso: true).FirstOrDefault();
    }

    public DxTarget? SelectBestDxSeen(IEnumerable<DecodeMessage> decodes, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings)
    {
        return SelectDxSeenRanked(decodes, logbook, indexes, settings, 1).FirstOrDefault();
    }

    public IReadOnlyList<DxTarget> SelectDxSeenRanked(IEnumerable<DecodeMessage> decodes, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings, int count)
    {
        var maxAge = Math.Max(30, settings.CandidateMaxAgeSeconds);
        var recent = decodes
            .Where(d => d.ReceivedAt > DateTime.Now.AddSeconds(-maxAge))
            .ToList();

        return OrderAndGroup(recent
                .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
                .Where(d => !string.IsNullOrWhiteSpace(d.ContactableCall))
                .Where(d => d.ParseConfidence != ParseConfidence.Low)
                .Select(d => MarkSelectability(_scorer.Score(d, logbook, indexes, recent, settings), d))
                .Where(t => t.Ranking.DxccStatus != DxccCandidateStatus.Unknown))
            .Take(Math.Max(1, count))
            .ToList();
    }

    public IReadOnlyList<DxTarget> SelectRanked(IEnumerable<DecodeMessage> decodes, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings, int count, bool includeActiveQso = true)
    {
        var maxAge = Math.Max(30, settings.CandidateMaxAgeSeconds);
        var recent = decodes
            .Where(d => d.ReceivedAt > DateTime.Now.AddSeconds(-maxAge))
            .ToList();

        var incomingMode = string.IsNullOrWhiteSpace(settings.AcceptIncomingCallsMode)
            ? (settings.AcceptIncomingCalls ? "Always" : "Off")
            : settings.AcceptIncomingCallsMode;
        var activeQso = includeActiveQso && !incomingMode.Equals("Off", StringComparison.OrdinalIgnoreCase)
            ? SelectActiveQso(recent, logbook, indexes, settings)
            : null;
        if (activeQso != null && incomingMode.Equals("Always", StringComparison.OrdinalIgnoreCase))
            return new[] { activeQso };

        var ranked = OrderAndGroup(recent
            .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
            .Where(d => !string.IsNullOrWhiteSpace(d.ContactableCall))
            .Where(d => d.Targetable)
            .Where(d => d.ParseConfidence != ParseConfidence.Low)
            .Select(d => MarkSelectability(_scorer.Score(d, logbook, indexes, recent, settings), d))
            .Where(t => t.Ranking.DxccStatus != DxccCandidateStatus.Unknown))
            .Take(Math.Max(1, count))
            .ToList();

        if (activeQso != null
            && incomingMode.Equals("OnlyIfNoBetterHunterTarget", StringComparison.OrdinalIgnoreCase)
            && ranked.Count == 0)
        {
            return new[] { activeQso };
        }

        return ranked;
    }

    private static IEnumerable<DxTarget> OrderAndGroup(IEnumerable<DxTarget> targets)
    {
        return targets
            .GroupBy(t => t.Callsign, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(t => t.Ranking.PriorityTier)
                .ThenByDescending(t => t.Ranking.AdjustedDxValueScore)
                .ThenByDescending(t => t.Ranking.GlobalRarityScore)
                .ThenByDescending(t => t.Ranking.UKDesirability)
                .ThenByDescending(t => t.Ranking.DistanceScore)
                .ThenByDescending(t => t.Ranking.IsSelectable)
                .ThenByDescending(t => t.Ranking.FreshnessScore)
                .ThenByDescending(t => t.Ranking.SignalScore)
                .ThenBy(t => t.Ranking.PenaltyScore)
                .First())
            .OrderBy(t => t.Ranking.PriorityTier)
            .ThenByDescending(t => t.Ranking.AdjustedDxValueScore)
            .ThenByDescending(t => t.Ranking.GlobalRarityScore)
            .ThenByDescending(t => t.Ranking.UKDesirability)
            .ThenByDescending(t => t.Ranking.DistanceScore)
            .ThenByDescending(t => t.Ranking.IsSelectable)
            .ThenByDescending(t => t.Ranking.FreshnessScore)
            .ThenByDescending(t => t.Ranking.SignalScore)
            .ThenBy(t => t.Ranking.PenaltyScore);
    }

    private static DxTarget MarkSelectability(DxTarget target, DecodeMessage decode)
    {
        if (decode.ParseConfidence == ParseConfidence.Low)
        {
            target.Ranking.IsSelectable = false;
            target.Ranking.SelectabilityStatus = "Not selectable";
            target.Ranking.NotSelectedReason = "Low parse confidence";
            return target;
        }

        if (!decode.Targetable)
        {
            target.Ranking.IsSelectable = false;
            target.Ranking.SelectabilityStatus = "Not selectable";
            target.Ranking.NotSelectedReason = string.IsNullOrWhiteSpace(decode.TargetabilityReason)
                ? "Decode is not targetable"
                : decode.TargetabilityReason;
            return target;
        }

        target.Ranking.IsSelectable = true;
        target.Ranking.SelectabilityStatus = "Candidate-level selectable";
        target.Ranking.NotSelectedReason = "";
        return target;
    }

    private DxTarget? SelectActiveQso(List<DecodeMessage> recent, IReadOnlyCollection<AdifQso> logbook, WorkedStatusIndexes indexes, AppSettings settings)
    {
        var myCall = settings.MyCallsign.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(myCall))
            return null;

        foreach (var decode in recent.Where(d => d.ReceivedAt > DateTime.Now.AddMinutes(-4)))
        {
            var calls = ExtractCallsigns(decode.RawText);
            if (!calls.Contains(myCall, StringComparer.OrdinalIgnoreCase))
                continue;

            var partner = calls.FirstOrDefault(c => !c.Equals(myCall, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(partner))
                continue;

            var activeDecode = CopyDecode(decode);
            activeDecode.Callsign = partner;
            var target = _scorer.Score(activeDecode, logbook, indexes, recent, settings);
            target.Score += 5000;
            target.Ranking.FinalScore += 5000;
            target.Reasons.Insert(0, $"Active QSO with {partner}");
            return target;
        }

        return null;
    }

    private static List<string> ExtractCallsigns(string text)
    {
        return Regex.Matches(text.ToUpperInvariant(), @"\b[A-Z0-9/]{3,12}\b")
            .Select(m => m.Value.Trim('/'))
            .Where(IsLikelyCallsign)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsLikelyCallsign(string value)
    {
        return value.Any(char.IsDigit)
            && value.Any(char.IsLetter)
            && value.Length is >= 3 and <= 12
            && !IsGrid(value);
    }

    private static bool IsGrid(string value)
    {
        return value.Length is 4 or 6
            && char.IsLetter(value[0])
            && char.IsLetter(value[1])
            && char.IsDigit(value[2])
            && char.IsDigit(value[3]);
    }

    private static DecodeMessage CopyDecode(DecodeMessage decode)
    {
        return new DecodeMessage
        {
            ReceivedAt = decode.ReceivedAt,
            DecodeTime = decode.DecodeTime,
            Snr = decode.Snr,
            Dt = decode.Dt,
            AudioOffset = decode.AudioOffset,
            Mode = decode.Mode,
            RawText = decode.RawText,
            SourceAppId = decode.SourceAppId,
            MessageType = decode.MessageType,
            IsCq = decode.IsCq,
            Call1 = decode.Call1,
            AddressedCall = decode.AddressedCall,
            Call1Entity = decode.Call1Entity,
            Call2 = decode.Call2,
            HeardCall = decode.HeardCall,
            Call2Entity = decode.Call2Entity,
            Payload = decode.Payload,
            GridOwnerCall = decode.GridOwnerCall,
            GridEntity = decode.GridEntity,
            PrimaryDisplayCall = decode.PrimaryDisplayCall,
            PrimaryDisplayEntity = decode.PrimaryDisplayEntity,
            PossibleHuntCalls = decode.PossibleHuntCalls,
            ContactableCall = decode.ContactableCall,
            HuntTarget = decode.HuntTarget,
            HuntTargetEntity = decode.HuntTargetEntity,
            HuntTargetReason = decode.HuntTargetReason,
            IsReport = decode.IsReport,
            IsRReport = decode.IsRReport,
            IsRrr = decode.IsRrr,
            IsRR73 = decode.IsRR73,
            Is73 = decode.Is73,
            IsHashedOrCompound = decode.IsHashedOrCompound,
            ContainsMyCall = decode.ContainsMyCall,
            Targetable = decode.Targetable,
            TargetabilityReason = decode.TargetabilityReason,
            ParserReason = decode.ParserReason,
            ParserInterpretation = decode.ParserInterpretation,
            ParseConfidence = decode.ParseConfidence,
            ParseDebugLine = decode.ParseDebugLine,
            Callsign = decode.Callsign,
            Grid = decode.Grid,
            Dxcc = decode.Dxcc,
            EntityName = decode.EntityName,
            State = decode.State,
            Band = decode.Band,
            DistanceKm = decode.DistanceKm,
            IsNewDxcc = decode.IsNewDxcc,
            IsNewGrid = decode.IsNewGrid,
            IsNewState = decode.IsNewState,
            LowConfidence = decode.LowConfidence
        };
    }
}
