namespace JtdxAutoResume.V3.ViewModels;

public sealed record BandAnalysisChartPoint(
    DateTime ObservedAtUtc,
    double Score,
    bool SelectedBand,
    bool CurrentSurvey,
    string Detail);

public sealed record BandAnalysisChartSeries(
    string Band,
    string Colour,
    IReadOnlyList<BandAnalysisChartPoint> Points);
