using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxBandButtonStripOverlay : Window
{
    private static readonly string[] ButtonLabels = ["160", "80", "60", "40", "30", "20", "17", "15", "12", "10", "6", "2"];
    private readonly BandStripCanvas _canvas;
    private int _jtdxWindowLeft;
    private int _jtdxWindowTop;
    private bool _updating;

    public JtdxBandButtonStripOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = JtdxBandButtonStripCalibration.MinimumWidth;
        MinHeight = JtdxBandButtonStripCalibration.MinimumHeight;
        _canvas = new BandStripCanvas { Labels = ButtonLabels };
        Content = _canvas;
        MouseLeftButtonDown += (_, _) => DragMove();
        MouseWheel += OnMouseWheel;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                Close();
        };
        LocationChanged += (_, _) => UpdateCalibrationFromWindow();
        SizeChanged += (_, _) => UpdateCalibrationFromWindow();
    }

    public event Action<JtdxBandButtonStripCalibration>? CalibrationChanged;

    public void ShowCalibration(
        JtdxBandButtonStripCalibration calibration,
        int absoluteWindowLeft,
        int absoluteWindowTop)
    {
        _updating = true;
        _jtdxWindowLeft = absoluteWindowLeft;
        _jtdxWindowTop = absoluteWindowTop;
        Left = absoluteWindowLeft + calibration.LeftRelative;
        Top = absoluteWindowTop + calibration.TopRelative;
        Width = calibration.Width;
        Height = calibration.Height;
        _canvas.Calibration = calibration;
        _canvas.InvalidateVisual();
        _updating = false;
        Show();
        Activate();
    }

    private void UpdateCalibrationFromWindow()
    {
        if (_updating || _canvas.Calibration == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var calibration = _canvas.Calibration;
        calibration.LeftRelative = (int)Math.Round(Left - _jtdxWindowLeft);
        calibration.TopRelative = (int)Math.Round(Top - _jtdxWindowTop);
        calibration.Width = Math.Max(JtdxBandButtonStripCalibration.MinimumWidth, (int)Math.Round(ActualWidth));
        calibration.Height = Math.Max(JtdxBandButtonStripCalibration.MinimumHeight, (int)Math.Round(ActualHeight));
        calibration.Version = $"band-strip-v1-{DateTime.Now:yyyyMMddHHmmss}";
        calibration.CalibrationDate = DateTime.Now;
        _canvas.InvalidateVisual();
        CalibrationChanged?.Invoke(calibration);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _updating = true;
        var step = e.Delta > 0 ? 1 : -1;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            Width = Math.Max(MinWidth, Width + step * 12);
        else
            Height = Math.Max(MinHeight, Height + step * 2);
        _updating = false;
        UpdateCalibrationFromWindow();
    }

    private sealed class BandStripCanvas : Canvas
    {
        public string[] Labels { get; init; } = [];
        public JtdxBandButtonStripCalibration? Calibration { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (Calibration == null || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var fill = new SolidColorBrush(WpfColor.FromArgb(72, 0, 145, 255));
            var outline = new WpfPen(WpfBrushes.DeepSkyBlue, 2);
            var divider = new WpfPen(WpfBrushes.White, 1);
            var centerPen = new WpfPen(WpfBrushes.Lime, 2);
            dc.DrawRectangle(fill, outline, new Rect(0, 0, ActualWidth, ActualHeight));
            var cellWidth = ActualWidth / JtdxBandButtonStripCalibration.ButtonCount;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            for (var index = 0; index < JtdxBandButtonStripCalibration.ButtonCount; index++)
            {
                var left = index * cellWidth;
                if (index > 0)
                    dc.DrawLine(divider, new WpfPoint(left, 0), new WpfPoint(left, ActualHeight));

                var centerX = left + cellWidth / 2;
                var centerY = ActualHeight / 2;
                dc.DrawLine(centerPen, new WpfPoint(centerX - 5, centerY), new WpfPoint(centerX + 5, centerY));
                dc.DrawLine(centerPen, new WpfPoint(centerX, centerY - 5), new WpfPoint(centerX, centerY + 5));
                var text = new FormattedText(
                    Labels[index],
                    CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"),
                    Math.Max(10, Math.Min(14, ActualHeight * 0.45)),
                    WpfBrushes.Black,
                    dpi);
                dc.DrawText(text, new WpfPoint(centerX - text.Width / 2, Math.Max(1, centerY - text.Height / 2)));
            }
        }
    }
}
