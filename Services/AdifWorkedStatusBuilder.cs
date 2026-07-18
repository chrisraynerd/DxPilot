using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class AdifWorkedStatusBuilder
{
    public static IReadOnlyList<string> RunGridNormalizationSelfTest()
    {
        var failures = new List<string>();
        var settings = new AppSettings { GridConfirmationMode = "LoTWOnly" };
        var builder = new AdifWorkedStatusBuilder();

        var lotw6 = builder.Build(new[]
        {
            new AdifQso { Call = "T1AAA", Band = "20m", Mode = "MFSK", Submode = "FT4", Grid = "EM89bp", LotwConfirmed = true, QsoDateText = "20260710", TimeOn = "000001" }
        }, Array.Empty<AdifQso>(), settings).Indexes.Grids;
        if (!lotw6.TryGetValue("EM89", out var lotw6Status) || !lotw6Status.LoTWConfirmedAny || lotw6Status.LoTWConfirmedQsoCount != 1)
            failures.Add("EM89bp LoTW did not confirm Grid4 EM89");
        if (lotw6Status == null || !lotw6Status.WorkedModes.Contains("FT4") || lotw6Status.WorkedModes.Contains("MFSK"))
            failures.Add("MFSK/SUBMODE FT4 did not index effective mode FT4");

        var lotw4 = builder.Build(new[]
        {
            new AdifQso { Call = "T1AAB", Band = "20m", Mode = "FT8", Grid = "EM89", LotwConfirmed = true, QsoDateText = "20260710", TimeOn = "000002" }
        }, Array.Empty<AdifQso>(), settings).Indexes.Grids;
        if (!lotw4.TryGetValue("EM89", out var lotw4Status) || !lotw4Status.LoTWConfirmedAny)
            failures.Add("EM89 LoTW did not confirm Grid4 EM89");

        var workedOnly = builder.Build(new[]
        {
            new AdifQso { Call = "T1AAC", Band = "20m", Mode = "FT8", Grid = "EM89bp", LotwConfirmed = false, QsoDateText = "20260710", TimeOn = "000003" }
        }, Array.Empty<AdifQso>(), settings).Indexes.Grids;
        if (!workedOnly.TryGetValue("EM89", out var workedOnlyStatus) || !workedOnlyStatus.WorkedAny || workedOnlyStatus.LoTWConfirmedAny)
            failures.Add("EM89bp worked-only did not index as worked but unconfirmed Grid4 EM89");

        var none = builder.Build(new[]
        {
            new AdifQso { Call = "T1AAD", Band = "20m", Mode = "FT8", Grid = "EN90", LotwConfirmed = true, QsoDateText = "20260710", TimeOn = "000004" }
        }, Array.Empty<AdifQso>(), settings).Indexes.Grids;
        if (none.ContainsKey("EM89"))
            failures.Add("Unrelated EN90 QSO incorrectly populated EM89");

        return failures;
    }

    public AdifMergeResult Build(
        IReadOnlyCollection<AdifQso> fullQsos,
        IReadOnlyCollection<AdifQso> liveQsos,
        AppSettings settings)
    {
        var merged = MergeDeduplicate(fullQsos, liveQsos, out var duplicateCount);
        var indexes = BuildIndexes(merged, settings);

        return new AdifMergeResult
        {
            FullQsoCount = fullQsos.Count,
            LiveQsoCount = liveQsos.Count,
            DuplicateCount = duplicateCount,
            UniqueQsos = merged,
            Indexes = indexes
        };
    }

    private static List<AdifQso> MergeDeduplicate(
        IReadOnlyCollection<AdifQso> fullQsos,
        IReadOnlyCollection<AdifQso> liveQsos,
        out int duplicateCount)
    {
        var exact = new Dictionary<string, AdifQso>(StringComparer.OrdinalIgnoreCase);
        duplicateCount = 0;

        foreach (var qso in fullQsos.Concat(liveQsos))
        {
            var key = ExactKey(qso);
            if (!string.IsNullOrWhiteSpace(key) && exact.TryGetValue(key, out var existing))
            {
                MergeInto(existing, qso);
                duplicateCount++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                exact[key] = Clone(qso);
                continue;
            }

            exact[$"row:{exact.Count}"] = Clone(qso);
        }

        var fallback = new Dictionary<string, AdifQso>(StringComparer.OrdinalIgnoreCase);
        foreach (var qso in exact.Values)
        {
            var key = FallbackKey(qso);
            if (string.IsNullOrWhiteSpace(key) || !fallback.TryGetValue(key, out var existing))
            {
                fallback[string.IsNullOrWhiteSpace(key) ? $"row:{fallback.Count}" : key] = qso;
                continue;
            }

            MergeInto(existing, qso);
            duplicateCount++;
        }

        return fallback.Values.ToList();
    }

    private static WorkedStatusIndexes BuildIndexes(IEnumerable<AdifQso> qsos, AppSettings settings)
    {
        var indexes = new WorkedStatusIndexes();

        foreach (var qso in qsos)
        {
            if (!string.IsNullOrWhiteSpace(qso.Dxcc) || !string.IsNullOrWhiteSpace(qso.Country))
                AddDxcc(indexes, qso, settings);

            if (!string.IsNullOrWhiteSpace(qso.Grid))
            {
                var normalized = MaidenheadGrid.Normalize(qso.Grid);
                if (normalized.IsValid)
                    AddSimple(indexes.Grids, normalized.Grid4, qso, settings.GridConfirmationMode);
            }

            if (!string.IsNullOrWhiteSpace(qso.State))
                AddSimple(indexes.States, qso.State, qso, settings.StateConfirmationMode);

            if (!string.IsNullOrWhiteSpace(qso.Iota))
                AddSimple(indexes.Iotas, qso.Iota, qso, settings.IotaConfirmationMode);
        }

        return indexes;
    }

    private static void AddDxcc(WorkedStatusIndexes indexes, AdifQso qso, AppSettings settings)
    {
        var key = !string.IsNullOrWhiteSpace(qso.Dxcc) ? qso.Dxcc : qso.Country;
        var status = indexes.Dxcc.GetValueOrDefault(key);
        if (status == null)
        {
            status = new DxccWorkedStatus { DxccNumber = qso.Dxcc, EntityName = qso.Country };
            indexes.Dxcc[key] = status;
        }

        if (string.IsNullOrWhiteSpace(status.DxccNumber))
            status.DxccNumber = qso.Dxcc;
        if (string.IsNullOrWhiteSpace(status.EntityName))
            status.EntityName = qso.Country;

        status.WorkedAny = true;
        status.Source = CombineSource(status.Source, qso.Source);
        status.LoTWConfirmedAny |= qso.LotwConfirmed;
        status.PaperConfirmedAny |= qso.PaperConfirmed;
        status.EqslConfirmedAny |= qso.EqslConfirmed;
        status.ConfirmedAny |= IsConfirmedForMode(qso, settings.DxccConfirmationMode);
        AddWorkedBandMode(status, qso);
        AddLotwBandMode(status, qso);
        if (IsConfirmedForMode(qso, settings.DxccConfirmationMode))
            AddConfirmedBandMode(status, qso);
        status.LastWorkedDate = Later(status.LastWorkedDate, qso.QsoDate);
        if (qso.HasAnyConfirmation)
            status.LastConfirmedDate = Later(status.LastConfirmedDate, qso.QsoDate);
    }

    private static void AddSimple(Dictionary<string, SimpleWorkedStatus> index, string id, AdifQso qso, string mode)
    {
        var status = index.GetValueOrDefault(id);
        if (status == null)
        {
            status = new SimpleWorkedStatus { Id = id };
            index[id] = status;
        }

        status.WorkedAny = true;
        status.WorkedQsoCount++;
        status.Source = CombineSource(status.Source, qso.Source);
        status.LoTWConfirmedAny |= qso.LotwConfirmed;
        if (qso.LotwConfirmed)
            status.LoTWConfirmedQsoCount++;
        status.PaperConfirmedAny |= qso.PaperConfirmed;
        status.EqslConfirmedAny |= qso.EqslConfirmed;
        status.ConfirmedAny |= IsConfirmedForMode(qso, mode);
        if (!string.IsNullOrWhiteSpace(qso.Band))
            status.WorkedBands.Add(qso.Band);
        if (!string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.WorkedModes.Add(qso.EffectiveMode);
        if (!string.IsNullOrWhiteSpace(qso.Band) && !string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.WorkedBandModes.Add(BandModeKey(qso.Band, qso.EffectiveMode));
        if (IsConfirmedForMode(qso, mode) && !string.IsNullOrWhiteSpace(qso.Band))
            status.ConfirmedBands.Add(qso.Band);
        if (qso.LotwConfirmed)
        {
            if (!string.IsNullOrWhiteSpace(qso.Band))
                status.LoTWConfirmedBands.Add(qso.Band);
            if (!string.IsNullOrWhiteSpace(qso.EffectiveMode))
                status.LoTWConfirmedModes.Add(qso.EffectiveMode);
            if (!string.IsNullOrWhiteSpace(qso.Band) && !string.IsNullOrWhiteSpace(qso.EffectiveMode))
                status.LoTWConfirmedBandModes.Add(BandModeKey(qso.Band, qso.EffectiveMode));
        }
    }

    private static void AddWorkedBandMode(DxccWorkedStatus status, AdifQso qso)
    {
        if (!string.IsNullOrWhiteSpace(qso.Band))
            status.WorkedBands.Add(qso.Band);
        if (!string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.WorkedModes.Add(qso.EffectiveMode);
        if (!string.IsNullOrWhiteSpace(qso.Band) && !string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.WorkedBandModes.Add(BandModeKey(qso.Band, qso.EffectiveMode));
    }

    private static void AddConfirmedBandMode(DxccWorkedStatus status, AdifQso qso)
    {
        if (!string.IsNullOrWhiteSpace(qso.Band))
            status.ConfirmedBands.Add(qso.Band);
        if (!string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.ConfirmedModes.Add(qso.EffectiveMode);
    }

    private static void AddLotwBandMode(DxccWorkedStatus status, AdifQso qso)
    {
        if (!qso.LotwConfirmed)
            return;

        if (!string.IsNullOrWhiteSpace(qso.Band))
            status.LoTWConfirmedBands.Add(qso.Band);
        if (!string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.LoTWConfirmedModes.Add(qso.EffectiveMode);
        if (!string.IsNullOrWhiteSpace(qso.Band) && !string.IsNullOrWhiteSpace(qso.EffectiveMode))
            status.LoTWConfirmedBandModes.Add(BandModeKey(qso.Band, qso.EffectiveMode));
    }

    private static string BandModeKey(string band, string mode)
    {
        return $"{band.Trim().ToUpperInvariant()}|{mode.Trim().ToUpperInvariant()}";
    }

    private static bool IsConfirmedForMode(AdifQso qso, string mode)
    {
        return mode switch
        {
            "WorkedOnly" => true,
            "LoTWOnly" => qso.LotwConfirmed,
            "PaperQslOnly" => qso.PaperConfirmed,
            "LoTWOrPaper" => qso.LotwConfirmed || qso.PaperConfirmed,
            "LoTWOrPaperOrEqsl" => qso.LotwConfirmed || qso.PaperConfirmed || qso.EqslConfirmed,
            _ => qso.LotwConfirmed
        };
    }

    private static string ExactKey(AdifQso qso)
    {
        if (string.IsNullOrWhiteSpace(qso.Call)
            || string.IsNullOrWhiteSpace(qso.Band)
            || string.IsNullOrWhiteSpace(qso.EffectiveMode)
            || string.IsNullOrWhiteSpace(qso.QsoDateText)
            || string.IsNullOrWhiteSpace(qso.TimeOn))
        {
            return "";
        }

        return $"{qso.Call}|{qso.Band}|{qso.EffectiveMode}|{qso.QsoDateText}|{qso.TimeOn}";
    }

    private static string FallbackKey(AdifQso qso)
    {
        if (string.IsNullOrWhiteSpace(qso.Call)
            || string.IsNullOrWhiteSpace(qso.Band)
            || string.IsNullOrWhiteSpace(qso.EffectiveMode)
            || string.IsNullOrWhiteSpace(qso.QsoDateText)
            || string.IsNullOrWhiteSpace(qso.Freq))
        {
            return "";
        }

        return $"{qso.Call}|{qso.Band}|{qso.EffectiveMode}|{qso.QsoDateText}|{qso.Freq}";
    }

    private static void MergeInto(AdifQso target, AdifQso source)
    {
        target.Dxcc = Prefer(target.Dxcc, source.Dxcc);
        target.Country = Prefer(target.Country, source.Country);
        target.Grid = Prefer(target.Grid, source.Grid);
        target.State = Prefer(target.State, source.State);
        target.Iota = Prefer(target.Iota, source.Iota);
        target.Freq = Prefer(target.Freq, source.Freq);
        target.Mode = Prefer(target.Mode, source.Mode);
        target.Submode = Prefer(target.Submode, source.Submode);
        target.TimeOn = Prefer(target.TimeOn, source.TimeOn);
        target.QsoDateText = Prefer(target.QsoDateText, source.QsoDateText);
        target.QsoDate ??= source.QsoDate;
        target.LotwConfirmed |= source.LotwConfirmed;
        target.PaperConfirmed |= source.PaperConfirmed;
        target.EqslConfirmed |= source.EqslConfirmed;
        target.Source = CombineSource(target.Source, source.Source);
    }

    private static AdifQso Clone(AdifQso qso)
    {
        return new AdifQso
        {
            Call = qso.Call,
            Band = qso.Band,
            Mode = qso.Mode,
            Submode = qso.Submode,
            QsoDateText = qso.QsoDateText,
            TimeOn = qso.TimeOn,
            Freq = qso.Freq,
            Dxcc = qso.Dxcc,
            Country = qso.Country,
            Grid = qso.Grid,
            State = qso.State,
            Iota = qso.Iota,
            LotwConfirmed = qso.LotwConfirmed,
            PaperConfirmed = qso.PaperConfirmed,
            EqslConfirmed = qso.EqslConfirmed,
            Source = qso.Source,
            QsoDate = qso.QsoDate
        };
    }

    private static string Prefer(string current, string incoming)
    {
        return string.IsNullOrWhiteSpace(current) ? incoming : current;
    }

    private static string CombineSource(string current, string incoming)
    {
        if (string.IsNullOrWhiteSpace(current))
            return incoming;
        if (string.IsNullOrWhiteSpace(incoming) || current.Contains(incoming, StringComparison.OrdinalIgnoreCase))
            return current;
        return $"{current}+{incoming}";
    }

    private static DateTime? Later(DateTime? current, DateTime? incoming)
    {
        if (!current.HasValue)
            return incoming;
        if (!incoming.HasValue)
            return current;
        return incoming > current ? incoming : current;
    }
}
