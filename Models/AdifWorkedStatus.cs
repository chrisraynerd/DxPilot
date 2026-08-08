namespace JtdxAutoResume.V3.Models;

public sealed class AdifMergeResult
{
    public IReadOnlyList<AdifQso> UniqueQsos { get; init; } = Array.Empty<AdifQso>();
    public int FullQsoCount { get; init; }
    public int LiveQsoCount { get; init; }
    public int DuplicateCount { get; init; }
    public WorkedStatusIndexes Indexes { get; init; } = new();
}

public sealed class WorkedStatusIndexes
{
    public Dictionary<string, DxccWorkedStatus> Dxcc { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SimpleWorkedStatus> Grids { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SimpleWorkedStatus> States { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SimpleWorkedStatus> Iotas { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DxccWorkedStatus
{
    public string DxccNumber { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string Source { get; set; } = "";
    public bool WorkedAny { get; set; }
    public bool LoTWConfirmedAny { get; set; }
    public bool PaperConfirmedAny { get; set; }
    public bool EqslConfirmedAny { get; set; }
    public bool ConfirmedAny { get; set; }
    public HashSet<string> WorkedBands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorkedBandModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConfirmedBands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorkedModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConfirmedModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConfirmedBandModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LoTWConfirmedBands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LoTWConfirmedModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LoTWConfirmedBandModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastWorkedDate { get; set; }
    public DateTime? LastConfirmedDate { get; set; }
}

public sealed class SimpleWorkedStatus
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public bool WorkedAny { get; set; }
    public int WorkedQsoCount { get; set; }
    public int LoTWConfirmedQsoCount { get; set; }
    public bool LoTWConfirmedAny { get; set; }
    public bool PaperConfirmedAny { get; set; }
    public bool EqslConfirmedAny { get; set; }
    public bool ConfirmedAny { get; set; }
    public HashSet<string> WorkedBands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorkedModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorkedBandModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConfirmedBands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConfirmedModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConfirmedBandModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LoTWConfirmedBands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LoTWConfirmedModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LoTWConfirmedBandModes { get; } = new(StringComparer.OrdinalIgnoreCase);
}
