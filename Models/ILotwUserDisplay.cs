namespace JtdxAutoResume.V3.Models;

/// <summary>
/// Display metadata from ARRL's LoTW user-activity directory.
/// These fields must never be used to rank or select a station.
/// </summary>
public interface ILotwUserDisplay
{
    bool IsLotwUser { get; set; }
    DateTime? LotwLastUploadUtc { get; set; }
    string LotwUserToolTip { get; set; }
}
