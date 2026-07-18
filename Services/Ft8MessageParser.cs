using System.Text.RegularExpressions;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed partial class Ft8MessageParser
{
    public DecodeMessage Parse(DecodeMessage decode, string myCallsign = "G1CEC")
    {
        decode.RawText = decode.RawText.Trim();
        var tokens = decode.RawText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanToken)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        decode.ContainsMyCall = tokens.Any(t => t.Equals(myCallsign, StringComparison.OrdinalIgnoreCase));
        decode.MessageType = Ft8MessageType.Unknown;
        decode.ParseConfidence = ParseConfidence.Low;
        decode.TargetabilityReason = "Low parse confidence";

        if (tokens.Length == 0)
            return FinalizeDecode(decode);

        decode.IsCq = tokens[0].Equals("CQ", StringComparison.OrdinalIgnoreCase);
        if (decode.RawText.Contains('<') || decode.RawText.Contains('>'))
            ParseHashed(decode, tokens);
        else if (decode.IsCq)
            ParseCq(decode, tokens);
        else
            ParseDirected(decode, tokens);

        if (decode.ContainsMyCall)
        {
            decode.Targetable = false;
            decode.TargetabilityReason = $"Contains my callsign {myCallsign}";
        }

        return FinalizeDecode(decode);
    }

    private void ParseCq(DecodeMessage decode, string[] tokens)
    {
        var callIndex = 1;
        if (tokens.Length >= 4 && IsCqModifier(tokens[1]))
            callIndex = 2;

        if (tokens.Length <= callIndex || !IsLikelyCallsign(tokens[callIndex]))
        {
            decode.MessageType = Ft8MessageType.Invalid;
            decode.Payload = tokens.LastOrDefault() ?? "";
            decode.TargetabilityReason = "CQ with no valid callsign";
            return;
        }

        decode.MessageType = Ft8MessageType.Cq;
        decode.Call1 = tokens[callIndex];
        decode.HeardCall = decode.Call1;
        decode.Payload = tokens.Length > callIndex + 1 ? tokens[callIndex + 1] : "";
        decode.Grid = IsValidGrid(decode.Payload) ? decode.Payload.ToUpperInvariant() : "";
        decode.GridOwnerCall = decode.Call1;
        decode.PrimaryDisplayCall = decode.Call1;
        decode.PossibleHuntCalls = decode.Call1;
        decode.ContactableCall = decode.Call1;
        decode.HuntTarget = decode.Call1;
        decode.HuntTargetReason = "CQ caller";
        decode.Targetable = true;
        decode.ParseConfidence = ParseConfidence.High;
        decode.TargetabilityReason = "CQ decode";
        decode.ParserReason = "CQ decode";
    }

    private void ParseDirected(DecodeMessage decode, string[] tokens)
    {
        if (tokens.Length < 3 || !IsLikelyCallsign(tokens[0]) || !IsLikelyCallsign(tokens[1]))
        {
            decode.MessageType = Ft8MessageType.Invalid;
            decode.Payload = tokens.LastOrDefault() ?? "";
            decode.TargetabilityReason = "Invalid directed message";
            return;
        }

        decode.Call1 = tokens[0];
        decode.AddressedCall = decode.Call1;
        decode.Call2 = tokens[1];
        decode.HeardCall = decode.Call2;
        decode.Payload = tokens[2].ToUpperInvariant();
        decode.PossibleHuntCalls = $"{decode.Call1}, {decode.Call2}";
        decode.ContactableCall = decode.Call2;

        if (IsValidGrid(decode.Payload))
        {
            decode.MessageType = Ft8MessageType.DirectedGrid;
            decode.Grid = decode.Payload;
            decode.GridOwnerCall = decode.Call2;
            decode.PrimaryDisplayCall = decode.Call2;
            decode.ParseConfidence = ParseConfidence.High;
            decode.Targetable = true;
            decode.HuntTargetReason = "Diagnostic contactable call";
            decode.TargetabilityReason = "Directed grid";
            decode.ParserReason = "Directed grid";
        }
        else if (IsReportToken(decode.Payload))
        {
            decode.MessageType = Ft8MessageType.DirectedReport;
            decode.IsReport = true;
            decode.PrimaryDisplayCall = decode.Call2;
            decode.ParseConfidence = ParseConfidence.High;
            decode.Targetable = true;
            decode.HuntTargetReason = "Diagnostic contactable call";
            decode.TargetabilityReason = "Directed report";
            decode.ParserReason = "Directed report";
        }
        else if (IsRReportToken(decode.Payload))
        {
            decode.MessageType = Ft8MessageType.DirectedRReport;
            decode.IsReport = true;
            decode.IsRReport = true;
            decode.PrimaryDisplayCall = decode.Call2;
            decode.ParseConfidence = ParseConfidence.High;
            decode.Targetable = true;
            decode.HuntTargetReason = "Diagnostic contactable call";
            decode.TargetabilityReason = "Directed R-report";
            decode.ParserReason = "Directed R-report";
        }
        else if (decode.Payload.Equals("RRR", StringComparison.OrdinalIgnoreCase))
        {
            decode.MessageType = Ft8MessageType.DirectedRrr;
            decode.IsRrr = true;
            decode.PrimaryDisplayCall = decode.Call2;
            decode.ParseConfidence = ParseConfidence.High;
            decode.Targetable = true;
            decode.HuntTargetReason = "Diagnostic contactable call";
            decode.TargetabilityReason = "Directed RRR";
            decode.ParserReason = "Directed RRR";
        }
        else if (decode.Payload.Equals("RR73", StringComparison.OrdinalIgnoreCase))
        {
            decode.MessageType = Ft8MessageType.DirectedRr73;
            decode.IsRR73 = true;
            decode.PrimaryDisplayCall = decode.Call2;
            decode.ParseConfidence = ParseConfidence.High;
            decode.Targetable = true;
            decode.HuntTargetReason = "Diagnostic contactable call";
            decode.TargetabilityReason = "Directed RR73";
            decode.ParserReason = "Directed RR73";
        }
        else if (decode.Payload.Equals("73", StringComparison.OrdinalIgnoreCase))
        {
            decode.MessageType = Ft8MessageType.Directed73;
            decode.Is73 = true;
            decode.PrimaryDisplayCall = decode.Call2;
            decode.ParseConfidence = ParseConfidence.High;
            decode.Targetable = true;
            decode.HuntTargetReason = "Diagnostic contactable call";
            decode.TargetabilityReason = "Directed 73";
            decode.ParserReason = "Directed 73";
        }
        else
        {
            decode.MessageType = Ft8MessageType.Invalid;
            decode.Targetable = false;
            decode.TargetabilityReason = "Invalid grid/report token";
            decode.ParserReason = "Invalid grid/report token";
        }
    }

    private void ParseHashed(DecodeMessage decode, string[] tokens)
    {
        decode.IsHashedOrCompound = true;
        decode.Call1 = tokens.FirstOrDefault(IsLikelyCallsign) ?? "";
        decode.HeardCall = decode.Call1;
        decode.Payload = tokens.LastOrDefault() ?? "";
        decode.PrimaryDisplayCall = decode.Call1;
        decode.PossibleHuntCalls = decode.Call1;
        decode.ParseConfidence = string.IsNullOrWhiteSpace(decode.Call1) ? ParseConfidence.Low : ParseConfidence.Medium;
        decode.ContactableCall = decode.ParseConfidence == ParseConfidence.Low ? "" : decode.Call1;
        decode.Targetable = false;
        decode.HuntTargetReason = "Hashed/compound message; watch only";

        if (decode.Payload.Equals("RR73", StringComparison.OrdinalIgnoreCase))
        {
            decode.MessageType = Ft8MessageType.HashedRr73;
            decode.IsRR73 = true;
            decode.TargetabilityReason = "Hashed RR73, watch only";
            decode.ParserReason = "Hashed RR73, watch only";
        }
        else if (decode.Payload.Equals("73", StringComparison.OrdinalIgnoreCase))
        {
            decode.MessageType = Ft8MessageType.Hashed73;
            decode.Is73 = true;
            decode.TargetabilityReason = "Hashed 73, watch only";
            decode.ParserReason = "Hashed 73, watch only";
        }
        else if (IsReportToken(decode.Payload))
        {
            decode.MessageType = Ft8MessageType.HashedReport;
            decode.IsReport = true;
            decode.TargetabilityReason = "Hashed report, watch only";
            decode.ParserReason = "Hashed report, watch only";
        }
        else if (IsRReportToken(decode.Payload))
        {
            decode.MessageType = Ft8MessageType.HashedRReport;
            decode.IsReport = true;
            decode.IsRReport = true;
            decode.TargetabilityReason = "Hashed R-report, watch only";
            decode.ParserReason = "Hashed R-report, watch only";
        }
        else
        {
            decode.MessageType = Ft8MessageType.HashedOther;
            decode.TargetabilityReason = "Hashed/compound message, watch only";
            decode.ParserReason = "Hashed/compound message, watch only";
        }
    }

    private DecodeMessage FinalizeDecode(DecodeMessage decode)
    {
        var displayCall = !string.IsNullOrWhiteSpace(decode.HuntTarget) ? decode.HuntTarget : decode.ContactableCall;
        decode.Callsign = displayCall;
        decode.GridSource = string.IsNullOrWhiteSpace(decode.Grid) ? "None" : "DecodePayload";
        decode.LowConfidence = decode.ParseConfidence == ParseConfidence.Low;
        decode.ParserInterpretation = BuildInterpretation(decode);
        decode.ParseDebugLine =
            $"RawMessage=[{decode.RawText}] MessageType=[{decode.MessageTypeText}] AddressedCall=[{decode.AddressedCall}] HeardCall=[{decode.HeardCall}] Payload=[{decode.Payload}] Grid=[{decode.Grid}] GridOwnerCall=[{decode.GridOwnerCall}] ContactableCall=[{decode.ContactableCall}] DisplayEntity=[{decode.PrimaryDisplayEntity}] ParseConfidence=[{decode.ParseConfidence}] ParserReason=[{decode.ParserReason}] Interpretation=[{decode.ParserInterpretation}]";
        return decode;
    }

    private static string BuildInterpretation(DecodeMessage decode)
    {
        if (decode.MessageType == Ft8MessageType.Cq && !string.IsNullOrWhiteSpace(decode.HeardCall))
        {
            return string.IsNullOrWhiteSpace(decode.Grid)
                ? $"Heard {decode.HeardCall} calling CQ"
                : $"Heard {decode.HeardCall} calling CQ from {decode.Grid}";
        }

        if (decode.MessageType == Ft8MessageType.DirectedGrid
            && !string.IsNullOrWhiteSpace(decode.HeardCall)
            && !string.IsNullOrWhiteSpace(decode.AddressedCall)
            && !string.IsNullOrWhiteSpace(decode.Grid))
        {
            return $"Heard {decode.HeardCall} sending grid {decode.Grid} to {decode.AddressedCall}";
        }

        if (!string.IsNullOrWhiteSpace(decode.HeardCall)
            && !string.IsNullOrWhiteSpace(decode.AddressedCall)
            && !string.IsNullOrWhiteSpace(decode.Payload))
        {
            return $"Heard {decode.HeardCall} sending {decode.Payload} to {decode.AddressedCall}";
        }

        if (decode.IsHashedOrCompound && !string.IsNullOrWhiteSpace(decode.HeardCall))
            return $"Heard {decode.HeardCall} in hashed/compound message";

        return decode.ParserReason;
    }

    private static string CleanToken(string value)
    {
        return value.Trim().Trim(':', ';', ',', '.', '!', '?').ToUpperInvariant();
    }

    private static bool IsCqModifier(string value)
    {
        return value is "DX" or "EU" or "NA" or "SA" or "AF" or "AS" or "OC" or "JA" or "TEST"
            || value.Length <= 4 && value.All(char.IsLetter);
    }

    public static bool IsLikelyCallsign(string value)
    {
        value = value.Trim().Trim('<', '>', '/').ToUpperInvariant();
        return value.Any(char.IsDigit)
            && value.Any(char.IsLetter)
            && value.Length is >= 3 and <= 12
            && !IsValidGrid(value)
            && !IsFinalOrReportToken(value);
    }

    public static bool IsValidGrid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var grid = value.Trim().ToUpperInvariant();
        if (IsFinalOrReportToken(grid) || grid.Contains('<') || grid.Contains('>'))
            return false;

        return GridRegex().IsMatch(grid);
    }

    private static bool IsFinalOrReportToken(string value)
    {
        return value.Equals("RR73", StringComparison.OrdinalIgnoreCase)
            || value.Equals("RRR", StringComparison.OrdinalIgnoreCase)
            || value.Equals("73", StringComparison.OrdinalIgnoreCase)
            || IsReportToken(value)
            || IsRReportToken(value);
    }

    private static bool IsReportToken(string value)
    {
        return ReportRegex().IsMatch(value.Trim());
    }

    private static bool IsRReportToken(string value)
    {
        return RReportRegex().IsMatch(value.Trim());
    }

    public static void RunParserSelfTest()
    {
        var parser = new Ft8MessageParser();
        var cases = new[]
        {
            "A61DI YO4CPO KN44",
            "WB7NAM <...> RR73",
            "N5UKZ <...> +10",
            "K7IOC EC7KW IM77",
            "CQ 9J2FI KH44",
            "CQ DX 9J2FI KH44",
            "G1CEC 9J2FI R-12",
            "9J2FI G1CEC RR73",
            "K1ABC G1CEC -10",
            "G1CEC K1ABC RRR",
            "V85NPV UX3HX KN79",
            "V85NPV YO4CPO KN44",
            "V85NPV SV8IIQ KM37",
            "V85NPV EC7KW IM77",
            "V85NPV R6SO KN77",
            "EW8AAC WI3W 73",
            "W8JDP 4X5JK -01",
            "W4ZGH Z35YL -22",
            "JA9RBZ K2K -07",
            "CQ K9OM EN65",
            "CQ EA9PB IM75",
            "W6C VE9ZY 73",
            "JA9JR R2BOQ RR73",
            "LU9OZX ZL4KX -16",
            "LU9OZX RK3DSW KO95",
            "A99AA WA0HJ -06",
            "JA9RBZ R2BQM -16",
            "CQ CR2WPA HM77",
            "CQ W8JDP EM79",
            "OD5ZZ WB2AA RR73"
        };

        foreach (var text in cases)
        {
            var parsed = parser.Parse(new DecodeMessage { RawText = text });
            if (parsed.RawText != text)
                throw new InvalidOperationException("Raw message was not preserved.");
            if (parsed.Grid is "RR73" or "RRR" or "73" or "+10" or "-10" or "R-12")
                throw new InvalidOperationException($"Invalid grid parsed from {text}.");
            if (text.Contains("G1CEC", StringComparison.OrdinalIgnoreCase) && !parsed.ContainsMyCall)
                throw new InvalidOperationException($"My callsign was not flagged in {text}.");
            if (text.Equals("V85NPV EC7KW IM77", StringComparison.OrdinalIgnoreCase)
                && (parsed.AddressedCall != "V85NPV" || parsed.HeardCall != "EC7KW" || parsed.GridOwnerCall != "EC7KW" || parsed.HuntTarget != ""))
            {
                throw new InvalidOperationException("Directed grid owner/hunt target handling failed.");
            }
            if (text.Equals("W8JDP 4X5JK -01", StringComparison.OrdinalIgnoreCase)
                && (parsed.AddressedCall != "W8JDP" || parsed.HeardCall != "4X5JK" || parsed.ContactableCall != "4X5JK" || parsed.HuntTarget != ""))
            {
                throw new InvalidOperationException("Directed report diagnostic mapping failed.");
            }
            if (text.Equals("CQ K9OM EN65", StringComparison.OrdinalIgnoreCase)
                && (parsed.HeardCall != "K9OM" || parsed.ContactableCall != "K9OM" || parsed.GridOwnerCall != "K9OM" || parsed.Grid != "EN65"))
            {
                throw new InvalidOperationException("CQ diagnostic mapping failed.");
            }
        }
    }

    [GeneratedRegex("^[A-R]{2}[0-9]{2}([A-X]{2})?$", RegexOptions.IgnoreCase)]
    private static partial Regex GridRegex();

    [GeneratedRegex("^[+-][0-9]{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportRegex();

    [GeneratedRegex("^R[+-][0-9]{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex RReportRegex();
}
