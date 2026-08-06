namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public enum SelectionFailureReason
{
    None,
    NotCurrentVisibleRow,
    RowIsMarker,
    RowIsPartialIgnoredRow,
    RowOutsideSafeGrid,
    CalibrationMissing,
    JtdxWindowNotFound,
    JtdxWindowMinimized,
    JtdxWindowNotFullScreen,
    DecodeBatchChangedBeforeClick,
    ConfirmationTimedOut,
    JtdxSelectedWrongCall,
    UdpReplyFailed,
    GuiClickFailed
}
