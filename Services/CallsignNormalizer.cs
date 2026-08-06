using System.Text.RegularExpressions;

namespace JtdxAutoResume.V3.Services;

public static class CallsignNormalizer
{
    private static readonly Regex ValidCallsign = new(@"^[A-Z0-9/]{3,20}$", RegexOptions.Compiled);

    public static string Normalize(string callsign)
    {
        return string.IsNullOrWhiteSpace(callsign) ? "" : callsign.Trim().ToUpperInvariant();
    }

    public static bool IsValidLookupCallsign(string callsign)
    {
        var normal = Normalize(callsign);
        return normal.Length > 0 && ValidCallsign.IsMatch(normal);
    }

    public static bool IsPotentiallyPortable(string callsign)
    {
        var normal = Normalize(callsign);
        return normal.EndsWith("/P", StringComparison.OrdinalIgnoreCase)
            || normal.EndsWith("/M", StringComparison.OrdinalIgnoreCase)
            || normal.EndsWith("/MM", StringComparison.OrdinalIgnoreCase)
            || normal.EndsWith("/AM", StringComparison.OrdinalIgnoreCase);
    }
}
