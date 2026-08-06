using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Xml.Linq;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public interface IQrzCallsignClient
{
    Task<CallsignLocationResult> LookupAsync(string callsign, AppSettings settings, CancellationToken cancellationToken);
    Task<string> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken);
}

public sealed class QrzCallsignClient : IQrzCallsignClient, IDisposable
{
    private static readonly Uri BaseUri = new("https://xmldata.qrz.com/xml/current/");
    private readonly HttpClient _httpClient = new();
    private string _sessionKey = "";
    private DateTimeOffset _sessionKeyRetrievedAt = DateTimeOffset.MinValue;

    public async Task<CallsignLocationResult> LookupAsync(string callsign, AppSettings settings, CancellationToken cancellationToken)
    {
        var normal = CallsignNormalizer.Normalize(callsign);
        try
        {
            if (!settings.EnableQrzCallsignLookup)
                return Disabled(normal, "QRZ lookup disabled.");
            if (!CallsignNormalizer.IsValidLookupCallsign(normal))
                return Error(normal, "Invalid callsign.");
            if (string.IsNullOrWhiteSpace(settings.QrzUsername) || string.IsNullOrWhiteSpace(settings.QrzPassword))
                return Disabled(normal, "QRZ credentials missing.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.QrzLookupTimeoutSeconds, 1, 15)));

            var key = await GetSessionKeyAsync(settings, timeout.Token);
            if (string.IsNullOrWhiteSpace(key))
                return Error(normal, "QRZ authentication failed.");

            var result = await LookupWithSessionAsync(normal, key, timeout.Token);
            if (result.ErrorMessage?.Contains("Session Timeout", StringComparison.OrdinalIgnoreCase) == true
                || result.ErrorMessage?.Contains("Invalid session", StringComparison.OrdinalIgnoreCase) == true)
            {
                _sessionKey = "";
                key = await GetSessionKeyAsync(settings, timeout.Token);
                if (!string.IsNullOrWhiteSpace(key))
                    result = await LookupWithSessionAsync(normal, key, timeout.Token);
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(normal, "QRZ lookup timed out.");
        }
        catch
        {
            return Error(normal, "QRZ lookup failed.");
        }
    }

    public async Task<string> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.EnableQrzCallsignLookup)
            return "QRZ lookup is disabled.";
        if (string.IsNullOrWhiteSpace(settings.QrzUsername) || string.IsNullOrWhiteSpace(settings.QrzPassword))
            return "QRZ credentials are missing.";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.QrzLookupTimeoutSeconds, 1, 15)));
        var key = await GetSessionKeyAsync(settings, timeout.Token);
        if (string.IsNullOrWhiteSpace(key))
            return "QRZ login failed or lookup access is unavailable.";

        var testCall = string.IsNullOrWhiteSpace(settings.QrzTestCallsign) ? settings.MyCallsign : settings.QrzTestCallsign;
        var result = await LookupAsync(testCall, settings, timeout.Token);
        return result.Status == CallsignLookupStatus.Resolved || result.Status == CallsignLookupStatus.NotUsCallsign
            ? $"QRZ login OK. Lookup OK for {result.Callsign}. State: {FieldState(result.State)}. Grid: {FieldState(result.Grid)}. Country: {FieldState(result.Country)}. DXCC: {(result.Dxcc.HasValue ? "available" : "missing")}."
            : $"QRZ login OK, lookup returned {result.Status}: {result.ErrorMessage}";
    }

    private async Task<string> GetSessionKeyAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_sessionKey) && DateTimeOffset.UtcNow - _sessionKeyRetrievedAt < TimeSpan.FromHours(12))
            return _sessionKey;

        var url = $"{BaseUri}?username={Uri.EscapeDataString(settings.QrzUsername)}&password={Uri.EscapeDataString(settings.QrzPassword)}&agent={Uri.EscapeDataString(Agent())}";
        var xml = await _httpClient.GetStringAsync(url, cancellationToken);
        var doc = XDocument.Parse(xml);
        var key = ElementValue(doc, "Key");
        var error = ElementValue(doc, "Error");
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(key))
            return "";

        _sessionKey = key;
        _sessionKeyRetrievedAt = DateTimeOffset.UtcNow;
        return _sessionKey;
    }

    private async Task<CallsignLocationResult> LookupWithSessionAsync(string callsign, string sessionKey, CancellationToken cancellationToken)
    {
        var url = $"{BaseUri}?s={Uri.EscapeDataString(sessionKey)}&callsign={Uri.EscapeDataString(callsign)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            return Error(callsign, $"Transient QRZ HTTP {(int)response.StatusCode}");

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = XDocument.Parse(xml);
        var sessionError = ElementValue(doc, "Error");
        if (!string.IsNullOrWhiteSpace(sessionError))
        {
            var status = sessionError.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? CallsignLookupStatus.NotFound
                : CallsignLookupStatus.Error;
            return new CallsignLocationResult(callsign, null, null, null, null, status, CallsignDataSource.Qrz, DateTimeOffset.UtcNow, sessionError);
        }

        var country = EmptyToNull(ElementValue(doc, "country"));
        var state = EmptyToNull(ElementValue(doc, "state"));
        var grid = EmptyToNull(ElementValue(doc, "grid"));
        var iota = EmptyToNull(ElementValue(doc, "iota"));
        var dxccRaw = EmptyToNull(ElementValue(doc, "dxcc")) ?? EmptyToNull(ElementValue(doc, "ccode"));
        int? dxcc = int.TryParse(dxccRaw, out var parsedDxcc) ? parsedDxcc : null;
        var validGrid = MaidenheadGrid.Normalize(grid ?? "").IsValid ? MaidenheadGrid.Normalize(grid ?? "").Grid4 : null;

        return new CallsignLocationResult(
            callsign,
            state,
            validGrid,
            country,
            dxcc,
            string.IsNullOrWhiteSpace(country) ? CallsignLookupStatus.NotFound : CallsignLookupStatus.Resolved,
            CallsignDataSource.Qrz,
            DateTimeOffset.UtcNow,
            Iota: iota);
    }

    private static string ElementValue(XDocument doc, string localName)
    {
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
    }

    private static string? EmptyToNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normal = value.Trim();
        return normal.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            || normal.Equals("NONE", StringComparison.OrdinalIgnoreCase)
            || normal.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || normal.Equals("0000", StringComparison.OrdinalIgnoreCase)
            ? null
            : normal;
    }

    private static CallsignLocationResult Disabled(string callsign, string reason)
    {
        return new CallsignLocationResult(callsign, null, null, null, null, CallsignLookupStatus.Disabled, CallsignDataSource.Unknown, DateTimeOffset.UtcNow, reason);
    }

    private static CallsignLocationResult Error(string callsign, string reason)
    {
        return new CallsignLocationResult(callsign, null, null, null, null, CallsignLookupStatus.Error, CallsignDataSource.Qrz, DateTimeOffset.UtcNow, reason);
    }

    private static string FieldState(string? value) => string.IsNullOrWhiteSpace(value) ? "missing" : "available";

    private static string Agent()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.0";
        return $"AutoResume-{version}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
