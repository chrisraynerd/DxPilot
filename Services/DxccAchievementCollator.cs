using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class DxccAchievementCollator
{
    public IReadOnlyList<AchievementQsoDetail> BuildQsoDetails(
        string dxccNumber,
        IEnumerable<AdifQso> qsos,
        IReadOnlyList<DxccEntityDefinition> entities,
        DxccResolver resolver)
    {
        var entityByNumber = BuildEntityNumberMap(entities);
        var entityNumberByName = BuildEntityNameMap(entities);
        return qsos
            .Where(qso => ResolveDxccNumber(qso, entityByNumber, entityNumberByName, resolver)
                .Equals(dxccNumber, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(qso => qso.QsoDate ?? DateTime.MinValue)
            .ThenByDescending(qso => qso.TimeOn, StringComparer.OrdinalIgnoreCase)
            .Select(qso => new AchievementQsoDetail
            {
                Call = DisplayValue(qso.Call),
                StationCallsign = DisplayValue(qso.StationCallsign),
                QsoDate = qso.QsoDate,
                DateDisplay = qso.QsoDate?.ToString("dd MMM yyyy") ?? DisplayAdifDate(qso.QsoDateText),
                TimeDisplay = DisplayAdifTime(qso.TimeOn),
                Band = DisplayValue(qso.Band),
                Mode = DisplayValue(qso.EffectiveMode),
                Frequency = DisplayValue(qso.Freq),
                Grid = DisplayValue(qso.Grid),
                LotwConfirmed = qso.LotwConfirmed,
                PaperDisplay = qso.PaperConfirmed ? "Confirmed" : "No",
                EqslDisplay = qso.EqslConfirmed ? "Confirmed" : "No",
                Source = DisplayValue(qso.Source)
            })
            .ToList();
    }

    public IReadOnlyList<AchievementDxccRow> Build(
        IEnumerable<AdifQso> qsos,
        IEnumerable<SessionDxOpportunity> history,
        IReadOnlyList<DxccEntityDefinition> entities,
        DxccResolver resolver,
        DxccRarityService rarityService)
    {
        var entityByNumber = BuildEntityNumberMap(entities);
        var entityNumberByName = BuildEntityNameMap(entities);
        var qsosByDxcc = new Dictionary<string, List<AdifQso>>(StringComparer.OrdinalIgnoreCase);
        var seenByDxcc = new Dictionary<string, SeenAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var qso in qsos)
        {
            var dxccNumber = ResolveDxccNumber(qso, entityByNumber, entityNumberByName, resolver);
            if (string.IsNullOrWhiteSpace(dxccNumber))
                continue;

            if (!qsosByDxcc.TryGetValue(dxccNumber, out var entityQsos))
            {
                entityQsos = new List<AdifQso>();
                qsosByDxcc[dxccNumber] = entityQsos;
            }

            entityQsos.Add(qso);
        }

        foreach (var item in history)
        {
            var dxccNumber = ResolveDxccNumber(item, entityByNumber, entityNumberByName, resolver);
            if (string.IsNullOrWhiteSpace(dxccNumber))
                continue;

            if (!seenByDxcc.TryGetValue(dxccNumber, out var seen))
            {
                seen = new SeenAggregate();
                seenByDxcc[dxccNumber] = seen;
            }

            seen.Count += Math.Max(1, Math.Max(item.SeenCount, item.DirectlyHeardCount));
            if (!string.IsNullOrWhiteSpace(item.Call))
                seen.Calls.Add(item.Call.Trim());
        }

        return entityByNumber.Values
            .Select(entity => BuildRow(
                entity,
                qsosByDxcc.GetValueOrDefault(entity.DxccNumber) ?? new List<AdifQso>(),
                seenByDxcc.GetValueOrDefault(entity.DxccNumber),
                rarityService.Get(entity.DxccNumber, entity.EntityName)))
            .OrderBy(row => row.ClubLogRank ?? int.MaxValue)
            .ThenBy(row => row.EntityName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AchievementDxccRow BuildRow(
        DxccEntityDefinition entity,
        IReadOnlyCollection<AdifQso> qsos,
        SeenAggregate? seen,
        DxccRarityInfo rarity)
    {
        var lotwCount = qsos.Count(qso => qso.LotwConfirmed);
        var qsoCount = qsos.Count;
        return new AchievementDxccRow
        {
            DxccNumber = entity.DxccNumber,
            EntityName = entity.EntityName,
            ClubLogRank = rarity.ClubLogRank,
            UKDesirability = rarity.UKDesirability,
            DifficultyBand = string.IsNullOrWhiteSpace(rarity.DesirabilityBand) ? "Unranked" : rarity.DesirabilityBand,
            SeenCount = seen?.Count ?? 0,
            SeenCallCount = seen?.Calls.Count ?? 0,
            QsoCount = qsoCount,
            UnconfirmedQsoCount = qsoCount - lotwCount,
            LotwConfirmedQsoCount = lotwCount,
            LastWorked = qsos.Select(qso => qso.QsoDate).Where(date => date.HasValue).Max(),
            Bands = JoinValues(qsos.Select(qso => qso.Band), BandOrder),
            Modes = JoinValues(qsos.Select(qso => qso.EffectiveMode), null),
            StatusKey = lotwCount > 0
                ? "LotwConfirmed"
                : qsoCount > 0 ? "WorkedUnconfirmed" : "Needed"
        };
    }

    private static string ResolveDxccNumber(
        SessionDxOpportunity item,
        IReadOnlyDictionary<string, DxccEntityDefinition> entityByNumber,
        IReadOnlyDictionary<string, string> entityNumberByName,
        DxccResolver resolver)
    {
        var storedDxcc = item.DxccNumber.Trim();
        if (!string.IsNullOrWhiteSpace(storedDxcc) && entityByNumber.ContainsKey(storedDxcc))
            return storedDxcc;

        if (!string.IsNullOrWhiteSpace(item.Call))
        {
            var resolved = resolver.Resolve(item.Call);
            if (resolved != null && entityByNumber.ContainsKey(resolved.Code))
                return resolved.Code;
        }

        var entity = DxccRarityService.NormaliseEntityName(item.Entity);
        return entityNumberByName.GetValueOrDefault(entity) ?? "";
    }

    private static string ResolveDxccNumber(
        AdifQso qso,
        IReadOnlyDictionary<string, DxccEntityDefinition> entityByNumber,
        IReadOnlyDictionary<string, string> entityNumberByName,
        DxccResolver resolver)
    {
        var adifDxcc = qso.Dxcc.Trim();
        if (!string.IsNullOrWhiteSpace(adifDxcc) && entityByNumber.ContainsKey(adifDxcc))
            return adifDxcc;

        if (!string.IsNullOrWhiteSpace(qso.Call))
        {
            var resolved = resolver.Resolve(qso.Call);
            if (resolved != null && entityByNumber.ContainsKey(resolved.Code))
                return resolved.Code;
        }

        var country = DxccRarityService.NormaliseEntityName(qso.Country);
        return entityNumberByName.GetValueOrDefault(country) ?? "";
    }

    private static string JoinValues(IEnumerable<string> values, IReadOnlyList<string>? preferredOrder)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var ordered = preferredOrder == null
            ? distinct.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            : distinct.OrderBy(value => PreferredIndex(value, preferredOrder)).ThenBy(value => value, StringComparer.OrdinalIgnoreCase);
        var result = ordered.ToList();
        return result.Count == 0 ? "—" : string.Join(", ", result);
    }

    private static Dictionary<string, DxccEntityDefinition> BuildEntityNumberMap(IEnumerable<DxccEntityDefinition> entities)
    {
        return entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.DxccNumber))
            .GroupBy(entity => entity.DxccNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildEntityNameMap(IEnumerable<DxccEntityDefinition> entities)
    {
        return entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.EntityName))
            .GroupBy(entity => DxccRarityService.NormaliseEntityName(entity.EntityName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DxccNumber, StringComparer.OrdinalIgnoreCase);
    }

    private static string DisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    }

    private static string DisplayAdifDate(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length >= 8
            && DateTime.TryParseExact(digits[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("dd MMM yyyy");
        }

        return DisplayValue(value);
    }

    private static string DisplayAdifTime(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            >= 6 => $"{digits[..2]}:{digits.Substring(2, 2)}:{digits.Substring(4, 2)} UTC",
            >= 4 => $"{digits[..2]}:{digits.Substring(2, 2)} UTC",
            _ => DisplayValue(value)
        };
    }

    private static int PreferredIndex(string value, IReadOnlyList<string> preferredOrder)
    {
        for (var index = 0; index < preferredOrder.Count; index++)
        {
            if (preferredOrder[index].Equals(value, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }

    private static readonly IReadOnlyList<string> BandOrder =
    [
        "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "4m", "2m"
    ];

    private sealed class SeenAggregate
    {
        public int Count { get; set; }
        public HashSet<string> Calls { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
