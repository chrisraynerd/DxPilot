namespace JtdxAutoResume.V3.Models;

public sealed class AchievementQsoDetail
{
    public string Call { get; init; } = "";
    public string StationCallsign { get; init; } = "";
    public DateTime? QsoDate { get; init; }
    public string DateDisplay { get; init; } = "—";
    public string TimeDisplay { get; init; } = "—";
    public string Band { get; init; } = "—";
    public string Mode { get; init; } = "—";
    public string Frequency { get; init; } = "—";
    public string Grid { get; init; } = "—";
    public bool LotwConfirmed { get; init; }
    public string LotwDisplay => LotwConfirmed ? "Confirmed" : "No";
    public string PaperDisplay { get; init; } = "No";
    public string EqslDisplay { get; init; } = "No";
    public string Source { get; init; } = "—";
}
