using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class TargetReasonFormatter
{
    public const string Unavailable = "Reason unavailable - check diagnostics";

    public static string FormatWantedReason(
        string category,
        NeedStatus needStatus,
        WantedScope scope,
        string value,
        string band,
        string mode)
    {
        category = NormaliseCategory(category);
        value = (value ?? "").Trim();
        band = CleanBandOrMode(band);
        mode = CleanBandOrMode(mode);

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(value))
            return Unavailable;

        if (needStatus is not (NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed))
            return Unavailable;

        var label = CategoryLabel(category);
        var prefix = needStatus == NeedStatus.NeverWorked ? $"New {label}" : $"Unconfirmed {label}";
        var scopeText = ScopeText(scope, band, mode);
        if (scopeText == null)
            return "Reason unavailable - missing band/mode";

        var detail = DetailText(needStatus, scope, band, mode);
        if (string.IsNullOrWhiteSpace(detail))
            return Unavailable;

        return $"{prefix}{scopeText}: {value} - {detail}";
    }

    public static string FormatDxcc(DxccCandidateStatus status, string entity, string band = "", string mode = "", WantedScope scope = WantedScope.Overall)
    {
        var need = status switch
        {
            DxccCandidateStatus.NotWorked => NeedStatus.NeverWorked,
            DxccCandidateStatus.WorkedUnconfirmed => NeedStatus.WorkedNotLoTWConfirmed,
            _ => NeedStatus.Unknown
        };
        return FormatWantedReason("DXCC", need, scope, entity, band, mode);
    }

    public static string FormatGrid(SimpleWorkedStatus? status, string grid, string band = "", string mode = "", WantedScope scope = WantedScope.Overall)
    {
        return FormatWantedReason("Grid", NeedFromSimpleStatus(status), scope, grid, band, mode);
    }

    public static string FormatState(SimpleWorkedStatus? status, string state, string band = "", string mode = "", WantedScope scope = WantedScope.Overall)
    {
        return FormatWantedReason("USA State", NeedFromSimpleStatus(status), scope, state, band, mode);
    }

    public static string FormatBandModeSlot(string callsign, string band, string mode)
    {
        band = CleanBandOrMode(band);
        mode = CleanBandOrMode(mode);
        callsign = (callsign ?? "").Trim();
        if (string.IsNullOrWhiteSpace(band) || string.IsNullOrWhiteSpace(mode))
            return "Reason unavailable - missing band/mode";

        var subject = string.IsNullOrWhiteSpace(callsign) ? "station" : callsign;
        return $"New band/mode for {subject}: never worked on {band} {mode}";
    }

    public static string FormatRareConfirmedDxcc(string entity)
    {
        entity = (entity ?? "").Trim();
        return string.IsNullOrWhiteSpace(entity)
            ? "Rare country (already confirmed)"
            : $"Rare country (already confirmed): {entity}";
    }

    public static string FormatGeneral(string fallback)
    {
        fallback = (fallback ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fallback)
            || fallback.Contains("Needed on Current ~", StringComparison.OrdinalIgnoreCase)
            || fallback.Equals("Needed on Current", StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable;
        }

        return fallback;
    }

    private static string CleanBandOrMode(string value)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("Current", StringComparison.OrdinalIgnoreCase)
            || value.Equals("~", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Current ~", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return value;
    }

    public static string NeedText(NeedStatus needStatus)
    {
        return needStatus switch
        {
            NeedStatus.NeverWorked => "New",
            NeedStatus.WorkedNotLoTWConfirmed => "Unconfirmed",
            NeedStatus.LoTWConfirmed => "LoTW confirmed",
            _ => "Unknown"
        };
    }

    private static NeedStatus NeedFromSimpleStatus(SimpleWorkedStatus? status)
    {
        if (status == null || !status.WorkedAny)
            return NeedStatus.NeverWorked;
        return status.LoTWConfirmedAny ? NeedStatus.LoTWConfirmed : NeedStatus.WorkedNotLoTWConfirmed;
    }

    private static string? ScopeText(WantedScope scope, string band, string mode)
    {
        return scope switch
        {
            WantedScope.CurrentBand => string.IsNullOrWhiteSpace(band) ? null : $" on {band}",
            WantedScope.CurrentMode => string.IsNullOrWhiteSpace(mode) ? null : $" on {mode}",
            WantedScope.CurrentBandMode => string.IsNullOrWhiteSpace(band) || string.IsNullOrWhiteSpace(mode) ? null : $" on {band} {mode}",
            _ => ""
        };
    }

    private static string DetailText(NeedStatus needStatus, WantedScope scope, string band, string mode)
    {
        var workedText = needStatus == NeedStatus.NeverWorked ? "never worked" : "worked";
        var suffix = needStatus == NeedStatus.WorkedNotLoTWConfirmed ? ", not LoTW confirmed" : "";
        return scope switch
        {
            WantedScope.CurrentBand => $"{workedText} on {band}{suffix}",
            WantedScope.CurrentMode => $"{workedText} on {mode}{suffix}",
            WantedScope.CurrentBandMode => $"{workedText} on {band} {mode}{suffix}",
            _ => $"{workedText}{suffix}"
        };
    }

    private static string NormaliseCategory(string category)
    {
        category = (category ?? "").Trim();
        if (category.Equals("State", StringComparison.OrdinalIgnoreCase)
            || category.Equals("USA States", StringComparison.OrdinalIgnoreCase)
            || category.Equals("USA state", StringComparison.OrdinalIgnoreCase))
        {
            return "USA State";
        }

        return category;
    }

    private static string CategoryLabel(string category)
    {
        return category.Equals("DXCC", StringComparison.OrdinalIgnoreCase) ? "DXCC" :
            category.Equals("Grid", StringComparison.OrdinalIgnoreCase) ? "grid" :
            category.Equals("USA State", StringComparison.OrdinalIgnoreCase) ? "USA state" :
            category;
    }
}
