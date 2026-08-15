namespace JtdxAutoResume.V3.Models;

public sealed record CallsignLogProfile(
    string Key,
    string Callsign,
    string DisplayLabel,
    int QsoCount,
    bool IsAllCallsigns,
    bool IsCurrentCallsign,
    IReadOnlyList<string> Variants);
