namespace JtdxAutoResume.V3.Models;

public enum CallsignLookupStatus
{
    Resolved,
    Pending,
    NotFound,
    NotUsCallsign,
    Disabled,
    Skipped,
    Error
}

public enum CallsignLookupPriority
{
    Background,
    DecisionCritical
}

public enum CallsignDataSource
{
    Qrz,
    Cache,
    Manual,
    Unknown
}

public enum DecodeGridSource
{
    Ft8Message,
    SessionObservation,
    Adif,
    Qrz,
    Manual,
    Unknown
}

public sealed record CallsignLocationResult(
    string Callsign,
    string? State,
    string? Grid,
    string? Country,
    int? Dxcc,
    CallsignLookupStatus Status,
    CallsignDataSource Source,
    DateTimeOffset RetrievedAt,
    string? ErrorMessage = null,
    string? Iota = null,
    double? Latitude = null,
    double? Longitude = null,
    string? GeoLocationSource = null,
    int PrecisionVersion = 0);

public sealed class CallsignLocationUpdatedEventArgs : EventArgs
{
    public CallsignLocationUpdatedEventArgs(CallsignLocationResult result)
    {
        Result = result;
    }

    public CallsignLocationResult Result { get; }
}
