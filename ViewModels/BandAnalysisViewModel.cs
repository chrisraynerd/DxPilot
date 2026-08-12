using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class BandAnalysisViewModel : ObservableObject
{
    public static readonly (string Band, string ButtonLabel)[] BandButtons =
    [
        ("160m", "160"), ("80m", "80"), ("60m", "60"), ("40m", "40"),
        ("30m", "30"), ("20m", "20"), ("17m", "17"), ("15m", "15"),
        ("12m", "12"), ("10m", "10"), ("6m", "6"), ("2m", "2")
    ];

    private int _dwellMinutes;
    private int _surveyCycles;
    private bool _returnToStartingBand;
    private bool _isRunning;
    private string _status = "Ready for band-strip calibration.";
    private string _calibrationStatus = "Band button strip not mapped.";
    private string _overallSummary = "Run Band Analysis to compare received opportunities and outward PSK propagation across the enabled bands.";
    private string _progress = "Not running";
    private string _pskProgress = "Not running";
    private BandAnalysisBandViewModel? _selectedBand;
    private bool _conditionsSearchEnabled;
    private bool _conditionsSearchUsePskProbes;
    private int _conditionsSearchCooldownMinutes;
    private int _conditionsSearchMinimumBandMinutes;
    private int _conditionsSearchMonitoringWindowMinutes;
    private int _conditionsSearchNoUsefulTargetMinutes;
    private int _conditionsSearchLowStationThreshold;
    private int _conditionsSearchLowActivityPersistMinutes;
    private int _conditionsSearchPoorReplyAttempts;
    private int _conditionsSearchPoorReplyDistinctStations;
    private int _conditionsSearchSilentMinutes;
    private int _conditionsSearchSwitchImprovementPercent;
    private bool _conditionsSearchUseQuickSurvey;
    private bool _conditionsSearchFullSurveyWhenAmbiguous;
    private bool _conditionsSearchMoveToBestBand;
    private bool _conditionsSearchSurveyOnStartup;
    private string _conditionsSearchScheduleUtc = "";
    private string _automaticStatus = "Automatic Conditions Search is off.";
    private string _historySummary = "Band trend history will appear after two surveys.";
    private int _pskPropagationProbeMinutes;
    private string _pskProbeStatus = "PSK propagation probing is ready after JTDX's Tx 15/45 (or Tx 00/30) timing button is mapped.";
    private bool _analysisBannerVisible;
    private string _analysisBannerTitle = "";
    private string _analysisBannerMessage = "";
    private string _analysisBannerPhase = "";
    private string _analysisBannerTone = "Pending";

    public BandAnalysisViewModel(AppSettings settings)
    {
        PskMap = new PskReporterMapViewModel();
        var enabled = new HashSet<string>(settings.BandAnalysisEnabledBands ?? [], StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < BandButtons.Length; index++)
        {
            var item = BandButtons[index];
            Bands.Add(new BandAnalysisBandViewModel(item.Band, item.ButtonLabel, index, enabled.Contains(item.Band)));
        }

        _dwellMinutes = Math.Clamp(settings.BandAnalysisDwellMinutes, 1, 3);
        _surveyCycles = Math.Clamp(settings.BandAnalysisSurveyCycles, 1, 5);
        _returnToStartingBand = settings.BandAnalysisReturnToStartingBand;
        _pskPropagationProbeMinutes = Math.Clamp(settings.PskPropagationProbeMinutes, 1, 5);
        _conditionsSearchEnabled = settings.ConditionsSearchEnabled;
        _conditionsSearchUsePskProbes = settings.ConditionsSearchUsePskProbes;
        _conditionsSearchCooldownMinutes = settings.ConditionsSearchCooldownMinutes;
        _conditionsSearchMinimumBandMinutes = settings.ConditionsSearchMinimumBandMinutes;
        _conditionsSearchMonitoringWindowMinutes = settings.ConditionsSearchMonitoringWindowMinutes;
        _conditionsSearchNoUsefulTargetMinutes = settings.ConditionsSearchNoUsefulTargetMinutes;
        _conditionsSearchLowStationThreshold = settings.ConditionsSearchLowStationThreshold;
        _conditionsSearchLowActivityPersistMinutes = settings.ConditionsSearchLowActivityPersistMinutes;
        _conditionsSearchPoorReplyAttempts = settings.ConditionsSearchPoorReplyAttempts;
        _conditionsSearchPoorReplyDistinctStations = settings.ConditionsSearchPoorReplyDistinctStations;
        _conditionsSearchSilentMinutes = settings.ConditionsSearchSilentMinutes;
        _conditionsSearchSwitchImprovementPercent = settings.ConditionsSearchSwitchImprovementPercent;
        _conditionsSearchUseQuickSurvey = settings.ConditionsSearchUseQuickSurvey;
        _conditionsSearchFullSurveyWhenAmbiguous = settings.ConditionsSearchFullSurveyWhenAmbiguous;
        _conditionsSearchMoveToBestBand = settings.ConditionsSearchMoveToBestBand;
        _conditionsSearchSurveyOnStartup = settings.ConditionsSearchSurveyOnStartup;
        _conditionsSearchScheduleUtc = settings.ConditionsSearchScheduleUtc ?? "";
        ConditionsIndicators.Add(new ConditionsIndicatorViewModel(
            "cooldown", "Time until another analysis is allowed",
            "Prevents Band Analysis from running repeatedly. The bar empties from the last completed analysis."));
        ConditionsIndicators.Add(new ConditionsIndicatorViewModel(
            "residence", "Minimum stay on the selected band",
            "Gives the chosen band time to produce contacts before DX Pilot considers moving again."));
        ConditionsIndicators.Add(new ConditionsIndicatorViewModel(
            "unanswered", "Calls without a reply",
            "Counts calling attempts since the most recent reply or QSO progress, across several different stations."));
        ConditionsIndicators.Add(new ConditionsIndicatorViewModel(
            "useful", "Time without a useful target",
            "Measures how long DX Pilot has gone without hearing a selectable CQ or wanted opportunity."));
        ConditionsIndicators.Add(new ConditionsIndicatorViewModel(
            "activity", "Band activity",
            "Combines complete silence with a persistently low number of unique stations in the recent listening window."));
        RefreshCalibration(settings);
        _pskProbeStatus = HasPskTransmitCalibration(settings)
            ? "JTDX PSK controls mapped: timing selector and Tx1 stable-mode reset are ready."
            : "Map JTDX's Tx timing selector and Tx1 button before starting a transmitted survey.";
    }

    public ObservableCollection<BandAnalysisBandViewModel> Bands { get; } = new();
    public ObservableCollection<ConditionsIndicatorViewModel> ConditionsIndicators { get; } = new();
    public PskReporterMapViewModel PskMap { get; }
    public IReadOnlyList<int> DwellMinuteOptions { get; } = [1, 2, 3];
    public IReadOnlyList<int> SurveyCycleOptions { get; } = [1, 2, 3, 4, 5];
    public IReadOnlyList<int> PskProbeMinuteOptions { get; } = [1, 2, 3, 4, 5];

    public int DwellMinutes
    {
        get => _dwellMinutes;
        set => SetProperty(ref _dwellMinutes, Math.Clamp(value, 1, 3));
    }

    public int SurveyCycles
    {
        get => _surveyCycles;
        set => SetProperty(ref _surveyCycles, Math.Clamp(value, 1, 5));
    }

    public bool ReturnToStartingBand
    {
        get => _returnToStartingBand;
        set => SetProperty(ref _returnToStartingBand, value);
    }

    public int PskPropagationProbeMinutes
    {
        get => _pskPropagationProbeMinutes;
        set
        {
            if (SetProperty(ref _pskPropagationProbeMinutes, Math.Clamp(value, 1, 5)))
                OnPropertyChanged(nameof(PskProbeTimingSummary));
        }
    }

    public string PskProbeTimingSummary =>
        $"{PskPropagationProbeMinutes} minute measurement: {PskPropagationProbeMinutes * 60 - 30}s passive listening followed by two verified consecutive 15s CQs.";

    public string PskProbeStatus
    {
        get => _pskProbeStatus;
        set => SetProperty(ref _pskProbeStatus, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string CalibrationStatus
    {
        get => _calibrationStatus;
        set => SetProperty(ref _calibrationStatus, value);
    }

    public string OverallSummary
    {
        get => _overallSummary;
        set => SetProperty(ref _overallSummary, value);
    }

    public string Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string PskProgress
    {
        get => _pskProgress;
        set => SetProperty(ref _pskProgress, value);
    }

    public BandAnalysisBandViewModel? SelectedBand
    {
        get => _selectedBand;
        set => SetProperty(ref _selectedBand, value);
    }

    public bool ConditionsSearchEnabled { get => _conditionsSearchEnabled; set => SetProperty(ref _conditionsSearchEnabled, value); }
    public bool ConditionsSearchUsePskProbes { get => _conditionsSearchUsePskProbes; set => SetProperty(ref _conditionsSearchUsePskProbes, value); }
    public int ConditionsSearchCooldownMinutes { get => _conditionsSearchCooldownMinutes; set => SetProperty(ref _conditionsSearchCooldownMinutes, Math.Clamp(value, 15, 180)); }
    public int ConditionsSearchMinimumBandMinutes { get => _conditionsSearchMinimumBandMinutes; set => SetProperty(ref _conditionsSearchMinimumBandMinutes, Math.Clamp(value, 5, 60)); }
    public int ConditionsSearchMonitoringWindowMinutes { get => _conditionsSearchMonitoringWindowMinutes; set => SetProperty(ref _conditionsSearchMonitoringWindowMinutes, Math.Clamp(value, 3, 15)); }
    public int ConditionsSearchNoUsefulTargetMinutes { get => _conditionsSearchNoUsefulTargetMinutes; set => SetProperty(ref _conditionsSearchNoUsefulTargetMinutes, Math.Clamp(value, 3, 60)); }
    public int ConditionsSearchLowStationThreshold { get => _conditionsSearchLowStationThreshold; set => SetProperty(ref _conditionsSearchLowStationThreshold, Math.Clamp(value, 1, 30)); }
    public int ConditionsSearchLowActivityPersistMinutes { get => _conditionsSearchLowActivityPersistMinutes; set => SetProperty(ref _conditionsSearchLowActivityPersistMinutes, Math.Clamp(value, 1, 15)); }
    public int ConditionsSearchPoorReplyAttempts { get => _conditionsSearchPoorReplyAttempts; set => SetProperty(ref _conditionsSearchPoorReplyAttempts, Math.Clamp(value, 3, 30)); }
    public int ConditionsSearchPoorReplyDistinctStations { get => _conditionsSearchPoorReplyDistinctStations; set => SetProperty(ref _conditionsSearchPoorReplyDistinctStations, Math.Clamp(value, 2, 10)); }
    public int ConditionsSearchSilentMinutes { get => _conditionsSearchSilentMinutes; set => SetProperty(ref _conditionsSearchSilentMinutes, Math.Clamp(value, 2, 20)); }
    public int ConditionsSearchSwitchImprovementPercent { get => _conditionsSearchSwitchImprovementPercent; set => SetProperty(ref _conditionsSearchSwitchImprovementPercent, Math.Clamp(value, 5, 100)); }
    public bool ConditionsSearchUseQuickSurvey { get => _conditionsSearchUseQuickSurvey; set => SetProperty(ref _conditionsSearchUseQuickSurvey, value); }
    public bool ConditionsSearchFullSurveyWhenAmbiguous { get => _conditionsSearchFullSurveyWhenAmbiguous; set => SetProperty(ref _conditionsSearchFullSurveyWhenAmbiguous, value); }
    public bool ConditionsSearchMoveToBestBand { get => _conditionsSearchMoveToBestBand; set => SetProperty(ref _conditionsSearchMoveToBestBand, value); }
    public bool ConditionsSearchSurveyOnStartup { get => _conditionsSearchSurveyOnStartup; set => SetProperty(ref _conditionsSearchSurveyOnStartup, value); }
    public string ConditionsSearchScheduleUtc { get => _conditionsSearchScheduleUtc; set => SetProperty(ref _conditionsSearchScheduleUtc, value ?? ""); }
    public string AutomaticStatus { get => _automaticStatus; set => SetProperty(ref _automaticStatus, value); }
    public string HistorySummary { get => _historySummary; set => SetProperty(ref _historySummary, value); }
    public bool AnalysisBannerVisible { get => _analysisBannerVisible; set => SetProperty(ref _analysisBannerVisible, value); }
    public string AnalysisBannerTitle { get => _analysisBannerTitle; set => SetProperty(ref _analysisBannerTitle, value); }
    public string AnalysisBannerMessage { get => _analysisBannerMessage; set => SetProperty(ref _analysisBannerMessage, value); }
    public string AnalysisBannerPhase { get => _analysisBannerPhase; set => SetProperty(ref _analysisBannerPhase, value); }
    public string AnalysisBannerTone { get => _analysisBannerTone; set => SetProperty(ref _analysisBannerTone, value); }

    public void ShowAnalysisBanner(string title, string message, string phase, string tone)
    {
        AnalysisBannerTitle = title;
        AnalysisBannerMessage = message;
        AnalysisBannerPhase = phase;
        AnalysisBannerTone = tone;
        AnalysisBannerVisible = true;
    }

    public void HideAnalysisBanner()
    {
        AnalysisBannerVisible = false;
        AnalysisBannerPhase = "";
    }

    public void UpdateConditionIndicator(string key, double remainingPercent, string detail, bool active = true)
    {
        ConditionsIndicators.First(item => item.Key == key).Update(remainingPercent, detail, active);
    }

    public void SaveTo(AppSettings settings)
    {
        settings.BandAnalysisEnabledBands = Bands.Where(row => row.Enabled).Select(row => row.Band).ToList();
        settings.BandAnalysisDwellMinutes = DwellMinutes;
        settings.BandAnalysisSurveyCycles = SurveyCycles;
        settings.BandAnalysisReturnToStartingBand = ReturnToStartingBand;
        settings.PskPropagationProbeMinutes = PskPropagationProbeMinutes;
        settings.ConditionsSearchEnabled = ConditionsSearchEnabled;
        settings.ConditionsSearchUsePskProbes = ConditionsSearchUsePskProbes;
        settings.ConditionsSearchCooldownMinutes = ConditionsSearchCooldownMinutes;
        settings.ConditionsSearchMinimumBandMinutes = ConditionsSearchMinimumBandMinutes;
        settings.ConditionsSearchMonitoringWindowMinutes = ConditionsSearchMonitoringWindowMinutes;
        settings.ConditionsSearchNoUsefulTargetMinutes = ConditionsSearchNoUsefulTargetMinutes;
        settings.ConditionsSearchLowStationThreshold = ConditionsSearchLowStationThreshold;
        settings.ConditionsSearchLowActivityPersistMinutes = ConditionsSearchLowActivityPersistMinutes;
        settings.ConditionsSearchPoorReplyAttempts = ConditionsSearchPoorReplyAttempts;
        settings.ConditionsSearchPoorReplyDistinctStations = ConditionsSearchPoorReplyDistinctStations;
        settings.ConditionsSearchSilentMinutes = ConditionsSearchSilentMinutes;
        settings.ConditionsSearchSwitchImprovementPercent = ConditionsSearchSwitchImprovementPercent;
        settings.ConditionsSearchUseQuickSurvey = ConditionsSearchUseQuickSurvey;
        settings.ConditionsSearchFullSurveyWhenAmbiguous = ConditionsSearchFullSurveyWhenAmbiguous;
        settings.ConditionsSearchMoveToBestBand = ConditionsSearchMoveToBestBand;
        settings.ConditionsSearchSurveyOnStartup = ConditionsSearchSurveyOnStartup;
        settings.ConditionsSearchScheduleUtc = ConditionsSearchScheduleUtc;
    }

    public void RefreshCalibration(AppSettings settings)
    {
        CalibrationStatus = HasUsableCalibration(settings)
            ? $"Mapped: window-relative box ({settings.JtdxBandButtonStripLeft}, {settings.JtdxBandButtonStripTop}) to ({settings.JtdxBandButtonStripRight}, {settings.JtdxBandButtonStripBottom}); {settings.JtdxBandButtonStripCalibrationDate:g}."
            : "Band button strip not mapped. Map the 160m-to-2m button row before testing or surveying.";
    }

    public static bool HasUsableCalibration(AppSettings settings)
    {
        return settings.JtdxBandButtonStripRight > settings.JtdxBandButtonStripLeft
            && settings.JtdxBandButtonStripBottom > settings.JtdxBandButtonStripTop
            && settings.JtdxBandButtonStripRight - settings.JtdxBandButtonStripLeft >= 360
            && settings.JtdxBandButtonStripBottom - settings.JtdxBandButtonStripTop >= 18;
    }

    public static bool HasTxEvenCalibration(AppSettings settings) =>
        settings.JtdxTxEvenCalibrationDate != DateTime.MinValue
        && settings.JtdxTxEvenRelativeX > 0
        && settings.JtdxTxEvenRelativeY > 0;

    public static bool HasTx1Calibration(AppSettings settings) =>
        settings.JtdxTx1CalibrationDate != DateTime.MinValue
        && settings.JtdxTx1RelativeX > 0
        && settings.JtdxTx1RelativeY > 0;

    public static bool HasPskTransmitCalibration(AppSettings settings) =>
        HasTxEvenCalibration(settings) && HasTx1Calibration(settings);
}
