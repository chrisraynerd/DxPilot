using System.Globalization;
using System.IO;
using System.Reflection;

namespace JtdxAutoResume.V3.Services;

public sealed record DxccEntity(
    string Code,
    string Name,
    string Callsign = "",
    string NormalisedCallsign = "",
    string LookupPrefix = "",
    string Continent = "",
    int? CqZone = null,
    int? ItuZone = null,
    double? Latitude = null,
    double? Longitude = null,
    string Source = "",
    string Confidence = "Low",
    string Reason = "");

public sealed class DxccResolver
{
    private static readonly string[] StrippableSuffixes = { "P", "M", "QRP", "A", "PORTABLE" };
    private readonly List<CountryRule> _rules = new();
    private readonly Dictionary<string, DxccEntity?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CountryRule> _exact = new(StringComparer.OrdinalIgnoreCase);
    private CountryRule[] _rulesByLength = Array.Empty<CountryRule>();

    public DxccResolver(string? countryFilePath = null)
    {
        CountryFilePath = ResolveCountryFilePath(countryFilePath);
        Load();
    }

    public string CountryFilePath { get; }
    public string SourceName { get; private set; } = "Built-in fallback";
    public int EntityCount { get; private set; }
    public int PrefixRuleCount => _rules.Count;
    public int ExactExceptionCount => _exact.Count;
    public DateTime? CountryFileLastModified { get; private set; }
    public DateTime? LoadedAt { get; private set; }
    public bool IsFallbackMode { get; private set; }
    public bool IsActive => PrefixRuleCount > 0;
    public string LoadError { get; private set; } = "";
    public string Diagnostics => $"CTY resolver active: {(IsActive ? "yes" : "no")}; Country file path: {CountryFilePath}; Source: {SourceName}; Entities loaded: {EntityCount}; Prefix rules loaded: {PrefixRuleCount}; Exact-call rules loaded: {ExactExceptionCount}; Fallback mode active: {(IsFallbackMode ? "yes" : "no")}; Last loaded: {(LoadedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "n/a")}; Last modified: {(CountryFileLastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "n/a")}; Load errors: {(string.IsNullOrWhiteSpace(LoadError) ? "none" : LoadError)}{(IsFallbackMode ? "; DXCC resolver running in limited fallback mode; entity matching may be incomplete." : "")}";

