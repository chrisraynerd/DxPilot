using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class WasStateEligibility
{
    private static readonly HashSet<string> WasDxccNumbers =
        new(StringComparer.OrdinalIgnoreCase) { "291", "6", "110" };

    private static readonly HashSet<string> WasCountryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "United States",
            "United States of America",
            "USA",
            "Alaska",
            "Hawaii"
        };

    public static bool IsEligible(AdifQso qso)
    {
        return IsEligible(qso.Dxcc, qso.Country);
    }

    public static bool IsEligible(DecodeMessage decode)
    {
        return IsEligible(decode.Dxcc, decode.EntityName)
            || IsEligible(decode.ContactableDxccNumber, decode.ContactableEntity);
    }

    public static bool IsEligible(string? dxccNumber, string? entityName)
    {
        var dxcc = (dxccNumber ?? "").Trim();
        var entity = (entityName ?? "").Trim();
        return WasDxccNumbers.Contains(dxcc) || WasCountryNames.Contains(entity);
    }

    public static string NormalizeState(string? state, bool includeDistrictOfColumbia)
    {
        return UsStateValidator.Normalize(state, includeDistrictOfColumbia);
    }

    public static string NormalizeState(AdifQso qso, bool includeDistrictOfColumbia)
    {
        return IsEligible(qso)
            ? NormalizeState(qso.State, includeDistrictOfColumbia)
            : "";
    }
}
