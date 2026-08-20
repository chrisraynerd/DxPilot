namespace JtdxAutoResume.V3.Services;

public static class QrzProfileUrl
{
    public static string Build(string? callsign)
    {
        var normalized = CallsignNormalizer.Normalize(callsign ?? "");
        if (!CallsignNormalizer.IsValidLookupCallsign(normalized))
            return "";

        return $"https://www.qrz.com/db/{Uri.EscapeDataString(normalized)}";
    }
}
