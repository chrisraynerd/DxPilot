using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class BandScheduler
{
    private readonly ScreenClicker _clicker;
    private readonly PixelDetector _pixels;
    private long _lastScheduleFire;
    private bool _lastWasGreen;
    private BandScheduleItem? _pendingSchedule;
    private DateTime _pendingSetAt = DateTime.MinValue;

    public BandScheduler(ScreenClicker clicker, PixelDetector pixels)
    {
        _clicker = clicker;
        _pixels = pixels;
    }

    public event Action<string>? ActionLogged;

    public void Reset(IEnumerable<BandScheduleItem> schedule)
    {
        foreach (var item in schedule)
            item.LastFiredDate = DateTime.MinValue.Date;

        _pendingSchedule = null;
        _pendingSetAt = DateTime.MinValue;
        _lastWasGreen = false;
        _lastScheduleFire = 0;
    }

    public void Tick(IList<BandScheduleItem> schedule, AppSettings settings)
    {
        if (schedule.Count == 0)
            return;

        var isGreenNow = IsRxGreenNow(settings);
        var justTurnedGreen = isGreenNow && !_lastWasGreen;
        _lastWasGreen = isGreenNow;

        if (_pendingSchedule != null)
        {
            if ((DateTime.Now - _pendingSetAt).TotalMilliseconds > 120000)
            {
                ActionLogged?.Invoke($"Band change '{Label(_pendingSchedule)}' timed out waiting for RX green.");
                _pendingSchedule = null;
                return;
            }

            if (justTurnedGreen)
            {
                _clicker.MoveClickRestore(_pendingSchedule.X, _pendingSchedule.Y);
                _pendingSchedule.LastFiredDate = DateTime.Now.Date;
                _lastScheduleFire = Environment.TickCount64;
                ActionLogged?.Invoke($"Band schedule fired '{Label(_pendingSchedule)}' at {DateTime.Now:HH:mm} after RX green.");
                _pendingSchedule = null;
            }

            return;
        }

        var since = Environment.TickCount64 - _lastScheduleFire;
        if (since >= 0 && since < 2000)
            return;

        var now = DateTime.Now;
        foreach (var item in schedule)
        {
            if (!item.Enabled || item.X == 0 && item.Y == 0)
                continue;
            if (item.Hour != now.Hour || item.Minute != now.Minute)
                continue;
            if (item.LastFiredDate == now.Date)
                continue;

            _pendingSchedule = item;
            _pendingSetAt = DateTime.Now;
            ActionLogged?.Invoke($"Band change '{Label(item)}' queued, waiting for RX green.");
            break;
        }
    }

    public bool IsRxGreenNow(AppSettings settings)
    {
        // V2-compatible fail-open behavior: if no RX gate has been configured,
        // scheduled band changes are allowed immediately.
        if (settings.RxX == 0 && settings.RxY == 0)
            return true;

        var greenPct = _pixels.PixelPercentMatch(
            settings.RxX,
            settings.RxY,
            settings.RxRadius,
            settings.RxGreenRgb,
            settings.RxTolerance);

        return greenPct >= settings.MinGreenPercent;
    }

    private static string Label(BandScheduleItem item)
    {
        return string.IsNullOrWhiteSpace(item.Label) ? "schedule" : item.Label;
    }
}
