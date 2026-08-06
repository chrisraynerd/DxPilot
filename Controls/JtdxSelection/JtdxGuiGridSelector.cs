using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxGuiGridSelector
{
    private static readonly TimeSpan RowSettleDelay = TimeSpan.FromMilliseconds(150);
    private readonly ScreenClicker _clicker;
    private readonly JtdxWindowLocator _windowLocator;
    private readonly Func<long> _getCurrentModelVersion;

    public JtdxGuiGridSelector(ScreenClicker clicker, JtdxWindowLocator windowLocator, Func<long> getCurrentModelVersion)
    {
        _clicker = clicker;
        _windowLocator = windowLocator;
        _getCurrentModelVersion = getCurrentModelVersion;
    }

    public async Task<SelectionResult> SelectAsync(
        DecodeMessage target,
        string expectedCall,
        AppSettings settings,
        JtdxVisibleRowModel visibleRows,
        JtdxBandActivityGridCalibration calibration,
        string dxCallBefore,
        Func<TimeSpan, Task> delayAsync)
    {
        var result = new SelectionResult
        {
            SelectionMethod = JtdxSelectionMethod.GuiGridDoubleClick,
            ExpectedCall = expectedCall,
            JtdxDxCallBefore = dxCallBefore,
            TargetRawMessage = target.RawText,
            MessageType = target.MessageType,
            VisibleRowModelVersion = visibleRows.Version,
            CalibrationVersion = calibration.Version
        };

        if (!settings.JtdxGuiSelectionEnabled)
            return Fail(result, SelectionFailureReason.CalibrationMissing, "GUI selection is disabled.");

        if (!calibration.IsUsable)
            return Fail(result, SelectionFailureReason.CalibrationMissing, $"{calibration.SafeVisibleFullRowCount}-row Band Activity calibration is incomplete.");

        var modelVersionBeforeSettle = visibleRows.Version;
        var rowBeforeSettle = visibleRows.FindDecode(target)?.ScreenRowIndex;
        var hiddenOverlays = JtdxBandActivityOverlay.HideAllForClick();
        try
        {
            // Let the remainder of the current UDP decode batch update the live row model,
            // then calculate the physical row immediately before the double-click.
            await delayAsync(RowSettleDelay);
            result.VisibleRowModelVersion = visibleRows.Version;

            var window = _windowLocator.FindMainWindow(settings.JtdxWindowTitleMatch);
            if (window == null)
                return Fail(result, SelectionFailureReason.JtdxWindowNotFound, $"No JTDX window matched '{settings.JtdxWindowTitleMatch}'.");

            if (window.IsMinimized)
                return Fail(result, SelectionFailureReason.JtdxWindowMinimized, "JTDX is minimised. Restore it before GUI row selection.");

            if (!WindowMatchesCalibration(window, calibration))
                return Fail(result, SelectionFailureReason.JtdxWindowNotFullScreen, $"JTDX was resized: current {window.Width}x{window.Height}, calibrated {calibration.JtdxWindowWidth}x{calibration.JtdxWindowHeight}. Realign the {calibration.SafeVisibleFullRowCount}-row grid before GUI selection.");

            if (visibleRows.Rows.Count < calibration.SafeVisibleFullRowCount)
                return Fail(result, SelectionFailureReason.NotCurrentVisibleRow, $"JTDX grid model is still filling ({visibleRows.Rows.Count}/{calibration.SafeVisibleFullRowCount} rows). Wait until the Band Activity pane has filled before grid-clicking.");

            var settledModelVersion = visibleRows.Version;
            var row = visibleRows.FindDecode(target);
            if (row == null)
                return Fail(result, SelectionFailureReason.NotCurrentVisibleRow, "Target DecodeMessage object is not in the current visible row model after the 150 ms settling period.");

            if (row.Kind == JtdxVisibleRowKind.MarkerRow)
                return Fail(result, SelectionFailureReason.RowIsMarker, "Marker rows are not clickable.");

            if (row.Kind == JtdxVisibleRowKind.IgnoredPartialRow)
                return Fail(result, SelectionFailureReason.RowIsPartialIgnoredRow, "Ignored partial top row is not clickable.");

            if (row.ScreenRowIndex < 0 || row.ScreenRowIndex >= calibration.SafeVisibleFullRowCount)
                return Fail(result, SelectionFailureReason.RowOutsideSafeGrid, $"Row {row.ScreenRowIndex} is outside 0-{calibration.SafeVisibleFullRowCount - 1}.");

            if (_getCurrentModelVersion() != settledModelVersion || visibleRows.Version != settledModelVersion)
                return Fail(result, SelectionFailureReason.DecodeBatchChangedBeforeClick, "A newer UDP decode changed the row model during final selection.");

            var clickX = window.Left + calibration.MessageClickXRelative;
            var clickY = (int)Math.Round(window.Top + calibration.FirstFullRowCentreYRelative + row.ScreenRowIndex * calibration.RowHeight);
            result.ScreenRowIndex = row.ScreenRowIndex;
            result.ClickX = clickX;
            result.ClickY = clickY;

            try
            {
                _clicker.MoveDoubleClickRestore(clickX, clickY);
                result.SelectionActionAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                return Fail(result, SelectionFailureReason.GuiClickFailed, ex.GetBaseException().Message);
            }

            result.Details = $"GUI grid settled 150 ms; model v{modelVersionBeforeSettle} row {rowBeforeSettle?.ToString() ?? "not visible"} became v{settledModelVersion} row {row.ScreenRowIndex}; double-clicked at {clickX},{clickY}.";
            return result;
        }
        finally
        {
            JtdxBandActivityOverlay.RestoreHiddenAfterClick(hiddenOverlays);
        }
    }

    private static bool WindowMatchesCalibration(JtdxWindowInfo window, JtdxBandActivityGridCalibration calibration)
    {
        if (calibration.JtdxWindowWidth <= 0 || calibration.JtdxWindowHeight <= 0)
            return true;

        return Math.Abs(window.Width - calibration.JtdxWindowWidth) <= 4
            && Math.Abs(window.Height - calibration.JtdxWindowHeight) <= 4;
    }

    private static SelectionResult Fail(SelectionResult result, SelectionFailureReason reason, string detail)
    {
        result.FailureReason = reason;
        result.FailureDetail = detail;
        return result;
    }
}
