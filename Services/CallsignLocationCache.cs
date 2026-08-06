using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class CallsignLocationCache
{
    private readonly string _folder;
    private readonly ConcurrentDictionary<string, CallsignLocationResult> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public CallsignLocationCache(string appFolder)
    {
        _folder = Path.Combine(appFolder, "callsign_location_cache");
        Directory.CreateDirectory(_folder);
    }

    public ValueTask<CallsignLocationResult?> GetAsync(string callsign, CancellationToken cancellationToken)
    {
        var normal = CallsignNormalizer.Normalize(callsign);
        if (string.IsNullOrWhiteSpace(normal))
            return ValueTask.FromResult<CallsignLocationResult?>(null);

        if (_memory.TryGetValue(normal, out var cached))
            return ValueTask.FromResult<CallsignLocationResult?>(cached with { Source = CallsignDataSource.Cache });

        try
        {
            var path = CachePath(normal);
            if (!File.Exists(path))
                return ValueTask.FromResult<CallsignLocationResult?>(null);

            cancellationToken.ThrowIfCancellationRequested();
            var stored = JsonSerializer.Deserialize<StoredCallsignLocation>(File.ReadAllText(path));
            if (stored == null)
                return ValueTask.FromResult<CallsignLocationResult?>(null);

            if (stored.ExpiresUtc.HasValue && stored.ExpiresUtc < DateTimeOffset.UtcNow)
                return ValueTask.FromResult<CallsignLocationResult?>(null);

            var result = stored.ToResult();
            _memory[normal] = result;
            return ValueTask.FromResult<CallsignLocationResult?>(result with { Source = CallsignDataSource.Cache });
        }
        catch
        {
            return ValueTask.FromResult<CallsignLocationResult?>(null);
        }
    }

    public async Task SaveAsync(CallsignLocationResult result, AppSettings settings, CancellationToken cancellationToken)
    {
        var normal = CallsignNormalizer.Normalize(result.Callsign);
        if (string.IsNullOrWhiteSpace(normal))
            return;

        var status = result.Status;
        if (status == CallsignLookupStatus.Error && result.ErrorMessage?.Contains("auth", StringComparison.OrdinalIgnoreCase) == true)
            return;

        var stored = StoredCallsignLocation.FromResult(result, ExpiresUtc(result, settings));
        _memory[normal] = result;
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(CachePath(normal), JsonSerializer.Serialize(stored, _jsonOptions), cancellationToken);
    }

    public void Clear()
    {
        _memory.Clear();
        if (!Directory.Exists(_folder))
            return;

        foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try { File.Delete(file); }
            catch { }
        }
    }

    private DateTimeOffset? ExpiresUtc(CallsignLocationResult result, AppSettings settings)
    {
        return result.Status switch
        {
            CallsignLookupStatus.Resolved or CallsignLookupStatus.NotUsCallsign => DateTimeOffset.UtcNow.AddDays(Math.Max(1, settings.QrzSuccessCacheDays)),
            CallsignLookupStatus.NotFound => DateTimeOffset.UtcNow.AddDays(Math.Max(1, settings.QrzNotFoundCacheDays)),
            CallsignLookupStatus.Error => DateTimeOffset.UtcNow.AddMinutes(15),
            _ => null
        };
    }

    private string CachePath(string callsign)
    {
        var safe = string.Concat(callsign.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        return Path.Combine(_folder, $"{safe}.json");
    }

    private sealed class StoredCallsignLocation
    {
        public string Callsign { get; set; } = "";
        public string? State { get; set; }
        public string? Grid { get; set; }
        public string? Country { get; set; }
        public int? Dxcc { get; set; }
        public string? Iota { get; set; }
        public CallsignLookupStatus Status { get; set; }
        public CallsignDataSource Source { get; set; }
        public DateTimeOffset RetrievedAt { get; set; }
        public DateTimeOffset? ExpiresUtc { get; set; }
        public string? ErrorMessage { get; set; }

        public static StoredCallsignLocation FromResult(CallsignLocationResult result, DateTimeOffset? expiresUtc)
        {
            return new StoredCallsignLocation
            {
                Callsign = result.Callsign,
                State = result.State,
                Grid = result.Grid,
                Country = result.Country,
                Dxcc = result.Dxcc,
                Iota = result.Iota,
                Status = result.Status,
                Source = result.Source,
                RetrievedAt = result.RetrievedAt,
                ExpiresUtc = expiresUtc,
                ErrorMessage = result.ErrorMessage
            };
        }

        public CallsignLocationResult ToResult()
        {
            return new CallsignLocationResult(Callsign, State, Grid, Country, Dxcc, Status, Source, RetrievedAt, ErrorMessage, Iota);
        }
    }
}
