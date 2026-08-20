using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class AchievementDxccDetailViewModel
{
    public required AchievementDxccRow Entity { get; init; }
    public required IReadOnlyList<AchievementQsoDetail> Qsos { get; init; }
    public string ProfileDisplay { get; init; } = "";
    public string Title => $"{Entity.EntityName} — DXCC {Entity.DxccNumber}";
    public string Summary => Qsos.Count == 0
        ? $"No matching ADIF QSOs in {ProfileDisplay}."
        : $"{Qsos.Count:N0} QSO{(Qsos.Count == 1 ? "" : "s")} in {ProfileDisplay} · "
          + $"{Qsos.Count(qso => qso.LotwConfirmed):N0} LoTW confirmed · "
          + $"{Qsos.Count(qso => !qso.LotwConfirmed):N0} awaiting LoTW";
}
