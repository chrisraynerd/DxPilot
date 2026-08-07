using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace JtdxAutoResume.V3.Controls.JtdxSelection;

public sealed class JtdxBandActivityOverlay : Window
{
    private static readonly List<JtdxBandActivityOverlay> OpenOverlays = [];
    private readonly OverlayCanvas _canvas;
    private int _jtdxWindowLeft;
    private int _jtdxWindowTop;
    private bool _updating;

    public JtdxBandActivityOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 120;
        MinHeight = 120;
        _canvas = new OverlayCanvas();
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
        Closed += (_, _) => OpenOverlays.Remove(this);
        OpenOverlays.Add(this);
    }

    public event Action<JtdxBandActivityGridCalibration>? CalibrationChanged;

    public static void CloseAll()
    {
        foreach (var overlay in OpenOverlays.ToList())
        {
            try
            {
                overlay.Close();
            }
            catch
            {
            }
        }

        OpenOverlays.Clear();
    }

    public static IReadOnlyList<JtdxBandActivityOverlay> HideAllForClick()
    {
        var hidden = new List<JtdxBandActivityOverlay>();
        foreach (var overlay in OpenOverlays.ToList())
        {
            try
            {
                if (!overlay.IsVisible)
                    continue;

                overlay.Hide();
                hidden.Add(overlay);
            }
            catch
            {
            }
        }

        return hidden;
    }

    public static void RestoreHiddenAfterClick(IReadOnlyList<JtdxBandActivityOverlay> hidden)
    {
        foreach (var overlay in hidden)
        {
            try
            {
                if (OpenOverlays.Contains(overlay))
                    overlay.Show();
            }
            catch
            {
            }
        }
    }

    public void ShowCalibration(JtdxBandActivityGridCalibration calibration, int absoluteWindowLeft, int absoluteWindowTop)
    {
        _updating = true;
        _jtdxWindowLeft = absoluteWindowLeft;
        _jtdxWindowTop = absoluteWindowTop;
        Left = absoluteWindowLeft + calibration.BandActivityLeftRelative;
        Top = absoluteWindowTop + calibration.BandActivityTopRelative;
        Width = calibration.BandActivityWidth;
        Height = calibration.BandActivityHeight;
        _canvas.Calibration = calibration;
        _canvas.InvalidateVisual();
        _updating = false;
        Show();
    }

    private void UpdateCalibrationFromWindow()
    {
        if (_updating || _canvas.Calibration == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var calibration = _canvas.Calibration;
        calibration.BandActivityLeftRelative = (int)Math.Round(Left - _jtdxWindowLeft);
        calibration.BandActivityTopRelative = (int)Math.Round(Top - _jtdxWindowTop);
        calibration.BandActivityWidth = Math.Max(1, (int)Math.Round(ActualWidth));
        calibration.BandActivityHeight = Math.Max(1, (int)Math.Round(ActualHeight));
        calibration.SafeVisibleFullRowCount =
            JtdxBandActivityGridCalibration.NormalizeRowCount(calibration.SafeVisibleFullRowCount);
        var partialTopAllowance = calibration.IgnoredPartialTopRow ? 0.5 : 0;
        calibration.RowHeight =
            calibration.BandActivityHeight / (calibration.SafeVisibleFullRowCount + partialTopAllowance);
        calibration.FirstFullRowCentreYRelative = (int)Math.Round(
            calibration.BandActivityTopRelative
            + (calibration.IgnoredPartialTopRow ? calibration.RowHeight : calibration.RowHeight / 2));
        if (calibration.MessageClickXRelative <= calibration.BandActivityLeftRelative
            || calibration.MessageClickXRelative >= calibration.BandActivityLeftRelative + calibration.BandActivityWidth)
        {
            calibration.MessageClickXRelative = calibration.BandActivityLeftRelative + calibration.BandActivityWidth / 2;
        }

        calibration.Version = $"grid-v2-{DateTime.Now:yyyyMMddHHmmss}";
        calibration.CalibrationDate = DateTime.Now;
        _canvas.InvalidateVisual();
        CalibrationChanged?.Invoke(calibration);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_canvas.Calibration == null)
            return;

        _updating = true;
        var step = e.Delta > 0 ? 1 : -1;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            Width = Math.Max(MinWidth, Width + step * 12);
        }
        else
        {
            Height = Math.Max(MinHeight, Height + step * _canvas.Calibration.RowHeight);
        }

        _updating = false;
        UpdateCalibrationFromWindow();
    }

    private sealed class OverlayCanvas : Canvas
    {
        public JtdxBandActivityGridCalibration? Calibration { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var calibration = Calibration;
            if (calibration == null)
                return;

            var boundaryPen = new WpfPen(WpfBrushes.DeepSkyBlue, 1);
            var centerPen = new WpfPen(WpfBrushes.LimeGreen, 1);
            var guidePen = new WpfPen(WpfBrushes.Orange, 2);
            var textBrush = WpfBrushes.Black;
            var rowHeight = calibration.RowHeight;
            var firstCenter = calibration.FirstFullRowCentreYRelative - calibration.BandActivityTopRelative;
            var clickX = calibration.MessageClickXRelative - calibration.BandActivityLeftRelative;
            dc.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(32, 0, 160, 255)), new WpfPen(WpfBrushes.DeepSkyBlue, 2), new Rect(0, 0, ActualWidth, ActualHeight));

            if (calibration.IgnoredPartialTopRow)
                dc.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(55, 255, 128, 0)), null, new Rect(0, 0, ActualWidth, Math.Max(0, firstCenter - rowHeight / 2)));

            for (var i = 0; i < calibration.SafeVisibleFullRowCount; i++)
            {
                var centerY = firstCenter + i * rowHeight;
                dc.DrawLine(centerPen, new WpfPoint(0, centerY), new WpfPoint(ActualWidth, centerY));
                dc.DrawLine(boundaryPen, new WpfPoint(0, centerY - rowHeight / 2), new WpfPoint(ActualWidth, centerY - rowHeight / 2));
                var label = new FormattedText(
                    i.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    11,
                    textBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new WpfPoint(Math.Max(2, ActualWidth - label.Width - 6), centerY - 8));
            }

            dc.DrawLine(guidePen, new WpfPoint(clickX, 0), new WpfPoint(clickX, ActualHeight));
        }
    }
}
