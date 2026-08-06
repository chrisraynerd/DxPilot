namespace JtdxAutoResume.V3.Models;

public enum WantedScope
{
    Overall,
    CurrentBand,
    CurrentMode,
    CurrentBandMode
}

public enum NeedStatus
{
    NeverWorked,
    WorkedNotLoTWConfirmed,
    LoTWConfirmed,
    Unknown
}

public enum WantedSniperMode
{
    Off,
    Active
}

public enum HuntingOperatingMode
{
    DxAssist,
    WantedSniper,
    LocationHunt
}

public enum WantedActionabilityStatus
{
    Actionable,
    Stale,
    QsoInProgress,
    SourceDecodeMissing,
    Suppressed,
    FailedSource,
    InvalidParse,
    UnknownDxcc,
    NotTargetable,
    AlreadyLoTWConfirmed,
    Other
}