    public IReadOnlyList<DxccEntityDefinition> EntityDefinitions()
    {
        return _rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Dxcc) && !string.IsNullOrWhiteSpace(r.Entity))
            .GroupBy(r => r.Dxcc, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DxccEntityDefinition(g.Key, g.First().Entity))
            .ToList();
    }

    public DxccEntity? Resolve(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return null;

        var original = callsign.Trim().ToUpperInvariant();
        if (_cache.TryGetValue(original, out var cached))
            return cached;

        DxccEntity? resolved;
        if (original.EndsWith("/MM", StringComparison.Ordinal) || original.Equals("MM", StringComparison.Ordinal))
        {
            resolved = Unknown(original, original, "Maritime mobile / ambiguous");
            _cache[original] = resolved;
            return resolved;
        }

        var normalized = NormaliseCallsign(original);
        if (_exact.TryGetValue(normalized, out var exactRule))
        {
            resolved = ToEntity(exactRule, original, normalized, normalized, "CTY exact exception", "High", "Exact callsign exception");
            _cache[original] = resolved;
            return resolved;
        }

        var portablePrefix = PortablePrefix(original);
        if (!string.IsNullOrWhiteSpace(portablePrefix) && TryPrefix(portablePrefix, out var portableRule, out var portableMatched))
        {
            resolved = ToEntity(portableRule, original, normalized, portableMatched, "Portable prefix", "High", $"Portable prefix {portablePrefix}");
            _cache[original] = resolved;
            return resolved;
        }

        // KG4 is shared by Guantanamo Bay and ordinary US amateur calls. The
        // Guantanamo allocation is KG4 plus two letters (KG4AA-KG4ZZ); standard
        // FCC-issued KG4 calls with one or three suffix letters belong to the
        // United States. CTY exact-call exceptions above remain authoritative.
        if (IsStandardUnitedStatesKg4(normalized)
            && TryFindRule("291", "K", out var unitedStatesRule))
        {
            resolved = ToEntity(
                unitedStatesRule,
                original,
                normalized,
                "KG4",
                "KG4 allocation rule",
                "High",
                "Standard US KG4 callsign (KG4 plus one or three letters)");
            _cache[original] = resolved;
            return resolved;
        }

        if (TryPrefix(normalized, out var rule, out var matched))
        {
            var source = normalized.Equals(original, StringComparison.OrdinalIgnoreCase) ? "CTY prefix" : "Suffix stripped + CTY prefix";
            resolved = ToEntity(rule, original, normalized, matched, source, "High", $"Longest prefix {matched}");
            _cache[original] = resolved;
            return resolved;
        }

        _cache[original] = null;
        return null;
    }

    public IReadOnlyList<string> RunSelfTest()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EX8BT"] = "Kyrgyzstan",
            ["4L/SP1MVG"] = "Georgia",
            ["OE5RLP"] = "Austria",
            ["SV1EAG"] = "Greece",
            ["LA1WWA"] = "Norway",
            ["DL0MLU/P"] = "Germany",
            ["PH7GD"] = "Netherlands",
            ["R6DJR"] = "European Russia",
            ["CR7BRV"] = "Portugal",
            ["YU7AS"] = "Serbia",
            ["4X0Y"] = "Israel",
            ["EA9PB"] = "Ceuta & Melilla",
            ["CR2WPA"] = "Azores",
            ["UN0GY"] = "Kazakhstan",
            ["BY6SX"] = "China",
            ["M8KKH"] = "England",
            ["EI7LK"] = "Ireland",
            ["MI0OBC"] = "Northern Ireland",
            ["KG4A"] = "United States",
            ["KG4ABC"] = "United States",
            ["KG4AC"] = "Guantanamo Bay",
            ["KG4AA"] = "Guantanamo Bay",
            ["KG44WW"] = "Guantanamo Bay"
        };

        var failures = new List<string>();
        foreach (var item in expected)
        {
            var resolved = Resolve(item.Key);
            if (resolved == null || !resolved.Name.Equals(item.Value, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{item.Key} -> {resolved?.Name ?? "Unknown"} expected {item.Value}");
        }

        foreach (var call in new[] { "UA9LLE", "RA9J" })
        {
            if (Resolve(call) == null)
                failures.Add($"{call} -> Unknown expected CTY-defined result");
        }

        return failures;
    }

    public string ResolveDiagnostic(string callsign)
    {
        var input = callsign.Trim();
        var normalised = string.IsNullOrWhiteSpace(input) ? "" : NormaliseCallsign(input);
        var resolved = Resolve(input);
        if (resolved == null)
        {
            return $"Input callsign: {input}\n"
                + $"Normalised callsign: {normalised}\n"
                + "Entity: Unknown\n"
                + "DXCC number: \n"
                + "Source: Unknown\n"
                + "Confidence: Low\n"
                + "Reason: No CTY prefix match";
        }

        return $"Input callsign: {input}\n"
            + $"Normalised callsign: {resolved.NormalisedCallsign}\n"
            + $"Lookup prefix: {resolved.LookupPrefix}\n"
            + $"Matched rule: {resolved.Source}\n"
            + $"Entity: {resolved.Name}\n"
            + $"DXCC number: {resolved.Code}\n"
            + $"Continent: {resolved.Continent}\n"
            + $"CQ zone: {resolved.CqZone}\n"
            + $"ITU zone: {resolved.ItuZone}\n"
            + $"Latitude/longitude: {resolved.Latitude}, {resolved.Longitude}\n"
            + $"Source: {resolved.Source}\n"
            + $"Confidence: {resolved.Confidence}\n"
            + $"Reason: {resolved.Reason}";
    }

    public static string NormaliseCallsign(string callsign)
    {
        var call = callsign.Trim().Trim('<', '>').ToUpperInvariant();
        var parts = call.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
            return call;

        if (StrippableSuffixes.Contains(parts[^1], StringComparer.OrdinalIgnoreCase))
            return string.Join('/', parts.Take(parts.Length - 1));

        return parts.OrderByDescending(p => p.Length).First();
    }

    private bool TryPrefix(string callsign, out CountryRule rule, out string matchedPrefix)
    {
        var cleaned = callsign.Replace("/", "", StringComparison.Ordinal);
        foreach (var candidate in _rulesByLength)
        {
            if (cleaned.StartsWith(candidate.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                rule = candidate;
                matchedPrefix = candidate.Prefix;
                return true;
            }
        }

        rule = default!;
        matchedPrefix = "";
        return false;
    }

    private bool TryFindRule(string dxcc, string preferredPrefix, out CountryRule rule)
    {
        rule = _rules.FirstOrDefault(candidate =>
            candidate.Dxcc.Equals(dxcc, StringComparison.OrdinalIgnoreCase)
            && candidate.Prefix.Equals(preferredPrefix, StringComparison.OrdinalIgnoreCase))!;
        return rule != null;
    }

    private static bool IsStandardUnitedStatesKg4(string callsign)
    {
        if (!callsign.StartsWith("KG4", StringComparison.OrdinalIgnoreCase)
            || callsign.Length is not (4 or 6))
        {
            return false;
        }

        return callsign.AsSpan(3).ToArray()
            .All(character => character is >= 'A' and <= 'Z');
    }

    private void Load()
    {
        try
        {
            LoadedAt = DateTime.Now;
            if (File.Exists(CountryFilePath))
            {
                foreach (var line in File.ReadLines(CountryFilePath))
                    LoadCsvLine(line);

                if (_rules.Count == 0)
                    throw new InvalidDataException("Country file loaded but no prefix rules were parsed.");

                CountryFileLastModified = File.GetLastWriteTime(CountryFilePath);
                SourceName = "CTY CSV";
                IsFallbackMode = false;
                EntityCount = _rules.Select(r => r.Dxcc).Distinct().Count();
                _rulesByLength = _rules
                    .GroupBy(r => r.Prefix, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderByDescending(r => r.Prefix.Length)
                    .ToArray();
                return;
            }
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            _rules.Clear();
            _exact.Clear();
        }

        LoadFallback();
        IsFallbackMode = true;
        EntityCount = _rules.Select(r => r.Dxcc).Distinct().Count();
        _rulesByLength = _rules
                .OrderByDescending(r => r.Prefix.Length)
                .ToArray();
    }

    private void LoadCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            return;

        var parts = SplitCsv(line);
        if (parts.Count < 10)
            return;

        var basePrefix = CleanPrefix(parts[0]);
        var rule = new CountryRule(
            basePrefix,
            parts[1].Trim(),
            parts[2].Trim(),
            parts[3].Trim(),
            ParseInt(parts[4]),
            ParseInt(parts[5]),
            ParseDouble(parts[6]),
            ParseDouble(parts[7]));

        AddRule(rule);

        foreach (var token in parts[9].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var prefix = CleanPrefix(token);
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            var tokenRule = rule with { Prefix = prefix };
            if (token.StartsWith("=", StringComparison.Ordinal))
                _exact[prefix] = tokenRule;
            else
                AddRule(tokenRule);
        }
    }

    private void AddRule(CountryRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Prefix))
            return;

        _rules.Add(rule);
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
            }
            else
            {
                current += c;
            }
        }

        result.Add(current.TrimEnd(';'));
        return result;
    }

    private static string CleanPrefix(string value)
    {
        var cleaned = value.Trim().TrimEnd(';').TrimStart('=').ToUpperInvariant();
        var paren = cleaned.IndexOf('(');
        if (paren >= 0)
            cleaned = cleaned[..paren];
        var bracket = cleaned.IndexOf('[');
        if (bracket >= 0)
            cleaned = cleaned[..bracket];
        return cleaned;
    }

    private static string PortablePrefix(string callsign)
    {
        var parts = callsign.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return "";

        var first = parts[0].ToUpperInvariant();
        if (StrippableSuffixes.Contains(first, StringComparer.OrdinalIgnoreCase) || first.Equals("MM", StringComparison.OrdinalIgnoreCase))
            return "";

        return first;
    }

    private static DxccEntity ToEntity(CountryRule rule, string callsign, string normalized, string lookupPrefix, string source, string confidence, string reason)
    {
        return new DxccEntity(rule.Dxcc, rule.Entity, callsign, normalized, lookupPrefix, rule.Continent, rule.CqZone, rule.ItuZone, rule.Latitude, rule.Longitude, source, confidence, reason);
    }

    private static DxccEntity Unknown(string callsign, string normalized, string reason)
    {
        return new DxccEntity("", "Unknown", callsign, normalized, "", "", null, null, null, null, "Unknown", "Low", reason);
    }

    private static string ResolveCountryFilePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "Data", "cty.csv");
        if (File.Exists(bundled))
            return bundled;

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
        return Path.Combine(assemblyDir, "Data", "cty.csv");
    }

    private void LoadFallback()
    {
        SourceName = "Built-in fallback";
        foreach (var item in new[]
        {
            ("EX", "135", "Kyrgyzstan"), ("4L", "75", "Georgia"), ("OE", "206", "Austria"), ("SV", "236", "Greece"),
            ("LA", "266", "Norway"), ("DL", "230", "Germany"), ("PA", "263", "Netherlands"), ("R", "54", "European Russia"),
            ("UA9", "15", "Asiatic Russia"), ("CR7", "272", "Portugal"), ("YU", "296", "Serbia"), ("4X", "336", "Israel"),
            ("EA9", "32", "Ceuta & Melilla"), ("CR2", "149", "Azores"), ("UN", "130", "Kazakhstan"), ("BY", "318", "China"),
            ("RA9", "15", "Asiatic Russia"), ("M", "223", "England"), ("EI", "245", "Ireland"), ("EA8", "29", "Canary Islands"),
            ("EA", "281", "Spain"), ("G", "223", "England"), ("MI", "265", "Northern Ireland")
        })
        {
            AddRule(new CountryRule(item.Item1, item.Item3, item.Item2, "", null, null, null, null));
        }
    }

    private static int? ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static double? ParseDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private sealed record CountryRule(string Prefix, string Entity, string Dxcc, string Continent, int? CqZone, int? ItuZone, double? Latitude, double? Longitude);
}

public sealed record DxccEntityDefinition(string DxccNumber, string EntityName);
