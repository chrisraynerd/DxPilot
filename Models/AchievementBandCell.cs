namespace JtdxAutoResume.V3.Models;

/// <summary>Read-only LoTW band totals; independent of hunting confirmation preferences.</summary>
public sealed record AchievementBandCell(string Band, int WorkedCount, int LotwConfirmedCount)
{
    public static IReadOnlyList<string> BandOrder { get; } = Array.AsReadOnly(
        new[] { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m" });
    public string StatusKey => LotwConfirmedCount > 0 ? "LotwConfirmed" : WorkedCount > 0 ? "WorkedUnconfirmed" : "Needed";
    public string DisplayCount => WorkedCount == 0 ? "" : (LotwConfirmedCount > 0 ? LotwConfirmedCount : WorkedCount).ToString("N0");
    public string ToolTip => $"{Band}: {WorkedCount:N0} worked · {LotwConfirmedCount:N0} LoTW confirmed · {WorkedCount - LotwConfirmedCount:N0} awaiting LoTW";

    public static IReadOnlyList<AchievementBandCell> Build(IEnumerable<AdifQso> qsos)
    {
        var byBand = qsos.GroupBy(q => q.Band.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Worked: g.Count(), Confirmed: g.Count(q => q.LotwConfirmed)), StringComparer.OrdinalIgnoreCase);
        return BandOrder.Select(band =>
        {
            var totals = byBand.GetValueOrDefault(band);
            return new AchievementBandCell(band, totals.Worked, totals.Confirmed);
        }).ToArray();
    }
}
