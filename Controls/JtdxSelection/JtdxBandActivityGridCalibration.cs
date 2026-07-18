using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxBandActivityGridCalibration
{
    public const int SafeFullRowCount = 52;
    public const int DefaultBandActivityLeft = 12;
    public const int DefaultBandActivityTop = 73;
    public const int DefaultBandActivityRight = 763;
    public const int DefaultBandActivityBottom = 915;
    public const int DefaultFirstFullRowCentreY = 89;
    public const double DefaultRowHeight = 16.038095238095238;
    public const int DefaultMessageClickX = 503;

    public string MonitorId { get; set; } = "";
    public string JtdxWindowTitle { get; set; } = "";
    public string JtdxWindowProcess { get; set; } = "";
    public int JtdxWindowLeft { get; set; }
    public int JtdxWindowTop { get; set; }
    public int JtdxWindowWidth { get; set; }
    public int JtdxWindowHeight { get; set; }
    public int BandActivityLeftRelative { get; set; }
    public int BandActivityTopRelative { get; set; }
    public int BandActivityWidth { get; set; }
    public int BandActivityHeight { get; set; }
    public int FirstFullRowCentreYRelative { get; set; }
    public double RowHeight { get; set; }
    public int SafeVisibleFullRowCount { get; set; } = SafeFullRowCount;
    public bool IgnoredPartialTopRow { get; set; } = true;
    public int MessageClickXRelative { get; set; }
    public bool NewestRowsAtBottom { get; set; } = true;
    public string Version { get; set; } = "grid-v1";
    public DateTime CalibrationDate { get; set; } = DateTime.Now;

    public static JtdxBandActivityGridCalibration CreateDefault(JtdxWindowInfo window)
    {
        return new JtdxBandActivityGridCalibration
        {
            MonitorId = $"{window.Left},{window.Top}",
            JtdxWindowTitle = window.Title,
            JtdxWindowProcess = window.ProcessId.ToString(),
            JtdxWindowLeft = window.Left,
            JtdxWindowTop = window.Top,
            JtdxWindowWidth = window.Width,
            JtdxWindowHeight = window.Height,
            BandActivityLeftRelative = DefaultBandActivityLeft,
            BandActivityTopRelative = DefaultBandActivityTop,
            BandActivityWidth = DefaultBandActivityRight - DefaultBandActivityLeft,
            BandActivityHeight = DefaultBandActivityBottom - DefaultBandActivityTop,
            FirstFullRowCentreYRelative = DefaultFirstFullRowCentreY,
            RowHeight = DefaultRowHeight,
            SafeVisibleFullRowCount = SafeFullRowCount,
            IgnoredPartialTopRow = true,
            MessageClickXRelative = DefaultMessageClickX,
            NewestRowsAtBottom = true,
            Version = $"grid-v1-{DateTime.Now:yyyyMMddHHmmss}",
            CalibrationDate = DateTime.Now
        };
    }

    public static JtdxBandActivityGridCalibration FromSettings(AppSettings settings)
    {
        var rowCount = settings.JtdxBandVisibleRowCount <= 0 ? SafeFullRowCount : settings.JtdxBandVisibleRowCount;
        return new JtdxBandActivityGridCalibration
        {
            MonitorId = settings.JtdxBandMonitorId,
            JtdxWindowTitle = settings.JtdxCalibratedWindowTitle,
            JtdxWindowProcess = settings.JtdxCalibratedWindowProcess,
            JtdxWindowLeft = settings.JtdxCalibratedWindowLeft,
            JtdxWindowTop = settings.JtdxCalibratedWindowTop,
            JtdxWindowWidth = settings.JtdxCalibratedWindowWidth,
            JtdxWindowHeight = settings.JtdxCalibratedWindowHeight,
            BandActivityLeftRelative = settings.JtdxBandActivityLeft,
            BandActivityTopRelative = settings.JtdxBandActivityTop,
            BandActivityWidth = Math.Max(0, settings.JtdxBandActivityRight - settings.JtdxBandActivityLeft),
            BandActivityHeight = Math.Max(0, settings.JtdxBandActivityBottom - settings.JtdxBandActivityTop),
            FirstFullRowCentreYRelative = settings.JtdxBandFirstRowCenterY,
            RowHeight = settings.JtdxBandRowHeight,
            SafeVisibleFullRowCount = rowCount,
            IgnoredPartialTopRow = settings.JtdxBandIgnoredPartialTopRow,
            MessageClickXRelative = settings.JtdxBandMessageClickX,
            NewestRowsAtBottom = settings.JtdxBandNewestRowsAtBottom,
            Version = string.IsNullOrWhiteSpace(settings.JtdxBandCalibrationVersion) ? "grid-v1" : settings.JtdxBandCalibrationVersion,
            CalibrationDate = settings.JtdxBandCalibrationDate == DateTime.MinValue ? DateTime.Now : settings.JtdxBandCalibrationDate
        };
    }

    public void SaveTo(AppSettings settings)
    {
        settings.JtdxBandMonitorId = MonitorId;
        settings.JtdxCalibratedWindowTitle = JtdxWindowTitle;
        settings.JtdxCalibratedWindowProcess = JtdxWindowProcess;
        settings.JtdxCalibratedWindowLeft = JtdxWindowLeft;
        settings.JtdxCalibratedWindowTop = JtdxWindowTop;
        settings.JtdxCalibratedWindowWidth = JtdxWindowWidth;
        settings.JtdxCalibratedWindowHeight = JtdxWindowHeight;
        settings.JtdxBandActivityLeft = BandActivityLeftRelative;
        settings.JtdxBandActivityTop = BandActivityTopRelative;
        settings.JtdxBandActivityRight = BandActivityLeftRelative + BandActivityWidth;
        settings.JtdxBandActivityBottom = BandActivityTopRelative + BandActivityHeight;
        settings.JtdxBandFirstRowCenterY = FirstFullRowCentreYRelative;
        settings.JtdxBandRowHeight = RowHeight;
        settings.JtdxBandVisibleRowCount = SafeVisibleFullRowCount;
        settings.JtdxBandIgnoredPartialTopRow = IgnoredPartialTopRow;
        settings.JtdxBandMessageClickX = MessageClickXRelative;
        settings.JtdxBandNewestRowsAtBottom = NewestRowsAtBottom;
        settings.JtdxBandCalibrationVersion = Version;
        settings.JtdxBandCalibrationDate = CalibrationDate;
    }

    public bool IsUsable => BandActivityWidth > 0
        && BandActivityHeight > 0
        && FirstFullRowCentreYRelative > 0
        && RowHeight > 0
        && MessageClickXRelative > 0
        && SafeVisibleFullRowCount == SafeFullRowCount;
}
