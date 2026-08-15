using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3.Views;

public partial class BandAnalysisHistoryChart : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IEnumerable<BandAnalysisChartSeries>),
        typeof(BandAnalysisHistoryChart),
        new PropertyMetadata(null, (_, _) => { }));

    public BandAnalysisHistoryChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => DrawChart();
        Loaded += (_, _) => DrawChart();
    }

    public IEnumerable<BandAnalysisChartSeries>? Series
    {
        get => (IEnumerable<BandAnalysisChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SeriesProperty && IsLoaded)
            DrawChart();
    }

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        var series = Series?.Where(item => item.Points.Count > 0).ToList() ?? [];
        var points = series.SelectMany(item => item.Points).ToList();
        var width = Math.Max(200, ActualWidth);
        var height = Math.Max(200, ActualHeight);
        const double left = 48;
        const double top = 38;
        const double right = 18;
        const double bottom = 50;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);

        for (var score = 0; score <= 100; score += 25)
        {
            var y = top + plotHeight * (1 - score / 100d);
            AddLine(left, y, left + plotWidth, y, "#E6EDF3", 1);
            AddLabel(score.ToString(), 8, y - 9, 34, "#657786", 10, TextAlignment.Right);
        }

        AddLine(left, top, left, top + plotHeight, "#9AA9B7", 1);
        AddLine(left, top + plotHeight, left + plotWidth, top + plotHeight, "#9AA9B7", 1);
        if (points.Count == 0)
        {
            AddLabel("Complete a Band Analysis to begin the conditions graph.", left + 18, top + plotHeight / 2 - 10, plotWidth - 36, "#657786", 13, TextAlignment.Center);
            return;
        }

        var earliest = points.Min(point => point.ObservedAtUtc);
        var latest = points.Max(point => point.ObservedAtUtc);
        if (latest <= earliest)
        {
            earliest = earliest.AddMinutes(-1);
            latest = latest.AddMinutes(1);
        }
        var totalSeconds = Math.Max(1, (latest - earliest).TotalSeconds);
        double X(DateTime observedAtUtc) => left + (observedAtUtc - earliest).TotalSeconds / totalSeconds * plotWidth;
        double Y(double score) => top + plotHeight * (1 - Math.Clamp(score, 0, 100) / 100d);

        var distinctAnalysisTimes = points
            .Select(point => point.ObservedAtUtc)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        var maximumTimeLabels = Math.Clamp((int)(plotWidth / 125), 2, 8);
        var labelledTimes = SelectAxisTimes(distinctAnalysisTimes, maximumTimeLabels);
        var showDate = earliest.ToLocalTime().Date != latest.ToLocalTime().Date
            || latest - earliest >= TimeSpan.FromHours(12);
        foreach (var time in labelledTimes)
        {
            var x = X(time);
            AddLine(x, top, x, top + plotHeight, "#EEF3F7", 1);
            AddLine(x, top + plotHeight, x, top + plotHeight + 5, "#9AA9B7", 1);
            var labelWidth = 88d;
            var labelX = Math.Clamp(x - labelWidth / 2, left, left + plotWidth - labelWidth);
            AddLabel(
                time.ToLocalTime().ToString(showDate ? "dd MMM\nHH:mm" : "HH:mm"),
                labelX,
                top + plotHeight + 7,
                labelWidth,
                "#657786",
                10,
                TextAlignment.Center);
        }

        var legendX = left;
        foreach (var bandSeries in series)
        {
            var brush = BrushFrom(bandSeries.Colour);
            var legendDot = new Ellipse { Width = 9, Height = 9, Fill = brush };
            Canvas.SetLeft(legendDot, legendX);
            Canvas.SetTop(legendDot, 13);
            ChartCanvas.Children.Add(legendDot);
            AddLabel(bandSeries.Band, legendX + 13, 7, 44, "#304455", 11, TextAlignment.Left);
            legendX += 58;

            BandAnalysisChartPoint? previous = null;
            foreach (var point in bandSeries.Points.OrderBy(point => point.ObservedAtUtc))
            {
                var x = X(point.ObservedAtUtc);
                var y = Y(point.Score);
                if (previous != null)
                    AddLine(X(previous.ObservedAtUtc), Y(previous.Score), x, y, bandSeries.Colour, 2);

                var dot = new Ellipse
                {
                    Width = point.SelectedBand ? 12 : 9,
                    Height = point.SelectedBand ? 12 : 9,
                    Fill = point.CurrentSurvey ? System.Windows.Media.Brushes.White : brush,
                    Stroke = brush,
                    StrokeThickness = point.SelectedBand ? 3 : 2,
                    ToolTip = $"{(point.CurrentSurvey ? "Analysis started" : "Analysis completed")}: "
                        + $"{point.ObservedAtUtc.ToLocalTime():dddd, dd MMMM yyyy 'at' HH:mm:ss}\n{point.Detail}"
                };
                Canvas.SetLeft(dot, x - dot.Width / 2);
                Canvas.SetTop(dot, y - dot.Height / 2);
                ChartCanvas.Children.Add(dot);
                previous = point;
            }
        }
    }

    private static IReadOnlyList<DateTime> SelectAxisTimes(IReadOnlyList<DateTime> times, int maximumLabels)
    {
        if (times.Count <= maximumLabels)
            return times.ToList();

        var selected = new List<DateTime>(maximumLabels);
        for (var label = 0; label < maximumLabels; label++)
        {
            var index = (int)Math.Round(label * (times.Count - 1d) / (maximumLabels - 1d));
            if (selected.Count == 0 || selected[^1] != times[index])
                selected.Add(times[index]);
        }
        return selected;
    }

    private void AddLine(double x1, double y1, double x2, double y2, string colour, double thickness)
    {
        ChartCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = BrushFrom(colour),
            StrokeThickness = thickness
        });
    }

    private void AddLabel(string text, double x, double y, double width, string colour, double size, TextAlignment alignment)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = Math.Max(1, width),
            Foreground = BrushFrom(colour),
            FontSize = size,
            TextAlignment = alignment
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        ChartCanvas.Children.Add(label);
    }

    private static System.Windows.Media.Brush BrushFrom(string colour)
    {
        try
        {
            return (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(colour)!;
        }
        catch
        {
            return System.Windows.Media.Brushes.SlateGray;
        }
    }
}
