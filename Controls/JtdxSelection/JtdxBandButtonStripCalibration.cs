using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxBandButtonStripCalibration
{
    public const int ButtonCount = 12;
    public const int MinimumWidth = 360;
    public const int MinimumHeight = 18;

    public int LeftRelative { get; set; }
    public int TopRelative { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Version { get; set; } = "band-strip-v1";
    public DateTime CalibrationDate { get; set; } = DateTime.Now;

    public bool IsUsable => Width >= MinimumWidth && Height >= MinimumHeight;

    public static JtdxBandButtonStripCalibration FromSettings(AppSettings settings)
    {
        return new JtdxBandButtonStripCalibration
        {
            LeftRelative = settings.JtdxBandButtonStripLeft,
            TopRelative = settings.JtdxBandButtonStripTop,
            Width = Math.Max(0, settings.JtdxBandButtonStripRight - settings.JtdxBandButtonStripLeft),
            Height = Math.Max(0, settings.JtdxBandButtonStripBottom - settings.JtdxBandButtonStripTop),
            Version = string.IsNullOrWhiteSpace(settings.JtdxBandButtonStripCalibrationVersion)
                ? "band-strip-v1"
                : settings.JtdxBandButtonStripCalibrationVersion,
            CalibrationDate = settings.JtdxBandButtonStripCalibrationDate == DateTime.MinValue
                ? DateTime.Now
                : settings.JtdxBandButtonStripCalibrationDate
        };
    }

    public static JtdxBandButtonStripCalibration CreateDefault(JtdxWindowInfo window)
    {
        var width = Math.Max(MinimumWidth, window.Width - 16);
        return new JtdxBandButtonStripCalibration
        {
            LeftRelative = 8,
            TopRelative = Math.Max(0, window.Height - 50),
            Width = width,
            Height = 28,
            Version = $"band-strip-v1-{DateTime.Now:yyyyMMddHHmmss}",
            CalibrationDate = DateTime.Now
        };
    }

    public void SaveTo(AppSettings settings)
    {
        settings.JtdxBandButtonStripLeft = LeftRelative;
        settings.JtdxBandButtonStripTop = TopRelative;
        settings.JtdxBandButtonStripRight = LeftRelative + Width;
        settings.JtdxBandButtonStripBottom = TopRelative + Height;
        settings.JtdxBandButtonStripCalibrationVersion = Version;
        settings.JtdxBandButtonStripCalibrationDate = CalibrationDate;
    }
}
