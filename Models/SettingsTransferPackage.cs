namespace JtdxAutoResume.V3.Models;

public sealed class SettingsTransferPackage
{
    public const string ExpectedFormat = "JtdxAutoResume.V3.SettingsExport";
    public const int CurrentFormatVersion = 1;

    public string Format { get; set; } = ExpectedFormat;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public string ApplicationVersion { get; set; } = "3.0";
    public bool QrzPasswordExcluded { get; set; } = true;
    public AppSettings Settings { get; set; } = new();
    public List<BandScheduleItem> Schedule { get; set; } = new();
}

public sealed class SettingsImportPayload
{
    public AppSettings Settings { get; set; } = new();
    public List<BandScheduleItem>? Schedule { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    public bool IsLegacySettingsFile { get; set; }
    public bool QrzPasswordExcluded { get; set; }
}
