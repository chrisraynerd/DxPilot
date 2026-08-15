using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed partial class AdifLogbookReader
{
    public IReadOnlyList<AdifQso> Load(string path)
    {
        return TryLoad(path, out var qsos) ? qsos : Array.Empty<AdifQso>();
    }

    public bool TryLoad(string path, out IReadOnlyList<AdifQso> qsos)
    {
        try
        {
            qsos = Array.Empty<AdifQso>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd();

            if (!text.Contains("<", StringComparison.Ordinal) || !text.Contains(">", StringComparison.Ordinal))
                return false;

            var records = text.Split(new[] { "<eor>", "<EOR>" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            qsos = records
                .Select(ParseRecordSafe)
                .Where(q => q != null && !string.IsNullOrWhiteSpace(q.Call))
                .Cast<AdifQso>()
                .ToList();
            return true;
        }
        catch
        {
            qsos = Array.Empty<AdifQso>();
            return false;
        }
    }

    private static AdifQso? ParseRecordSafe(string record)
    {
        try
        {
            return ParseRecord(record);
        }
        catch
        {
            return null;
        }
    }

    private static AdifQso ParseRecord(string record)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in FieldRegex().Matches(record))
        {
            var key = match.Groups["name"].Value.ToUpperInvariant();
            fields[key] = match.Groups["value"].Value.Trim();
        }

        return new AdifQso
        {
            Call = Get(fields, "CALL").ToUpperInvariant(),
            Band = Get(fields, "BAND").ToLowerInvariant(),
            Mode = NormalizeMode(Get(fields, "MODE")),
            Submode = NormalizeMode(Get(fields, "SUBMODE")),
            QsoDateText = Get(fields, "QSO_DATE"),
            TimeOn = NormalizeTime(Get(fields, "TIME_ON")),
            Freq = NormalizeFrequency(Get(fields, "FREQ")),
            Dxcc = Get(fields, "DXCC"),
            Country = Get(fields, "COUNTRY"),
            Grid = Get(fields, "GRIDSQUARE").ToUpperInvariant(),
            State = Get(fields, "STATE").ToUpperInvariant(),
            Iota = Get(fields, "IOTA").ToUpperInvariant(),
            StationCallsign = Get(fields, "STATION_CALLSIGN").ToUpperInvariant(),
            OperatorCallsign = Get(fields, "OPERATOR").ToUpperInvariant(),
            LotwConfirmed = IsConfirmed(Get(fields, "LOTW_QSL_RCVD")),
            PaperConfirmed = IsConfirmed(Get(fields, "QSL_RCVD")),
            EqslConfirmed = IsConfirmed(Get(fields, "EQSL_QSL_RCVD")),
            QsoDate = ParseDate(Get(fields, "QSO_DATE"))
        };
    }

    private static string Get(Dictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : "";
    }

    private static string NormalizeMode(string mode)
    {
        return mode.Trim().ToUpperInvariant();
    }

    private static string NormalizeTime(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 6 ? digits[..6] : digits;
    }

    private static string NormalizeFrequency(string value)
    {
        if (decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
            return freq.ToString("0.#####", CultureInfo.InvariantCulture);

        return value.Trim();
    }

    private static bool IsConfirmed(string value)
    {
        return value.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ParseDate(string value)
    {
        return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    [GeneratedRegex("<(?<name>[A-Z0-9_]+):(?<length>\\d+)(?::[^>]*)?>(?<value>[^<]*)", RegexOptions.IgnoreCase)]
    private static partial Regex FieldRegex();
}
