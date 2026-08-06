using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class AutoResumeService
{
    private readonly PixelDetector _pixels;
    private readonly ScreenClicker _clicker;
    private readonly BandScheduler _scheduler;
    private CancellationTokenSource? _cts;
    private bool _lastWasGrey;
    private long _lastFire;
    private DateTime _lastBlockedLogAt = DateTime.MinValue;

    public AutoResumeService(PixelDetector pixels, ScreenClicker clicker, BandScheduler scheduler)
    {
        _pixels = pixels;
        _clicker = clicker;
        _scheduler = scheduler;
        _scheduler.ActionLogged += message => ActionLogged?.Invoke(message);
    }

    public bool IsRunning => _cts != null;
    public int ResumeCount { get; private set; }
    public DateTime? LastResumeAt { get; private set; }

    public event Action<string>? StatusChanged;
    public event Action<string>? ActionLogged;
    public event Action? Resumed;
    public event Action<int, int, bool>? PixelStateChanged;
    public Func<bool>? ShouldUseCqReset { get; set; }
    public Func<bool>? ShouldClickEnableTx { get; set; }

    public void Start(AppSettings settings, IList<BandScheduleItem> schedule)
    {
        Stop();

        var scheduleSnapshot = schedule
            .Select(item => new BandScheduleItem
            {
                Enabled = item.Enabled,
                Label = item.Label,
                Hour = item.Hour,
                Minute = item.Minute,
                X = item.X,
                Y = item.Y
            })
            .ToList();

        ResumeCount = 0;
        LastResumeAt = null;
        _lastWasGrey = false;
        _lastFire = 0;
        _scheduler.Reset(scheduleSnapshot);
        _clicker.KeepAwake(true);

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunLoopAsync(settings, scheduleSnapshot, _cts.Token));
        StatusChanged?.Invoke($"AutoResume running at {settings.IntervalMs} ms.");
        ActionLogged?.Invoke("AutoResume started.");
    }

    public void Stop()
    {
        if (_cts == null)
            return;

        _cts.Cancel();
        _cts = null;
        _clicker.KeepAwake(false);
        StatusChanged?.Invoke("AutoResume stopped.");
        ActionLogged?.Invoke("AutoResume stopped.");
    }

    private async Task RunLoopAsync(AppSettings settings, IList<BandScheduleItem> schedule, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Tick(settings, schedule);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"AutoResume tick error: {ex.Message}");
            }

            await Task.Delay(Math.Max(50, settings.IntervalMs), cancellationToken).ContinueWith(_ => { });
        }
    }

    private void Tick(AppSettings settings, IList<BandScheduleItem> schedule)
    {
        _scheduler.Tick(schedule, settings);

        var elapsed = Environment.TickCount64 - _lastFire;
        if (elapsed < settings.CooldownMs)
            return;

        var (greyPct, redPct) = _pixels.GetEnableTxStats(
            settings.EnableTxX,
            settings.EnableTxY,
            settings.BoxRadius,
            settings.EnableTxOffRgb,
            settings.EnableTxOnRgb,
            settings.Tolerance);

        var looksOff = greyPct >= settings.MinGreyPercent && redPct <= settings.MaxRedPercent;
        PixelStateChanged?.Invoke(greyPct, redPct, looksOff);
        StatusChanged?.Invoke($"AutoResume running. Grey {greyPct}% / red {redPct}%.");

        if (looksOff)
        {
            if (!_lastWasGrey)
            {
                _lastWasGrey = true;
                return;
            }

            // AutoResume never deliberately selects CQ/TX6. It may only re-enable
            // TX after the target gate confirms a locked station.
            _ = InvokeOnUiThread(ShouldUseCqReset);
            var shouldClickEnable = InvokeOnUiThread(ShouldClickEnableTx) ?? false;
            if (!shouldClickEnable)
            {
                _lastFire = Environment.TickCount64;
                _lastWasGrey = false;
                LogBlocked("CQ/TX6 is disabled; Enable TX remains blocked until JTDX confirms a selected target.");
                StatusChanged?.Invoke("AutoResume waiting for JTDX to accept selected target before enabling TX.");
                return;
            }

            _clicker.MoveClickRestore(settings.EnableTxX, settings.EnableTxY);

            _lastFire = Environment.TickCount64;
            _lastWasGrey = false;
            ResumeCount++;
            LastResumeAt = DateTime.Now;

            var message = $"Resumed at {LastResumeAt.Value:HH:mm:ss} using Locked Target Recovery: Enable TX only.";
            StatusChanged?.Invoke(message);
            ActionLogged?.Invoke(message);
            Resumed?.Invoke();
        }
        else
        {
            _lastWasGrey = false;
        }
    }

    private static bool? InvokeOnUiThread(Func<bool>? callback)
    {
        if (callback == null)
            return null;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return callback();

        return dispatcher.Invoke(callback);
    }

    private void LogBlocked(string message)
    {
        if (DateTime.Now - _lastBlockedLogAt < TimeSpan.FromSeconds(10))
            return;

        _lastBlockedLogAt = DateTime.Now;
        ActionLogged?.Invoke(message);
    }
}
