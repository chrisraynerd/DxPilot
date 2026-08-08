using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class MapOpportunityClassifier
{
    public static IReadOnlyList<string> LotwConfirmedGridsForScope(
        WorkedStatusIndexes indexes,
        WantedScope scope,
        string band,
        string mode)
    {
        return indexes.Grids
            .Where(pair => scope switch
            {
                WantedScope.CurrentBand => pair.Value.LoTWConfirmedBands.Contains(band ?? ""),
                WantedScope.CurrentMode => pair.Value.LoTWConfirmedModes.Contains(mode ?? ""),
                _ => pair.Value.LoTWConfirmedAny
            })
            .Select(pair => pair.Key)
            .OrderBy(grid => grid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static MapOpportunityProfile Classify(DecodeMessage decode, WorkedStatusIndexes indexes)
    {
        indexes.Dxcc.TryGetValue(decode.Dxcc, out var dxcc);
        var normalizedGrid = NormalizeBestAvailableGrid(decode);
        indexes.Grids.TryGetValue(normalizedGrid.Grid4, out var grid);
        indexes.States.TryGetValue(decode.State, out var state);

        return new MapOpportunityProfile(
            Flags(decode, normalizedGrid.IsValid, dxcc, grid, state, WantedScope.Overall),
            Flags(decode, normalizedGrid.IsValid, dxcc, grid, state, WantedScope.CurrentBand),
            Flags(decode, normalizedGrid.IsValid, dxcc, grid, state, WantedScope.CurrentMode),
            Flags(decode, normalizedGrid.IsValid, dxcc, grid, state, WantedScope.CurrentBandMode));
    }

    private static NormalizedGrid NormalizeBestAvailableGrid(DecodeMessage decode)
    {
        // Use the same parent square that the map is able to plot. A later CQ
        // may carry no locator while its enriched Effective/ADIF/QRZ location
        // remains valid; classifying only decode.Grid would incorrectly turn
        // that station orange even though Wanted still identifies the square.
        var normalized = MaidenheadGrid.Normalize(decode.EffectiveGrid);
        if (normalized.IsValid) return normalized;
        normalized = MaidenheadGrid.Normalize(decode.TransmittedGrid);
        if (normalized.IsValid) return normalized;
        normalized = MaidenheadGrid.Normalize(decode.Grid);
        if (normalized.IsValid) return normalized;
        normalized = MaidenheadGrid.Normalize(decode.AdifGrid);
        if (normalized.IsValid) return normalized;
        normalized = MaidenheadGrid.Normalize(decode.QrzGrid);
        if (normalized.IsValid) return normalized;

        return default;
    }

    private static MapOpportunityFlags Flags(
        DecodeMessage decode,
        bool hasGrid,
        DxccWorkedStatus? dxcc,
        SimpleWorkedStatus? grid,
        SimpleWorkedStatus? state,
        WantedScope scope)
    {
        if (!ScopeIsAvailable(scope, decode.Band, decode.Mode))
            return default;

        var hasDxcc = !string.IsNullOrWhiteSpace(decode.Dxcc);
        var dxccWorked = hasDxcc && IsWorked(dxcc, scope, decode.Band, decode.Mode);
        var dxccConfirmed = hasDxcc && IsConfirmed(dxcc, scope, decode.Band, decode.Mode);
        var hasState = !string.IsNullOrWhiteSpace(decode.State);

        return new MapOpportunityFlags(
            IsNewDxcc: hasDxcc && !dxccWorked,
            IsUnconfirmedDxcc: hasDxcc && dxccWorked && !dxccConfirmed,
            IsNewGrid: hasGrid && !IsConfirmed(grid, scope, decode.Band, decode.Mode),
            IsNewState: hasState && !IsConfirmed(state, scope, decode.Band, decode.Mode));
    }

    private static bool ScopeIsAvailable(WantedScope scope, string band, string mode) => scope switch
    {
        WantedScope.CurrentBand => !string.IsNullOrWhiteSpace(band),
        WantedScope.CurrentMode => !string.IsNullOrWhiteSpace(mode),
        WantedScope.CurrentBandMode => !string.IsNullOrWhiteSpace(band) && !string.IsNullOrWhiteSpace(mode),
        _ => true
    };

    private static bool IsWorked(DxccWorkedStatus? status, WantedScope scope, string band, string mode)
    {
        if (status == null)
            return false;
        return scope switch
        {
            WantedScope.CurrentBand => status.WorkedBands.Contains(band),
            WantedScope.CurrentMode => status.WorkedModes.Contains(mode),
            WantedScope.CurrentBandMode => status.WorkedBandModes.Contains(BandModeKey(band, mode)),
            _ => status.WorkedAny
        };
    }

    private static bool IsConfirmed(DxccWorkedStatus? status, WantedScope scope, string band, string mode)
    {
        if (status == null)
            return false;
        return scope switch
        {
            WantedScope.CurrentBand => status.LoTWConfirmedBands.Contains(band),
            WantedScope.CurrentMode => status.LoTWConfirmedModes.Contains(mode),
            WantedScope.CurrentBandMode => status.LoTWConfirmedBandModes.Contains(BandModeKey(band, mode)),
            _ => status.LoTWConfirmedAny
        };
    }

    private static bool IsConfirmed(SimpleWorkedStatus? status, WantedScope scope, string band, string mode)
    {
        if (status == null)
            return false;
        return scope switch
        {
            WantedScope.CurrentBand => status.LoTWConfirmedBands.Contains(band),
            WantedScope.CurrentMode => status.LoTWConfirmedModes.Contains(mode),
            WantedScope.CurrentBandMode => status.LoTWConfirmedBandModes.Contains(BandModeKey(band, mode)),
            _ => status.LoTWConfirmedAny
        };
    }

    private static string BandModeKey(string band, string mode)
    {
        return string.IsNullOrWhiteSpace(band) || string.IsNullOrWhiteSpace(mode)
            ? ""
            : $"{band.Trim().ToUpperInvariant()}|{mode.Trim().ToUpperInvariant()}";
    }
}
