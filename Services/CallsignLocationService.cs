using System.Collections.Concurrent;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public interface ICallsignLocationService : IDisposable
{
    ValueTask<CallsignLocationResult?> GetCachedAsync(string callsign, CancellationToken cancellationToken);
    bool QueueLookup(string callsign, CallsignLookupPriority priority, DateTime lastHeardUtc, bool forceRefresh = false);
    Task<string> TestQrzConnectionAsync(CancellationToken cancellationToken);
    void ClearCache();
    event EventHandler<CallsignLocationUpdatedEventArgs>? LocationUpdated;
}

public sealed class CallsignLocationService : ICallsignLocationService
{
    private readonly AppSettings _settings;
    private readonly CallsignLocationCache _cache;
    private readonly IQrzCallsignClient _client;
    private readonly ConcurrentStack<LookupRequest> _priorityQueue = new();
    private readonly ConcurrentQueue<LookupRequest> _backgroundQueue = new();
    private readonly ConcurrentDictionary<string, LookupRequest> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _failureCount;
    private DateTimeOffset _circuitOpenUntil = DateTimeOffset.MinValue;

    public CallsignLocationService(AppSettings settings, string appFolder, IQrzCallsignClient client)
    {
        _settings = settings;
        _client = client;
        _cache = new CallsignLocationCache(appFolder);
        _worker = Task.Run(WorkerAsync);
    }

    public event EventHandler<CallsignLocationUpdatedEventArgs>? LocationUpdated;

    public ValueTask<CallsignLocationResult?> GetCachedAsync(string callsign, CancellationToken cancellationToken)
    {
        return _cache.GetAsync(callsign, cancellationToken);
    }

    public bool QueueLookup(string callsign, CallsignLookupPriority priority, DateTime lastHeardUtc, bool forceRefresh = false)
    {
        var normal = CallsignNormalizer.Normalize(callsign);
        if (!_settings.EnableQrzCallsignLookup || !CallsignNormalizer.IsValidLookupCallsign(normal))
            return false;

        var heardUtc = lastHeardUtc.Kind == DateTimeKind.Utc ? lastHeardUtc : lastHeardUtc.ToUniversalTime();
        while (_queued.TryGetValue(normal, out var existing))
        {
            var promoted = existing.Priority == CallsignLookupPriority.Background
                && priority == CallsignLookupPriority.DecisionCritical;
            var updated = existing with
            {
                LastHeardUtc = heardUtc > existing.LastHeardUtc ? heardUtc : existing.LastHeardUtc,
                Priority = promoted ? CallsignLookupPriority.DecisionCritical : existing.Priority,
                ForceRefresh = existing.ForceRefresh || forceRefresh
            };
            if (!_queued.TryUpdate(normal, updated, existing))
                continue;

            if (promoted)
                Enqueue(updated);
            return true;
        }

        if (_queued.Count >= Math.Max(100, _settings.QrzLookupQueueLimit))
            return false;

        var request = new LookupRequest(normal, heardUtc, priority, forceRefresh);
        if (!_queued.TryAdd(normal, request))
            return QueueLookup(normal, priority, heardUtc, forceRefresh);

        Enqueue(request);
        return true;
    }

    public Task<string> TestQrzConnectionAsync(CancellationToken cancellationToken)
    {
        return _client.TestConnectionAsync(_settings, cancellationToken);
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    private async Task WorkerAsync()
    {
        try
        {
            while (true)
            {
                await _queueSignal.WaitAsync(_cts.Token);
                if (!TryTakeNext(out var queuedRequest))
                    continue;

                if (!_queued.TryGetValue(queuedRequest.Callsign, out var current))
                    continue;

                // A background token can remain after the same call is promoted. The
                // newer priority token owns the work, so the old token is harmlessly ignored.
                if (queuedRequest.Priority == CallsignLookupPriority.Background
                    && current.Priority == CallsignLookupPriority.DecisionCritical)
                {
                    continue;
                }

                try
                {
                    if (DateTime.UtcNow - current.LastHeardUtc > TimeSpan.FromSeconds(StaleLookupSeconds()))
                    {
                        PublishTerminalResult(current.Callsign, CallsignLookupStatus.Skipped,
                            "QRZ lookup skipped because the station is no longer fresh.");
                        continue;
                    }

                    await ProcessAsync(current, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PublishTerminalResult(current.Callsign, CallsignLookupStatus.Error,
                        $"QRZ lookup failed: {ex.Message}");
                }
                finally
                {
                    _queued.TryRemove(current.Callsign, out _);
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(LookupRequest request, CancellationToken cancellationToken)
    {
        if (!request.ForceRefresh)
        {
            var cached = await _cache.GetAsync(request.Callsign, cancellationToken);
            if (cached != null)
            {
                LocationUpdated?.Invoke(this, new CallsignLocationUpdatedEventArgs(cached));
                return;
            }
        }

        if (DateTimeOffset.UtcNow < _circuitOpenUntil)
        {
            PublishTerminalResult(request.Callsign, CallsignLookupStatus.Error,
                $"QRZ lookups are paused until {_circuitOpenUntil.LocalDateTime:HH:mm:ss} after repeated connection failures.");
            return;
        }

        var delay = Math.Clamp(_settings.QrzDelayBetweenLookupsMs, 0, 5000);
        if (request.ForceRefresh && request.Priority == CallsignLookupPriority.Background)
            delay = Math.Max(delay, 1500);
        if (delay > 0)
            await Task.Delay(delay, cancellationToken);

        var result = await _client.LookupAsync(request.Callsign, _settings, cancellationToken);
        if (result.Status == CallsignLookupStatus.Error)
        {
            _failureCount++;
            if (_failureCount >= Math.Max(3, _settings.QrzCircuitBreakerFailureCount))
                _circuitOpenUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _settings.QrzCircuitBreakerMinutes));
        }
        else
        {
            _failureCount = 0;
            _circuitOpenUntil = DateTimeOffset.MinValue;
        }

        if (result.Status is CallsignLookupStatus.Resolved or CallsignLookupStatus.NotFound or CallsignLookupStatus.NotUsCallsign)
            await _cache.SaveAsync(result, _settings, cancellationToken);

        LocationUpdated?.Invoke(this, new CallsignLocationUpdatedEventArgs(result));
    }

    private void Enqueue(LookupRequest request)
    {
        if (request.Priority == CallsignLookupPriority.DecisionCritical)
            _priorityQueue.Push(request);
        else
            _backgroundQueue.Enqueue(request);
        _queueSignal.Release();
    }

    private bool TryTakeNext(out LookupRequest request)
    {
        if (_priorityQueue.TryPop(out request!))
            return true;
        return _backgroundQueue.TryDequeue(out request!);
    }

    private int StaleLookupSeconds()
    {
        return Math.Max(30, _settings.CandidateMaxAgeSeconds);
    }

    private void PublishTerminalResult(string callsign, CallsignLookupStatus status, string reason)
    {
        var result = new CallsignLocationResult(
            callsign, null, null, null, null, status, CallsignDataSource.Qrz,
            DateTimeOffset.UtcNow, reason);
        LocationUpdated?.Invoke(this, new CallsignLocationUpdatedEventArgs(result));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); }
        catch { }
        _queueSignal.Dispose();
        _cts.Dispose();
        if (_client is IDisposable disposable)
            disposable.Dispose();
    }


    private sealed record LookupRequest(
        string Callsign,
        DateTime LastHeardUtc,
        CallsignLookupPriority Priority,
        bool ForceRefresh);
}
