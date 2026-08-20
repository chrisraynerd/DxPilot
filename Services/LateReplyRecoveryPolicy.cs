using System.Text.RegularExpressions;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class RecentCallAttempt
{
    public string Callsign { get; init; } = "";
    public string Band { get; init; } = "";
    public string Mode { get; init; } = "";
    public DateTime LastAttemptUtc { get; set; }
    public bool WasNewDxcc { get; init; }
    public string WantedReason { get; init; } = "";
    public string SourceBlock { get; init; } = "";
    public bool Consumed { get; set; }
}

public static class LateReplyRecoveryPolicy
{
    public static DateTime FinalReplyGuardUntil(DateTime transmittedAt, TimeSpan completeTxCycle) =>
        transmittedAt + completeTxCycle + TimeSpan.FromSeconds(2);

    public static bool CanInterruptCurrentTarget(
        bool currentTargetExists,
        bool currentQsoHasProgress,
        bool currentTargetIsNewDxcc,
        bool replyingTargetIsNewDxcc) =>
        !currentTargetExists
        || !currentQsoHasProgress && (!currentTargetIsNewDxcc || replyingTargetIsNewDxcc);

    public static bool TryMatch(
        DecodeMessage decode,
        string myCallsign,
        IEnumerable<RecentCallAttempt> recentAttempts,
        DateTime nowUtc,
        int recoveryMinutes,
        out RecentCallAttempt? match)
    {
        match = null;
        if (!TryGetDirectedProgressCall(decode.RawText, myCallsign, out var replyingCall))
            return false;

        var decodeUtc = decode.ReceivedAt.Kind == DateTimeKind.Utc
            ? decode.ReceivedAt
            : decode.ReceivedAt.ToUniversalTime();
        var decodeAge = nowUtc - decodeUtc;
        if (decodeAge < TimeSpan.FromSeconds(-2) || decodeAge > TimeSpan.FromSeconds(45))
            return false;

        var window = TimeSpan.FromMinutes(Math.Clamp(recoveryMinutes, 1, 15));
        var mode = AmateurBandMapper.NormalizeMode(decode.Mode);
        match = recentAttempts
            .Where(attempt => !attempt.Consumed)
            .Where(attempt => attempt.Callsign.Equals(replyingCall, StringComparison.OrdinalIgnoreCase))
            .Where(attempt => attempt.Band.Equals(decode.Band, StringComparison.OrdinalIgnoreCase))
            .Where(attempt => AmateurBandMapper.NormalizeMode(attempt.Mode).Equals(mode, StringComparison.OrdinalIgnoreCase))
            .Where(attempt => nowUtc - attempt.LastAttemptUtc >= TimeSpan.Zero
                && nowUtc - attempt.LastAttemptUtc <= window)
            .OrderByDescending(attempt => attempt.LastAttemptUtc)
            .FirstOrDefault();
        return match != null;
    }

    public static bool TryGetDirectedProgressCall(string text, string myCallsign, out string replyingCall)
    {
        replyingCall = "";
        var tokens = NormalizeTokens(text);
        var myCall = CallsignNormalizer.Normalize(myCallsign);
        if (tokens.Length < 3
            || string.IsNullOrWhiteSpace(myCall)
            || !tokens[0].Equals(myCall, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = CallsignNormalizer.Normalize(tokens[1]);
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Equals(myCall, StringComparison.OrdinalIgnoreCase)
            || !candidate.Any(char.IsDigit)
            || !candidate.Any(char.IsLetter))
        {
            return false;
        }

        var payload = tokens[2];
        var isQsoProgress = Regex.IsMatch(payload, @"^R?[+-]\d{1,2}$", RegexOptions.CultureInvariant)
            || payload.Equals("RRR", StringComparison.OrdinalIgnoreCase)
            || payload.Equals("RR73", StringComparison.OrdinalIgnoreCase)
            || payload.Equals("73", StringComparison.OrdinalIgnoreCase);
        if (!isQsoProgress)
            return false;

        replyingCall = candidate;
        return true;
    }

    private static string[] NormalizeTokens(string text) =>
        text.ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token != "~" && token != "TX")
            .Select(token => token.Trim('~', '*'))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
}
