using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxGuiGridSelector
{
    private readonly ScreenClicker _clicker;
    private readonly JtdxWindowLocator _windowLocator;
    private readonly Func<long> _getCurrentModelVersion;

    public JtdxGuiGridSelector(ScreenClicker clicker, JtdxWindowLocator windowLocator, Func<long> getCurrentModelVersion)
    {
        _clicker = clicker;
        _windowLocator = windowLocator;
        _getCurrentModelVersion = getCurrentModelVersion;
    }

    public SelectionResult Select(
        DecodeMessage target,
        string expectedCall,
        AppSettings settings,
        JtdxVisibleRowModel visibleRows,
        JtdxBandActivityGridCalibration calibration,
        string dxCallBefore)
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
            return Fail(result, SelectionFailureReason.CalibrationMissing, "52-row Band Activity calibration is incomplete.");

        var window = _windowLocator.FindMainWindow(settings.JtdxWindowTitleMatch);
        if (window == null)
            return Fail(result, SelectionFailureReason.JtdxWindowNotFound, $"No JTDX window matched '{settings.JtdxWindowTitleMatch}'.");

        if (!WindowMatchesCalibration(window, calibration))
            return Fail(result, SelectionFailureReason.JtdxWindowNotFullScreen, $"Current JTDX window {window.Width}x{window.Height} does not match calibrated {calibration.JtdxWindowWidth}x{calibration.JtdxWindowHeight}.");

        if (visibleRows.Rows.Count < calibration.SafeVisibleFullRowCount)
            return Fail(result, SelectionFailureReason.NotCurrentVisibleRow, $"JTDX grid model is still filling ({visibleRows.Rows.Count}/{calibration.SafeVisibleFullRowCount} rows). Wait until the Band Activity pane has filled before grid-clicking.");

        var row = visibleRows.FindDecode(target);
        if (row == null)
            return Fail(result, SelectionFailureReason.NotCurrentVisibleRow, "Target DecodeMessage object is not in the current visible row model.");

        if (row.Kind == JtdxVisibleRowKind.MarkerRow)
            return Fail(result, SelectionFailureReason.RowIsMarker, "Marker rows are not clickable.");

        if (row.Kind == JtdxVisibleRowKind.IgnoredPartialRow)
            return Fail(result, SelectionFailureReason.RowIsPartialIgnoredRow, "Ignored partial top row is not clickable.");

        if (row.ScreenRowIndex < 0 || row.ScreenRowIndex >= calibration.SafeVisibleFullRowCount)
            return Fail(result, SelectionFailureReason.RowOutsideSafeGrid, $"Row {row.ScreenRowIndex} is outside 0-{calibration.SafeVisibleFullRowCount - 1}.");

        var maxAge = Math.Max(15, settings.JtdxGuiMaxRowAgeSeconds);
        if (DateTime.Now - target.ReceivedAt > TimeSpan.FromSeconds(maxAge))
            return Fail(result, SelectionFailureReason.NotCurrentVisibleRow, $"Target decode is older than {maxAge}s.");

        if (_getCurrentModelVersion() != visibleRows.Version)
            return Fail(result, SelectionFailureReason.DecodeBatchChangedBeforeClick, "A newer UDP decode batch arrived before the click.");

        var clickX = window.Left + calibration.MessageClickXRelative;
        var clickY = (int)Math.Round(window.Top + calibration.FirstFullRowCentreYRelative + row.ScreenRowIndex * calibration.RowHeight);
        result.ScreenRowIndex = row.ScreenRowIndex;
        result.ClickX = clickX;
        result.ClickY = clickY;

        try
        {
            var hiddenOverlays = JtdxBandActivityOverlay.HideAllForClick();
            Thread.Sleep(150);
            try
            {
                _clicker.MoveDoubleClickRestore(clickX, clickY);
            }
            finally
            {
                JtdxBandActivityOverlay.RestoreHiddenAfterClick(hiddenOverlays);
            }

            result.Details = $"GUI grid double-click row {row.ScreenRowIndex} at {clickX},{clickY}.";
            return result;
        }
        catch (Exception ex)
        {
            return Fail(result, SelectionFailureReason.GuiClickFailed, ex.GetBaseException().Message);
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
