namespace JtdxAutoResume.V3.Models;

public readonly record struct MapOpportunityFlags(
    bool IsNewDxcc,
    bool IsUnconfirmedDxcc,
    bool IsNewGrid,
    bool IsNewState);

public readonly record struct MapOpportunityProfile(
    MapOpportunityFlags Overall,
    MapOpportunityFlags CurrentBand,
    MapOpportunityFlags CurrentMode,
    MapOpportunityFlags CurrentBandMode)
{
    public MapOpportunityFlags ForScope(WantedScope scope) => scope switch
    {
        WantedScope.CurrentBand => CurrentBand,
        WantedScope.CurrentMode => CurrentMode,
        WantedScope.CurrentBandMode => CurrentBandMode,
        _ => Overall
    };

    public static MapOpportunityProfile FromOverall(MapOpportunityFlags flags) =>
        new(flags, flags, flags, flags);

    public static MapOpportunityProfile MergeMissingLocationCategories(
        MapOpportunityProfile previous,
        MapOpportunityProfile current,
        bool hasCurrentGrid,
        bool hasCurrentState)
    {
        return new MapOpportunityProfile(
            Merge(previous.Overall, current.Overall, hasCurrentGrid, hasCurrentState),
            Merge(previous.CurrentBand, current.CurrentBand, hasCurrentGrid, hasCurrentState),
            Merge(previous.CurrentMode, current.CurrentMode, hasCurrentGrid, hasCurrentState),
            Merge(previous.CurrentBandMode, current.CurrentBandMode, hasCurrentGrid, hasCurrentState));
    }

    private static MapOpportunityFlags Merge(
        MapOpportunityFlags previous,
        MapOpportunityFlags current,
        bool hasCurrentGrid,
        bool hasCurrentState)
    {
        return current with
        {
            IsNewGrid = hasCurrentGrid ? current.IsNewGrid : previous.IsNewGrid,
            IsNewState = hasCurrentState ? current.IsNewState : previous.IsNewState
        };
    }
}

public sealed record MapColourScopeOption(WantedScope Scope, string Label);
