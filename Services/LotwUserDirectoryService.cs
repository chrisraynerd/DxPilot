using System.Globalization;
using System.IO;
using System.Net.Http;

namespace JtdxAutoResume.V3.Services;

public sealed record LotwUserActivity(
    string DisplayedCallsign,
    string MatchedCallsign,
    DateTime LastUploadUtc)
{
    public bool UsedBaseCallsign => !DisplayedCallsign.Equals(MatchedCallsign, StringComparison.OrdinalIgnoreCase);

    public string ToolTip
    {
        get
        {
            var match = UsedBaseCallsign ? $" (matched base callsign {MatchedCallsign})" : "";
            return $"LoTW user{match}\nLast upload to LoTW: {LastUploadUtc.ToLocalTime():dd MMM yyyy HH:mm}";
        }
    }
}

public sealed record LotwDirectoryRefreshResult(
    bool Updated,
    int CallsignCount,
    string Status,
    DateTime? DirectoryDateUtc);

/// <summary>
/// Maintains a local copy of ARRL's public Logbook of The World user-activity directory.
/// This service is display-only: it is not involved in ranking, targeting or transmission.
/// </summary>
public sealed class LotwUserDirectoryService : IDisposable
{
    public static readonly Uri DirectoryUri = new("https://lotw.arrl.org/lotw-user-activity.csv");
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);

    private const int MinimumValidDirectoryEntries = 1000;
    private const int MaximumDirectoryBytes = 32 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private Dictionary<string, DateTime> _lastUploadUtcByCall = new(StringComparer.OrdinalIgnoreCase);

    public LotwUserDirectoryService(string appFolder, HttpClient? httpClient = null)
    {
        CachePath = Path.Combine(appFolder, "lotw-user-activity.csv");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttpClient = httpClient == null;
    }

    public string CachePath { get; }
    public int Count
    {
        get
        {
            lock (_gate)
                return _lastUploadUtcByCall.Count;
        }
    }

    public DateTime? CacheDateUtc => File.Exists(CachePath) ? File.GetLastWriteTimeUtc(CachePath) : null;

    public LotwDirectoryRefreshResult LoadCached()
    {
        if (!File.Exists(CachePath))
            return new(false, 0, "LoTW station directory has not been downloaded yet.", null);

        try
        {
            var parsed = ParseCsv(File.ReadAllText(CachePath));
            if (parsed.Count < MinimumValidDirectoryEntries)
                return new(false, 0, "The cached LoTW station directory is incomplete and will be replaced.", CacheDateUtc);

            ReplaceDirectory(parsed);
            return new(false, parsed.Count, $"LoTW station directory loaded: {parsed.Count:N0} callsigns.", CacheDateUtc);
        }
        catch (Exception ex)
        {
            return new(false, 0, $"The cached LoTW station directory could not be read: {ex.GetBaseException().Message}", CacheDateUtc);
        }
    }

    public bool RefreshDue(DateTime utcNow)
    {
        var cacheDate = CacheDateUtc;
        return !cacheDate.HasValue || utcNow - cacheDate.Value >= RefreshInterval;
    }

    public async Task<LotwDirectoryRefreshResult> RefreshIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!RefreshDue(DateTime.UtcNow) && Count > 0)
            return new(false, Count, $"LoTW station directory is current: {Count:N0} callsigns.", CacheDateUtc);

        try
        {
            using var response = await _httpClient.GetAsync(
                DirectoryUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaximumDirectoryBytes)
                throw new InvalidDataException("ARRL returned a LoTW directory larger than the safety limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            if (memory.Length > MaximumDirectoryBytes)
                throw new InvalidDataException("ARRL returned a LoTW directory larger than the safety limit.");

            var csv = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            var parsed = ParseCsv(csv);
            if (parsed.Count < MinimumValidDirectoryEntries)
                throw new InvalidDataException($"ARRL returned only {parsed.Count:N0} valid LoTW entries.");

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var temporaryPath = $"{CachePath}.download.{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, csv, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, CachePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            ReplaceDirectory(parsed);
            var cacheDate = CacheDateUtc;
            return new(true, parsed.Count, $"LoTW station directory updated from ARRL: {parsed.Count:N0} callsigns.", cacheDate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var fallback = Count > 0 ? $" The cached list of {Count:N0} callsigns remains in use." : "";
            return new(false, Count, $"LoTW station directory update failed: {ex.GetBaseException().Message}.{fallback}", CacheDateUtc);
        }
    }

    public bool TryGetActivity(string? displayedCallsign, out LotwUserActivity activity)
    {
        lock (_gate)
            return TryResolveActivity(_lastUploadUtcByCall, displayedCallsign, out activity);
    }

    public static bool TryResolveActivity(
        IReadOnlyDictionary<string, DateTime> directory,
        string? displayedCallsign,
        out LotwUserActivity activity)
    {
        activity = default!;
        var displayed = Normalize(displayedCallsign);
        if (string.IsNullOrWhiteSpace(displayed))
            return false;

        foreach (var candidate in LookupCandidates(displayed))
        {
            if (!directory.TryGetValue(candidate, out var lastUploadUtc))
                continue;

            activity = new LotwUserActivity(displayed, candidate, lastUploadUtc);
            return true;
        }

        return false;
    }

    public static Dictionary<string, DateTime> ParseCsv(string csv)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(csv ?? "");
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
                continue;

            var callsign = Normalize(parts[0]);
            if (!CallsignNormalizer.IsValidLookupCallsign(callsign))
                continue;

            if (!DateTime.TryParseExact(
                    $"{parts[1]} {parts[2]}",
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var lastUploadUtc))
            {
                continue;
            }

            if (!result.TryGetValue(callsign, out var previous) || lastUploadUtc > previous)
                result[callsign] = lastUploadUtc;
        }

        return result;
    }

    private static IEnumerable<string> LookupCandidates(string displayed)
    {
        yield return displayed;

        var parts = displayed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            yield break;

        var portableSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "P", "M", "MM", "AM", "A", "QRP"
        };
        var candidates = parts
            .Where(part => !portableSuffixes.Contains(part))
            .Where(CallsignNormalizer.IsValidOnAirCallsign)
            .OrderByDescending(part => part.Any(char.IsDigit) && part.Any(char.IsLetter))
            .ThenByDescending(part => part.Length)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (!candidate.Equals(displayed, StringComparison.OrdinalIgnoreCase))
                yield return candidate;
        }
    }

    private static string Normalize(string? callsign) =>
        CallsignNormalizer.Normalize(callsign ?? "").Trim('<', '>');

    private void ReplaceDirectory(Dictionary<string, DateTime> parsed)
    {
        lock (_gate)
            _lastUploadUtcByCall = parsed;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
