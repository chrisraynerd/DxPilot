using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class WantedTargetPriorityPolicy
{
    public static WantedItem? SelectBest(
        IEnumerable<(WantedItem Item, int CategoryPriority)> candidates)
    {
        return candidates
            .Where(candidate => candidate.Item.IsActionable)
            .OrderBy(candidate => candidate.CategoryPriority)
            .ThenBy(candidate => candidate.Item.PriorityTier ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.Item.IsLotwUser)
            .ThenByDescending(candidate => candidate.Item.AdjustedDxValueScore ?? 0)
            .ThenByDescending(candidate => candidate.Item.UKDesirability ?? 0)
            .ThenByDescending(candidate => candidate.Item.LastSeenUtc)
            .ThenByDescending(candidate => candidate.Item.Snr)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    public static WantedItem? SelectBestDxcc(IEnumerable<WantedItem> candidates)
    {
        return candidates
            .Where(item => item.IsActionable)
            .OrderBy(item => item.PriorityTier ?? int.MaxValue)
            .ThenByDescending(item => item.IsLotwUser)
            .ThenByDescending(item => item.AdjustedDxValueScore ?? 0)
            .ThenByDescending(item => item.UKDesirability ?? 0)
            .ThenByDescending(item => item.LastSeenUtc)
            .ThenByDescending(item => item.Snr)
            .FirstOrDefault();
    }
}
