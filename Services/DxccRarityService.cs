using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed partial class DxccRarityService
{
    public const int DefaultRarityScore = 1000;
    public const int DefaultGlobalRarityScore = 15;
    private readonly Dictionary<string, DxccRarityInfo> _byDxcc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REP OF THE CONGO"] = "REPUBLIC OF THE CONGO",
        ["REPUBLIC OF CONGO"] = "REPUBLIC OF THE CONGO",
        ["CONGO REPUBLIC OF"] = "REPUBLIC OF THE CONGO",
        ["DEM REP OF THE CONGO"] = "DEMOCRATIC REPUBLIC OF THE CONGO",
        ["DEMOCRATIC REPUBLIC OF CONGO"] = "DEMOCRATIC REPUBLIC OF THE CONGO",
        ["FED REP OF GERMANY"] = "GERMANY",
        ["FEDERAL REPUBLIC OF GERMANY"] = "GERMANY",
        ["FED REP GERMANY"] = "GERMANY",
        ["USA"] = "UNITED STATES",
        ["UNITED STATES OF AMERICA"] = "UNITED STATES",
        ["CEUTA AND MELILLA"] = "CEUTA AND MELILLA",
        ["CEUTA MELILLA"] = "CEUTA AND MELILLA",
        ["ST HELENA"] = "SAINT HELENA",
        ["S COOK ISLANDS"] = "SOUTH COOK ISLANDS",
        ["N COOK ISLANDS"] = "NORTH COOK ISLANDS",
        ["BOSNIA HERZEGOVINA"] = "BOSNIA AND HERZEGOVINA",
        ["DPRK NORTH KOREA"] = "NORTH KOREA"
    };

    public DxccRarityDiagnostics Diagnostics { get; } = new();

    public void Load(string? configuredPath, DxccResolver resolver)
    {
        _byDxcc.Clear();
        Diagnostics.FilePath = ResolveRarityFilePath(configuredPath);
        Diagnostics.Loaded = false;
        Diagnostics.RowsLoaded = 0;
        Diagnostics.MatchedToDxcc = 0;
        Diagnostics.MatchedByExactName = 0;
        Diagnostics.MatchedByAlias = 0;
        Diagnostics.Unmatched = 0;
        Diagnostics.LoadError = "";
        Diagnostics.UnmatchedRows.Clear();

        if (string.IsNullOrWhiteSpace(Diagnostics.FilePath) || !File.Exists(Diagnostics.FilePath))
            return;

        try
        {
            var nameToDxcc = BuildNameMap(resolver);
            var rows = new List<CombinedDxccScoreRow>();
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(Diagnostics.FilePath))
            {
                var parts = SplitCsv(line);
                if (parts.Count < 2)
                    continue;

                if (headerMap.Count == 0 && parts.Any(part => part.Equals("ClubLogRank", StringComparison.OrdinalIgnoreCase)))
                {
                    for (var i = 0; i < parts.Count; i++)
                        headerMap[parts[i].Trim()] = i;
                    continue;
                }

                var rankText = Field(parts, headerMap, "ClubLogRank", 0);
                var entity = Field(parts, headerMap, "Entity", 1);
                if (!int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank)
                    || string.IsNullOrWhiteSpace(entity))
                {
                    continue;
                }

                rows.Add(new CombinedDxccScoreRow(
                    rank,
                    entity.Trim(),
                    ParseDouble(Field(parts, headerMap, "UKDesirability", 2)),
                    Field(parts, headerMap, "DesirabilityBand", 3),
                    Field(parts, headerMap, "UKRegionBand", 4),
                    Field(parts, headerMap, "Notes", 5),
                    Field(parts, headerMap, "SuggestedUse", 6)));
            }

            var maxRank = rows.Count == 0 ? 340 : rows.Max(r => r.Rank);
            foreach (var row in rows)
            {
                Diagnostics.RowsLoaded++;
                var normalised = NormaliseEntityName(row.Name);
                var matchSource = "ExactName";
                var confidence = "High";
                if (!nameToDxcc.TryGetValue(normalised, out var match))
                {
                    matchSource = "Alias";
                    var alias = _aliases.GetValueOrDefault(normalised);
                    if (string.IsNullOrWhiteSpace(alias) || !nameToDxcc.TryGetValue(NormaliseEntityName(alias), out match))
                    {
                        Diagnostics.Unmatched++;
                        Diagnostics.UnmatchedRows.Add($"{row.Name} -> {normalised}");
                        continue;
                    }
                }

                var info = new DxccRarityInfo
                {
                    DxccNumber = match.Dxcc,
                    CtyEntityName = match.Entity,
                    ClubLogEntityName = row.Name,
                    RarityRank = row.Rank,
                    RarityScore = Math.Max(1, ((maxRank + 1) - row.Rank) * 100),
                    GlobalRarityScore = RankToGlobalRarityScore(row.Rank, maxRank),
                    UKDesirability = row.UKDesirability,
                    DesirabilityBand = row.DesirabilityBand,
                    UKRegionBand = row.UKRegionBand,
                    SuggestedUse = row.SuggestedUse,
                    MatchConfidence = confidence,
                    MatchSource = matchSource,
                    Notes = string.Join("; ", new[]
                    {
                        matchSource == "Alias" ? $"Alias {normalised}" : "",
                        row.Notes
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
                };
                _byDxcc[info.DxccNumber] = info;
                Diagnostics.MatchedToDxcc++;
                if (matchSource == "Alias")
                    Diagnostics.MatchedByAlias++;
                else
                    Diagnostics.MatchedByExactName++;
            }

            Diagnostics.Loaded = Diagnostics.RowsLoaded > 0;
            Diagnostics.LastLoadedAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            Diagnostics.LoadError = ex.Message;
            _byDxcc.Clear();
        }
    }

    public DxccRarityInfo Get(string dxccNumber, string entityName = "")
    {
        if (!string.IsNullOrWhiteSpace(dxccNumber) && _byDxcc.TryGetValue(dxccNumber, out var info))
            return info;

        return new DxccRarityInfo
        {
            DxccNumber = dxccNumber,
            CtyEntityName = entityName,
            ClubLogEntityName = entityName,
            RarityScore = DefaultRarityScore,
            GlobalRarityScore = DefaultGlobalRarityScore,
            UKDesirability = 0,
            MatchConfidence = "Low",
            MatchSource = "Default",
            Notes = Diagnostics.Loaded ? "No rarity match for DXCC" : "No DXCC rarity file loaded; using default rarity scores."
        };
    }

    public static string NormaliseEntityName(string value)
    {
        var upper = value.ToUpperInvariant().Replace("&", " AND ");
        upper = upper.Replace("'", "");
        upper = upper.Replace(".", "");
        upper = PunctuationRegex().Replace(upper, " ");
        upper = WhiteSpaceRegex().Replace(upper, " ").Trim();
        return upper;
    }

    private static Dictionary<string, (string Dxcc, string Entity)> BuildNameMap(DxccResolver resolver)
    {
        var map = new Dictionary<string, (string Dxcc, string Entity)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in resolver.EntityDefinitions())
        {
            var key = NormaliseEntityName(entity.EntityName);
            if (!map.ContainsKey(key))
                map[key] = (entity.DxccNumber, entity.EntityName);
        }

        return map;
    }

    private static string ResolveRarityFilePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "Data", "DXCC-UK-Desirability-G1CEC.csv");
        if (File.Exists(bundled))
            return bundled;

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
        var assemblyBundled = Path.Combine(assemblyDir, "Data", "DXCC-UK-Desirability-G1CEC.csv");
        if (File.Exists(assemblyBundled))
            return assemblyBundled;

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(assemblyDir, "Data", "DXCC-UK-Desirability-G1CEC.csv")
            : configured;
    }

    private static string Field(IReadOnlyList<string> parts, IReadOnlyDictionary<string, int> headerMap, string name, int fallbackIndex)
    {
        var index = headerMap.TryGetValue(name, out var mapped) ? mapped : fallbackIndex;
        return index >= 0 && index < parts.Count ? parts[index].Trim() : "";
    }

    private static double ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : 0;
    }

    private static double RankToGlobalRarityScore(int rank, int maxRank)
    {
        if (rank <= 0 || maxRank <= 1)
            return DefaultGlobalRarityScore;

        return Math.Clamp(((maxRank + 1 - rank) / (double)maxRank) * 100.0, 0, 100);
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
                continue;
            }

            current += c;
        }

        result.Add(current);
        return result;
    }

    [GeneratedRegex("[,;:/()\\[\\]-]+")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhiteSpaceRegex();
}

internal sealed record CombinedDxccScoreRow(
    int Rank,
    string Name,
    double UKDesirability,
    string DesirabilityBand,
    string UKRegionBand,
    string Notes,
    string SuggestedUse);
