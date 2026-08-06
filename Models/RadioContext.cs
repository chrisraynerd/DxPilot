namespace JtdxAutoResume.V3.Models;

public sealed class RadioContext
{
    public ulong DialFrequencyHz { get; init; }
    public string Band { get; init; } = "";
    public string Mode { get; init; } = "";
    public uint TrPeriodSeconds { get; init; }
    public long Generation { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;

    public string FrequencyDisplay => DialFrequencyHz == 0
        ? "Frequency unknown"
        : DialFrequencyHz >= 1_000_000
            ? $"{DialFrequencyHz / 1_000_000d:0.000000} MHz"
            : $"{DialFrequencyHz / 1_000d:0.000} kHz";

    public string BandDisplay => string.IsNullOrWhiteSpace(Band) ? "Unknown band" : Band;
    public string ModeDisplay => string.IsNullOrWhiteSpace(Mode) ? "Unknown mode" : Mode;
    public string Display => $"{BandDisplay} · {ModeDisplay} · {FrequencyDisplay}";
}
