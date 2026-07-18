using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public enum JtdxSelectionMethod
{
    UdpReply,
    GuiGridDoubleClick
}

public sealed class SelectionResult
{
    public bool Success { get; set; }
    public JtdxSelectionMethod SelectionMethod { get; set; }
    public string ExpectedCall { get; set; } = "";
    public string JtdxDxCallBefore { get; set; } = "";
    public string JtdxDxCallAfter { get; set; } = "";
    public string TargetRawMessage { get; set; } = "";
    public Ft8MessageType MessageType { get; set; } = Ft8MessageType.Unknown;
    public int? ScreenRowIndex { get; set; }
    public int? ClickX { get; set; }
    public int? ClickY { get; set; }
    public string CalibrationVersion { get; set; } = "";
    public SelectionFailureReason FailureReason { get; set; }
    public string FailureDetail { get; set; } = "";
    public DateTime? ConfirmationTime { get; set; }
    public long VisibleRowModelVersion { get; set; }
    public string Details { get; set; } = "";

    public string FailureText => FailureReason == SelectionFailureReason.None
        ? ""
        : string.IsNullOrWhiteSpace(FailureDetail) ? FailureReason.ToString() : $"{FailureReason}: {FailureDetail}";
}
