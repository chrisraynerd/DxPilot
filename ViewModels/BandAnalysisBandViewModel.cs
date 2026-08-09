using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class BandAnalysisBandViewModel : ObservableObject
{
    private bool _enabled;
    private string _surveyStatus = "Waiting";
    private string _movementStatus = "Not tested";
    private int _secondsObserved;
    private string _trend = "Building history";
    private int _trendScore;
    private double _conditionsScore;
    private BandQualitySnapshot _quality;
    private PskReporterMetrics _pskMetrics = new();

    public BandAnalysisBandViewModel(string band, string buttonLabel, int buttonIndex, bool enabled)
    {
        Band = band;
        ButtonLabel = buttonLabel;
        ButtonIndex = buttonIndex;
        _enabled = enabled;
        _quality = new BandQualitySnapshot { Band = band };
    }

    public string Band { get; }
    public string ButtonLabel { get; }
    public int ButtonIndex { get; }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string SurveyStatus
    {
        get => _surveyStatus;
        set => SetProperty(ref _surveyStatus, value);
    }

    public string MovementStatus
    {
        get => _movementStatus;
        set => SetProperty(ref _movementStatus, value);
    }

    public int SecondsObserved
    {
        get => _secondsObserved;
        set
        {
            if (SetProperty(ref _secondsObserved, value))
                OnPropertyChanged(nameof(ObservedDisplay));
        }
    }

    public string ObservedDisplay => SecondsObserved <= 0 ? "—" : $"{SecondsObserved / 60}:{SecondsObserved % 60:00}";
    public int TotalDecodes => _quality.TotalDecodes;
    public int UniqueStations => _quality.UniqueStations;
    public int CqCallers => _quality.CqCallers;
    public int NewDxccStations => _quality.NewDxccStations;
    public string MainArea => _quality.MainArea;
    public int DistantStations => _quality.DistantStations;
    public int LongDxStations => _quality.LongDxStations;
    public int WantedStations => _quality.WantedStations;
    public int ActivityScore => _quality.ActivityScore;
    public int DxReachScore => _quality.DxReachScore;
    public string Assessment => _quality.Assessment;
    public string Detail => _quality.Detail;
    public string ReachDisplay => _quality.EightiethPercentileDistanceMiles.HasValue
        ? $"{_quality.EightiethPercentileDistanceMiles.Value:N0} mi"
        : "Unknown";
    public string FarthestDisplay => _quality.FarthestDistanceMiles.HasValue
        ? $"{_quality.FarthestDistanceMiles.Value:N0} mi"
        : "Unknown";
    public bool PskMeasured => _pskMetrics.Measured;
    public int PskReportCount => _pskMetrics.ReportCount;
    public int PskUniqueReceivers => _pskMetrics.UniqueReceivers;
    public string PskReceiversDisplay => _pskMetrics.Measured ? _pskMetrics.UniqueReceivers.ToString() : "—";
    public string PskReachDisplay => _pskMetrics.Measured
        ? _pskMetrics.FarthestDistanceMiles.HasValue ? $"{_pskMetrics.FarthestDistanceMiles.Value:N0} mi" : "No report"
        : "—";
    public string PskSnrDisplay => _pskMetrics.Measured
        ? _pskMetrics.StrongestSnr.HasValue ? $"{_pskMetrics.StrongestSnr.Value:+0;-0;0} dB" : "Unknown"
        : "—";
    public string PskMainArea => _pskMetrics.Measured ? _pskMetrics.MainArea : "—";
    public string PskScoreDisplay => _pskMetrics.Measured ? _pskMetrics.PropagationScore.ToString() : "—";
    public string PskAssessment => _pskMetrics.Measured ? _pskMetrics.Assessment : "Not measured";
    public string PskDetail => _pskMetrics.Detail;
    public PskReporterMetrics PskMetrics => _pskMetrics;
    public string Trend
    {
        get => _trend;
        set => SetProperty(ref _trend, value);
    }
    public int TrendScore
    {
        get => _trendScore;
        set => SetProperty(ref _trendScore, value);
    }
    public double ConditionsScore
    {
        get => _conditionsScore;
        set => SetProperty(ref _conditionsScore, value);
    }
    public BandQualitySnapshot Quality => _quality;

    public void ResetSurvey()
    {
        _quality = new BandQualitySnapshot { Band = Band };
        _pskMetrics = new PskReporterMetrics();
        SecondsObserved = 0;
        SurveyStatus = Enabled ? "Waiting" : "Skipped";
        NotifyQualityChanged();
    }

    public void Apply(BandQualitySnapshot snapshot)
    {
        _quality = snapshot;
        NotifyQualityChanged();
    }

    public void ApplyPsk(PskReporterMetrics metrics)
    {
        _pskMetrics = metrics;
        OnPropertyChanged(nameof(PskMeasured));
        OnPropertyChanged(nameof(PskReportCount));
        OnPropertyChanged(nameof(PskUniqueReceivers));
        OnPropertyChanged(nameof(PskReceiversDisplay));
        OnPropertyChanged(nameof(PskReachDisplay));
        OnPropertyChanged(nameof(PskSnrDisplay));
        OnPropertyChanged(nameof(PskMainArea));
        OnPropertyChanged(nameof(PskScoreDisplay));
        OnPropertyChanged(nameof(PskAssessment));
        OnPropertyChanged(nameof(PskDetail));
        OnPropertyChanged(nameof(PskMetrics));
    }

    private void NotifyQualityChanged()
    {
        OnPropertyChanged(nameof(TotalDecodes));
        OnPropertyChanged(nameof(UniqueStations));
        OnPropertyChanged(nameof(CqCallers));
        OnPropertyChanged(nameof(NewDxccStations));
        OnPropertyChanged(nameof(MainArea));
        OnPropertyChanged(nameof(DistantStations));
        OnPropertyChanged(nameof(LongDxStations));
        OnPropertyChanged(nameof(WantedStations));
        OnPropertyChanged(nameof(ActivityScore));
        OnPropertyChanged(nameof(DxReachScore));
        OnPropertyChanged(nameof(Assessment));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(ReachDisplay));
        OnPropertyChanged(nameof(FarthestDisplay));
    }
}
