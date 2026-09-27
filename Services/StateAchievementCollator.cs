using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

/// <summary>Display-only analysis of ADIF state records. Never guesses a state from a callsign.</summary>
public static class StateAchievementCollator
{
    private static readonly string[] Names =
    [
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia",
        "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland",
        "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey",
        "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island", "South Carolina",
        "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming"
    ];

    public static string StateCode(AdifQso qso) => WasStateEligibility.NormalizeState(qso, false);

    public static IReadOnlyList<AchievementDxccRow> Build(IEnumerable<AdifQso> qsos)
    {
        var groups = qsos.GroupBy(StateCode).ToDictionary(g => g.Key, g => g.ToArray());
        return UsStateValidator.StandardStateCodes.Select((code, i) =>
        {
            var records = groups.GetValueOrDefault(code) ?? [];
            var confirmed = records.Count(q => q.LotwConfirmed);
            return new AchievementDxccRow
            {
                StateCode = code, EntityName = Names[i], QsoCount = records.Length,
                LotwConfirmedQsoCount = confirmed, UnconfirmedQsoCount = records.Length - confirmed,
                BandCells = AchievementBandCell.Build(records),
                LastWorked = records.Select(q => q.QsoDate).DefaultIfEmpty().Max(),
                Modes = string.Join(", ", records.Select(q => q.EffectiveMode).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).Order()),
                StatusKey = confirmed > 0 ? "LotwConfirmed" : records.Length > 0 ? "WorkedUnconfirmed" : "Needed"
            };
        }).ToArray();
    }
}
