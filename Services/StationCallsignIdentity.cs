using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class StationCallsignIdentity
{
    public const string AllCallsignsKey = "ALL";

    private static readonly HashSet<string> NonIdentitySuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "P", "M", "MM", "AM", "QRP", "A", "NHS"
    };

    public static string Canonicalize(string callsign)
    {
        var clean = (callsign ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
            return parts[0];

        var candidates = parts
            .Where(part => !NonIdentitySuffixes.Contains(part))
            .Where(LooksLikeCallsign)
            .OrderByDescending(CallsignIdentityScore)
            .ThenByDescending(part => part.Length)
            .ToList();

        return candidates.FirstOrDefault() ?? parts[0];
    }

    public static IReadOnlyList<CallsignLogProfile> BuildProfiles(
        IReadOnlyCollection<AdifQso> qsos,
        string currentCallsign)
    {
        var currentKey = Canonicalize(currentCallsign);
        var groups = qsos
            .Where(qso => !string.IsNullOrWhiteSpace(qso.StationCallsign))
            .GroupBy(qso => Canonicalize(qso.StationCallsign), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new CallsignLogProfile(
                group.Key,
                group.Key,
                ProfileLabel(group.Key, group.Count(), group.Key.Equals(currentKey, StringComparison.OrdinalIgnoreCase)),
                group.Count(),
                false,
                group.Key.Equals(currentKey, StringComparison.OrdinalIgnoreCase),
                group.Select(qso => qso.StationCallsign.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentKey)
            && groups.All(profile => !profile.Key.Equals(currentKey, StringComparison.OrdinalIgnoreCase)))
        {
            groups.Add(new CallsignLogProfile(
                currentKey,
                currentKey,
                ProfileLabel(currentKey, 0, true),
                0,
                false,
                true,
                new[] { currentKey }));
        }

        var profiles = new List<CallsignLogProfile>
        {
            new(
                AllCallsignsKey,
                "",
                $"All callsigns · {qsos.Count:N0} QSOs",
                qsos.Count,
                true,
                false,
                Array.Empty<string>())
        };
        profiles.AddRange(groups
            .OrderByDescending(profile => profile.IsCurrentCallsign)
            .ThenByDescending(profile => profile.QsoCount)
            .ThenBy(profile => profile.Callsign, StringComparer.OrdinalIgnoreCase));
        return profiles;
    }

    public static bool Matches(string stationCallsign, string profileKey)
    {
        return profileKey.Equals(AllCallsignsKey, StringComparison.OrdinalIgnoreCase)
            || Canonicalize(stationCallsign).Equals(profileKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string ProfileLabel(string callsign, int count, bool current)
    {
        var currentLabel = current ? " · current" : "";
        return $"{callsign}{currentLabel} · {count:N0} QSOs";
    }

    private static bool LooksLikeCallsign(string value)
    {
        return value.Length >= 3
            && value.Any(char.IsLetter)
            && value.Any(char.IsDigit)
            && value.All(char.IsLetterOrDigit);
    }

    private static int CallsignIdentityScore(string value)
    {
        var score = value.Length * 10;
        if (value.Length >= 4)
            score += 30;
        if (char.IsLetter(value[^1]))
            score += 10;
        if (value.Count(char.IsLetter) >= 3)
            score += 10;
        return score;
    }
}
