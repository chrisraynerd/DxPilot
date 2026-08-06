namespace JtdxAutoResume.V3.Services;

public static class UsStateValidator
{
    private static readonly string[] OrderedStates =
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
        "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
        "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
        "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
        "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY"
    };
    private static readonly HashSet<string> States =
        new(OrderedStates, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> StandardStateCodes => OrderedStates;

    public static string Normalize(string? state, bool includeDistrictOfColumbia)
    {
        if (string.IsNullOrWhiteSpace(state))
            return "";

        var normal = state.Trim().ToUpperInvariant();
        if (States.Contains(normal))
            return normal;

        return includeDistrictOfColumbia && normal == "DC" ? "DC" : "";
    }
}
