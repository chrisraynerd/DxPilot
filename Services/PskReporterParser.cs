using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class PskReporterParser
{
    public static bool TryParseLiveJson(string json, out PskReporterSpot spot)
    {
        spot = new PskReporterSpot();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var frequency = Int64(root, "f");
            var timestamp = Int64(root, "t_tx");
            if (timestamp <= 0)
                timestamp = Int64(root, "t");
            var sender = Text(root, "sc").Trim().ToUpperInvariant();
            var receiver = Text(root, "rc").Trim().ToUpperInvariant();
            if (frequency <= 0 || timestamp <= 0 || sender.Length == 0 || receiver.Length == 0)
                return false;

            spot = new PskReporterSpot
            {
                SequenceNumber = Int64(root, "sq"),
                FrequencyHz = frequency,
                Band = FirstNonBlank(Text(root, "b"), AmateurBandMapper.FromDialFrequency((ulong)frequency)),
                Mode = Text(root, "md").Trim().ToUpperInvariant(),
                SignalReportDb = NullableInt32(root, "rp"),
                TransmissionTimeUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime,
                SenderCallsign = sender,
                SenderLocator = Text(root, "sl").Trim().ToUpperInvariant(),
                ReceiverCallsign = receiver,
                ReceiverLocator = Text(root, "rl").Trim().ToUpperInvariant(),
                ReceiverDxccCode = Text(root, "ra"),
                Source = "Live"
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<PskReporterSpot> ParseQueryXml(string xml)
    {
        var results = new List<PskReporterSpot>();
        var document = XDocument.Parse(xml, LoadOptions.None);
        foreach (var element in document.Descendants("receptionReport"))
        {
            var frequency = ParseLong(Attribute(element, "frequency"));
            var timestamp = ParseLong(Attribute(element, "flowStartSeconds"));
            var sender = Attribute(element, "senderCallsign").Trim().ToUpperInvariant();
            var receiver = Attribute(element, "receiverCallsign").Trim().ToUpperInvariant();
            if (frequency <= 0 || timestamp <= 0 || sender.Length == 0 || receiver.Length == 0)
                continue;

            results.Add(new PskReporterSpot
            {
                FrequencyHz = frequency,
                Band = AmateurBandMapper.FromDialFrequency((ulong)frequency),
                Mode = Attribute(element, "mode").Trim().ToUpperInvariant(),
                SignalReportDb = ParseNullableInt(Attribute(element, "sNR")),
                TransmissionTimeUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime,
                SenderCallsign = sender,
                SenderLocator = Attribute(element, "senderLocator").Trim().ToUpperInvariant(),
                ReceiverCallsign = receiver,
                ReceiverLocator = Attribute(element, "receiverLocator").Trim().ToUpperInvariant(),
                ReceiverDxcc = Attribute(element, "receiverDXCC").Trim(),
                ReceiverDxccCode = Attribute(element, "receiverDXCCCode").Trim(),
                Source = "Query"
            });
        }

        return results;
    }

    public static string DedupeKey(PskReporterSpot spot) =>
        $"R:{spot.ReceiverCallsign}|{spot.Band}|{spot.TransmissionTimeUtc.ToUnixSeconds()}";

    private static string Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value ?? "";

    private static long Int64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String ? ParseLong(value.GetString()) : 0;
    }

    private static int? NullableInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String ? ParseNullableInt(value.GetString()) : null;
    }

    private static string Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static long ParseLong(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static int? ParseNullableInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static long ToUnixSeconds(this DateTime value) =>
        new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds();
}
