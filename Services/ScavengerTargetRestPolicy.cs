namespace JtdxAutoResume.V3.Services;

// Per-callsign Scavenger scheduling only; never changes permanent suppression.
public sealed class ScavengerTargetRestPolicy
{
    private sealed record Attempt(DateTime First, DateTime Last);
    private sealed record Rest(int EligibleRound, int Rounds);
    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rest> _rests = new(StringComparer.OrdinalIgnoreCase);

    public void RecordAttempt(string call, DateTime now)
    {
        _attempts[call] = new(_attempts.TryGetValue(call, out var previous) ? previous.First : now, now);
    }

    public bool LimitReached(string call, DateTime now, int minutes, bool qsoHasProgress) =>
        !qsoHasProgress && _attempts.TryGetValue(call, out var attempt)
        && now >= attempt.First.AddMinutes(Math.Clamp(minutes, 1, 60));

    public DateTime FinalReplyUntil(string call, TimeSpan cycle) =>
        _attempts.TryGetValue(call, out var attempt)
            ? LateReplyRecoveryPolicy.FinalReplyGuardUntil(attempt.Last, cycle) : DateTime.MinValue;

    public void RestCall(string call, int round, int rounds)
    {
        rounds = Math.Clamp(rounds, 1, 12);
        _attempts.Remove(call);
        // The remainder of this round is not counted as a complete search round.
        _rests[call] = new(round + rounds + 1, rounds);
    }

    public int RoundsRemaining(string call, int round) => _rests.TryGetValue(call, out var rest)
        ? Math.Clamp(rest.EligibleRound - round, 0, rest.Rounds) : 0;

    public string Summary(int round) => string.Join(" · ", _rests
        .Where(pair => RoundsRemaining(pair.Key, round) > 0)
        .Select(pair => $"{pair.Key} resting — {RoundsRemaining(pair.Key, round)} search rounds remaining"));

    public void ForgetAttempts(string call) => _attempts.Remove(call);
}
