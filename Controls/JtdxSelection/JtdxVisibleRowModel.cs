using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public enum JtdxVisibleRowKind
{
    DecodeRow,
    MarkerRow,
    IgnoredPartialRow
}

public sealed class JtdxVisibleRow
{
    public JtdxVisibleRowKind Kind { get; init; }
    public DecodeMessage? Decode { get; init; }
    public int ScreenRowIndex { get; init; }
    public string MarkerText { get; init; } = "";
}

public sealed class JtdxVisibleRowModel
{
    private readonly List<JtdxVisibleRow> _rows = [];

    public long Version { get; private set; }
    public IReadOnlyList<JtdxVisibleRow> Rows => _rows;
    public bool HasKnownClear { get; private set; }
    public bool UsesClearedPaneOrigin { get; private set; }
    private long _clearGeneration;

    public void ResetForContext(long generation, bool bandChanged)
    {
        _clearGeneration = generation;
        HasKnownClear = bandChanged;
        UsesClearedPaneOrigin = false;
        _rows.Clear();
        Version++;
    }

    public bool CanLocateRows(JtdxBandActivityGridCalibration calibration) =>
        _rows.Count >= calibration.SafeVisibleFullRowCount || (HasKnownClear && calibration.NewestRowsAtBottom);

    public double RowCentreYRelative(JtdxVisibleRow row, JtdxBandActivityGridCalibration calibration)
    {
        // The calibrated first FULL row excludes the top row of the scrolled
        // pane. Following a known clear, that top row is present and counted.
        var origin = calibration.FirstFullRowCentreYRelative
            - (UsesClearedPaneOrigin && calibration.IgnoredPartialTopRow ? calibration.RowHeight : 0);
        return origin + row.ScreenRowIndex * calibration.RowHeight;
    }

    public void Rebuild(IReadOnlyList<DecodeMessage> decodeHistory, JtdxBandActivityGridCalibration calibration)
    {
        Version++;
        _rows.Clear();

        var safeRowCount = JtdxBandActivityGridCalibration.NormalizeRowCount(calibration.SafeVisibleFullRowCount);
        var indexedDecodes = decodeHistory
            .Where(decode => !HasKnownClear || decode.RadioContextGeneration == _clearGeneration)
            .Select((decode, index) => new { Decode = decode, Index = index })
            .ToList();
        var orderedDecodes = calibration.NewestRowsAtBottom
            ? indexedDecodes
                .OrderBy(item => item.Decode.ReceivedAt)
                .ThenByDescending(item => item.Index)
                .Select(item => item.Decode)
                .ToList()
            : indexedDecodes
                .OrderByDescending(item => item.Decode.ReceivedAt)
                .ThenByDescending(item => item.Index)
                .Select(item => item.Decode)
                .ToList();

        var fullVisualRows = new List<JtdxVisibleRow>();
        string? previousCycle = null;
        foreach (var decode in orderedDecodes)
        {
            var cycle = CycleKey(decode);
            if (previousCycle == null || !cycle.Equals(previousCycle, StringComparison.Ordinal))
            {
                fullVisualRows.Add(new JtdxVisibleRow
                {
                    Kind = JtdxVisibleRowKind.MarkerRow,
                    MarkerText = cycle
                });
            }

            fullVisualRows.Add(new JtdxVisibleRow
            {
                Kind = JtdxVisibleRowKind.DecodeRow,
                Decode = decode
            });
            previousCycle = cycle;
        }

        UsesClearedPaneOrigin = HasKnownClear && calibration.NewestRowsAtBottom
            && fullVisualRows.Count <= safeRowCount;
        var visible = calibration.NewestRowsAtBottom
            ? fullVisualRows.Skip(Math.Max(0, fullVisualRows.Count - safeRowCount)).ToList()
            : fullVisualRows.Take(safeRowCount).ToList();

        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            _rows.Add(new JtdxVisibleRow
            {
                Kind = row.Kind,
                Decode = row.Decode,
                MarkerText = row.MarkerText,
                ScreenRowIndex = i
            });
        }
    }

    public JtdxVisibleRow? FindDecode(DecodeMessage decode)
    {
        return _rows.FirstOrDefault(row => row.Kind == JtdxVisibleRowKind.DecodeRow
            && row.Decode != null
            && (ReferenceEquals(row.Decode, decode) || SameDecode(row.Decode, decode)));
    }

    private static bool SameDecode(DecodeMessage left, DecodeMessage right)
    {
        return left.RawText.Equals(right.RawText, StringComparison.Ordinal)
            && left.ContactableCall.Equals(right.ContactableCall, StringComparison.OrdinalIgnoreCase)
            && left.Callsign.Equals(right.Callsign, StringComparison.OrdinalIgnoreCase)
            && left.AudioOffset == right.AudioOffset
            && left.DecodeTime == right.DecodeTime
            && Math.Abs((left.ReceivedAt - right.ReceivedAt).TotalSeconds) < 1;
    }

    private static string CycleKey(DecodeMessage decode)
    {
        if (decode.DecodeTime.HasValue)
            return decode.DecodeTime.Value.ToString(@"hh\:mm\:ss");

        var seconds = decode.ReceivedAt.Second < 30 ? 0 : 30;
        return new DateTime(decode.ReceivedAt.Year, decode.ReceivedAt.Month, decode.ReceivedAt.Day, decode.ReceivedAt.Hour, decode.ReceivedAt.Minute, seconds).ToString("HH:mm:ss");
    }
}
