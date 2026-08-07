using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using Microsoft.Win32;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private enum HuntState
    {
        Idle,
        Calling,
        InQso
    }

    private enum QsoStage
    {
        None,
        CallingInitial,
        TargetReportSeen,
        MyReportSent,
        MyRReportSent,
        WaitingForRrrOrRr73,
        CompletionPending,
        QsoStuck,
        Completed
    }

    private sealed class WorkedCallDisplayInfo
    {
        public int QsoCount { get; set; }
        public bool LoTWConfirmedAny { get; set; }
        public bool PaperConfirmedAny { get; set; }
        public bool EqslConfirmedAny { get; set; }
        public DateTime? LastWorkedDate { get; set; }
        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly SettingsService _settingsService;
    private readonly PixelDetector _pixels;
    private readonly ScreenClicker _clicker;
    private readonly AutoResumeService _autoResume;
    private readonly JtdxUdpListener _udpListener;
    private readonly JtdxUdpClient _udpClient;
    private readonly JtdxAllTxtMonitor _allTxtMonitor;
    private readonly JtdxWindowLocator _jtdxWindowLocator;
    private readonly JtdxSelectionController _selectionController;
    private readonly JtdxVisibleRowModel _visibleRowModel = new();
    private readonly AdifLogbookReader _adifReader;
    private readonly AdifWorkedStatusBuilder _adifStatusBuilder;
    private readonly DxccResolver _dxccResolver;
    private readonly DxccRarityService _rarityService;
    private readonly DxTargetScorer _targetScorer;
    private readonly TargetSelector _targetSelector;
    private readonly ICallsignLocationService _callsignLocationService;
    private readonly List<DecodeMessage> _decodeHistory = new();
    private readonly List<AdifQso> _logbook = new();
    private readonly List<AdifQso> _fullLogbook = new();
    private readonly List<AdifQso> _liveLogbook = new();
    private readonly Dictionary<string, DateTime> _lastHeardUtcByCall = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _displayRankByCall = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkedCallDisplayInfo> _workedCallDisplayByCall = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _suppressedTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _permanentlySuppressedCallsigns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedReplySources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _forceGuiSelectionSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _guiSelectionClickCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _guiSelectionLastClickAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionWorked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionUnresolvedCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _huntTimer;
    private readonly DispatcherTimer _candidateRefreshTimer;
    private readonly DispatcherTimer _adifReloadTimer;
    private readonly DispatcherTimer _wantedRefreshTimer;
    private bool _huntTickRunning;
    private BandScheduleItem? _selectedScheduleItem;
    private DxTarget? _selectedIntendedTarget;
    private DxTarget? _lockedTarget;
    private HuntState _huntState = HuntState.Idle;
    private DateTime _targetStartedAt = DateTime.MinValue;
    private DateTime _targetStartedUtc = DateTime.MinValue;
    private DateTime _lastReplyAt = DateTime.MinValue;
    private DateTime _lastCallAttemptAt = DateTime.MinValue;
    private DateTime _lastSelectionNudgeAt = DateTime.MinValue;
    private DateTime _lastAcquisitionAttemptAt = DateTime.MinValue;
    private DateTime _unconfirmedRecoveryStartedAt = DateTime.MinValue;
    private DateTime _targetConfirmationWaitUntil = DateTime.MinValue;
    private DateTime _lastQsoProgressAt = DateTime.MinValue;
    private DateTime _lastRecoveryBlockLogAt = DateTime.MinValue;
    private DateTime _lastTxMismatchCycleAt = DateTime.MinValue;
    private DateTime _lastForcedTxOffAt = DateTime.MinValue;
    private DateTime _manualTxOffDetectedAt = DateTime.MinValue;
    private string _lastReportRepeatCycleKey = "";
    private string _lastObservedTransmitState = "Unknown";
    private string _actualJtdxDxCall = "";
    private string _txVerificationState = "Unknown";
    private string _recoveryMode = "None";
    private string _lastCorrectiveAction = "None";
    private string _lastObservedQsoMessage = "";
    private string _lastObservedTxMessage = "Unknown";
    private string _lastObservedTxCycleTime = "";
    private string _lastIntendedTxMessage = "";
    private string _lastExpectedQsoStage = "None";
    private string _lastProgressMessageFromTarget = "";
    private DateTime _lastProgressTime = DateTime.MinValue;
    private string _lastRepeatedStage = "";
    private DateTime _lastStageChangeAt = DateTime.MinValue;
    private string _stuckReason = "";
    private bool _targetConfirmedInFeed;
    private bool _targetConfirmedInJtdx;
    private bool _jtdxShowsWrongTx;
    private string _observedWrongTargetCall = "";
    private bool _wrongTargetQsoProgress;
    private int _wrongTargetNoProgressCount;
    private DateTime _lastWrongTargetNoProgressAt = DateTime.MinValue;
    private bool _wrongTargetNudgeSent;
    private bool _pendingLockedReplyWhenIdle;
    private string _pendingLockedReplyReason = "";
    private string _manualSuppressionOverrideCall = "";
    private QsoStage _qsoStage = QsoStage.None;
    private int _callAttemptCount;
    private int _acquisitionAttemptCount;
    private int _reportAttemptCount;
    private int _txMismatchCycleCount;
    private int _completionGraceCycleCount;
    private string _lastCallAttemptCycleKey = "";
    private string _lastCompletionGraceCycleKey = "";
    private bool _myFinal73SeenDuringCompletion;
    private DateTime _completionPendingStartedAt = DateTime.MinValue;
    private DateTime _lastCompletionProtectionLogAt = DateTime.MinValue;
    private DateTime _postQsoTransitionUntil = DateTime.MinValue;
    private DateTime _lastEnableTxArmAt = DateTime.MinValue;
    private FileSystemWatcher? _adifWatcher;
    private bool _immediateTxRetargetInProgress;
    private string _allTxtAwaitingCorrectionCall = "";
    private DateTime _allTxtCorrectionRequestedAt = DateTime.MinValue;
    private DateTime _lastFullAdifLoadedAt = DateTime.MinValue;
    private DateTime _lastLiveAdifReloadAt = DateTime.MinValue;
    private DateTime _lastLiveAdifWriteUtc = DateTime.MinValue;
    private DateTime _lastDecodePacketAt = DateTime.MinValue;
    private DateTime _lastAutoResumeStatusUiAt = DateTime.MinValue;
    private DateTime _lastPixelStateUiAt = DateTime.MinValue;
    private string _lastAutoResumeStatusUi = "";
    private string _lastPixelStateUi = "";
    private string _logbookStatus = "No ADIF loaded.";
    private string _adifDiagnostics = "No ADIF loaded.";
    private string _allTxtDiagnostics = "JTDX outgoing-message monitor not started.";
    private string _resolverDiagnostics = "";
    private string _rarityDiagnostics = "";
    private string _diagnosticCallsign = "";
    private string _diagnosticGrid = "";
    private string _diagnosticState = "";
    private string _diagnosticIota = "";
    private string _diagnosticLookupResult = "Enter a callsign, grid, state, or IOTA reference, then run lookup.";
    private string _qrzStatus = "QRZ lookup disabled.";
    private string _gridOverlayButtonText =
        $"Show {JtdxBandActivityGridCalibration.DefaultVisibleRowCount}-Row Grid";
    private AdifMergeResult _adifMergeResult = new();
    private bool _isPicking;
    private bool _wantedSniperBusy;
    private bool _rebuildingWantedScopes;
    private bool _targetSelectionInProgress;
    private CancellationTokenSource? _targetSelectionCancellation;
    private HuntingOperatingMode _operatingMode = HuntingOperatingMode.DxAssist;
    private DateTime _lastWantedSniperNoTargetLogAt = DateTime.MinValue;
    private string _targetSource = "None";
    private string _wantedReason = "";
    private string _wantedSourceBlock = "";
    private JtdxBandActivityOverlay? _bandActivityOverlay;
    private RadioContext? _radioContext;
    private long _radioContextGeneration;
    private bool _radioContextSettling;
    private bool _radioContextHasDecode;
    private DateTime _radioContextSettleUntil = DateTime.MaxValue;
    private string _radioContextDisplay = "Radio: waiting for JTDX Status";
    private string _radioContextStatus = "Waiting for frequency and mode.";
    private string _settingsTransferStatus = "No settings file exported or imported in this session.";

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _pixels = new PixelDetector();
        _clicker = new ScreenClicker();
        var scheduler = new BandScheduler(_clicker, _pixels);
        _autoResume = new AutoResumeService(_pixels, _clicker, scheduler);
        _udpListener = new JtdxUdpListener();
        _udpClient = new JtdxUdpClient();
        _allTxtMonitor = new JtdxAllTxtMonitor();
        _jtdxWindowLocator = new JtdxWindowLocator();
        var udpReplySelector = new JtdxUdpReplySelector(_udpClient);
        var guiGridSelector = new JtdxGuiGridSelector(_clicker, _jtdxWindowLocator, () => _visibleRowModel.Version);
        _selectionController = new JtdxSelectionController(
            udpReplySelector,
            guiGridSelector,
            _visibleRowModel,
            CurrentJtdxDxCall,
            () => _udpListener.LastStatus);
        _adifReader = new AdifLogbookReader();
        _adifStatusBuilder = new AdifWorkedStatusBuilder();
        Settings = new SettingsViewModel { Settings = _settingsService.LoadSettings() };
        _gridOverlayButtonText = $"Show {Settings.Settings.JtdxBandVisibleRowCount}-Row Grid";
        _permanentlySuppressedCallsigns.UnionWith(Settings.Settings.PermanentlySuppressedCallsigns);
        _dxccResolver = new DxccResolver(Settings.Settings.CountryFilePath);
        _rarityService = new DxccRarityService();
        _rarityService.Load(Settings.Settings.DxccRarityFilePath, _dxccResolver);
        _targetScorer = new DxTargetScorer(_dxccResolver, _rarityService, new GridDistanceCalculator());
        _targetSelector = new TargetSelector(_targetScorer);
        _callsignLocationService = new CallsignLocationService(Settings.Settings, _settingsService.AppFolder, new QrzCallsignClient());
        _autoResume.ShouldUseCqReset = ShouldUseIdleRecovery;
        _autoResume.ShouldClickEnableTx = ShouldClickEnableTxRecovery;

        Dashboard = new DashboardViewModel();
        DxAssist = new DxAssistViewModel();
        Wanted = new WantedViewModel();
        Location = new LocationViewModel();
        Location.SetSelectedAreas(Settings.Settings.LocationHuntAreas ?? new List<string>());
        SessionHistory = new SessionHistoryViewModel();
        Scheduler = new SchedulerViewModel();
        DxAssist.AutoSelectBestCq = Settings.Settings.AutoSelectBestCq;

        foreach (var item in _settingsService.LoadSchedule())
            Scheduler.ScheduleItems.Add(item);

        StartDxAssistCommand = new RelayCommand(StartDxAssist);
        StartWantedSniperCommand = new RelayCommand(StartWantedSniper);
        StartLocationHuntCommand = new RelayCommand(StartLocationHunt);
        StartAutoResumeCommand = new RelayCommand(StartDxAssist);
        StopAutoResumeCommand = new RelayCommand(StopAll);
        StartUdpCommand = new RelayCommand(StartUdpAsync);
        StopUdpCommand = new RelayCommand(StopUdp);
        SelectBestTargetCommand = new RelayCommand(SelectBestTarget);
        ReplyToBestCommand = new RelayCommand(ReplyToBestAsync);
        LoadAdifCommand = new RelayCommand(LoadAdif);
        SaveSettingsCommand = new RelayCommand(SaveAll);
        ExportSettingsCommand = new RelayCommand(ExportSettings);
        ImportSettingsCommand = new RelayCommand(ImportSettings);
        BrowseAllTxtCommand = new RelayCommand(BrowseAllTxt);
        RunDiagnosticLookupCommand = new RelayCommand(RunDiagnosticLookup);
        AddScheduleCommand = new RelayCommand(AddSchedule);
        RemoveScheduleCommand = new RelayCommand(RemoveSchedule);
        PickEnableCommand = new RelayCommand(() => PickPointAsync("Enable TX", (x, y) => { Settings.Settings.EnableTxX = x; Settings.Settings.EnableTxY = y; }));
        PickCqCommand = new RelayCommand(() => PickPointAsync("CQ/TX6", (x, y) => { Settings.Settings.CqTx6X = x; Settings.Settings.CqTx6Y = y; }));
        PickRxCommand = new RelayCommand(() => PickPointAsync("RX green bar", (x, y) => { Settings.Settings.RxX = x; Settings.Settings.RxY = y; }));
        PickEnableColorCommand = new RelayCommand(() => PickColorAsync("Enable TX off grey", rgb => Settings.Settings.EnableTxOffRgb = rgb));
        PickRxColorCommand = new RelayCommand(() => PickColorAsync("RX green", rgb => Settings.Settings.RxGreenRgb = rgb));
        TestScheduleClickCommand = new RelayCommand(TestScheduleClick);
        CaptureJtdxWindowCommand = new RelayCommand(CaptureJtdxWindow);
        PickBandActivityTopLeftCommand = new RelayCommand(async () => await PickWindowRelativePointAsync("Band Activity top-left", ApplyBandActivityTopLeft));
        PickBandActivityBottomRightCommand = new RelayCommand(async () => await PickWindowRelativePointAsync("Band Activity bottom-right", ApplyBandActivityBottomRight));
        ShowBandActivityGridOverlayCommand = new RelayCommand(ShowBandActivityGridOverlay);
        TestGuiSelectionCommand = new RelayCommand(TestGuiSelectionAsync);
        TestQrzConnectionCommand = new RelayCommand(TestQrzConnectionAsync);
        ClearQrzCacheCommand = new RelayCommand(ClearQrzCache);
        CallNowCommand = new RelayCommand(CallNowAsync, CanCallNow);
        PermanentlySuppressCallsignCommand = new RelayCommand(PermanentlySuppressCallsign, CanPermanentlySuppressCallsign);
        ReleaseSuppressionCommand = new RelayCommand(ReleaseSuppression, CanReleaseSuppression);
        Wanted.CallWantedCommand = new RelayCommand(async item => await CallWantedItemAsync(item as WantedItem));
        Wanted.WatchOnlyCommand = new RelayCommand(item => WatchWantedItem(item as WantedItem));
        Wanted.SuppressWantedCommand = new RelayCommand(item => SuppressWantedItem(item as WantedItem));
        Wanted.CopyCallsignCommand = new RelayCommand(item => CopyWantedCallsign(item as WantedItem));
        Wanted.CopyRawMessageCommand = new RelayCommand(item => CopyWantedRawMessage(item as WantedItem));
        Location.CallTargetCommand = new RelayCommand(async row => await CallLocationTargetAsync(row as DxCandidateRow));
        Location.CopyCallsignCommand = new RelayCommand(row => CopyLocationCallsign(row as DxCandidateRow));
        Location.SelectedAreasChanged += (_, _) =>
        {
            Settings.Settings.LocationHuntAreas = Location.SelectedAreaKeys.ToList();
            Settings.Settings.LocationProfile = Location.SelectedAreasDisplay;
            UpdateNextBestTargets();
            SaveAll();
        };
        SessionHistory.ExportCommand = new RelayCommand(ExportSessionHistory);
        SessionHistory.ClearCommand = new RelayCommand(ClearSessionHistory);
        ExportRecentActionsCommand = new RelayCommand(ExportRecentActions);

        _huntTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _huntTimer.Tick += async (_, _) => await HuntTickAsync();
        _candidateRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _candidateRefreshTimer.Tick += (_, _) =>
        {
            _candidateRefreshTimer.Stop();
            UpdateNextBestTargets();
        };
        _adifReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _adifReloadTimer.Tick += (_, _) =>
        {
            _adifReloadTimer.Stop();
            ReloadAdifIfChanged();
        };
        _wantedRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _wantedRefreshTimer.Tick += (_, _) =>
        {
            RefreshWantedTimeColumns();
            CompleteRadioContextSettlingIfReady();
        };
        _wantedRefreshTimer.Start();

        WireEvents();
        Dashboard.OverallStatus = "DX Pilot ready. Start UDP and select a hunting mode when JTDX is open.";
        ResolverDiagnostics = _dxccResolver.Diagnostics;
        RarityDiagnostics = _rarityService.Diagnostics.Summary;
        AddAction(ResolverDiagnostics);
        AddAction(RarityDiagnostics);
        var resolverFailures = _dxccResolver.RunSelfTest();
        AddAction(resolverFailures.Count == 0
            ? "DXCC resolver self-test passed."
            : $"DXCC resolver self-test warnings: {string.Join("; ", resolverFailures.Take(5))}");
        var gridFailures = AdifWorkedStatusBuilder.RunGridNormalizationSelfTest();
        AddAction(gridFailures.Count == 0
            ? "Grid normalization self-test passed."
            : $"Grid normalization self-test warnings: {string.Join("; ", gridFailures.Take(5))}");
        var allTxtFailures = JtdxAllTxtMonitor.RunSelfTest();
        AddAction(allTxtFailures.Count == 0
            ? "JTDX ALL.TXT outgoing-message parser self-test passed."
            : $"JTDX ALL.TXT parser self-test warnings: {string.Join("; ", allTxtFailures)}");
        UpdateHuntStateDisplay();
        LoadAdifSources();
        StartAdifWatcher();
        StartAllTxtMonitor();
        UpdateLocationPanels();
    }

    public DashboardViewModel Dashboard { get; }
    public DxAssistViewModel DxAssist { get; }
    public WantedViewModel Wanted { get; }
    public LocationViewModel Location { get; }
    public SessionHistoryViewModel SessionHistory { get; }
    public SchedulerViewModel Scheduler { get; }
    public SettingsViewModel Settings { get; }
    public TargetStatusSummaryViewModel CurrentTargetStatus { get; } = new();

    public ObservableCollection<string> RecentActions => DxAssist.RecentActions;
    public ObservableCollection<string> DxAssistRecentActions { get; } = new();
    public ObservableCollection<string> WantedRecentActions { get; } = new();
    public ObservableCollection<string> SessionHistoryRecentActions { get; } = new();

    public BandScheduleItem? SelectedScheduleItem
    {
        get => _selectedScheduleItem;
        set => SetProperty(ref _selectedScheduleItem, value);
    }

    public string LogbookStatus
    {
        get => _logbookStatus;
        set => SetProperty(ref _logbookStatus, value);
    }

    public string AdifDiagnostics
    {
        get => _adifDiagnostics;
        set => SetProperty(ref _adifDiagnostics, value);
    }

    public string ResolverDiagnostics
    {
        get => _resolverDiagnostics;
        set => SetProperty(ref _resolverDiagnostics, value);
    }

    public string RarityDiagnostics
    {
        get => _rarityDiagnostics;
        set => SetProperty(ref _rarityDiagnostics, value);
    }

    public string DiagnosticCallsign
    {
        get => _diagnosticCallsign;
        set => SetProperty(ref _diagnosticCallsign, value);
    }

    public string DiagnosticGrid
    {
        get => _diagnosticGrid;
        set => SetProperty(ref _diagnosticGrid, value);
    }

    public string DiagnosticState
    {
        get => _diagnosticState;
        set => SetProperty(ref _diagnosticState, value);
    }

    public string DiagnosticIota
    {
        get => _diagnosticIota;
        set => SetProperty(ref _diagnosticIota, value);
    }

    public string DiagnosticLookupResult
    {
        get => _diagnosticLookupResult;
        set => SetProperty(ref _diagnosticLookupResult, value);
    }

    public string AllTxtDiagnostics
    {
        get => _allTxtDiagnostics;
        set => SetProperty(ref _allTxtDiagnostics, value);
    }

    public string SettingsTransferStatus
    {
        get => _settingsTransferStatus;
        set => SetProperty(ref _settingsTransferStatus, value);
    }

    public string GridOverlayButtonText
    {
        get => _gridOverlayButtonText;
        set => SetProperty(ref _gridOverlayButtonText, value);
    }

    public int JtdxVisibleRowCount
    {
        get => JtdxBandActivityGridCalibration.NormalizeRowCount(Settings.Settings.JtdxBandVisibleRowCount);
        set
        {
            var rowCount = JtdxBandActivityGridCalibration.NormalizeRowCount(value);
            if (Settings.Settings.JtdxBandVisibleRowCount == rowCount)
                return;

            Settings.Settings.JtdxBandVisibleRowCount = rowCount;
            RecalculateBandActivityGridDefaults();
            GridOverlayButtonText = $"{(_bandActivityOverlay == null ? "Show" : "Hide")} {rowCount}-Row Grid";
            OnPropertyChanged();
            Settings.Refresh();
            SaveAll();

            DxAssist.GuiSelectionStatus =
                $"Visible JTDX row count changed to {rowCount}. Show and align the {rowCount}-row grid before GUI selection.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction(DxAssist.GuiSelectionStatus);

            if (_bandActivityOverlay != null)
                RefreshOpenBandActivityOverlay();
        }
    }

    public string QrzStatus
    {
        get => _qrzStatus;
        set => SetProperty(ref _qrzStatus, value);
    }

    public string RadioContextDisplay
    {
        get => _radioContextDisplay;
        private set => SetProperty(ref _radioContextDisplay, value);
    }

    public string RadioContextStatus
    {
        get => _radioContextStatus;
        private set => SetProperty(ref _radioContextStatus, value);
    }

    public string CurrentBand => _radioContext?.BandDisplay ?? "Unknown band";
    public string CurrentDigitalMode => _radioContext?.ModeDisplay ?? "Unknown mode";
    public string CurrentDialFrequency => _radioContext?.FrequencyDisplay ?? "Frequency unknown";

    public ICommand StartAutoResumeCommand { get; }
    public ICommand StartDxAssistCommand { get; }
    public ICommand StartWantedSniperCommand { get; }
    public ICommand StartLocationHuntCommand { get; }
    public ICommand StopAutoResumeCommand { get; }
    public ICommand StartUdpCommand { get; }
    public ICommand StopUdpCommand { get; }
    public ICommand SelectBestTargetCommand { get; }
    public ICommand ReplyToBestCommand { get; }
    public ICommand LoadAdifCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }
    public ICommand BrowseAllTxtCommand { get; }
    public ICommand ExportRecentActionsCommand { get; }
    public ICommand RunDiagnosticLookupCommand { get; }
    public ICommand AddScheduleCommand { get; }
    public ICommand RemoveScheduleCommand { get; }
    public ICommand PickEnableCommand { get; }
    public ICommand PickCqCommand { get; }
    public ICommand PickRxCommand { get; }
    public ICommand PickEnableColorCommand { get; }
    public ICommand PickRxColorCommand { get; }
    public ICommand TestScheduleClickCommand { get; }
    public ICommand CaptureJtdxWindowCommand { get; }
    public ICommand PickBandActivityTopLeftCommand { get; }
    public ICommand PickBandActivityBottomRightCommand { get; }
    public ICommand ShowBandActivityGridOverlayCommand { get; }
    public ICommand TestGuiSelectionCommand { get; }
    public ICommand TestQrzConnectionCommand { get; }
    public ICommand ClearQrzCacheCommand { get; }
    public RelayCommand CallNowCommand { get; }
    public RelayCommand PermanentlySuppressCallsignCommand { get; }
    public RelayCommand ReleaseSuppressionCommand { get; }

    public bool KeepCallingNewDxccUntilStale
    {
        get => Settings.Settings.KeepCallingNewDxccUntilStale;
        set
        {
            if (Settings.Settings.KeepCallingNewDxccUntilStale == value)
                return;

            Settings.Settings.KeepCallingNewDxccUntilStale = value;
            OnPropertyChanged();
            Wanted.Status = value
                ? "New DXCC persistence enabled: the active New DXCC will be called until it goes stale."
                : "New DXCC persistence disabled: normal call-attempt and timeout limits apply.";
            SaveAll();
            UpdateHuntStateDisplay();
        }
    }

    public bool IncludeBandWanted
    {
        get => Settings.Settings.IncludeBandWanted;
        set
        {
            if (Settings.Settings.IncludeBandWanted == value)
                return;
            Settings.Settings.IncludeBandWanted = value;
            OnPropertyChanged();
            ApplyWantedScopeSettingsChange();
        }
    }

    public bool IncludeModeWanted
    {
        get => Settings.Settings.IncludeModeWanted;
        set
        {
            if (Settings.Settings.IncludeModeWanted == value)
                return;
            Settings.Settings.IncludeModeWanted = value;
            OnPropertyChanged();
            ApplyWantedScopeSettingsChange();
        }
    }

    public bool IncludeBandModeWanted
    {
        get => Settings.Settings.IncludeBandModeWanted;
        set
        {
            if (Settings.Settings.IncludeBandModeWanted == value)
                return;
            Settings.Settings.IncludeBandModeWanted = value;
            OnPropertyChanged();
            ApplyWantedScopeSettingsChange();
        }
    }

    public bool SniperTargetsDxcc
    {
        get => Settings.Settings.EnableWantedDxcc;
        set
        {
            if (Settings.Settings.EnableWantedDxcc == value)
                return;

            Settings.Settings.EnableWantedDxcc = value;
            OnPropertyChanged();
            ApplySniperCategorySettingsChange();
        }
    }

    public bool SniperTargetsGrids
    {
        get => Settings.Settings.EnableWantedGrids;
        set
        {
            if (Settings.Settings.EnableWantedGrids == value)
                return;

            Settings.Settings.EnableWantedGrids = value;
            OnPropertyChanged();
            ApplySniperCategorySettingsChange();
        }
    }

    public bool SniperTargetsStates
    {
        get => Settings.Settings.EnableWantedStates;
        set
        {
            if (Settings.Settings.EnableWantedStates == value)
                return;

            Settings.Settings.EnableWantedStates = value;
            OnPropertyChanged();
            ApplySniperCategorySettingsChange();
        }
    }

    public bool IsDxAssistActive => _autoResume.IsRunning && _operatingMode == HuntingOperatingMode.DxAssist;
    public bool IsWantedSniperActive => _autoResume.IsRunning && _operatingMode == HuntingOperatingMode.WantedSniper;
    public bool IsLocationHuntActive => _autoResume.IsRunning && _operatingMode == HuntingOperatingMode.LocationHunt;

    private void WireEvents()
    {
        _udpListener.StatusChanged += message => Dispatch(() =>
        {
            Dashboard.UdpStatus = message;
            AddAction(message);
        });

        _udpListener.DecodeReceived += decode => Dispatch(() =>
        {
            _lastDecodePacketAt = DateTime.Now;
            if (!PrepareDecodeForCurrentRadioContext(decode))
                return;

            PrepareDecodeLocationFields(decode);
            _targetScorer.EnrichDecode(decode, _logbook, _adifMergeResult.Indexes, Settings.Settings);
            RecordLastHeard(decode);
            decode.IsPermanentlySuppressed = IsPermanentlySuppressed(DecodeTargetCall(decode));
            if (!string.IsNullOrWhiteSpace(decode.Callsign)
                && string.IsNullOrWhiteSpace(decode.Dxcc)
                && _sessionUnresolvedCalls.Add(decode.Callsign))
            {
                AddAction($"Unresolved callsign: {decode.Callsign} normalised '{DxccResolver.NormaliseCallsign(decode.Callsign)}' from '{decode.RawText}'. Reason: {decode.EntityReason}");
            }
            _decodeHistory.Insert(0, decode);
            while (_decodeHistory.Count > 500)
                _decodeHistory.RemoveAt(_decodeHistory.Count - 1);
            _visibleRowModel.Rebuild(_decodeHistory, JtdxBandActivityGridCalibration.FromSettings(Settings.Settings));

            DxAssist.RecentDecodes.Insert(0, decode);
            TrimLiveDecodeDisplay();
            UpdateWantedItems(decode);
            RequestNextBestTargetsUpdate();

            ProcessDecodeForCurrentQso(decode);
            _ = StartCallsignLocationEnrichmentAsync(decode);
        });

        _callsignLocationService.LocationUpdated += (_, args) => Dispatch(() => ApplyCallsignLocationUpdate(args.Result));

        _udpListener.StatusMessageReceived += status => Dispatch(() =>
        {
            HandleRadioContextStatus(status);
            _ = ProcessJtdxStatusForCurrentTargetAsync(status);
        });

        _allTxtMonitor.StatusChanged += message => Dispatch(() =>
        {
            AllTxtDiagnostics = message;
            AddAction(message);
        });
        _allTxtMonitor.TransmissionObserved += transmission => Dispatch(() =>
        {
            _ = ProcessAllTxtTransmissionAsync(transmission);
        });

        _autoResume.StatusChanged += message => Dispatch(() =>
        {
            var statusAge = DateTime.Now - _lastAutoResumeStatusUiAt;
            if ((message.Equals(_lastAutoResumeStatusUi, StringComparison.Ordinal)
                    || message.StartsWith("DX Pilot running.", StringComparison.Ordinal))
                && statusAge < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _lastAutoResumeStatusUi = message;
            _lastAutoResumeStatusUiAt = DateTime.Now;
            Dashboard.AutoResumeStatus = message;
            Dashboard.ResumeCount = _autoResume.ResumeCount;
        });

        _autoResume.PixelStateChanged += (grey, red, off) => Dispatch(() =>
        {
            var text = off
                ? $"Enable TX looks OFF: grey {grey}% / red {red}%"
                : $"Enable TX active or unknown: grey {grey}% / red {red}%";
            var pixelAge = DateTime.Now - _lastPixelStateUiAt;
            if (pixelAge < TimeSpan.FromSeconds(2)
                && (text.Equals(_lastPixelStateUi, StringComparison.Ordinal)
                    || _lastPixelStateUi.Contains("Enable TX", StringComparison.Ordinal)))
            {
                return;
            }

            _lastPixelStateUi = text;
            _lastPixelStateUiAt = DateTime.Now;
            Dashboard.PixelState = text;
        });

        _autoResume.ActionLogged += message => Dispatch(() => AddAction(message));
        _autoResume.Resumed += () => Dispatch(() => _ = NudgeLockedTargetAfterResumeAsync());
    }

    private void HandleRadioContextStatus(JtdxStatusMessage status)
    {
        var newMode = AmateurBandMapper.NormalizeMode(status.Mode);
        var newBand = string.IsNullOrWhiteSpace(status.Band)
            ? AmateurBandMapper.FromDialFrequency(status.DialFrequencyHz)
            : status.Band;
        var previous = _radioContext;
        var firstContext = previous == null;
        var bandChanged = previous != null
            && !previous.Band.Equals(newBand, StringComparison.OrdinalIgnoreCase);
        var modeChanged = previous != null
            && !previous.Mode.Equals(newMode, StringComparison.OrdinalIgnoreCase);
        var frequencyDelta = previous == null
            ? 0UL
            : previous.DialFrequencyHz > status.DialFrequencyHz
                ? previous.DialFrequencyHz - status.DialFrequencyHz
                : status.DialFrequencyHz - previous.DialFrequencyHz;
        var meaningfulFrequencyChange = previous != null
            && previous.DialFrequencyHz != 0
            && status.DialFrequencyHz != 0
            && frequencyDelta >= 1_000;
        var contextChanged = firstContext || bandChanged || modeChanged || meaningfulFrequencyChange;

        if (!contextChanged)
        {
            _radioContext = new RadioContext
            {
                DialFrequencyHz = status.DialFrequencyHz,
                Band = newBand,
                Mode = newMode,
                TrPeriodSeconds = status.TrPeriodSeconds,
                Generation = previous!.Generation,
                StartedAt = previous.StartedAt
            };
            RefreshRadioContextDisplay();
            return;
        }

        _radioContextGeneration++;
        _radioContext = new RadioContext
        {
            DialFrequencyHz = status.DialFrequencyHz,
            Band = newBand,
            Mode = newMode,
            TrPeriodSeconds = status.TrPeriodSeconds,
            Generation = _radioContextGeneration,
            StartedAt = status.ReceivedAt
        };
        _radioContextSettling = true;
        _radioContextHasDecode = false;
        _radioContextSettleUntil = DateTime.MaxValue;
        RefreshRadioContextDisplay();

        if (firstContext)
        {
            ClearLiveRadioTables();
            RadioContextStatus = $"Detected {_radioContext.Display}. Waiting for the first complete decode batch.";
            Dashboard.OverallStatus = RadioContextStatus;
            AddAction($"Radio context detected: {_radioContext.Display}. Live tables reset; waiting for the first complete decode batch.");
            return;
        }

        var previousDisplay = previous!.Display;
        if (_lockedTarget != null || _autoResume.IsRunning)
            EnsureEnableTxOff("JTDX frequency/band/mode change");
        if (_lockedTarget != null)
            ClearLockedTarget($"Radio context changed from {previousDisplay} to {_radioContext.Display}; active target released without suppression.");

        ClearLiveRadioTables();
        RadioContextStatus = $"Changed from {previousDisplay} to {_radioContext.Display}. Waiting for the first complete decode batch.";
        Dashboard.OverallStatus = RadioContextStatus;
        AddAction($"Radio context changed: {previousDisplay} -> {_radioContext.Display}. All live station tables, ranks and JTDX row positions were cleared.");
    }

    private bool PrepareDecodeForCurrentRadioContext(DecodeMessage decode)
    {
        decode.Mode = AmateurBandMapper.NormalizeMode(decode.Mode);
        if (_radioContext == null)
            return true;

        if (!string.IsNullOrWhiteSpace(_radioContext.Mode)
            && !string.IsNullOrWhiteSpace(decode.Mode)
            && !decode.Mode.Equals(_radioContext.Mode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_radioContextSettling
            && decode.DecodeTime.HasValue
            && DecodeSlotStart(decode.DecodeTime.Value, decode.ReceivedAt) < _radioContext.StartedAt.AddMilliseconds(-250))
        {
            return false;
        }

        decode.Band = _radioContext.Band;
        decode.DialFrequencyHz = _radioContext.DialFrequencyHz;
        if (string.IsNullOrWhiteSpace(decode.Mode))
            decode.Mode = _radioContext.Mode;
        decode.RadioContextGeneration = _radioContext.Generation;

        if (_radioContextSettling)
        {
            _radioContextHasDecode = true;
            var settleSeconds = Math.Clamp(
                AmateurBandMapper.ReceivePeriod(_radioContext.Mode, _radioContext.TrPeriodSeconds).TotalSeconds / 5d,
                1.2,
                3);
            _radioContextSettleUntil = DateTime.Now.AddSeconds(settleSeconds);
            RadioContextStatus = $"Receiving {_radioContext.BandDisplay} {_radioContext.ModeDisplay} decodes; waiting for JTDX rows to settle.";
        }

        return true;
    }

    private static DateTime DecodeSlotStart(TimeSpan timeOfDay, DateTime receivedAt)
    {
        // WSJT-X/JTDX serializes QTime as milliseconds since midnight UTC.
        // Convert the reconstructed UTC slot back to local time before comparing
        // it with ReceivedAt/RadioContext.StartedAt (which use DateTime.Now).
        var receivedUtc = receivedAt.ToUniversalTime();
        var candidateUtc = DateTime.SpecifyKind(receivedUtc.Date.Add(timeOfDay), DateTimeKind.Utc);
        if (candidateUtc > receivedUtc.AddHours(12))
            candidateUtc = candidateUtc.AddDays(-1);
        else if (candidateUtc < receivedUtc.AddHours(-12))
            candidateUtc = candidateUtc.AddDays(1);
        return candidateUtc.ToLocalTime();
    }

    private void CompleteRadioContextSettlingIfReady()
    {
        if (!_radioContextSettling
            || !_radioContextHasDecode
            || DateTime.Now < _radioContextSettleUntil
            || _radioContext == null)
        {
            return;
        }

        _radioContextSettling = false;
        RadioContextStatus = $"{_radioContext.Display} ready. Live ranks use only this radio context.";
        Dashboard.OverallStatus = RadioContextStatus;
        AddAction($"Radio context ready: {_radioContext.Display}. JTDX rows settled; automatic and manual target selection re-enabled.");
        CallNowCommand.RaiseCanExecuteChanged();
        RequestNextBestTargetsUpdate();
    }

    private void RefreshRadioContextDisplay()
    {
        RadioContextDisplay = _radioContext == null
            ? "Radio: waiting for JTDX Status"
            : $"Radio: {_radioContext.Display}";
        OnPropertyChanged(nameof(CurrentBand));
        OnPropertyChanged(nameof(CurrentDigitalMode));
        OnPropertyChanged(nameof(CurrentDialFrequency));
    }

    private void ClearLiveRadioTables()
    {
        _candidateRefreshTimer.Stop();
        _decodeHistory.Clear();
        _lastHeardUtcByCall.Clear();
        _displayRankByCall.Clear();
        _failedReplySources.Clear();
        _forceGuiSelectionSources.Clear();
        _guiSelectionClickCounts.Clear();
        _guiSelectionLastClickAt.Clear();
        _visibleRowModel.Rebuild(Array.Empty<DecodeMessage>(), JtdxBandActivityGridCalibration.FromSettings(Settings.Settings));

        DxAssist.RecentDecodes.Clear();
        DxAssist.NextBestTargets.Clear();
        DxAssist.CandidateRows.Clear();
        DxAssist.BestTarget = null;
        DxAssist.SelectedCandidate = null;

        Wanted.WantedDxcc.Clear();
        Wanted.WantedGrids.Clear();
        Wanted.WantedStates.Clear();
        Wanted.WantedBandMode.Clear();

        foreach (var panel in Location.Panels)
        {
            panel.Candidates.Clear();
            panel.Summary = "Waiting for current radio-context decodes.";
        }

        Dashboard.BestTarget = "No target selected.";
        Dashboard.BestReason = "";
        Wanted.Status = "Live Wanted tables cleared; waiting for the current radio context to settle.";
        Location.Status = "Live Location tables cleared; waiting for the current radio context to settle.";
        CallNowCommand.RaiseCanExecuteChanged();
        UpdateHuntStateDisplay();
    }

    private bool RadioContextReadyForSelection()
    {
        return _radioContext == null || !_radioContextSettling;
    }

    private TimeSpan ActiveAttemptCycle()
    {
        return AmateurBandMapper.OwnTransmitCycle(_radioContext?.Mode, _radioContext?.TrPeriodSeconds ?? 0);
    }

    private TimeSpan ActiveReceivePeriod()
    {
        return AmateurBandMapper.ReceivePeriod(_radioContext?.Mode, _radioContext?.TrPeriodSeconds ?? 0);
    }

    private static void PrepareDecodeLocationFields(DecodeMessage decode)
    {
        if (string.IsNullOrWhiteSpace(decode.Grid))
            return;

        decode.TransmittedGrid = decode.Grid;
        decode.EffectiveGrid = decode.Grid;
        decode.EffectiveGridSource = DecodeGridSource.Ft8Message;
    }

    private async Task StartCallsignLocationEnrichmentAsync(DecodeMessage decode)
    {
        var call = FirstNonBlank(decode.ContactableCall, decode.Callsign);
        if (string.IsNullOrWhiteSpace(call))
            return;

        if (!Settings.Settings.EnableQrzCallsignLookup)
        {
            decode.CallsignLookupStatus = CallsignLookupStatus.Disabled;
            UpdateWantedItems(decode);
            RequestNextBestTargetsUpdate();
            return;
        }

        try
        {
            // Cache reads are local and normally complete immediately. Applying them
            // before using the internet queue keeps known calls out of the backlog.
            var cached = await _callsignLocationService.GetCachedAsync(call, CancellationToken.None);
            if (cached != null)
            {
                ApplyCallsignLocationUpdate(cached);
                return;
            }

            decode.CallsignLookupStatus = CallsignLookupStatus.Pending;
            decode.CallsignDataSource = CallsignDataSource.Unknown;
            decode.CallsignLookupError = "";
            var priority = IsQrzDecisionCritical(decode)
                ? CallsignLookupPriority.DecisionCritical
                : CallsignLookupPriority.Background;
            if (!_callsignLocationService.QueueLookup(call, priority, LastHeardUtc(call, decode)))
            {
                ApplyCallsignLocationUpdate(new CallsignLocationResult(
                    call, null, null, null, null, CallsignLookupStatus.Skipped,
                    CallsignDataSource.Qrz, DateTimeOffset.UtcNow,
                    "QRZ lookup queue is full; this call was not left pending."));
                return;
            }

            UpdateWantedItems(decode);
            RequestNextBestTargetsUpdate();
        }
        catch (Exception ex)
        {
            ApplyCallsignLocationUpdate(new CallsignLocationResult(
                call, null, null, null, null, CallsignLookupStatus.Error,
                CallsignDataSource.Qrz, DateTimeOffset.UtcNow,
                $"Local QRZ lookup preparation failed: {ex.Message}"));
        }
    }

    private bool IsQrzDecisionCritical(DecodeMessage decode)
    {
        if (!decode.Targetable)
            return false;

        var needsGrid = Settings.Settings.EnableQrzGridEnrichment
            && string.IsNullOrWhiteSpace(decode.Grid)
            && (Settings.Settings.UseQrzGridsForNewGridTargeting
                || Settings.Settings.UseQrzGridsForUnconfirmedGridTargeting);
        var isUsa = WasStateEligibility.IsEligible(decode);
        var needsState = isUsa
            && string.IsNullOrWhiteSpace(decode.State);
        var needsIota = Location.IsAreaSelected("IOTA")
            && string.IsNullOrWhiteSpace(decode.Iota);

        return needsGrid || needsState || needsIota;
    }

    private void ApplyCallsignLocationUpdate(CallsignLocationResult result)
    {
        QrzStatus = result.Status == CallsignLookupStatus.Error
            ? $"QRZ lookup issue for {result.Callsign}: {result.ErrorMessage}"
            : result.Status == CallsignLookupStatus.Resolved
                ? $"QRZ lookup: {result.Callsign} resolved from {result.Source}."
                : $"QRZ lookup: {result.Callsign} {result.Status}.";

        var updated = 0;
        foreach (var decode in _decodeHistory.Where(d => DecodeTargetCall(d).Equals(result.Callsign, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (ApplyCallsignLocationToDecode(decode, result))
                updated++;
        }

        if (updated == 0)
            return;

        AddAction($"QRZ result {result.Status} applied to {updated} recent decode(s) for {result.Callsign}.");
        _visibleRowModel.Rebuild(_decodeHistory, JtdxBandActivityGridCalibration.FromSettings(Settings.Settings));
        foreach (var decode in _decodeHistory
                     .Where(d => DecodeTargetCall(d).Equals(result.Callsign, StringComparison.OrdinalIgnoreCase))
                     .Where(IsFreshDecode))
        {
            UpdateWantedItems(decode);
        }

        RequestNextBestTargetsUpdate();
    }

    private bool ApplyCallsignLocationToDecode(DecodeMessage decode, CallsignLocationResult result)
    {
        var resultError = result.ErrorMessage ?? "";
        var changed = decode.CallsignLookupStatus != result.Status
            || decode.CallsignDataSource != result.Source
            || !decode.CallsignLookupError.Equals(resultError, StringComparison.Ordinal);
        decode.CallsignLookupStatus = result.Status;
        decode.CallsignDataSource = result.Source;
        decode.CallsignLookupError = resultError;

        if (!string.IsNullOrWhiteSpace(result.Country) && string.IsNullOrWhiteSpace(decode.EntityName))
        {
            decode.EntityName = result.Country;
            decode.PrimaryDisplayEntity = result.Country;
            decode.EntitySource = result.Source.ToString();
            changed = true;
        }

        if (result.Dxcc.HasValue && string.IsNullOrWhiteSpace(decode.Dxcc))
        {
            decode.Dxcc = result.Dxcc.Value.ToString();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(result.Iota) && string.IsNullOrWhiteSpace(decode.Iota))
        {
            decode.Iota = result.Iota.Trim().ToUpperInvariant();
            changed = true;
        }

        var state = UsStateValidator.Normalize(result.State, Settings.Settings.IncludeDistrictOfColumbia);
        if (!string.IsNullOrWhiteSpace(state) && string.IsNullOrWhiteSpace(decode.State))
        {
            decode.State = state;
            decode.StateSource = result.Source.ToString();
            changed = true;
        }

        var normalizedGrid = MaidenheadGrid.Normalize(result.Grid ?? "");
        if (Settings.Settings.EnableQrzGridEnrichment && normalizedGrid.IsValid)
        {
            decode.QrzGrid = normalizedGrid.Grid4;
            var portable = CallsignNormalizer.IsPotentiallyPortable(DecodeTargetCall(decode));
            var canPromoteQrzGrid = string.IsNullOrWhiteSpace(decode.Grid)
                && (!portable || !Settings.Settings.IgnoreQrzTargetingForPotentiallyPortableCalls)
                && (Settings.Settings.UseQrzGridsForNewGridTargeting || Settings.Settings.UseQrzGridsForUnconfirmedGridTargeting);
            if (canPromoteQrzGrid)
            {
                decode.Grid = normalizedGrid.Grid4;
                decode.GridSource = "QRZ";
                decode.EffectiveGrid = normalizedGrid.Grid4;
                decode.EffectiveGridSource = DecodeGridSource.Qrz;
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(decode.Grid) && string.IsNullOrWhiteSpace(decode.EffectiveGrid))
        {
            decode.EffectiveGrid = decode.Grid;
            decode.EffectiveGridSource = decode.GridSource.Equals("QRZ", StringComparison.OrdinalIgnoreCase)
                ? DecodeGridSource.Qrz
                : DecodeGridSource.Ft8Message;
        }

        if (changed)
            _targetScorer.EnrichDecode(decode, _logbook, _adifMergeResult.Indexes, Settings.Settings);

        return changed;
    }

    private bool IsFreshDecode(DecodeMessage decode)
    {
        return decode.ReceivedAt >= DateTime.Now.AddSeconds(-CandidateStaleSeconds(decode));
    }

    private int NormalStaleSeconds() => Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds);

    private int NewDxccStaleSeconds() => Math.Max(NormalStaleSeconds(), Settings.Settings.NewDxccStaleSeconds);

    private int CandidateStaleSeconds(DecodeMessage decode)
    {
        return IsUnconfirmedDxccDecode(decode) ? NewDxccStaleSeconds() : NormalStaleSeconds();
    }

    private int WantedStaleSeconds(WantedItem item)
    {
        return item.Section.Equals("DXCC", StringComparison.OrdinalIgnoreCase)
            && IsUnconfirmedDxccNeed(item.NeedStatus)
                ? NewDxccStaleSeconds()
                : NormalStaleSeconds();
    }

    private bool IsUnconfirmedDxccDecode(DecodeMessage decode)
    {
        return !string.IsNullOrWhiteSpace(decode.Dxcc)
            && (!_adifMergeResult.Indexes.Dxcc.TryGetValue(decode.Dxcc, out var status) || !status.ConfirmedAny);
    }

    private static bool IsUnconfirmedDxccStatus(DxccCandidateStatus status) =>
        status is DxccCandidateStatus.NotWorked or DxccCandidateStatus.WorkedUnconfirmed;

    private static bool IsUnconfirmedDxccNeed(NeedStatus status) =>
        status is NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed;

    private bool KeepCallingActiveNewDxccUntilStale()
    {
        return Settings.Settings.KeepCallingNewDxccUntilStale
            && _lockedTarget != null
            && _huntState == HuntState.Calling
            && IsUnconfirmedDxccStatus(_lockedTarget.Ranking.DxccStatus);
    }

    private bool ActiveCallingTargetHasGoneStale()
    {
        if (_lockedTarget == null
            || _huntState != HuntState.Calling
            || _targetSelectionInProgress
            || _immediateTxRetargetInProgress
            || _targetConfirmedInFeed
            || _qsoStage >= QsoStage.TargetReportSeen)
        {
            return false;
        }

        var keepCallingNewDxccUntilStale = KeepCallingActiveNewDxccUntilStale();
        // A previous real call protects a target only while JTDX still confirms
        // that selection. Otherwise an old source plus a cleared DX Call can
        // strand the lock forever with TX deliberately held off.
        if (!keepCallingNewDxccUntilStale
            && _targetConfirmedInJtdx)
        {
            return false;
        }

        var lastHeardUtc = LastHeardUtc(_lockedTarget.Callsign, _lockedTarget.Decode);
        var staleSeconds = keepCallingNewDxccUntilStale
            ? NewDxccStaleSeconds()
            : NormalStaleSeconds();
        return DateTime.UtcNow - lastHeardUtc > TimeSpan.FromSeconds(staleSeconds);
    }

    private string CallAttemptProgressText()
    {
        return KeepCallingActiveNewDxccUntilStale()
            ? $"{_callAttemptCount} (until stale)"
            : $"{_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}";
    }

    private void RefreshModeIndicators()
    {
        OnPropertyChanged(nameof(IsDxAssistActive));
        OnPropertyChanged(nameof(IsWantedSniperActive));
        OnPropertyChanged(nameof(IsLocationHuntActive));
    }

    private async void StartDxAssist()
    {
        _operatingMode = HuntingOperatingMode.DxAssist;
        RefreshModeIndicators();
        AddAction("Mode selected: DX Assist. Wanted Sniper and Location Hunt stopped.");
        await StartAutoResumeAsync();
    }

    private async void StartWantedSniper()
    {
        _operatingMode = HuntingOperatingMode.WantedSniper;
        RefreshModeIndicators();
        AddAction("Mode selected: Wanted Sniper active. DX Assist and Location Hunt paused.");
        await StartAutoResumeAsync();
    }

    private async void StartLocationHunt()
    {
        _operatingMode = HuntingOperatingMode.LocationHunt;
        RefreshModeIndicators();
        AddAction($"Mode selected: Location Hunt active ({Location.SelectedAreasDisplay}). DX Assist and Wanted Sniper paused.");
        await StartAutoResumeAsync();
    }

    private async Task StartAutoResumeAsync()
    {
        SaveAll();
        LoadAdifSources();
        StartAdifWatcher();
        if (!_udpListener.IsRunning)
            await StartUdpAsync();
        else
            CaptureJtdxWindow(resetGrid: false, source: "DX Pilot start");
        _autoResume.Start(Settings.Settings, Scheduler.ScheduleItems);
        AddAction(_operatingMode switch
        {
            HuntingOperatingMode.WantedSniper => "Start mode: Wanted Sniper active; other hunting paused.",
            HuntingOperatingMode.LocationHunt => $"Start mode: Location Hunt active ({Location.SelectedAreasDisplay}); other hunting paused.",
            _ => "Start mode: DX Assist."
        });
        _huntTimer.Start();
        await HuntTickAsync();
        if (_operatingMode == HuntingOperatingMode.DxAssist)
        {
            ArmEnableTxForSelectedTarget("Start DX Pilot");
        }
        else if (_lockedTarget == null)
        {
            EnsureEnableTxOff($"{OperatingModeLabel()} active at start");
        }
        Dashboard.OverallStatus = _operatingMode switch
        {
            HuntingOperatingMode.WantedSniper => "Wanted Sniper is active; other hunting is paused.",
            HuntingOperatingMode.LocationHunt => $"Location Hunt is active: {Location.SelectedAreasDisplay}.",
            _ => "DX Assist is running."
        };
        RefreshModeIndicators();
    }

    private async void StopAll()
    {
        _autoResume.Stop();
        _huntTimer.Stop();
        _operatingMode = HuntingOperatingMode.DxAssist;
        await ReleaseLockedTargetAndMaybeResumeAsync("DX Pilot stopped", "Abandoned - DX Pilot stopped", suppress: false, resumeSniper: false);
        EnsureEnableTxOff("Stop All");
        Dashboard.OverallStatus = "Stopped. DX Assist, Wanted Sniper and Location Hunt are off.";
        AddAction("Stop All: all hunting modes stopped and active target cleared.");
        RefreshModeIndicators();
    }

    private async void StopUdp()
    {
        _udpListener.Stop();
        if (_autoResume.IsRunning)
        {
            _autoResume.Stop();
            _huntTimer.Stop();
            _operatingMode = HuntingOperatingMode.DxAssist;
            await ReleaseLockedTargetAndMaybeResumeAsync("UDP stopped; DX Pilot stopped to avoid blind TX control", "Abandoned - DX Pilot stopped", suppress: false, resumeSniper: false);
            Dashboard.OverallStatus = "DX Pilot stopped because the UDP listener was stopped.";
            AddAction("DX Pilot stopped because the UDP listener was stopped; UDP status is required before enabling TX.");
            RefreshModeIndicators();
        }
    }

    private async Task StartUdpAsync()
    {
        SaveAll();
        await _udpListener.StartAsync(
            Settings.Settings.UdpListenPort,
            Settings.Settings.UdpForwardEnabled,
            Settings.Settings.UdpForwardHost,
            Settings.Settings.UdpForwardPort);
        CaptureJtdxWindow(resetGrid: false, source: "UDP start");
    }

    public async Task StartUdpOnLaunchAsync()
    {
        if (!_udpListener.IsRunning)
            await StartUdpAsync();
    }

    private void SelectBestTarget()
    {
        var best = _targetSelector.SelectBest(CurrentCandidateDecodes(), _logbook, _adifMergeResult.Indexes, Settings.Settings);
        DxAssist.BestTarget = best;
        if (best == null)
        {
            Dashboard.BestTarget = "No CQ target found.";
            Dashboard.BestReason = "";
            return;
        }

        UpdateBestTarget(best);
        UpdateNextBestTargets();
        AddAction($"Best target selected: {best.Callsign} ({best.PrimaryReason}).");
    }

    private async Task ReplyToBestAsync()
    {
        if (DxAssist.BestTarget == null)
        {
            SelectBestTarget();
            if (DxAssist.BestTarget == null)
                return;
        }

        if (!IsFreshTarget(DxAssist.BestTarget))
        {
            AddAction($"UDP Reply blocked for {DxAssist.BestTarget.Callsign}: source decode is stale.");
            DxAssist.BestTarget = null;
            Dashboard.BestTarget = "No fresh target selected.";
            Dashboard.BestReason = "";
            return;
        }

        try
        {
            await SendReplyAsync(DxAssist.BestTarget);
        }
        catch (Exception ex)
        {
            AddAction($"UDP Reply failed: {ex.Message}");
        }
    }

    private void LoadAdif()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ADIF files (*.adi;*.adif)|*.adi;*.adif|All files (*.*)|*.*",
            Title = "Load full/master ADIF logbook"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            Settings.Settings.FullAdifPath = dialog.FileName;
            Settings.Settings.AutoLoadFullAdifOnStartup = true;
            _settingsService.SaveSettings(Settings.Settings);
            LoadAdifSources();
            AddAction($"Full ADIF path saved: {dialog.FileName}");
            SelectBestTarget();
        }
        catch (Exception ex)
        {
            LogbookStatus = $"ADIF import failed: {ex.Message}";
            AddAction(LogbookStatus);
        }
    }

    private void SaveAll()
    {
        Settings.Settings.AutoSelectBestCq = DxAssist.AutoSelectBestCq;
        Settings.Settings.AdifFilePath = Settings.Settings.LiveJtdxAdifPath;
        Settings.Settings.JtdxAllTxtPath = JtdxAllTxtMonitor.ResolveCurrentPath(Settings.Settings.JtdxAllTxtPath);
        _rarityService.Load(Settings.Settings.DxccRarityFilePath, _dxccResolver);
        RarityDiagnostics = _rarityService.Diagnostics.Summary;
        _settingsService.SaveSettings(Settings.Settings);
        _settingsService.SaveSchedule(Scheduler.ScheduleItems);
        StartAllTxtMonitor();
        UpdateAdifDiagnostics();
        AddAction("Settings saved.");
    }

    private void BrowseAllTxt()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select the current JTDX ALL.TXT outgoing-message log",
            Filter = "JTDX ALL.TXT files (*_ALL.TXT)|*_ALL.TXT|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            FileName = Path.GetFileName(Settings.Settings.JtdxAllTxtPath),
            InitialDirectory = Path.GetDirectoryName(Settings.Settings.JtdxAllTxtPath)
        };
        if (dialog.ShowDialog() != true)
            return;

        Settings.Settings.JtdxAllTxtPath = dialog.FileName;
        Settings.Settings.WatchJtdxAllTxt = true;
        Settings.Refresh();
        StartAllTxtMonitor(forceRestart: true);
        SaveAll();
        AddAction($"JTDX ALL.TXT path selected: {dialog.FileName}");
    }

    private void ExportSettings()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export DX Pilot settings",
            Filter = "DX Pilot settings (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"DXPilot-Settings-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SaveAll();
            _settingsService.ExportPortableSettings(
                dialog.FileName,
                Settings.Settings,
                Scheduler.ScheduleItems);
            SettingsTransferStatus =
                $"Settings exported to {dialog.FileName}. QRZ password excluded; {Scheduler.ScheduleItems.Count} scheduler rows included.";
            AddAction(SettingsTransferStatus);
            System.Windows.MessageBox.Show(
                "Settings exported successfully.\n\nThe QRZ password was deliberately excluded. Enter it again after importing on another installation.",
                "DX Pilot Settings Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SettingsTransferStatus = $"Settings export failed: {ex.GetBaseException().Message}";
            AddAction(SettingsTransferStatus);
            System.Windows.MessageBox.Show(
                SettingsTransferStatus,
                "DX Pilot Settings Export",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import DX Pilot settings",
            Filter = "DX Pilot settings (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        if (!_settingsService.TryReadSettingsImport(dialog.FileName, out var payload, out var error))
        {
            SettingsTransferStatus = $"Settings import rejected: {error}";
            AddAction(SettingsTransferStatus);
            System.Windows.MessageBox.Show(
                SettingsTransferStatus,
                "DX Pilot Settings Import",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var exportedText = payload.ExportedAtUtc.HasValue
            ? payload.ExportedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "legacy settings file";
        var scheduleText = payload.Schedule == null
            ? "The current scheduler will be retained."
            : $"{payload.Schedule.Count} scheduler rows will be imported.";
        var credentialText = payload.QrzPasswordExcluded
            ? "\nThe QRZ password is not contained in this file and will need to be entered again."
            : "";
        var confirmation =
            $"Import settings from:\n{dialog.FileName}\n\n"
            + $"Exported: {exportedText}\n"
            + $"JTDX visible rows: {payload.Settings.JtdxBandVisibleRowCount}\n"
            + $"{scheduleText}{credentialText}\n\n"
            + "The current configuration will be backed up automatically. "
            + "DX Pilot will then close so the imported settings can be loaded cleanly.";

        if (System.Windows.MessageBox.Show(
                confirmation,
                "Confirm DX Pilot Settings Import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            SettingsTransferStatus = "Settings import cancelled; no changes made.";
            return;
        }

        try
        {
            SaveAll();
            var backupFolder = _settingsService.BackupCurrentConfiguration();

            _autoResume.Stop();
            _huntTimer.Stop();
            EnsureEnableTxOff("Settings import");
            _udpListener.Stop();
            if (_lockedTarget != null)
                ClearLockedTarget("Settings import: active target released without suppression.");

            _settingsService.ApplyImportedConfiguration(payload);
            Settings.Settings = payload.Settings;
            Settings.Refresh();
            if (payload.Schedule != null)
            {
                Scheduler.ScheduleItems.Clear();
                foreach (var item in payload.Schedule)
                    Scheduler.ScheduleItems.Add(item);
            }

            SettingsTransferStatus =
                $"Settings imported from {dialog.FileName}. Previous configuration backed up to {backupFolder}.";
            AddAction(SettingsTransferStatus);
            System.Windows.MessageBox.Show(
                $"Settings imported successfully.\n\nBackup:\n{backupFolder}\n\nDX Pilot will now close. Reopen this build to use the imported configuration.",
                "DX Pilot Settings Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            SettingsTransferStatus = $"Settings import failed: {ex.GetBaseException().Message}";
            AddAction(SettingsTransferStatus);
            System.Windows.MessageBox.Show(
                SettingsTransferStatus + "\n\nThe previous settings remain available in the automatic backup if it was created.",
                "DX Pilot Settings Import",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RunDiagnosticLookup()
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(DiagnosticCallsign))
            lines.Add(BuildCallsignDiagnostic(DiagnosticCallsign));

        if (!string.IsNullOrWhiteSpace(DiagnosticGrid))
            lines.Add(BuildSimpleDiagnostic("Grid", DiagnosticGrid.Trim().ToUpperInvariant(), _adifMergeResult.Indexes.Grids, Settings.Settings.GridConfirmationMode));

        if (!string.IsNullOrWhiteSpace(DiagnosticState))
            lines.Add(BuildSimpleDiagnostic("State", DiagnosticState.Trim().ToUpperInvariant(), _adifMergeResult.Indexes.States, Settings.Settings.StateConfirmationMode));

        if (!string.IsNullOrWhiteSpace(DiagnosticIota))
            lines.Add(BuildSimpleDiagnostic("IOTA", DiagnosticIota.Trim().ToUpperInvariant(), _adifMergeResult.Indexes.Iotas, Settings.Settings.IotaConfirmationMode));

        DiagnosticLookupResult = lines.Count == 0
            ? "Enter a callsign, grid, state, or IOTA reference, then run lookup."
            : string.Join("\n\n", lines);
    }

    private async Task TestQrzConnectionAsync()
    {
        SaveAll();
        QrzStatus = "Testing QRZ connection...";
        AddAction("QRZ connection test started.");
        try
        {
            var result = await _callsignLocationService.TestQrzConnectionAsync(CancellationToken.None);
            QrzStatus = result;
            AddAction($"QRZ connection test: {result}");
        }
        catch (Exception ex)
        {
            QrzStatus = $"QRZ connection test failed safely: {ex.GetType().Name}.";
            AddAction(QrzStatus);
        }
    }

    private void ClearQrzCache()
    {
        _callsignLocationService.ClearCache();
        QrzStatus = "QRZ callsign cache cleared.";
        AddAction(QrzStatus);
    }

    private string BuildCallsignDiagnostic(string callsign)
    {
        var call = callsign.Trim().ToUpperInvariant();
        var resolver = _dxccResolver.ResolveDiagnostic(call);
        var resolved = _dxccResolver.Resolve(call);
        var dxccStatus = resolved != null && !string.IsNullOrWhiteSpace(resolved.Code)
            ? _adifMergeResult.Indexes.Dxcc.GetValueOrDefault(resolved.Code)
            : null;
        var latestQso = _logbook
            .Where(q => q.Call.Equals(call, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(q => q.QsoDate ?? DateTime.MinValue)
            .ThenByDescending(q => q.TimeOn)
            .FirstOrDefault();

        return resolver
            + "\n"
            + $"DXCC worked: {dxccStatus?.WorkedAny ?? false}\n"
            + $"DXCC confirmed: {dxccStatus?.ConfirmedAny ?? false}\n"
            + $"Confirmation mode: {Settings.Settings.DxccConfirmationMode}\n"
            + $"DXCC source ADIF: {DisplaySource(dxccStatus?.Source)}\n"
            + $"Latest matching QSO: {FormatQso(latestQso)}";
    }

    private static string BuildSimpleDiagnostic(
        string label,
        string id,
        Dictionary<string, SimpleWorkedStatus> index,
        string confirmationMode)
    {
        index.TryGetValue(id, out var status);
        return $"{label}: {id}\n"
            + $"Worked: {status?.WorkedAny ?? false}\n"
            + $"Confirmed: {status?.ConfirmedAny ?? false}\n"
            + $"Confirmation mode: {confirmationMode}\n"
            + $"LoTW confirmed: {status?.LoTWConfirmedAny ?? false}\n"
            + $"Paper confirmed: {status?.PaperConfirmedAny ?? false}\n"
            + $"eQSL confirmed: {status?.EqslConfirmedAny ?? false}\n"
            + $"Worked bands: {FormatSet(status?.WorkedBands)}\n"
            + $"Source ADIF: {DisplaySource(status?.Source)}";
    }

    private async Task HuntTickAsync()
    {
        if (_huntTickRunning)
            return;

        _huntTickRunning = true;
        try
        {
        ReloadAdifIfChanged();
        ExpireSuppressedTargets();
        UpdateNextBestTargets();
        CompleteRadioContextSettlingIfReady();
        if (!RadioContextReadyForSelection())
        {
            EnsureEnableTxOff("Radio context is settling");
            UpdateHuntStateDisplay();
            return;
        }

        if (_lockedTarget != null && HasFreshLiveQso(_lockedTarget.Callsign))
        {
            CompleteLockedTarget($"QSO released: ADIF confirmed {_lockedTarget.Callsign}.");
        }

        if (_huntState == HuntState.Calling && await TryPreemptForFreshNewDxccAsync())
            return;

        if (_huntState == HuntState.Idle)
        {
            var globalNewDxcc = SelectGlobalNewDxccTarget();
            if (globalNewDxcc != null)
            {
                Location.Status = $"Global New DXCC priority: {globalNewDxcc.Callsign} ({globalNewDxcc.Decode.EntityName}).";
                AddAction($"Global New DXCC priority: {globalNewDxcc.Callsign} ({globalNewDxcc.Decode.EntityName}) selected ahead of all mode and location filters.");
                await LockAndReplyAsync(globalNewDxcc, "Global New DXCC priority", globalNewDxcc.PrimaryReason, "All locations");
                return;
            }
        }

        if (_operatingMode == HuntingOperatingMode.LocationHunt)
        {
            if (_huntState is HuntState.Calling or HuntState.InQso)
            {
                await MaintainLockedTargetAsync();
                return;
            }

            if (DateTime.Now < _postQsoTransitionUntil)
            {
                _recoveryMode = "PostQsoTransition";
                _lastCorrectiveAction = "Waiting for JTDX to settle after completed QSO";
                UpdateHuntStateDisplay();
                return;
            }

            var locationTarget = SelectLocationHuntTarget();
            if (locationTarget == null)
            {
                Location.Status = $"Location Hunt active ({Location.SelectedAreasDisplay}): no actionable target right now.";
                EnsureEnableTxOff("Location Hunt has no target");
                UpdateHuntStateDisplay();
                return;
            }

            LogPostQsoSelectingNext(locationTarget.Callsign);
            Location.Status = locationTarget.Decode.IsNewDxcc && !MatchesSelectedLocationAreas(locationTarget)
                ? $"Global New DXCC override: {locationTarget.Callsign} ({locationTarget.Decode.EntityName})."
                : $"Location Hunt selected {locationTarget.Callsign} from {Location.SelectedAreasDisplay}.";
            await LockAndReplyAsync(locationTarget, "Location Hunt", locationTarget.PrimaryReason, Location.SelectedAreasDisplay);
            return;
        }

        if (_operatingMode == HuntingOperatingMode.WantedSniper)
        {
            if (_huntState is HuntState.Calling or HuntState.InQso)
            {
                if (_qsoStage == QsoStage.CompletionPending && _lockedTarget != null)
                    AddThrottledCompletionLog($"Retarget blocked: QSO completion pending with {_lockedTarget.Callsign}.");

                if (await TryPreemptForWantedDxccAsync())
                    return;

                await MaintainLockedTargetAsync();
                return;
            }

            if (DateTime.Now < _postQsoTransitionUntil)
            {
                _recoveryMode = "PostQsoTransition";
                _lastCorrectiveAction = "Waiting for JTDX to settle after completed QSO";
                UpdateHuntStateDisplay();
                return;
            }

            await TryWantedSniperAsync();

            Dashboard.OverallStatus = "Wanted Sniper active; DX Assist general hunting paused.";
            UpdateHuntStateDisplay();
            return;
        }

        if (!Settings.Settings.AutoHuntEnabled)
        {
            UpdateNextBestTargets();
            UpdateHuntStateDisplay();
            return;
        }

        if (_huntState is HuntState.Calling or HuntState.InQso)
        {
            await MaintainLockedTargetAsync();
            return;
        }

        if (DateTime.Now < _postQsoTransitionUntil)
        {
            _recoveryMode = "PostQsoTransition";
            _lastCorrectiveAction = "Waiting for JTDX to settle after completed QSO";
            UpdateHuntStateDisplay();
            return;
        }

        if (_huntState != HuntState.Idle)
            return;

        var best = SelectNextAutomatedTarget();
        if (best == null)
        {
            UpdateHuntStateDisplay();
            return;
        }

        LogPostQsoSelectingNext(best.Callsign);
        await LockAndReplyAsync(best, "Auto-ranked", best.PrimaryReason, "");
        }
        finally
        {
            _huntTickRunning = false;
        }
    }

    private async Task MaintainLockedTargetAsync()
    {
        if (_lockedTarget == null)
        {
            _huntState = HuntState.Idle;
            UpdateHuntStateDisplay();
            return;
        }

        if (HasFreshLiveQso(_lockedTarget.Callsign))
        {
            CompleteLockedTarget($"QSO released: ADIF confirmed {_lockedTarget.Callsign}.");
            return;
        }

        var keepCallingNewDxcc = KeepCallingActiveNewDxccUntilStale();
        if (ActiveCallingTargetHasGoneStale())
        {
            var staleReason = keepCallingNewDxcc
                ? $"New DXCC persistence ended: {_lockedTarget.Callsign} has gone stale"
                : $"Target became stale before QSO progress: {_lockedTarget.Callsign}";
            await AbandonStaleCallingTargetAsync(staleReason);
            return;
        }

        if (_huntState == HuntState.InQso && _qsoStage == QsoStage.CompletionPending)
        {
            if (CompletionPendingTimedOut())
            {
                CompleteLockedTarget($"QSO released: completion timeout expired for {_lockedTarget.Callsign}.");
                await HuntTickAsync();
            }
            else
            {
                _lastCorrectiveAction = _myFinal73SeenDuringCompletion
                    ? "Waiting for completion grace cycles after final 73"
                    : "Completion pending; waiting for final 73/RR73 or ADIF confirmation";
                UpdateHuntStateDisplay();
            }
            return;
        }

        if (_huntState == HuntState.InQso && InQsoNoProgressTimedOut())
        {
            var stalledCall = _lockedTarget.Callsign;
            _stuckReason =
                $"QSO abandoned: no new progress from {stalledCall} for {Math.Max(1, Settings.Settings.CallTimeoutMinutes)} minute(s).";
            EnsureEnableTxOff(_stuckReason);
            AddAction($"{_stuckReason} Releasing the stale QSO lock and returning to hunting.");
            await ReleaseLockedTargetAndMaybeResumeAsync(
                _stuckReason,
                "Missed - QSO progress timed out",
                suppress: true,
                resumeSniper: true);
            return;
        }

        if (_huntState == HuntState.Calling
            && !keepCallingNewDxcc
            && !_targetConfirmedInJtdx
            && !_targetSelectionInProgress
            && !_immediateTxRetargetInProgress
            && AcquisitionFailed())
        {
            await FailCurrentReplySourceAndRetargetAsync($"JTDX did not confirm {_lockedTarget.Callsign} within acquisition window");
            return;
        }

        var maxCallAttempts = Math.Max(1, Settings.Settings.MaxCallAttempts);
        var maxReportAttempts = Math.Max(1, Settings.Settings.MaxReportAttempts);

        if (_huntState == HuntState.Calling
            && _jtdxShowsWrongTx
            && DateTime.Now - _lastSelectionNudgeAt >= ActiveAttemptCycle())
        {
            AddAction($"JTDX is not aimed at {_lockedTarget.Callsign}; nudging UDP Reply without CQ/TX6 reset.");
            _lastCorrectiveAction = $"Sent UDP Reply nudge to {_lockedTarget.Callsign}";
            await SendReplyAsync(_lockedTarget, countAttempt: false);
            _lastSelectionNudgeAt = DateTime.Now;
            UpdateHuntStateDisplay();
            return;
        }

        if (_huntState == HuntState.Calling
            && !keepCallingNewDxcc
            && _callAttemptCount >= maxCallAttempts)
        {
            await ReleaseLockedTargetAndMaybeResumeAsync(
                $"Target released: {_lockedTarget.Callsign} - call attempts exceeded {_callAttemptCount}/{maxCallAttempts}",
                "Missed - no reply",
                suppress: true,
                resumeSniper: true);
            return;
        }

        if (_huntState == HuntState.Calling && _pendingLockedReplyWhenIdle)
        {
            _recoveryMode = "WaitingForJtdxIdle";
            _lastCorrectiveAction = $"Waiting for RX before selecting {_lockedTarget.Callsign}";
            UpdateHuntStateDisplay();
            return;
        }

        if (_huntState == HuntState.Calling
            && !keepCallingNewDxcc
            && NoQsoProgressTimedOut())
        {
            await ReleaseLockedTargetAndMaybeResumeAsync(
                $"Target released: {_lockedTarget.Callsign} - no QSO progress",
                "Missed - no progress",
                suppress: true,
                resumeSniper: true);
            return;
        }

        if (_huntState == HuntState.InQso && _reportAttemptCount >= maxReportAttempts)
        {
            _qsoStage = QsoStage.QsoStuck;
            _stuckReason = $"QSO stuck: report repeats exceeded {_reportAttemptCount}/{maxReportAttempts}.";
            SuppressTarget(_lockedTarget.Callsign);
            _lastCorrectiveAction = "Suppressed target due to stuck reports";
            AddAction($"{_stuckReason} Suppressing {_lockedTarget.Callsign} and releasing lock.");
            await ReleaseLockedTargetAndMaybeResumeAsync(
                "QSO stuck: report repeats exceeded",
                "Missed - no reply",
                suppress: false,
                resumeSniper: true);
            return;
        }

        if (_huntState == HuntState.Calling
            && !_targetSelectionInProgress
            && DateTime.Now - _lastCallAttemptAt >= ActiveAttemptCycle())
        {
            if (!_targetConfirmedInJtdx)
            {
                AddAction($"JTDX has not confirmed {_lockedTarget.Callsign} as DX Call; resending UDP Reply.");
                await SendReplyAsync(_lockedTarget, countAttempt: false);
                _lastCallAttemptAt = DateTime.Now;
                _lastSelectionNudgeAt = DateTime.Now;
            }
            else
            {
                _lastCallAttemptAt = DateTime.Now;
                AddAction($"Awaiting an observed transmission to {_lockedTarget.Callsign}; call-attempt count remains {CallAttemptProgressText()}.");
            }
        }

        UpdateHuntStateDisplay();
    }

    private DxTarget? SelectNextAutomatedTarget()
    {
        var eligible = CurrentCandidateDecodes()
            .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
            .Where(d => !IsFailedReplySource(d))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)))
            .Where(d => !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .Where(d => !IsSuppressed(DecodeTargetCall(d)))
            .ToList();

        return _targetSelector.SelectBest(eligible, _logbook, _adifMergeResult.Indexes, Settings.Settings);
    }

    private DxTarget? SelectLocationHuntTarget()
    {
        var eligible = CurrentCandidateDecodes()
            .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
            .Where(d => !IsFailedReplySource(d))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)))
            .Where(d => !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .Where(d => !IsSuppressed(DecodeTargetCall(d)))
            .Where(IsSelectableDecodeForAcquisition)
            .ToList();

        // Rank the complete fresh pool before applying the selected-area union so
        // a quiet selected area cannot be hidden by 100 higher rows elsewhere.
        var ranked = _targetSelector.SelectRanked(eligible, _logbook, _adifMergeResult.Indexes, Settings.Settings, 500, includeActiveQso: false);
        var globalNewDxcc = ranked.FirstOrDefault(t => IsUnconfirmedDxccStatus(t.Ranking.DxccStatus));
        return globalNewDxcc ?? ranked.FirstOrDefault(MatchesSelectedLocationAreas);
    }

    private DxTarget? SelectGlobalNewDxccTarget()
    {
        var eligible = CurrentCandidateDecodes()
            .Where(IsSelectableDecodeForAcquisition)
            .Where(d => !IsFailedReplySource(d))
            .Where(d => !IsSuppressed(DecodeTargetCall(d)))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)))
            .Where(d => !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .ToList();

        return _targetSelector
            .SelectRanked(eligible, _logbook, _adifMergeResult.Indexes, Settings.Settings, 25, includeActiveQso: false)
            .FirstOrDefault(t => IsUnconfirmedDxccStatus(t.Ranking.DxccStatus));
    }

    private async Task<bool> TryPreemptForFreshNewDxccAsync()
    {
        if (_huntState != HuntState.Calling || _qsoStage >= QsoStage.TargetReportSeen || _lockedTarget == null)
            return false;

        if (IsUnconfirmedDxccStatus(_lockedTarget.Ranking.DxccStatus))
            return false;

        var newDxcc = SelectGlobalNewDxccTarget();
        if (newDxcc?.Callsign.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase) == true)
            return false;
        if (newDxcc == null)
            return false;

        var previous = _lockedTarget.Callsign;
        AddAction($"Global New DXCC override: releasing {previous} before QSO progress because {newDxcc.Callsign} ({newDxcc.Decode.EntityName}) appeared.");
        ClearLockedTarget($"Released {previous}; fresh New DXCC {newDxcc.Callsign} has absolute priority.");
        Location.Status = $"Global New DXCC override: {newDxcc.Callsign} ({newDxcc.Decode.EntityName}).";
        await LockAndReplyAsync(newDxcc, "Global New DXCC override", newDxcc.PrimaryReason, "All locations");
        return true;
    }

    private void LogPostQsoSelectingNext(string callsign)
    {
        if (_postQsoTransitionUntil == DateTime.MinValue || DateTime.Now < _postQsoTransitionUntil)
            return;

        AddAction($"Post-QSO transition: selecting next target {callsign}.");
        _postQsoTransitionUntil = DateTime.MinValue;
    }

    private async Task TryWantedSniperAsync()
    {
        if (_wantedSniperBusy || CurrentWantedSniperMode() != WantedSniperMode.Active)
            return;
        if (!_autoResume.IsRunning)
        {
            Wanted.Status = "Wanted Sniper is selected, but DX Pilot is stopped.";
            return;
        }
        if (_huntState != HuntState.Idle)
            return;

        _wantedSniperBusy = true;
        try
        {
            var best = SelectWantedSniperTarget();
            if (best == null)
            {
                Wanted.Status = "Wanted Sniper active: no actionable wanted target right now.";
                LogWantedSniperNoTarget();
                EnsureEnableTxOff("Wanted Sniper has no target");
                return;
            }

            var target = _targetScorer.Score(best.SourceDecode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
            target.Reasons.Insert(0, best.WantedDetail);
            LogPostQsoSelectingNext(target.Callsign);
            Wanted.Status = $"Wanted Sniper target selected: {best.ContactableCall} - {best.WantedDetail}";
            AddAction($"Wanted Sniper target: {best.ContactableCall} from {best.Block}; {best.WantedDetail}; method {best.SelectionMethod}.");
            AddAction($"Wanted Sniper source decode: {best.SourceRawMessage}");
            await LockAndReplyAsync(target, "Wanted Sniper", best.WantedDetail, best.Block);
        }
        finally
        {
            _wantedSniperBusy = false;
        }
    }

    private async Task<bool> TryPreemptForWantedDxccAsync()
    {
        if (_wantedSniperBusy
            || !Settings.Settings.EnableWantedDxcc
            || _huntState != HuntState.Calling
            || _lockedTarget == null
            || IsUnconfirmedDxccStatus(_lockedTarget.Ranking.DxccStatus)
            || _wantedSourceBlock.Equals("Wanted DXCC", StringComparison.OrdinalIgnoreCase)
            || _qsoStage >= QsoStage.TargetReportSeen)
        {
            return false;
        }

        var dxcc = SelectWantedDxccOverride();
        if (dxcc == null)
            return false;

        // This override is intended to interrupt a lower-priority target when a
        // different New DXCC appears. Never release or suppress the target that
        // is already locked, even if it reached the sniper through a global
        // "All locations" selection rather than the Wanted DXCC block.
        if (WantedDxccMatchesLockedTarget(dxcc))
            return false;

        var previous = _lockedTarget.Callsign;
        UpdateWantedActionability(dxcc);
        AddAction($"Wanted DXCC override: releasing {previous} ({_wantedSourceBlock}) because {dxcc.ContactableCall} appeared; {dxcc.WantedDetail}; {dxcc.ActionabilityText}.");
        SuppressTarget(previous);
        ClearLockedTarget($"Released {previous} because Wanted DXCC {dxcc.ContactableCall} appeared.");
        await TryWantedSniperAsync();

        return true;
    }

    private WantedItem? SelectWantedDxccOverride()
    {
        foreach (var item in Wanted.WantedDxcc)
            UpdateWantedActionability(item);

        return Wanted.WantedDxcc
            .Where(item => item.IsActionable)
            .Where(item => !WantedDxccMatchesLockedTarget(item))
            .OrderBy(item => item.PriorityTier ?? int.MaxValue)
            .ThenByDescending(item => item.AdjustedDxValueScore ?? 0)
            .ThenByDescending(item => item.UKDesirability ?? 0)
            .ThenByDescending(item => item.LastSeenUtc)
            .ThenByDescending(item => item.Snr)
            .FirstOrDefault();
    }

    private bool WantedDxccMatchesLockedTarget(WantedItem item)
    {
        if (_lockedTarget == null)
            return false;

        var lockedCall = CallsignNormalizer.Normalize(_lockedTarget.Callsign);
        var wantedCall = CallsignNormalizer.Normalize(WantedItemTargetCall(item));
        if (!string.IsNullOrWhiteSpace(lockedCall)
            && lockedCall.Equals(wantedCall, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lockedDxcc = FirstNonBlank(_lockedTarget.Decode.Dxcc, _lockedTarget.Decode.DxccNumber);
        var wantedDxcc = FirstNonBlank(item.DxccNumber, item.SourceDecode.Dxcc, item.SourceDecode.DxccNumber);
        return !string.IsNullOrWhiteSpace(lockedDxcc)
            && lockedDxcc.Equals(wantedDxcc, StringComparison.OrdinalIgnoreCase);
    }

    private WantedItem? SelectWantedSniperTarget()
    {
        var candidates = new List<(WantedItem Item, int CategoryPriority)>();
        if (Settings.Settings.EnableWantedDxcc)
            candidates.AddRange(Wanted.WantedDxcc.Select(item => (item, 0)));
        if (Settings.Settings.EnableWantedStates)
            candidates.AddRange(Wanted.WantedStates.Select(item => (item, 1)));
        if (Settings.Settings.EnableWantedGrids)
            candidates.AddRange(Wanted.WantedGrids.Select(item => (item, 2)));

        foreach (var candidate in candidates)
            UpdateWantedActionability(candidate.Item);

        return candidates
            .Where(candidate => candidate.Item.IsActionable)
            .OrderBy(candidate => candidate.CategoryPriority)
            .ThenBy(candidate => candidate.Item.PriorityTier ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.Item.AdjustedDxValueScore ?? 0)
            .ThenByDescending(candidate => candidate.Item.UKDesirability ?? 0)
            .ThenByDescending(candidate => candidate.Item.LastSeenUtc)
            .ThenByDescending(candidate => candidate.Item.Snr)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private async Task TryUpgradeLockedTargetSourceAsync(DecodeMessage decode)
    {
        if (_wantedSniperBusy
            || _lockedTarget == null
            || _huntState != HuntState.Calling
            || _targetConfirmedInJtdx
            || !IsInitialAcquisitionMessage(decode)
            || IsFailedReplySource(decode)
            || !decode.ContactableCall.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsInitialAcquisitionMessage(_lockedTarget.Decode)
            && !IsFailedReplySource(_lockedTarget.Decode)
            && IsFreshTarget(_lockedTarget))
        {
            return;
        }

        _wantedSniperBusy = true;
        try
        {
            var upgraded = _targetScorer.Score(decode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
            if (!string.IsNullOrWhiteSpace(_wantedReason))
                upgraded.Reasons.Insert(0, _wantedReason);

            _lockedTarget = upgraded;
            _selectedIntendedTarget = upgraded;
            DxAssist.BestTarget = upgraded;
            _lastCorrectiveAction = $"Switched to fresh acquisition source for {upgraded.Callsign}";
            AddAction($"Locked target source upgraded for {upgraded.Callsign}: using fresh {decode.MessageTypeText} '{decode.RawText}' instead of stale/progress source.");
            UpdateBestTarget(upgraded);
            UpdateHuntStateDisplay();
            await SendReplyAsync(upgraded, countAttempt: false);
            _lastSelectionNudgeAt = DateTime.Now;
        }
        finally
        {
            _wantedSniperBusy = false;
        }
    }

    private static bool IsInitialAcquisitionMessage(DecodeMessage decode)
    {
        return decode.MessageType is Ft8MessageType.Cq or Ft8MessageType.DirectedGrid;
    }

    private void LogWantedSniperNoTarget()
    {
        if (DateTime.Now - _lastWantedSniperNoTargetLogAt < TimeSpan.FromSeconds(10))
            return;

        _lastWantedSniperNoTargetLogAt = DateTime.Now;
        var items = Wanted.WantedDxcc
            .Concat(Wanted.WantedStates)
            .Concat(Wanted.WantedGrids)
            .ToList();
        var wantedCount = items.Count;
        var actionableCount = items.Count(item => item.IsActionable);
        var sampleBlocked = items
            .Where(item => !item.IsActionable)
            .Take(3)
            .Select(item => $"{item.ContactableCall}: {item.NotActionableReason}")
            .ToList();
        var detail = sampleBlocked.Count == 0
            ? ""
            : " Blocked examples: " + string.Join("; ", sampleBlocked) + ".";

        AddAction($"Wanted Sniper active: {wantedCount} wanted rows, {actionableCount} actionable.{detail}");
    }

    private async Task SendReplyAsync(
        DxTarget target,
        bool countAttempt = true,
        bool allowDuringTransmit = false,
        bool confirmedTransmitMismatch = false,
        bool preserveLockOnFailure = false)
    {
        if (!IsFreshTarget(target))
        {
            AddAction($"Selection blocked for {target.Callsign}: source decode is stale ({FormatAge(DateTime.Now - target.Decode.ReceivedAt)} old).");
            if (ReferenceEquals(_lockedTarget, target)
                && _huntState == HuntState.Calling
                && !_targetConfirmedInJtdx
                && !_targetSelectionInProgress
                && !_immediateTxRetargetInProgress)
            {
                await AbandonStaleCallingTargetAsync(
                    $"Target recovery ended: {target.Callsign} is no longer confirmed by JTDX and its source decode is stale");
            }
            return;
        }

        if (_immediateTxRetargetInProgress && !confirmedTransmitMismatch)
        {
            AddAction($"Ordinary selection request for {target.Callsign} ignored while immediate in-slot correction owns target selection.");
            return;
        }

        if (_lockedTarget?.Callsign.Equals(target.Callsign, StringComparison.OrdinalIgnoreCase) == true
            && _udpListener.LastStatus?.Transmitting == true
            && !allowDuringTransmit)
        {
            EnsureTargetAcquisitionTxOff($"Selection of {target.Callsign} deferred while JTDX is transmitting");
            QueueReplyWhenIdle($"selection of {target.Callsign} deferred while JTDX is transmitting");
            _recoveryMode = "WaitingForJtdxIdle";
            _lastCorrectiveAction = $"Waiting for RX before selecting {target.Callsign}";
            AddAction($"Selection of {target.Callsign} deferred until RX because JTDX is transmitting; no UDP Reply or GUI row-click attempt was consumed.");
            UpdateHuntStateDisplay();
            return;
        }

        if (_targetSelectionInProgress)
        {
            AddAction($"Selection request for {target.Callsign} ignored because another target selection is still being completed.");
            return;
        }

        var endpoint = _udpListener.LastSenderEndpoint
            ?? new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Settings.Settings.UdpReplyFallbackPort);
        var destinationAppId = !string.IsNullOrWhiteSpace(target.Decode.SourceAppId)
            ? target.Decode.SourceAppId
            : !string.IsNullOrWhiteSpace(_udpListener.LastAppId)
                ? _udpListener.LastAppId
                : Settings.Settings.UdpAppId;
        var fallbackEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Settings.Settings.UdpReplyFallbackPort);
        var sendFallback = endpoint.Port != fallbackEndpoint.Port || !System.Net.IPAddress.IsLoopback(endpoint.Address);
        var usesGuiSelection = !ShouldUseUdpReplyForSource(target.Decode);
        var guiSourceKey = usesGuiSelection ? ReplySourceKey(target.Decode) : "";
        var guiClickCountBefore = usesGuiSelection ? GuiSelectionClickCount(guiSourceKey) : 0;
        var maxGuiClicks = MaxGuiSelectionClicks();
        var guiCorrectionAuthorised = usesGuiSelection
            && (confirmedTransmitMismatch && ReferenceEquals(_lockedTarget, target)
                || GuiCorrectionIsAuthorised(target));
        if (usesGuiSelection
            && !CanAttemptGuiSelection(guiClickCountBefore, maxGuiClicks, guiCorrectionAuthorised))
        {
            if (guiClickCountBefore >= maxGuiClicks)
            {
                if (preserveLockOnFailure)
                {
                    _targetConfirmedInJtdx = false;
                    StartBoundedTargetRecovery();
                    _jtdxShowsWrongTx = true;
                    _lastCorrectiveAction = $"Immediate correction exhausted for {target.Callsign}";
                    AddAction($"Immediate correction could not reload {target.Callsign}: all {maxGuiClicks} safe GUI double-clicks for this source have already been used. Target lock retained; TX is being stopped because no safe correction remains.");
                    EnsureEnableTxOff($"No safe immediate correction remains for {target.Callsign}");
                    return;
                }

                AddAction($"GUI selection source exhausted for {target.Callsign}: {guiClickCountBefore}/{maxGuiClicks} real double-clicks were made. The exact source will retry after one receive period if its row remains visible; otherwise it will wait for a newer decode.");
                await FailCurrentReplySourceAndRetargetAsync(
                    $"GUI selection source exhausted for {target.Callsign} after {guiClickCountBefore}/{maxGuiClicks} real double-clicks");
                return;
            }

            AddAction($"GUI re-click deferred for {target.Callsign}: one real double-click has already been made and JTDX has not supplied a fresh RX Status showing the target is wrong or cleared.");
            return;
        }

        SelectionResult selection;
        var selectionCancellation = _targetSelectionCancellation;
        _targetSelectionInProgress = true;
        try
        {
            selection = await _selectionController.SelectTargetAsync(
                target,
                Settings.Settings,
                endpoint,
                fallbackEndpoint,
                destinationAppId,
                sendFallback,
                selectionCancellation?.Token ?? CancellationToken.None,
                confirmedTransmitMismatch ? TimeSpan.FromSeconds(4) : null,
                action =>
                {
                    if (action.SelectionMethod == JtdxSelectionMethod.GuiGridDoubleClick)
                    {
                        AddAction(
                            $"GUI double-click issued for {target.Callsign}: row {action.ScreenRowIndex?.ToString() ?? "n/a"}, "
                            + $"coordinates {action.ClickX?.ToString() ?? "n/a"},{action.ClickY?.ToString() ?? "n/a"}; "
                            + "waiting up to 4 seconds for a fresh JTDX DX Call confirmation.");
                    }
                    else
                    {
                        AddAction($"UDP Reply issued for {target.Callsign}; waiting for fresh JTDX DX Call confirmation.");
                    }
                },
                forceGuiGridClick: usesGuiSelection);
        }
        finally
        {
            _targetSelectionInProgress = false;
        }

        var guiClickCount = guiClickCountBefore;
        if (usesGuiSelection && selection.SelectionActionAt.HasValue)
        {
            guiClickCount++;
            _guiSelectionClickCounts[guiSourceKey] = guiClickCount;
            _guiSelectionLastClickAt[guiSourceKey] = selection.SelectionActionAt.Value;
            AddAction($"GUI selection real double-click {guiClickCount}/{maxGuiClicks} for {target.Callsign}: row {selection.ScreenRowIndex?.ToString() ?? "n/a"}, coordinates {selection.ClickX?.ToString() ?? "n/a"},{selection.ClickY?.ToString() ?? "n/a"}.");
        }

        if (selectionCancellation?.IsCancellationRequested == true || !ReferenceEquals(_lockedTarget, target))
        {
            AddAction($"Discarded completed selection result for {target.Callsign} because that target is no longer locked.");
            EnsureTargetAcquisitionTxOff($"Discarded obsolete selection of {target.Callsign}");

            var replacement = _lockedTarget;
            if (replacement != null
                && _huntState == HuntState.Calling
                && !_targetConfirmedInJtdx
                && !_pendingLockedReplyWhenIdle)
            {
                await SendReplyAsync(replacement, countAttempt: false);
            }
            return;
        }

        _lastReplyAt = DateTime.Now;
        if (countAttempt)
            RecordAcquisitionAttempt(target);
        else if (_lockedTarget?.Callsign.Equals(target.Callsign, StringComparison.OrdinalIgnoreCase) == true && !_targetConfirmedInJtdx)
            RecordAcquisitionAttempt(target);

        DxAssist.SelectionMethodText = $"Selection Method: {selection.SelectionMethod}";
        DxAssist.GuiSelectionStatus = selection.SelectionMethod == JtdxSelectionMethod.GuiGridDoubleClick
            ? (selection.Success
                ? $"GUI Selection confirmed: row {selection.ScreenRowIndex}, click {selection.ClickX},{selection.ClickY}, DX Call {selection.JtdxDxCallAfter}."
                : $"GUI Selection failed: {selection.FailureText}")
            : "GUI Selection: not used for CQ/UDP target.";

        AddAction($"Selection attempt: raw '{selection.TargetRawMessage}', expected {selection.ExpectedCall}, type {selection.MessageType}, method {selection.SelectionMethod}, model v{selection.VisibleRowModelVersion}, calibration {selection.CalibrationVersion}, row {selection.ScreenRowIndex?.ToString() ?? "n/a"}, click {selection.ClickX?.ToString() ?? "n/a"},{selection.ClickY?.ToString() ?? "n/a"}, before DX '{selection.JtdxDxCallBefore}', after DX '{selection.JtdxDxCallAfter}', action {selection.SelectionActionAt?.ToString("HH:mm:ss.fff") ?? "none"}, fresh confirmation status {selection.ConfirmationStatusReceivedAt?.ToString("HH:mm:ss.fff") ?? "none"}, success {selection.Success}, failure {selection.FailureReason}.");

        if (!selection.Success)
        {
            _lastCorrectiveAction = selection.FailureText;
            AddAction($"{selection.SelectionMethod} selection failed for {target.Callsign}: {selection.FailureText}. TX remains blocked until JTDX confirms the expected DX Call.");
            if (preserveLockOnFailure)
            {
                if (_targetConfirmedInJtdx
                    && string.IsNullOrWhiteSpace(_allTxtAwaitingCorrectionCall))
                {
                    _recoveryMode = "ImmediateInSlotRetarget";
                    _lastCorrectiveAction = $"ALL.TXT confirmed immediate correction to {target.Callsign}";
                    AddAction($"UDP confirmation timed out after the immediate reload of {target.Callsign}, but JTDX ALL.TXT independently confirmed the corrected transmission. TX remains enabled.");
                    UpdateHuntStateDisplay();
                    return;
                }

                _targetConfirmedInJtdx = false;
                StartBoundedTargetRecovery();
                _jtdxShowsWrongTx = true;
                _recoveryMode = "ImmediateTransmitCorrectionFailed";
                AddAction($"Immediate in-slot correction for {target.Callsign} was not confirmed. Target lock retained without suppression; TX is being stopped because the intended target could not be safely reloaded.");
                EnsureEnableTxOff($"Immediate correction not confirmed for {target.Callsign}");
                QueueReplyWhenIdle($"immediate correction for {target.Callsign} was not confirmed; bounded RX recovery required");
                UpdateHuntStateDisplay();
                return;
            }

            if (usesGuiSelection
                && selection.SelectionActionAt.HasValue
                && IsRetriableGuiSelectionFailure(selection.FailureReason)
                && guiClickCount < maxGuiClicks)
            {
                _targetConfirmedInJtdx = false;
                StartBoundedTargetRecovery();
                _recoveryMode = "Locked Target Recovery";
                _lastCorrectiveAction = $"GUI click {guiClickCount}/{maxGuiClicks} not confirmed for {target.Callsign}; waiting for fresh RX evidence before retry";
                AddAction($"GUI click {guiClickCount}/{maxGuiClicks} did not secure {target.Callsign}. Lock retained; another physical double-click is permitted after JTDX freshly reports a wrong or cleared DX Call while in RX.");
                UpdateHuntStateDisplay();
                return;
            }

            if (_lockedTarget?.Callsign.Equals(target.Callsign, StringComparison.OrdinalIgnoreCase) == true
                && _huntState == HuntState.Calling
                && !_targetConfirmedInJtdx)
            {
                _failedReplySources[ReplySourceKey(target.Decode)] = DateTime.Now;
                await ReleaseLockedTargetAndMaybeResumeAsync(
                    usesGuiSelection && selection.SelectionActionAt.HasValue
                        ? $"GUI selection source failed for {target.Callsign} after {guiClickCount}/{maxGuiClicks} real double-clicks: {selection.FailureText}"
                        : $"Selection failed for {target.Callsign}: {selection.FailureText}",
                    usesGuiSelection && selection.SelectionActionAt.HasValue
                        ? "Abandoned - GUI source exhausted"
                        : "Abandoned - selection failed",
                    suppress: false,
                    resumeSniper: true);
            }
            return;
        }

        _targetConfirmedInJtdx = true;
        _unconfirmedRecoveryStartedAt = DateTime.MinValue;
        ResetWrongTargetState();
        _lastCorrectiveAction = selection.SelectionMethod == JtdxSelectionMethod.GuiGridDoubleClick
            ? $"GUI grid double-click confirmed for {target.Callsign}"
            : $"UDP Reply confirmed for {target.Callsign}";
        AddAction($"{selection.SelectionMethod} selection confirmed for {target.Callsign}. {selection.Details}");
        ArmEnableTxForSelectedTarget($"{selection.SelectionMethod} selection confirmed");
    }

    private int MaxGuiSelectionClicks()
    {
        return Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles);
    }

    private int GuiSelectionClickCount(string sourceKey)
    {
        return _guiSelectionClickCounts.TryGetValue(sourceKey, out var count) ? count : 0;
    }

    private bool GuiCorrectionIsAuthorised(DxTarget target)
    {
        var status = _udpListener.LastStatus;
        var sourceKey = ReplySourceKey(target.Decode);
        return _lockedTarget != null
            && ReferenceEquals(_lockedTarget, target)
            && _huntState == HuntState.Calling
            && !_targetConfirmedInJtdx
            && _jtdxShowsWrongTx
            && status != null
            && !status.Transmitting
            && _guiSelectionLastClickAt.TryGetValue(sourceKey, out var lastClickAt)
            && IsFreshWrongTargetStatusForGuiCorrection(status, target.Callsign, lastClickAt);
    }

    private static bool IsFreshWrongTargetStatusForGuiCorrection(
        JtdxStatusMessage status,
        string expectedCall,
        DateTime lastClickAt)
    {
        return status.ReceivedAt >= lastClickAt
            && !status.DxCall.Equals(expectedCall, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanAttemptGuiSelection(
        int completedClicks,
        int maximumClicks,
        bool correctionAuthorised)
    {
        return completedClicks <= 0
            || correctionAuthorised && completedClicks < Math.Max(1, maximumClicks);
    }

    private static bool IsRetriableGuiSelectionFailure(SelectionFailureReason failureReason)
    {
        return failureReason is SelectionFailureReason.ConfirmationTimedOut
            or SelectionFailureReason.JtdxSelectedWrongCall;
    }

    private async Task LockAndReplyAsync(DxTarget target, string source, string wantedReason, string sourceBlock)
    {
        if (!RadioContextReadyForSelection())
        {
            AddAction($"{source} target {target.Callsign} ignored while JTDX rows are settling after a frequency/band/mode change.");
            return;
        }

        if (!IsFreshTarget(target))
        {
            AddAction($"{source} target {target.Callsign} ignored: source decode is stale ({FormatAge(DateTime.Now - target.Decode.ReceivedAt)} old).");
            if (DxAssist.BestTarget?.Callsign.Equals(target.Callsign, StringComparison.OrdinalIgnoreCase) == true)
            {
                DxAssist.BestTarget = null;
                Dashboard.BestTarget = "No fresh target selected.";
                Dashboard.BestReason = "";
            }
            return;
        }

        _selectedIntendedTarget = target;
        _lockedTarget = target;
        _targetSelectionCancellation?.Cancel();
        _targetSelectionCancellation?.Dispose();
        _targetSelectionCancellation = new CancellationTokenSource();
        DxAssist.BestTarget = target;
        _huntState = HuntState.Calling;
        _targetStartedAt = DateTime.Now;
        _targetStartedUtc = DateTime.UtcNow;
        _lastReplyAt = DateTime.MinValue;
        _lastCallAttemptAt = DateTime.MinValue;
        _lastSelectionNudgeAt = DateTime.MinValue;
        _lastAcquisitionAttemptAt = DateTime.MinValue;
        _unconfirmedRecoveryStartedAt = DateTime.MinValue;
        _targetConfirmationWaitUntil = DateTime.Now.AddSeconds(3);
        _lastQsoProgressAt = DateTime.MinValue;
        _lastTxMismatchCycleAt = DateTime.MinValue;
        _lastReportRepeatCycleKey = "";
        _lastObservedTransmitState = "Unknown";
        _txVerificationState = "Unknown";
        _recoveryMode = "Locked Target Recovery";
        _lastCorrectiveAction = "Selection sent to target";
        _lastObservedQsoMessage = "";
        _lastObservedTxMessage = "Unknown";
        _lastObservedTxCycleTime = "";
        _lastIntendedTxMessage = BuildInitialCallMessage(target.Callsign);
        _lastExpectedQsoStage = FormatQsoStage(QsoStage.CallingInitial);
        _lastProgressMessageFromTarget = "";
        _lastProgressTime = DateTime.MinValue;
        _lastRepeatedStage = "";
        _lastStageChangeAt = DateTime.Now;
        _stuckReason = "";
        _targetConfirmedInFeed = false;
        _targetConfirmedInJtdx = false;
        _unconfirmedRecoveryStartedAt = DateTime.MinValue;
        _jtdxShowsWrongTx = false;
        ResetWrongTargetState();
        _qsoStage = QsoStage.CallingInitial;
        _callAttemptCount = 0;
        _acquisitionAttemptCount = 0;
        _reportAttemptCount = 0;
        _txMismatchCycleCount = 0;
        _completionGraceCycleCount = 0;
        _lastCallAttemptCycleKey = "";
        _lastCompletionGraceCycleKey = "";
        _myFinal73SeenDuringCompletion = false;
        _completionPendingStartedAt = DateTime.MinValue;
        _targetSource = source;
        _wantedReason = wantedReason;
        _wantedSourceBlock = sourceBlock;
        TrackOpportunitySelected(target, source.Contains("Manual", StringComparison.OrdinalIgnoreCase));
        UpdateBestTarget(target);
        UpdateHuntStateDisplay();
        AddAction($"{source} target locked {target.Callsign} on {target.Decode.Band} {target.Decode.Mode}: {wantedReason}.");
        AddAction($"Reply source selected: {target.Decode.RawText}, age {FormatAge(DateTime.Now - target.Decode.ReceivedAt)}, offset {target.Decode.AudioOffset?.ToString() ?? "unknown"}.");
        EnsureTargetAcquisitionTxOff($"{source} target acquisition");

        if (_udpListener.LastStatus?.Transmitting == true)
        {
            QueueReplyWhenIdle($"{source} initial selection deferred until RX");
            _recoveryMode = "WaitingForJtdxIdle";
            _lastCorrectiveAction = $"Waiting for RX before selecting {target.Callsign}";
            AddAction($"JTDX is transmitting; {target.Callsign} is locked and its first selection is queued for RX. No GUI row-click attempt was consumed.");
            UpdateHuntStateDisplay();
            return;
        }

        await SendReplyAsync(target, countAttempt: false);
        _lastCallAttemptAt = DateTime.Now;
        if (_autoResume.IsRunning)
            ArmEnableTxForSelectedTarget(source);
    }

    private void ArmEnableTxForSelectedTarget(string source)
    {
        if (_lockedTarget == null)
            return;

        var settings = Settings.Settings;
        var (greyPct, redPct) = _pixels.GetEnableTxStats(
            settings.EnableTxX,
            settings.EnableTxY,
            settings.BoxRadius,
            settings.EnableTxOffRgb,
            settings.EnableTxOnRgb,
            settings.Tolerance);
        var looksOff = greyPct >= settings.MinGreyPercent && redPct <= settings.MaxRedPercent;
        Dashboard.PixelState = looksOff
            ? $"Enable TX looks OFF: grey {greyPct}% / red {redPct}%"
            : $"Enable TX active or unknown: grey {greyPct}% / red {redPct}%";

        var currentDxCall = (_udpListener.LastStatus?.DxCall ?? _actualJtdxDxCall).Trim();
        if (!string.IsNullOrWhiteSpace(currentDxCall)
            && !_lockedTarget.Callsign.Equals(currentDxCall, StringComparison.OrdinalIgnoreCase))
        {
            _targetConfirmedInJtdx = false;
            StartBoundedTargetRecovery();
            _jtdxShowsWrongTx = true;
            _observedWrongTargetCall = currentDxCall;
            _lastCorrectiveAction = $"Enable TX blocked because JTDX DX Call is {currentDxCall}, not {_lockedTarget.Callsign}";
            AddAction($"{source}: Enable TX blocked; JTDX DX Call is {currentDxCall}, expected {_lockedTarget.Callsign}.");
            if (!looksOff && DateTime.Now - _lastForcedTxOffAt >= TimeSpan.FromSeconds(4))
            {
                _lastForcedTxOffAt = DateTime.Now;
                _clicker.MoveClickRestore(settings.EnableTxX, settings.EnableTxY);
                AddAction($"{source}: clicked Enable TX off because JTDX is aimed at {currentDxCall}, not {_lockedTarget.Callsign}.");
            }
            return;
        }

        if (!looksOff)
            return;
        if (!ShouldClickEnableTxRecovery())
        {
            _lastCorrectiveAction = $"Waiting for JTDX to accept {_lockedTarget.Callsign} before enabling TX";
            AddAction($"{source}: Enable TX is off, but JTDX has not confirmed {_lockedTarget.Callsign}; not enabling TX yet.");
            return;
        }
        if (DateTime.Now - _lastEnableTxArmAt < TimeSpan.FromSeconds(2))
            return;

        _clicker.MoveClickRestore(settings.EnableTxX, settings.EnableTxY);
        _lastEnableTxArmAt = DateTime.Now;
        _lastCorrectiveAction = $"Enable TX armed for {_lockedTarget.Callsign}";
        AddAction($"{source}: Enable TX was off; clicked Enable TX for {_lockedTarget.Callsign}.");
    }

    private void EnsureTargetAcquisitionTxOff(string source)
    {
        if (_lockedTarget == null || _targetConfirmedInJtdx || _huntState == HuntState.InQso)
            return;

        EnsureEnableTxOff(source, _udpListener.LastStatus?.TxEnabled == true);
    }

    private void EnsureEnableTxOff(string source, bool statusConfirmsEnabled = false)
    {
        var settings = Settings.Settings;
        var (greyPct, redPct) = _pixels.GetEnableTxStats(
            settings.EnableTxX,
            settings.EnableTxY,
            settings.BoxRadius,
            settings.EnableTxOffRgb,
            settings.EnableTxOnRgb,
            settings.Tolerance);
        var looksOff = greyPct >= settings.MinGreyPercent && redPct <= settings.MaxRedPercent;
        Dashboard.PixelState = looksOff
            ? $"Enable TX looks OFF: grey {greyPct}% / red {redPct}%"
            : $"Enable TX active or unknown: grey {greyPct}% / red {redPct}%";

        if ((!statusConfirmsEnabled && looksOff)
            || DateTime.Now - _lastForcedTxOffAt < TimeSpan.FromSeconds(4))
            return;

        _lastForcedTxOffAt = DateTime.Now;
        _clicker.MoveClickRestore(settings.EnableTxX, settings.EnableTxY);
        AddAction($"{source}: clicked Enable TX off.");
    }

    private bool RecordCallAttempt(string cycleKey = "")
    {
        if (_huntState != HuntState.Calling)
            return false;

        if (!string.IsNullOrWhiteSpace(cycleKey))
        {
            if (cycleKey.Equals(_lastCallAttemptCycleKey, StringComparison.Ordinal))
                return false;

            _lastCallAttemptCycleKey = cycleKey;
        }

        _callAttemptCount++;
        _lastCallAttemptAt = DateTime.Now;
        TrackOpportunityAttempt(_lockedTarget);
        UpdateHuntStateDisplay();
        return true;
    }

    private void RecordAcquisitionAttempt(DxTarget target)
    {
        if (_lockedTarget == null
            || _huntState != HuntState.Calling
            || _targetConfirmedInJtdx
            || !target.Callsign.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (DateTime.Now - _lastAcquisitionAttemptAt < TimeSpan.FromSeconds(2))
        {
            _lastCorrectiveAction = $"UDP Reply sent; awaiting JTDX confirmation for {target.Callsign}";
            return;
        }

        _acquisitionAttemptCount++;
        _lastAcquisitionAttemptAt = DateTime.Now;
        _lastCorrectiveAction = $"UDP Reply sent; awaiting JTDX confirmation for {target.Callsign}";
        AddAction($"UDP Reply sent for {target.Callsign}; awaiting JTDX confirmation. Acquisition attempts {_acquisitionAttemptCount}/{Math.Max(1, Settings.Settings.MaxUdpReplyNudgesBeforeConfirmed)}.");
    }

    private bool AcquisitionFailed()
    {
        if (_lockedTarget == null || _targetConfirmedInJtdx || _huntState != HuntState.Calling)
            return false;

        var maxNudges = Math.Max(1, Settings.Settings.MaxUdpReplyNudgesBeforeConfirmed);
        var maxCycles = Math.Max(1, Settings.Settings.MaxTargetAcquisitionCycles);
        var elapsed = DateTime.Now - _targetStartedAt;
        var boundedRecoveryExpired = _unconfirmedRecoveryStartedAt != DateTime.MinValue
            && DateTime.Now - _unconfirmedRecoveryStartedAt
                >= TimeSpan.FromTicks(ActiveReceivePeriod().Ticks * maxCycles);
        return boundedRecoveryExpired
            || (_acquisitionAttemptCount >= maxNudges
                && elapsed >= TimeSpan.FromTicks(ActiveReceivePeriod().Ticks * maxCycles));
    }

    private void StartBoundedTargetRecovery()
    {
        if (_unconfirmedRecoveryStartedAt == DateTime.MinValue)
            _unconfirmedRecoveryStartedAt = DateTime.Now;
    }

    private bool NoQsoProgressTimedOut()
    {
        if (_lockedTarget == null
            || _huntState != HuntState.Calling
            || _qsoStage != QsoStage.CallingInitial
            || _targetConfirmedInFeed
            || !_targetConfirmedInJtdx)
        {
            return false;
        }

        var timeout = TimeSpan.FromMinutes(Math.Max(1, Settings.Settings.CallTimeoutMinutes));
        return _targetStartedAt != DateTime.MinValue
            && DateTime.Now - _targetStartedAt >= timeout;
    }

    private bool InQsoNoProgressTimedOut()
    {
        if (_lockedTarget == null
            || _huntState != HuntState.InQso
            || _qsoStage == QsoStage.CompletionPending)
        {
            return false;
        }

        var lastProgress = _lastQsoProgressAt != DateTime.MinValue
            ? _lastQsoProgressAt
            : _targetStartedAt;
        if (lastProgress == DateTime.MinValue)
            return false;

        return DateTime.Now - lastProgress
            >= TimeSpan.FromMinutes(Math.Max(1, Settings.Settings.CallTimeoutMinutes));
    }

    private async Task<bool> ReleaseIfManualTxOffAsync(JtdxStatusMessage status)
    {
        if (_lockedTarget == null
            || !_targetSource.Equals("Wanted Sniper", StringComparison.OrdinalIgnoreCase)
            || _huntState != HuntState.Calling
            || _qsoStage != QsoStage.CallingInitial
            || !_targetConfirmedInJtdx
            || _targetConfirmedInFeed
            || status.Transmitting
            || status.TxEnabled)
        {
            _manualTxOffDetectedAt = DateTime.MinValue;
            return false;
        }

        if (_manualTxOffDetectedAt == DateTime.MinValue)
        {
            _manualTxOffDetectedAt = DateTime.Now;
            AddAction($"Wanted Sniper: TX is off while {_lockedTarget.Callsign} is selected; waiting one cycle before abandoning.");
            return false;
        }

        if (DateTime.Now - _manualTxOffDetectedAt < ActiveAttemptCycle())
            return false;

        await ReleaseLockedTargetAndMaybeResumeAsync(
            $"Target abandoned: {_lockedTarget.Callsign} - TX stopped manually",
            "Abandoned - TX stopped",
            suppress: true,
            resumeSniper: true);
        return true;
    }

    private async Task ReleaseLockedTargetAndMaybeResumeAsync(string releaseMessage, string outcome, bool suppress, bool resumeSniper)
    {
        if (_lockedTarget == null)
            return;

        var releasedCall = _lockedTarget.Callsign;
        AddAction($"{releaseMessage}.");
        if (suppress)
            SuppressTarget(releasedCall);

        ClearLockedTarget($"{outcome}: {releaseMessage}.");

        if (!resumeSniper || CurrentWantedSniperMode() != WantedSniperMode.Active)
            return;

        ExpireWantedItems();
        AddAction($"Wanted Sniper resuming scan after releasing {releasedCall}.");

        var next = SelectWantedSniperTarget();
        if (next == null)
        {
            AddAction($"No actionable wanted targets after releasing {releasedCall}.");
            EnsureEnableTxOff("Wanted Sniper release");
            UpdateHuntStateDisplay();
            return;
        }

        AddAction($"Wanted Sniper next target: {next.ContactableCall} - {next.WantedDetail}.");
        await TryWantedSniperAsync();
    }

    private async Task FailCurrentReplySourceAndRetargetAsync(string reason)
    {
        if (_lockedTarget == null)
            return;

        var failed = _lockedTarget;
        _failedReplySources[ReplySourceKey(failed.Decode)] = DateTime.Now;
        AddAction($"{reason}.");
        AddAction($"Reply source failed: {failed.Decode.RawText}. Candidate {failed.Callsign} remains eligible if heard again, or if this exact row remains visible after one receive period.");
        ClearLockedTarget($"No usable confirmed reply from current source for {failed.Callsign}; retargeting.");

        if (CurrentWantedSniperMode() == WantedSniperMode.Active)
        {
            await TryWantedSniperAsync();
            UpdateHuntStateDisplay();
            return;
        }

        if (_operatingMode == HuntingOperatingMode.LocationHunt)
        {
            var locationTarget = SelectLocationHuntTarget();
            if (locationTarget == null)
            {
                AddAction("No usable Location Hunt reply source remains; waiting for a fresh decode.");
                EnsureEnableTxOff("Location Hunt retarget");
                UpdateHuntStateDisplay();
                return;
            }

            AddAction($"Location Hunt retargeting to {locationTarget.Callsign}.");
            await LockAndReplyAsync(locationTarget, "Location Hunt retarget", locationTarget.PrimaryReason, Location.SelectedAreasDisplay);
            return;
        }

        var next = SelectNextAutomatedTarget();
        if (next == null)
        {
            AddAction("No usable reply source remains; waiting for a fresh decode.");
            UpdateHuntStateDisplay();
            return;
        }

        AddAction($"Retargeting to next candidate: {next.Callsign}.");
        await LockAndReplyAsync(next, "Auto-ranked retarget", next.PrimaryReason, "");
    }

    private async Task AbandonStaleCallingTargetAsync(string reason)
    {
        if (_lockedTarget == null)
            return;

        _pendingLockedReplyWhenIdle = false;
        _pendingLockedReplyReason = "";
        EnsureEnableTxOff(reason, _udpListener.LastStatus?.TxEnabled == true);
        await FailCurrentReplySourceAndRetargetAsync(reason);
    }

    private bool IsFailedReplySource(DecodeMessage decode)
    {
        ExpireFailedReplySources();
        var sourceKey = ReplySourceKey(decode);
        if (!_failedReplySources.TryGetValue(sourceKey, out var failedAt))
            return false;

        if (!CanRearmVisibleFailedReplySource(decode, failedAt))
            return true;

        _failedReplySources.Remove(sourceKey);
        _guiSelectionClickCounts.Remove(sourceKey);
        _guiSelectionLastClickAt.Remove(sourceKey);
        _forceGuiSelectionSources.Add(sourceKey);
        AddAction(
            $"Reply source re-armed for {DecodeTargetCall(decode)}: the exact failed row is still visible. "
            + "The next attempt will use one controlled JTDX grid double-click.");
        return false;
    }

    private bool CanRearmVisibleFailedReplySource(DecodeMessage decode, DateTime failedAt)
    {
        return IsFreshDecode(decode)
            && _visibleRowModel.FindDecode(decode) != null
            && DateTime.Now - failedAt >= ActiveReceivePeriod();
    }

    private bool ShouldUseUdpReplyForSource(DecodeMessage decode)
    {
        return JtdxSelectionController.ShouldUseUdpReply(decode)
            && !_forceGuiSelectionSources.Contains(ReplySourceKey(decode));
    }

    private void ExpireFailedReplySources()
    {
        var cutoff = DateTime.Now.AddSeconds(-NewDxccStaleSeconds());
        foreach (var item in _failedReplySources.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList())
            _failedReplySources.Remove(item);
    }

    private static string ReplySourceKey(DecodeMessage decode)
    {
        return $"{decode.Callsign}|{decode.RawText}|{decode.AudioOffset}|{decode.DecodeTime?.TotalMilliseconds}|{decode.ReceivedAt:O}";
    }

    private async Task ProcessAllTxtTransmissionAsync(JtdxOutgoingTransmission transmission)
    {
        if (!_autoResume.IsRunning)
            return;

        var expectedCall = _lockedTarget?.Callsign ?? "";
        var analysis = JtdxAllTxtMonitor.AnalyseMessage(
            transmission.Message,
            Settings.Settings.MyCallsign,
            expectedCall);
        var verb = transmission.IsRetransmitting ? "Retransmitting" : "Transmitting";
        AddAction($"JTDX ALL.TXT {verb}: {transmission.Message}");

        if (_lockedTarget == null)
        {
            if (analysis.Disposition is JtdxOutgoingMessageDisposition.Cq
                or JtdxOutgoingMessageDisposition.WrongTarget)
            {
                _txVerificationState = "Unauthorised transmission - no locked target";
                _lastCorrectiveAction = $"Stopped {transmission.Message}: no target is locked";
                AddAction($"JTDX transmitted '{transmission.Message}' while DX Pilot had no locked target. Clicking Enable TX off.");
                EnsureEnableTxOff("ALL.TXT detected transmission with no locked target");
            }
            return;
        }

        if (analysis.Disposition == JtdxOutgoingMessageDisposition.ExpectedTarget)
        {
            var correctionConfirmed = _allTxtAwaitingCorrectionCall.Equals(
                _lockedTarget.Callsign,
                StringComparison.OrdinalIgnoreCase);
            var correctionElapsed = _allTxtCorrectionRequestedAt == DateTime.MinValue
                ? TimeSpan.Zero
                : transmission.ObservedAt - _allTxtCorrectionRequestedAt;
            _targetConfirmedInJtdx = true;
            _unconfirmedRecoveryStartedAt = DateTime.MinValue;
            ResetWrongTargetState();
            _actualJtdxDxCall = _lockedTarget.Callsign;
            _txVerificationState = transmission.IsRetransmitting
                ? "Correct target confirmed by ALL.TXT retransmission"
                : "Correct target confirmed by ALL.TXT";
            _lastObservedTransmitState = $"{verb} actual message '{transmission.Message}'";
            _lastObservedTxMessage = transmission.Message;
            _lastObservedTxCycleTime = transmission.ObservedAt.ToString("HH:mm:ss");
            _allTxtAwaitingCorrectionCall = "";
            _allTxtCorrectionRequestedAt = DateTime.MinValue;

            var syntheticStatus = new JtdxStatusMessage
            {
                ReceivedAt = transmission.ObservedAt,
                SourceAppId = "JTDX ALL.TXT",
                DialFrequencyHz = _udpListener.LastStatus?.DialFrequencyHz ?? 0,
                Band = _udpListener.LastStatus?.Band ?? CurrentBand,
                Mode = transmission.Mode,
                TxMode = transmission.Mode,
                TrPeriodSeconds = _udpListener.LastStatus?.TrPeriodSeconds ?? 15,
                DxCall = _lockedTarget.Callsign,
                TxMessage = transmission.Message,
                TxEnabled = true,
                Transmitting = true,
                Decoding = false
            };
            ObserveMyTransmitCycle(syntheticStatus);
            if (correctionConfirmed)
            {
                _lastCorrectiveAction = $"Immediate correction confirmed: {transmission.Message}";
                AddAction(
                    $"Immediate in-slot correction confirmed by JTDX ALL.TXT: {verb} '{transmission.Message}'. "
                    + $"Lock on {_lockedTarget.Callsign} retained; confirmation arrived after {Math.Max(0, correctionElapsed.TotalMilliseconds):0} ms.");
            }
            UpdateHuntStateDisplay();
            return;
        }

        if (analysis.Disposition is not (JtdxOutgoingMessageDisposition.Cq
            or JtdxOutgoingMessageDisposition.WrongTarget))
        {
            AddAction($"JTDX ALL.TXT message could not be classified safely: '{transmission.Message}'. No automatic target change was made.");
            return;
        }

        _jtdxShowsWrongTx = true;
        _targetConfirmedInJtdx = false;
        StartBoundedTargetRecovery();
        _observedWrongTargetCall = analysis.ObservedTargetCall;
        _txVerificationState = analysis.Disposition == JtdxOutgoingMessageDisposition.Cq
            ? "CQ detected by ALL.TXT - correcting now"
            : $"Wrong target {analysis.ObservedTargetCall} detected by ALL.TXT - correcting now";
        var mismatch = analysis.Disposition == JtdxOutgoingMessageDisposition.Cq
            ? $"CQ '{transmission.Message}'"
            : $"wrong target {analysis.ObservedTargetCall} in '{transmission.Message}'";
        await ImmediatelyReloadLockedTargetAsync($"JTDX ALL.TXT detected {mismatch}");
    }

    private async Task ImmediatelyReloadLockedTargetAsync(string reason)
    {
        if (_lockedTarget == null)
        {
            EnsureEnableTxOff($"{reason}; no locked target");
            return;
        }

        if (_immediateTxRetargetInProgress)
        {
            AddAction($"Immediate target reload already running for {_lockedTarget.Callsign}; duplicate mismatch ignored.");
            return;
        }

        var lockedCall = _lockedTarget.Callsign;
        _immediateTxRetargetInProgress = true;
        _allTxtAwaitingCorrectionCall = lockedCall;
        _allTxtCorrectionRequestedAt = DateTime.Now;
        _pendingLockedReplyWhenIdle = false;
        _pendingLockedReplyReason = "";
        _wrongTargetNudgeSent = true;
        _recoveryMode = "ImmediateInSlotRetarget";
        try
        {
            // The ALL.TXT transmit line arrives while the just-finished receive
            // batch may still be reaching us over UDP. Wait briefly for that
            // stream to go quiet, then choose the source row from the final model.
            var settle = await WaitForImmediateRowModelSettleAsync();
            if (_lockedTarget == null
                || !_lockedTarget.Callsign.Equals(lockedCall, StringComparison.OrdinalIgnoreCase))
            {
                AddAction($"Immediate correction for {lockedCall} cancelled because the locked target changed while rows were settling.");
                return;
            }
            if (_targetConfirmedInJtdx && string.IsNullOrWhiteSpace(_allTxtAwaitingCorrectionCall))
            {
                AddAction($"Immediate correction for {lockedCall} required no further click: JTDX ALL.TXT confirmed the target while the row model was settling.");
                return;
            }

            var sourceDecode = FindFreshCallableDecodeForLockedTarget(_lockedTarget);
            if (sourceDecode == null)
            {
                _lastCorrectiveAction = $"Could not immediately reload {lockedCall}: no fresh selectable source";
                _recoveryMode = "ImmediateTransmitCorrectionUnavailable";
                AddAction($"{reason}. {lockedCall} remains locked, but no fresh selectable UDP/grid source is available. Clicking Enable TX off rather than allowing CQ/wrong-target transmission.");
                EnsureEnableTxOff($"No selectable source available to reload {lockedCall}");
                QueueReplyWhenIdle($"no selectable immediate source for {lockedCall}; make one bounded RX recovery attempt");
                UpdateHuntStateDisplay();
                return;
            }

            var usesUdpReply = ShouldUseUdpReplyForSource(sourceDecode);
            if (!usesUdpReply && !settle.Stable)
            {
                _lastCorrectiveAction = $"Immediate row model did not settle for {lockedCall}";
                _recoveryMode = "ImmediateTransmitRowsUnstable";
                AddAction($"Immediate correction for {lockedCall} waited {settle.Elapsed.TotalMilliseconds:0} ms, but the JTDX row model continued changing. No unsafe row click was made; clicking Enable TX off.");
                EnsureEnableTxOff($"Rows did not settle for immediate correction to {lockedCall}");
                QueueReplyWhenIdle($"row model was unstable during immediate correction for {lockedCall}; retry once settled in RX");
                UpdateHuntStateDisplay();
                return;
            }

            var recoveryTarget = _lockedTarget;
            if (!ReferenceEquals(sourceDecode, recoveryTarget.Decode))
            {
                var refreshed = _targetScorer.Score(
                    sourceDecode,
                    _logbook,
                    _adifMergeResult.Indexes,
                    _decodeHistory,
                    Settings.Settings);
                foreach (var existingReason in recoveryTarget.Reasons.AsEnumerable().Reverse())
                {
                    if (!refreshed.Reasons.Contains(existingReason, StringComparer.OrdinalIgnoreCase))
                        refreshed.Reasons.Insert(0, existingReason);
                }
                _lockedTarget = refreshed;
                _selectedIntendedTarget = refreshed;
                DxAssist.BestTarget = refreshed;
                recoveryTarget = refreshed;
            }

            var method = usesUdpReply ? "UDP Reply" : "GUI double-click";
            var settledRow = usesUdpReply ? null : _visibleRowModel.FindDecode(sourceDecode)?.ScreenRowIndex;
            _lastCorrectiveAction = $"Immediate {method} reload of {lockedCall}";
            AddAction(
                $"{reason}. Row model {(settle.Stable ? "settled" : "not required")} after {settle.Elapsed.TotalMilliseconds:0} ms at v{settle.Version}; "
                + $"re-resolved {lockedCall} from '{sourceDecode.RawText}'"
                + (settledRow.HasValue ? $" on row {settledRow.Value}" : "")
                + $". Immediately reloading by {method} during the current TX slot; Enable TX remains on.");
            await SendReplyAsync(
                recoveryTarget,
                countAttempt: false,
                allowDuringTransmit: true,
                confirmedTransmitMismatch: true,
                preserveLockOnFailure: true);
            _lastSelectionNudgeAt = DateTime.Now;
        }
        finally
        {
            _immediateTxRetargetInProgress = false;
        }
    }

    private async Task<(bool Stable, TimeSpan Elapsed, long Version)> WaitForImmediateRowModelSettleAsync()
    {
        var startedAt = DateTime.Now;
        var stableSince = startedAt;
        var observedVersion = _visibleRowModel.Version;
        var quietPeriod = TimeSpan.FromMilliseconds(650);
        var maximumWait = TimeSpan.FromMilliseconds(2200);

        while (DateTime.Now - startedAt < maximumWait)
        {
            await Task.Delay(50);
            var now = DateTime.Now;
            var currentVersion = _visibleRowModel.Version;
            if (currentVersion != observedVersion)
            {
                observedVersion = currentVersion;
                stableSince = now;
            }

            var lastActivity = _lastDecodePacketAt > stableSince ? _lastDecodePacketAt : stableSince;
            if (now - lastActivity >= quietPeriod)
                return (true, now - startedAt, observedVersion);
        }

        return (false, DateTime.Now - startedAt, _visibleRowModel.Version);
    }

    private async Task ProcessJtdxStatusForCurrentTargetAsync(JtdxStatusMessage status)
    {
        if (_lockedTarget == null)
        {
            PreventUnwantedCq(status);
            return;
        }

        if (!_autoResume.IsRunning)
        {
            ClearLockedTarget("DX Pilot stopped; clearing locked target.");
            return;
        }

        if (ActiveCallingTargetHasGoneStale())
        {
            var staleReason = KeepCallingActiveNewDxccUntilStale()
                ? $"New DXCC persistence ended: {_lockedTarget.Callsign} has gone stale"
                : $"Target became stale before QSO progress: {_lockedTarget.Callsign}";
            await AbandonStaleCallingTargetAsync(staleReason);
            return;
        }

        var targetCall = _lockedTarget.Callsign.Trim().ToUpperInvariant();
        _actualJtdxDxCall = status.DxCall.Trim();
        _lastObservedTransmitState = BuildObservedTransmitState(status);
        var statusMatchesTarget = status.DxCall.Equals(targetCall, StringComparison.OrdinalIgnoreCase);

        if (await HandleInQsoCqContradictionAsync(status))
            return;

        if (!_targetConfirmedInJtdx
            && !statusMatchesTarget
            && status.TxEnabled
            && !_immediateTxRetargetInProgress)
            EnsureEnableTxOff($"Target acquisition safety for {targetCall}", statusConfirmsEnabled: true);

        if (_targetSelectionInProgress)
        {
            _lastCorrectiveAction = $"Completing selection of {targetCall}";
            UpdateHuntStateDisplay();
            return;
        }

        if (_qsoStage == QsoStage.CompletionPending && _pendingLockedReplyWhenIdle)
        {
            _pendingLockedReplyWhenIdle = false;
            _pendingLockedReplyReason = "";
            AddThrottledCompletionLog($"Retarget blocked: QSO completion pending with {_lockedTarget.Callsign}.");
        }

        if (_pendingLockedReplyWhenIdle)
        {
            if (status.Transmitting)
            {
                if (PreventUnwantedCq(status))
                    return;

                _recoveryMode = "WaitingForJtdxIdle";
                _lastCorrectiveAction = $"Waiting for RX before selecting {targetCall}";
                UpdateHuntStateDisplay();
                return;
            }

            _pendingLockedReplyWhenIdle = false;
            var reason = string.IsNullOrWhiteSpace(_pendingLockedReplyReason) ? "queued correction" : _pendingLockedReplyReason;
            _pendingLockedReplyReason = "";
            _lastCorrectiveAction = $"Selecting {targetCall} on first RX status";
            AddAction($"JTDX entered RX; executing the queued selection of {targetCall} ({reason}).");
            await SendReplyAsync(_lockedTarget, countAttempt: false);
            _lastCallAttemptAt = DateTime.Now;
            _lastSelectionNudgeAt = DateTime.Now;
            UpdateHuntStateDisplay();
            return;
        }

        if (PreventUnwantedCq(status))
            return;

        if (!statusMatchesTarget)
        {
            if (LooksLikeCqOrWrongTarget(status, targetCall))
            {
                await HandleWrongTargetOrMismatchAsync(status, targetCall);
            }

            return;
        }

        if (_jtdxShowsWrongTx || _txMismatchCycleCount > 0)
            AddAction($"TX verification OK for {targetCall}; mismatch count reset.");

        ResetWrongTargetState();
        _txVerificationState = "OK";

        if (await ReleaseIfManualTxOffAsync(status))
            return;

        if (status.Transmitting)
            ObserveMyTransmitCycle(status);

        if (_targetConfirmedInJtdx)
        {
            UpdateHuntStateDisplay();
            return;
        }

        _targetConfirmedInJtdx = true;
        _unconfirmedRecoveryStartedAt = DateTime.MinValue;
        AddAction($"Target confirmed by JTDX Status DX Call = {_lockedTarget.Callsign}. TX gate may open.");
        UpdateHuntStateDisplay();
    }

    private bool PreventUnwantedCq(JtdxStatusMessage status)
    {
        var cq = LooksLikeCq(status.TxMessage);
        if (!cq || !status.TxEnabled)
            return false;

        if (_lockedTarget != null)
        {
            _ = ImmediatelyReloadLockedTargetAsync($"JTDX UDP Status detected CQ '{status.TxMessage}'");
            return true;
        }

        if (DateTime.Now - _lastForcedTxOffAt < TimeSpan.FromSeconds(5))
            return true;

        _lastForcedTxOffAt = DateTime.Now;
        _lastCorrectiveAction = "Forced Enable TX off: CQ detected with no locked target";
        _recoveryMode = "NoLockedTarget";
        AddAction($"Prevented unwanted CQ '{status.TxMessage}'; clicked Enable TX off because DX Pilot has no locked target.");
        _clicker.MoveClickRestore(Settings.Settings.EnableTxX, Settings.Settings.EnableTxY);
        return true;
    }

    private async Task<bool> HandleInQsoCqContradictionAsync(JtdxStatusMessage status)
    {
        if (_lockedTarget == null
            || _huntState != HuntState.InQso
            || _qsoStage == QsoStage.CompletionPending
            || !LooksLikeCq(status.TxMessage))
        {
            return false;
        }

        var targetCall = _lockedTarget.Callsign;
        _jtdxShowsWrongTx = true;
        _targetConfirmedInJtdx = false;
        StartBoundedTargetRecovery();
        _txVerificationState = "CQ during QSO - immediate correction";
        _recoveryMode = "ImmediateInSlotRetarget";
        _lastCorrectiveAction = $"Immediately reloading {targetCall} during CQ";
        _stuckReason = $"JTDX prepared CQ while DX Pilot still held an InQso lock for {targetCall}.";
        AddAction($"In-QSO CQ contradiction detected for {targetCall}: '{status.TxMessage}'. Immediately reloading the locked target without stopping TX or suppressing it.");
        await ImmediatelyReloadLockedTargetAsync($"In-QSO CQ contradiction for {targetCall}");
        return true;
    }

    private async Task NudgeLockedTargetAfterResumeAsync()
    {
        if (_lockedTarget == null)
        {
            var target = _selectedIntendedTarget ?? DxAssist.BestTarget;
            if (target != null && IsFreshDecode(target.Decode))
            {
                _recoveryMode = "WaitingForJtdxIdle";
                _selectedIntendedTarget = target;
                _lastCorrectiveAction = $"JTDX recovery found target {target.Callsign}; sent UDP Reply instead of CQ";
                AddAction($"JTDX calling/recovery while selected target {target.Callsign} exists; sending UDP Reply instead of CQ recovery.");
                await LockAndReplyAsync(target, "Auto-ranked recovery", target.PrimaryReason, "");
            }
            return;
        }

        _lastSelectionNudgeAt = DateTime.MinValue;
        _recoveryMode = "Locked Target Recovery";
        if (_huntState == HuntState.InQso)
        {
            if (LooksLikeCq(_udpListener.LastStatus?.TxMessage ?? "")
                || InQsoNoProgressTimedOut())
            {
                _targetConfirmedInJtdx = false;
                _lastCorrectiveAction = "Enable TX recovery blocked: CQ/no-progress contradiction during QSO";
                _recoveryMode = "InQsoCqSafety";
                EnsureEnableTxOff(
                    $"Blocked locked-target recovery for {_lockedTarget.Callsign}");
                AddAction(
                    $"Locked recovery blocked for {_lockedTarget.Callsign}: JTDX is on CQ or the QSO has no fresh progress. Enable TX was not re-armed.");
                UpdateHuntStateDisplay();
                return;
            }

            _lastCorrectiveAction = "Clicked Enable TX only; UDP Reply nudge skipped during QSO";
            AddAction($"Locked recovery: Enable TX clicked only; QSO is already in progress with {_lockedTarget.Callsign}, so original UDP Reply was not resent.");
        }
        else
        {
            if (StatusConfirmsTarget(_udpListener.LastStatus, _lockedTarget.Callsign))
            {
                _targetConfirmedInJtdx = true;
                _unconfirmedRecoveryStartedAt = DateTime.MinValue;
                ResetWrongTargetState();
                _lastCorrectiveAction = $"Locked recovery retained confirmed target {_lockedTarget.Callsign}";
                AddAction($"Locked recovery: JTDX already confirms {_lockedTarget.Callsign}; no redundant Reply or target reset was made.");
                _lastSelectionNudgeAt = DateTime.Now;
                UpdateHuntStateDisplay();
                return;
            }

            _targetConfirmedInJtdx = false;
            StartBoundedTargetRecovery();
            _jtdxShowsWrongTx = true;
            _lastCorrectiveAction = $"Clicked Enable TX only; sent UDP Reply nudge to {_lockedTarget.Callsign}";
            AddAction($"Locked recovery: Enable TX clicked only; nudging {_lockedTarget.Callsign} again.");
            await SendReplyAsync(_lockedTarget, countAttempt: false);
        }
        _lastSelectionNudgeAt = DateTime.Now;
        UpdateHuntStateDisplay();
    }

    private static bool StatusConfirmsTarget(JtdxStatusMessage? status, string targetCall)
    {
        return status != null
            && !string.IsNullOrWhiteSpace(targetCall)
            && status.DxCall.Trim().Equals(targetCall.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCqOrWrongTarget(JtdxStatusMessage status, string targetCall)
    {
        if (!string.IsNullOrWhiteSpace(status.DxCall)
            && !status.DxCall.Equals(targetCall, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tx = status.TxMessage.Trim();
        // JTDX commonly clears both fields during RX after a one-shot row
        // selection. For a locked Calling target that blank state is positive
        // evidence that the selection was lost, so recover before the next TX
        // slot can fall back to CQ.
        if (string.IsNullOrWhiteSpace(tx))
            return true;

        var upper = tx.ToUpperInvariant();
        return upper.StartsWith("CQ ", StringComparison.Ordinal)
            || upper.Equals("CQ", StringComparison.Ordinal)
            || !upper.Contains(targetCall, StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleWrongTargetOrMismatchAsync(JtdxStatusMessage status, string expectedCall)
    {
        if (_lockedTarget == null)
            return;

        var hadActuallyAttemptedTarget = HasActuallyAttemptedLockedTarget();
        var observedCall = ObservedWrongTargetCall(status, expectedCall);
        if (string.IsNullOrWhiteSpace(observedCall))
        {
            await HandleGenericTxMismatchAsync(status, expectedCall);
            return;
        }

        _jtdxShowsWrongTx = true;
        _targetConfirmedInJtdx = false;
        StartBoundedTargetRecovery();
        _observedWrongTargetCall = observedCall;
        _wrongTargetQsoProgress = HasReceivedQsoProgressFrom(observedCall) || HasRecentLiveQso(observedCall, DateTime.UtcNow.AddMinutes(-10));
        AddAction($"Wrong target detected: expected {expectedCall}, observed {observedCall}.");

        if (_wrongTargetQsoProgress)
        {
            _txVerificationState = "Wrong target - active QSO progress";
            AddAction($"Wrong target active QSO progress detected from {observedCall}; {Settings.Settings.WrongTargetActiveQsoPolicy}.");
            if (!Settings.Settings.AcceptIncomingCalls)
            {
                _lastCorrectiveAction = $"Rejected incoming/wrong-target QSO progress from {observedCall}";
                AddAction($"Incoming/wrong-target QSO from {observedCall} ignored because Accept incoming calls is off.");
                await ForceLockedTargetCorrectionAsync(status, expectedCall, $"incoming/wrong-target QSO {observedCall}");
            }
            else if (Settings.Settings.WrongTargetActiveQsoPolicy.Equals("AdoptAndMonitor", StringComparison.OrdinalIgnoreCase))
            {
                AdoptWrongTargetQso(observedCall, status);
            }
            else
            {
                _lastCorrectiveAction = $"Waiting while active wrong-target QSO progresses with {observedCall}";
            }

            UpdateHuntStateDisplay();
            return;
        }

        _txVerificationState = "Wrong target - no QSO progress";
        AddAction($"Observed wrong target has no received QSO progress from {observedCall}.");
        await ForceLockedTargetCorrectionAsync(status, expectedCall, $"wrong target {observedCall}");
        if (_lockedTarget == null
            || !_lockedTarget.Callsign.Equals(expectedCall, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!RecordWrongTargetNoProgressCycle(status))
        {
            UpdateHuntStateDisplay();
            return;
        }

        var max = Math.Max(1, Settings.Settings.MaxWrongTargetNoProgressCycles);
        AddAction($"Wrong target no-progress {_wrongTargetNoProgressCount}/{max}.");

        if (_wrongTargetNoProgressCount >= max)
        {
            _lastCorrectiveAction = $"Release and hunt: wrong target {observedCall} made no progress";
            _stuckReason = $"Wrong target with no QSO progress: expected {expectedCall}, observed {observedCall}.";
            AddAction($"Wrong target no-progress {_wrongTargetNoProgressCount}/{max}; releasing lock and returning to hunting.");
            if (!ShouldUseUdpReplyForSource(_lockedTarget.Decode))
            {
                _failedReplySources[ReplySourceKey(_lockedTarget.Decode)] = DateTime.Now;
                ClearLockedTarget($"GUI source could not secure {expectedCall} after bounded correction clicks. The source will retry after one receive period if its exact row remains visible; otherwise it will wait for a newer decode without suppressing the station.");
            }
            else if (hadActuallyAttemptedTarget)
            {
                SuppressTarget(_lockedTarget.Callsign);
                ClearLockedTarget($"Wrong target with no QSO progress after real target attempts: expected {expectedCall}, observed {observedCall}. Releasing lock and returning to hunting.");
            }
            else
            {
                ClearLockedTarget($"Wrong target before JTDX confirmed {expectedCall}; releasing without suppressing.");
            }

            await HuntTickAsync();
            return;
        }

        _lastCorrectiveAction = $"Correcting wrong target {observedCall} back to {expectedCall}";

        UpdateHuntStateDisplay();
    }

    private async Task HandleGenericTxMismatchAsync(JtdxStatusMessage status, string targetCall)
    {
        var hadActuallyAttemptedTarget = HasActuallyAttemptedLockedTarget();
        _targetConfirmedInJtdx = false;
        StartBoundedTargetRecovery();
        _jtdxShowsWrongTx = true;
        var isCqMismatch = LooksLikeCq(status.TxMessage);
        _txVerificationState = isCqMismatch ? "CQ mismatch - correcting" : "Mismatch";
        AddAction($"TX mismatch: expected target {targetCall} but detected {DescribeMismatch(status)}.");

        if (isCqMismatch && _huntState != HuntState.InQso)
        {
            _lastCorrectiveAction = status.Transmitting
                ? $"JTDX is transmitting CQ; immediately reloading {targetCall}"
                : $"JTDX was calling CQ; resent UDP Reply to {targetCall}";
            if (status.Transmitting)
            {
                AddAction($"JTDX is transmitting CQ while {targetCall} is locked; immediately reloading the target in the current TX slot.");
                await ImmediatelyReloadLockedTargetAsync($"CQ mismatch while transmitting '{status.TxMessage}'");
                UpdateHuntStateDisplay();
                return;
            }

            if (DateTime.Now - _lastSelectionNudgeAt >= TimeSpan.FromSeconds(5))
            {
                AddAction($"JTDX is calling CQ while {targetCall} is locked; resending UDP Reply and keeping target unsuppressed.");
                await SendReplyAsync(_lockedTarget!, countAttempt: false);
                ArmEnableTxForSelectedTarget("CQ mismatch recovery");
                _lastSelectionNudgeAt = DateTime.Now;
            }

            UpdateHuntStateDisplay();
            return;
        }

        if (RecordTransmitMismatchCycle(status))
        {
            if (_txMismatchCycleCount >= Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles))
            {
                _stuckReason = $"Wrong target correction failed {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)} - releasing {targetCall} and returning to hunting.";
                if (_lockedTarget != null
                    && !ShouldUseUdpReplyForSource(_lockedTarget.Decode))
                {
                    _failedReplySources[ReplySourceKey(_lockedTarget.Decode)] = DateTime.Now;
                    _lastCorrectiveAction = "Failed GUI decode source after bounded wrong-target corrections";
                    ClearLockedTarget($"GUI source could not secure {targetCall}. The source will retry after one receive period if its exact row remains visible; otherwise it will wait for a newer decode without suppressing the station.");
                }
                else if (hadActuallyAttemptedTarget)
                {
                    SuppressTarget(_lockedTarget!.Callsign);
                    _lastCorrectiveAction = "Suppressed target due to TX mismatch after real target attempts";
                    ClearLockedTarget("TX mismatch unresolved after real target attempts: suppressing/releasing target.");
                }
                else
                {
                    _lastCorrectiveAction = "Released target due to setup/TX mismatch before real attempt";
                    ClearLockedTarget("TX mismatch unresolved before JTDX confirmed the target; releasing without suppressing.");
                }

                await HuntTickAsync();
                return;
            }

            if (_huntState != HuntState.InQso)
            {
                await ForceLockedTargetCorrectionAsync(status, targetCall, "TX mismatch");
                if (_lockedTarget == null
                    || !_lockedTarget.Callsign.Equals(targetCall, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        UpdateHuntStateDisplay();
    }

    private async Task ForceLockedTargetCorrectionAsync(JtdxStatusMessage status, string targetCall, string reason)
    {
        if (_lockedTarget == null || _huntState == HuntState.InQso)
            return;

        if (status.Transmitting)
        {
            _wrongTargetNudgeSent = true;
            _lastCorrectiveAction = $"Immediate target reload to {targetCall}";
            AddAction($"JTDX is transmitting the wrong target/CQ while {targetCall} is locked; immediately reloading the target in this TX slot.");
            await ImmediatelyReloadLockedTargetAsync($"{reason}; observed '{status.TxMessage}' / DX Call {status.DxCall}");
            return;
        }

        if (DateTime.Now - _lastSelectionNudgeAt < TimeSpan.FromSeconds(3))
        {
            QueueReplyWhenIdle($"{reason}; retry throttled");
            _lastCorrectiveAction = $"Retry queued for {targetCall}";
            return;
        }

        _wrongTargetNudgeSent = true;
        var selectionMethod = ShouldUseUdpReplyForSource(_lockedTarget.Decode)
            ? "UDP Reply"
            : "GUI double-click";
        _lastCorrectiveAction = $"Sent {selectionMethod} correction to {targetCall}";
        AddAction($"{selectionMethod} correction authorised for locked target {targetCall} ({reason}).");
        var correctingTarget = _lockedTarget;
        await SendReplyAsync(correctingTarget, countAttempt: false);
        if (!ReferenceEquals(_lockedTarget, correctingTarget))
            return;

        _lastSelectionNudgeAt = DateTime.Now;
        ArmEnableTxForSelectedTarget("Wrong-target correction");
    }

    private bool HasActuallyAttemptedLockedTarget()
    {
        return _targetConfirmedInJtdx
            || _targetConfirmedInFeed
            || _callAttemptCount > 0;
    }

    private void QueueReplyWhenIdle(string reason)
    {
        _pendingLockedReplyWhenIdle = true;
        _pendingLockedReplyReason = reason;
    }

    private string ObservedWrongTargetCall(JtdxStatusMessage status, string expectedCall)
    {
        if (!string.IsNullOrWhiteSpace(status.DxCall)
            && !status.DxCall.Equals(expectedCall, StringComparison.OrdinalIgnoreCase))
            return status.DxCall.Trim().ToUpperInvariant();

        var tokens = status.TxMessage.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var myCall = Settings.Settings.MyCallsign.Trim().ToUpperInvariant();
        if (tokens.Length >= 2
            && tokens[1].Equals(myCall, StringComparison.OrdinalIgnoreCase)
            && !tokens[0].Equals(expectedCall, StringComparison.OrdinalIgnoreCase))
        {
            return tokens[0];
        }

        return "";
    }

    private bool RecordWrongTargetNoProgressCycle(JtdxStatusMessage status)
    {
        if (DateTime.Now - _lastWrongTargetNoProgressAt < ActiveAttemptCycle())
            return false;

        _wrongTargetNoProgressCount++;
        _lastWrongTargetNoProgressAt = DateTime.Now;
        RecordTransmitMismatchCycle(status);
        return true;
    }

    private bool HasReceivedQsoProgressFrom(string observedCall)
    {
        if (string.IsNullOrWhiteSpace(observedCall))
            return false;

        return _decodeHistory
            .Where(d => d.ReceivedAt > DateTime.Now.AddMinutes(-4))
            .Any(d => IsReceivedFromStationToMe(d.RawText, observedCall));
    }

    private bool IsReceivedFromStationToMe(string text, string observedCall)
    {
        var tokens = NormalizedFt8Tokens(text);
        var myCall = Settings.Settings.MyCallsign.Trim().ToUpperInvariant();
        return tokens.Length >= 2
            && tokens[0].Equals(myCall, StringComparison.OrdinalIgnoreCase)
            && tokens[1].Equals(observedCall.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInboundReplyToMe(string text, out string inboundCall)
    {
        inboundCall = "";
        var tokens = NormalizedFt8Tokens(text);
        var myCall = Settings.Settings.MyCallsign.Trim().ToUpperInvariant();
        if (tokens.Length < 3 || string.IsNullOrWhiteSpace(myCall) || !tokens[0].Equals(myCall, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = tokens[1].Trim();
        if (candidate.Equals(myCall, StringComparison.OrdinalIgnoreCase) || IsGrid(candidate))
            return false;

        inboundCall = candidate;
        return candidate.Any(char.IsDigit) && candidate.Any(char.IsLetter);
    }

    private static bool IsGrid(string value)
    {
        return value.Length is 4 or 6
            && char.IsLetter(value[0])
            && char.IsLetter(value[1])
            && char.IsDigit(value[2])
            && char.IsDigit(value[3]);
    }

    private void AdoptWrongTargetQso(string observedCall, JtdxStatusMessage status)
    {
        if (_lockedTarget == null)
            return;

        var adoptedDecode = _decodeHistory
            .Where(d => d.Callsign.Equals(observedCall, StringComparison.OrdinalIgnoreCase) || d.ContactableCall.Equals(observedCall, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.ReceivedAt)
            .FirstOrDefault();

        if (adoptedDecode == null)
        {
            adoptedDecode = new DecodeMessage
            {
                Callsign = observedCall,
                ContactableCall = observedCall,
                RawText = status.TxMessage,
                ReceivedAt = DateTime.Now,
                SourceAppId = status.SourceAppId,
                Targetable = false,
                ParserReason = "Adopted wrong-target active QSO from JTDX status"
            };
            _targetScorer.EnrichDecode(adoptedDecode, _logbook, _adifMergeResult.Indexes, Settings.Settings);
        }

        _lockedTarget = _targetScorer.Score(adoptedDecode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
        _selectedIntendedTarget = null;
        _huntState = HuntState.InQso;
        _targetSource = "Adopted from JTDX";
        _actualJtdxDxCall = observedCall;
        _qsoStage = QsoStage.TargetReportSeen;
        _targetConfirmedInFeed = true;
        _targetConfirmedInJtdx = true;
        _unconfirmedRecoveryStartedAt = DateTime.MinValue;
        ResetWrongTargetState();
        _txVerificationState = "Wrong target - active QSO progress";
        _recoveryMode = "InboundQsoAdoption";
        _lastCorrectiveAction = $"Adopted active QSO with {observedCall}";
        AddAction($"Wrong target active QSO progress detected from {observedCall}; adopting/monitoring.");
    }

    private void ResetWrongTargetState()
    {
        _jtdxShowsWrongTx = false;
        _txMismatchCycleCount = 0;
        _lastTxMismatchCycleAt = DateTime.MinValue;
        _observedWrongTargetCall = "";
        _wrongTargetQsoProgress = false;
        _wrongTargetNoProgressCount = 0;
        _lastWrongTargetNoProgressAt = DateTime.MinValue;
        _wrongTargetNudgeSent = false;
        _pendingLockedReplyWhenIdle = false;
        _pendingLockedReplyReason = "";
    }

    private bool ShouldUseIdleRecovery()
    {
        var freshBestCandidate = DxAssist.BestTarget != null && IsFreshDecode(DxAssist.BestTarget.Decode);
        var postQsoTransition = DateTime.Now < _postQsoTransitionUntil;
        _recoveryMode = postQsoTransition
            ? "PostQsoTransition"
            : _lockedTarget != null || _selectedIntendedTarget != null
                ? "LockedTargetRecovery"
                : freshBestCandidate
                    ? "WaitingForJtdxIdle"
                    : "None";
        var target = _selectedIntendedTarget?.Callsign ?? _lockedTarget?.Callsign ?? DxAssist.BestTarget?.Callsign ?? "";
        if (DateTime.Now - _lastRecoveryBlockLogAt >= TimeSpan.FromSeconds(10))
        {
            _lastRecoveryBlockLogAt = DateTime.Now;
            AddAction(string.IsNullOrWhiteSpace(target)
                ? "CQ/TX6 reset blocked: target or QSO state active."
                : $"CQ/TX6 reset blocked because next/active target exists: {target}.");
        }

        // DX Pilot never intentionally selects CQ/TX6. If no target is locked,
        // Enable TX remains off until hunting supplies a safe target.
        return false;
    }

    private bool ShouldClickEnableTxRecovery()
    {
        if (_operatingMode != HuntingOperatingMode.DxAssist && _lockedTarget == null)
        {
            _recoveryMode = OperatingModeLabel().Replace(" ", "");
            _lastCorrectiveAction = $"Enable TX blocked because {OperatingModeLabel()} has no locked target";
            return false;
        }

        if (!_udpListener.IsRunning)
        {
            _recoveryMode = "UdpRequired";
            _lastCorrectiveAction = "Enable TX blocked because UDP listener is stopped";
            return false;
        }

        if (DateTime.Now < _postQsoTransitionUntil)
        {
            _recoveryMode = "PostQsoTransition";
            _lastCorrectiveAction = "Enable TX blocked during post-QSO settle period";
            return false;
        }

        if (_lockedTarget == null)
        {
            _recoveryMode = "WaitingForLockedTarget";
            _lastCorrectiveAction = "Enable TX blocked because no DX target is locked";
            return false;
        }

        if (_huntState == HuntState.InQso
            && (LooksLikeCq(_udpListener.LastStatus?.TxMessage ?? "")
                || InQsoNoProgressTimedOut()))
        {
            _targetConfirmedInJtdx = false;
            _recoveryMode = "InQsoCqSafety";
            _lastCorrectiveAction =
                $"Enable TX blocked: JTDX is on CQ or no QSO progress exists for {_lockedTarget.Callsign}";
            return false;
        }

        if (_huntState == HuntState.InQso && _qsoStage == QsoStage.CompletionPending)
        {
            var currentDxCall = (_udpListener.LastStatus?.DxCall ?? _actualJtdxDxCall).Trim();
            if (string.IsNullOrWhiteSpace(currentDxCall) || _lockedTarget.Callsign.Equals(currentDxCall, StringComparison.OrdinalIgnoreCase) || _targetConfirmedInJtdx)
            {
                _recoveryMode = "CompletionPending";
                _lastCorrectiveAction = $"Completion TX allowed for {_lockedTarget.Callsign}";
                AddThrottledCompletionLog("Completion TX allowed: locked target still confirmed; allowing final 73.");
                return true;
            }

            _recoveryMode = "CompletionPendingWrongTarget";
            _lastCorrectiveAction = $"Completion TX blocked because JTDX DX Call is {currentDxCall}, not {_lockedTarget.Callsign}";
            return false;
        }

        if (_targetConfirmedInJtdx)
            return true;

        if (DateTime.Now >= _targetConfirmationWaitUntil)
        {
            _recoveryMode = "Locked Target Recovery";
            _lastCorrectiveAction = $"JTDX has not confirmed {_lockedTarget.Callsign}; Enable TX remains blocked";
            AddAction($"Target acquisition blocked TX: waiting for JTDX confirmation of {_lockedTarget.Callsign}.");
            QueueReplyWhenIdle($"waiting for JTDX to accept {_lockedTarget.Callsign}");
            _ = SendReplyAsync(_lockedTarget, countAttempt: false);
            return false;
        }

        _recoveryMode = "WaitingForJtdxIdle";
        _lastCorrectiveAction = $"Waiting for JTDX to accept {_lockedTarget.Callsign}";
        return false;
    }

    private bool RecordTransmitMismatchCycle(JtdxStatusMessage status)
    {
        if (DateTime.Now - _lastTxMismatchCycleAt < ActiveAttemptCycle())
            return false;

        var before = _txMismatchCycleCount;
        _txMismatchCycleCount++;
        _lastTxMismatchCycleAt = DateTime.Now;
        AddAction($"Wrong target correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)} - expected {_lockedTarget?.Callsign}, JTDX currently shows {DescribeMismatch(status)}.");
        AddAction($"TX mismatch debug: before {before}, expected {_lockedTarget?.Callsign}, state {_qsoStage}, observed '{status.TxMessage}'.");
        return true;
    }

    private void ObserveMyTransmitCycle(JtdxStatusMessage status)
    {
        if (_lockedTarget == null)
            return;

        var cycleKey = GetCycleKey(status.ReceivedAt);
        var txMessage = string.IsNullOrWhiteSpace(status.TxMessage) ? "(blank TX message)" : status.TxMessage.Trim();
        _lastObservedTxMessage = txMessage;
        _lastObservedTxCycleTime = status.ReceivedAt.ToString("HH:mm:ss");
        if (!string.IsNullOrWhiteSpace(status.TxMessage))
            _lastIntendedTxMessage = status.TxMessage.Trim();

        var dxCallMatchesLockedTarget = status.DxCall.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase);
        var isVerifiedInitialCall = IsInitialCallTransmitForLockedTarget(status.TxMessage)
            || (dxCallMatchesLockedTarget && string.IsNullOrWhiteSpace(status.TxMessage));
        if (_huntState == HuntState.Calling && isVerifiedInitialCall)
        {
            if (RecordCallAttempt(cycleKey))
            {
                var evidence = string.IsNullOrWhiteSpace(status.TxMessage)
                    ? $"JTDX transmitting with DX Call {_lockedTarget.Callsign}; optional TX message field is blank"
                    : txMessage;
                AddAction($"Observed TX call attempt {CallAttemptProgressText()} for {_lockedTarget.Callsign}: {evidence}.");
            }
            return;
        }

        if (_huntState != HuntState.InQso || _qsoStage < QsoStage.TargetReportSeen)
            return;

        if (_qsoStage == QsoStage.CompletionPending)
        {
            if (IsCompletionMessage(status.TxMessage) && IsMyTransmitMessage(status.TxMessage))
                MarkMyFinal73Seen(status.TxMessage, "JTDX status");

            RecordCompletionGraceCycle(cycleKey, txMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(status.TxMessage))
        {
            IncrementReportRepeat(cycleKey, "TX cycle with no JTDX message, no new progress from target", txMessage, "JTDX status");
            return;
        }

        var newStage = DetectQsoStage(status.TxMessage, Settings.Settings.MyCallsign);
        if (newStage == QsoStage.CallingInitial && _qsoStage >= QsoStage.TargetReportSeen)
        {
            _stuckReason = $"TX regression detected: expected QSO progress but JTDX sent initial message {status.TxMessage}.";
            AddAction(_stuckReason);
            IncrementReportRepeat(cycleKey, "TX regression to initial/grid message", status.TxMessage, "JTDX status");
            return;
        }

        ObserveQsoStage(newStage, status.TxMessage, cycleKey, "JTDX status", isMyTransmit: true);
    }

    private void ObserveQsoStage(QsoStage newStage, string observedMessage, string cycleKey, string source, bool isMyTransmit = false)
    {
        if (_lockedTarget == null || _huntState != HuntState.InQso || newStage < QsoStage.TargetReportSeen)
            return;

        var previousStage = _qsoStage;

        if (!isMyTransmit && newStage > _qsoStage)
        {
            _qsoStage = newStage;
            _lastQsoProgressAt = DateTime.Now;
            _reportAttemptCount = 0;
            _lastReportRepeatCycleKey = cycleKey;
            _lastObservedQsoMessage = observedMessage;
            _lastProgressMessageFromTarget = observedMessage;
            _lastProgressTime = DateTime.Now;
            _lastStageChangeAt = DateTime.Now;
            _lastExpectedQsoStage = FormatQsoStage(_qsoStage);
            AddAction($"Report repeat reset: new QSO stage detected ({FormatQsoStage(previousStage)} -> {FormatQsoStage(_qsoStage)}).");
            AddAction($"QSO progress: {_lockedTarget.Callsign} sent {ExtractPayload(observedMessage)}; stage {FormatQsoStage(_qsoStage)}.");
            return;
        }

        if (isMyTransmit && newStage > _qsoStage)
        {
            _qsoStage = newStage;
            _lastStageChangeAt = DateTime.Now;
            _lastExpectedQsoStage = FormatQsoStage(_qsoStage);
            AddAction($"My TX observed: {observedMessage}; stage {FormatQsoStage(_qsoStage)}.");
        }

        IncrementReportRepeat(cycleKey, isMyTransmit ? $"repeated {FormatQsoStage(_qsoStage)}, no new progress from {_lockedTarget.Callsign}" : $"same received stage {FormatQsoStage(_qsoStage)}", observedMessage, source);
    }

    private void IncrementReportRepeat(string cycleKey, string reason, string observedMessage, string source)
    {
        if (_lockedTarget == null)
            return;

        if (cycleKey.Equals(_lastReportRepeatCycleKey, StringComparison.Ordinal))
            return;

        var before = _reportAttemptCount;
        _lastReportRepeatCycleKey = cycleKey;
        _lastObservedQsoMessage = observedMessage;
        _lastRepeatedStage = FormatQsoStage(_qsoStage);
        _reportAttemptCount++;
        AddAction($"Report repeat {_reportAttemptCount}/{Math.Max(1, Settings.Settings.MaxReportAttempts)} - waiting for target progress. {reason}.");
        AddAction($"Report repeat debug: target {_lockedTarget.Callsign}, current {FormatQsoStage(_qsoStage)}, source {source}, observed '{observedMessage}', count {before}->{_reportAttemptCount}.");
    }

    private void RecordCompletionGraceCycle(string cycleKey, string observedMessage)
    {
        if (_lockedTarget == null || cycleKey.Equals(_lastCompletionGraceCycleKey, StringComparison.Ordinal))
            return;

        if (Settings.Settings.WaitForFinal73AfterRr73 && !_myFinal73SeenDuringCompletion)
        {
            _lastCompletionGraceCycleKey = cycleKey;
            AddThrottledCompletionLog($"Completion pending: target sent RR73/73; waiting for final 73 or ADIF confirmation.");
            return;
        }

        _lastCompletionGraceCycleKey = cycleKey;
        _completionGraceCycleCount++;
        AddAction($"Completion pending grace cycle {_completionGraceCycleCount}/{Math.Max(1, Settings.Settings.CompletionGraceCycles)} for {_lockedTarget.Callsign}. Observed '{observedMessage}'.");

        if (_completionGraceCycleCount >= Math.Max(1, Settings.Settings.CompletionGraceCycles))
        {
            CompleteLockedTarget($"QSO released: completion grace elapsed for {_lockedTarget.Callsign}.");
            _ = HuntTickAsync();
        }
    }

    private void StartCompletionPending(string observedMessage, string cycleKey)
    {
        if (_lockedTarget == null)
            return;

        _huntState = HuntState.InQso;
        _qsoStage = QsoStage.CompletionPending;
        _completionGraceCycleCount = 0;
        _lastCompletionGraceCycleKey = cycleKey;
        _myFinal73SeenDuringCompletion = false;
        _completionPendingStartedAt = DateTime.Now;
        _lastQsoProgressAt = DateTime.Now;
        _lastProgressMessageFromTarget = observedMessage;
        _lastProgressTime = DateTime.Now;
        _lastStageChangeAt = DateTime.Now;
        _lastExpectedQsoStage = FormatQsoStage(_qsoStage);
        AddAction("Completion pending: target sent RR73/73; waiting for final 73 or ADIF confirmation.");
    }

    private void MarkMyFinal73Seen(string observedMessage, string source)
    {
        if (_lockedTarget == null)
            return;

        if (!_myFinal73SeenDuringCompletion)
        {
            _myFinal73SeenDuringCompletion = true;
            _completionGraceCycleCount = 0;
            _lastCompletionGraceCycleKey = "";
            AddAction($"Completion TX allowed: locked target still confirmed; allowing final 73. Source {source}: {observedMessage}");
        }
    }

    private bool CompletionPendingTimedOut()
    {
        if (_qsoStage != QsoStage.CompletionPending || _completionPendingStartedAt == DateTime.MinValue)
            return false;

        return DateTime.Now - _completionPendingStartedAt >= TimeSpan.FromSeconds(Math.Max(30, Settings.Settings.CompletionTimeoutSeconds));
    }

    private void AddThrottledCompletionLog(string message)
    {
        if (DateTime.Now - _lastCompletionProtectionLogAt < TimeSpan.FromSeconds(10))
            return;

        _lastCompletionProtectionLogAt = DateTime.Now;
        AddAction(message);
    }

    private string GetCycleKey(DecodeMessage decode)
    {
        if (decode.DecodeTime.HasValue)
        {
            return $"decode:{decode.DecodeTime.Value.Ticks / Math.Max(1, ActiveAttemptCycle().Ticks)}";
        }

        return GetCycleKey(decode.ReceivedAt);
    }

    private string GetCycleKey(DateTime timestamp)
    {
        return $"clock:{timestamp.Ticks / Math.Max(1, ActiveAttemptCycle().Ticks)}";
    }

    private static bool LooksLikeCq(string message)
    {
        var upper = message.Trim().ToUpperInvariant();
        return upper.Equals("CQ", StringComparison.Ordinal)
            || upper.StartsWith("CQ ", StringComparison.Ordinal);
    }

    private static string DescribeMismatch(JtdxStatusMessage status)
    {
        if (LooksLikeCq(status.TxMessage))
            return $"CQ: '{status.TxMessage}'";

        if (!string.IsNullOrWhiteSpace(status.DxCall))
            return $"DX Call {status.DxCall}, TX '{status.TxMessage}'";

        return string.IsNullOrWhiteSpace(status.TxMessage)
            ? "blank/unknown JTDX TX state"
            : $"TX '{status.TxMessage}'";
    }

    private static string BuildObservedTransmitState(JtdxStatusMessage status)
    {
        var txState = status.Transmitting ? "TX" : status.TxEnabled ? "TX enabled" : "TX disabled";
        var dx = string.IsNullOrWhiteSpace(status.DxCall) ? "no DX Call" : $"DX Call {status.DxCall}";
        var message = string.IsNullOrWhiteSpace(status.TxMessage) ? "blank TX message" : $"TX '{status.TxMessage}'";
        return $"{txState}; {dx}; {message}";
    }

    private void ProcessDecodeForCurrentQso(DecodeMessage decode)
    {
        if (_lockedTarget == null)
        {
            TryAdoptInboundQso(decode);
            return;
        }

        if (!MessageInvolvesCurrentTarget(decode.RawText))
            return;

        if (_huntState == HuntState.Calling && IsInitialCallTransmitForLockedTarget(decode.RawText))
        {
            _lastObservedTxMessage = decode.RawText.Trim();
            _lastObservedTxCycleTime = decode.ReceivedAt.ToString("HH:mm:ss");
            _lastIntendedTxMessage = decode.RawText.Trim();
            if (RecordCallAttempt(GetCycleKey(decode)))
                AddAction($"Observed decode TX call attempt {CallAttemptProgressText()} for {_lockedTarget.Callsign}: {decode.RawText}.");
            UpdateHuntStateDisplay();
            return;
        }

        _targetConfirmedInFeed = true;
        var newStage = DetectQsoStage(decode.RawText, Settings.Settings.MyCallsign);
        if (newStage == QsoStage.Completed || IsCompletionMessage(decode.RawText))
        {
            if (IsMyTransmitMessage(decode.RawText))
            {
                if (_qsoStage != QsoStage.CompletionPending)
                    StartCompletionPending(decode.RawText, GetCycleKey(decode));
                MarkMyFinal73Seen(decode.RawText, "decode");
                UpdateHuntStateDisplay();
                return;
            }

            StartCompletionPending(decode.RawText, GetCycleKey(decode));
            UpdateHuntStateDisplay();
            return;
        }

        if (_huntState == HuntState.Calling)
        {
            _huntState = HuntState.InQso;
            _qsoStage = newStage > QsoStage.CallingInitial ? newStage : QsoStage.TargetReportSeen;
            _lastQsoProgressAt = DateTime.Now;
            _reportAttemptCount = 0;
            _lastReportRepeatCycleKey = GetCycleKey(decode);
            _lastObservedQsoMessage = decode.RawText;
            _lastProgressMessageFromTarget = decode.RawText;
            _lastProgressTime = DateTime.Now;
            _lastStageChangeAt = DateTime.Now;
            _lastExpectedQsoStage = FormatQsoStage(_qsoStage);
            _txVerificationState = "OK";
            AddAction($"QSO progress: {_lockedTarget.Callsign} sent {ExtractPayload(decode.RawText)}; stage {FormatQsoStage(_qsoStage)}.");
        }
        else
            ObserveQsoStage(newStage, decode.RawText, GetCycleKey(decode), "decode");

        UpdateHuntStateDisplay();
    }

    private void TryAdoptInboundQso(DecodeMessage decode)
    {
        if (!Settings.Settings.AcceptIncomingCalls)
            return;

        if (_huntState != HuntState.Idle || !IsInboundReplyToMe(decode.RawText, out var inboundCall))
            return;

        var sourceTarget = _targetScorer.Score(decode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
        _selectedIntendedTarget = null;
        _lockedTarget = sourceTarget;
        _huntState = HuntState.InQso;
        _targetStartedAt = DateTime.Now;
        _targetStartedUtc = DateTime.UtcNow;
        _targetSource = "Inbound CQ reply / AdoptedFromJTDX";
        _recoveryMode = "InboundQsoAdoption";
        _actualJtdxDxCall = inboundCall;
        _qsoStage = DetectQsoStage(decode.RawText, Settings.Settings.MyCallsign);
        if (_qsoStage < QsoStage.TargetReportSeen)
            _qsoStage = QsoStage.TargetReportSeen;
        _targetConfirmedInFeed = true;
        _reportAttemptCount = 0;
        _lastReportRepeatCycleKey = GetCycleKey(decode);
        _lastObservedQsoMessage = decode.RawText;
        _lastProgressMessageFromTarget = decode.RawText;
        _lastProgressTime = DateTime.Now;
        _lastStageChangeAt = DateTime.Now;
        _lastCorrectiveAction = $"Adopted inbound QSO with {inboundCall}";
        AddAction($"Inbound CQ reply detected from {inboundCall}.");
        AddAction($"Adopting inbound QSO: {inboundCall}.");
        var best = DxAssist.BestTarget?.Callsign;
        if (!string.IsNullOrWhiteSpace(best) && !best.Equals(inboundCall, StringComparison.OrdinalIgnoreCase))
            AddAction($"Suspending selected candidate {best} while inbound QSO {inboundCall} is active.");
        TrackOpportunitySelected(sourceTarget, manual: false);
        UpdateHuntStateDisplay();
    }

    private bool MessageInvolvesCurrentTarget(string text)
    {
        if (_lockedTarget == null)
            return false;

        var upper = text.ToUpperInvariant();
        var myCall = Settings.Settings.MyCallsign.Trim().ToUpperInvariant();
        var targetCall = _lockedTarget.Callsign.Trim().ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(myCall)
            && upper.Contains(myCall, StringComparison.OrdinalIgnoreCase)
            && upper.Contains(targetCall, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCompletionMessage(string text)
    {
        if (!MessageInvolvesCurrentTarget(text))
            return false;

        var upper = text.ToUpperInvariant();
        return upper.Contains("RR73", StringComparison.Ordinal)
            || upper.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(p => p.Equals("73", StringComparison.OrdinalIgnoreCase));
    }

    private static QsoStage DetectQsoStage(string text, string myCall)
    {
        var upper = text.ToUpperInvariant();
        var tokens = NormalizedFt8Tokens(text);
        if (upper.Contains("RR73", StringComparison.Ordinal)
            || tokens.Any(p => p.Equals("73", StringComparison.OrdinalIgnoreCase)))
        {
            return QsoStage.Completed;
        }

        if (tokens.Any(p => p.Equals("RRR", StringComparison.OrdinalIgnoreCase)))
            return QsoStage.WaitingForRrrOrRr73;

        if (Regex.IsMatch(upper, @"\bR[-+]\d{1,2}\b", RegexOptions.CultureInvariant))
            return IsMyTransmit(tokens, myCall) ? QsoStage.MyRReportSent : QsoStage.WaitingForRrrOrRr73;

        if (Regex.IsMatch(upper, @"\b[-+]\d{1,2}\b", RegexOptions.CultureInvariant))
            return IsMyTransmit(tokens, myCall) ? QsoStage.MyReportSent : QsoStage.TargetReportSeen;

        return QsoStage.CallingInitial;
    }

    private static bool IsMyTransmit(string[] tokens, string myCall)
    {
        return tokens.Length >= 2
            && !string.IsNullOrWhiteSpace(myCall)
            && tokens[1].Equals(myCall.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMyTransmitMessage(string text)
    {
        var tokens = NormalizedFt8Tokens(text);
        return IsMyTransmit(tokens, Settings.Settings.MyCallsign);
    }

    private bool IsInitialCallTransmitForLockedTarget(string text)
    {
        if (_lockedTarget == null || string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = NormalizedFt8Tokens(text);
        var targetCall = _lockedTarget.Callsign.Trim().ToUpperInvariant();
        var myCall = Settings.Settings.MyCallsign.Trim().ToUpperInvariant();
        return tokens.Length >= 2
            && tokens[0].Equals(targetCall, StringComparison.OrdinalIgnoreCase)
            && tokens[1].Equals(myCall, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] NormalizedFt8Tokens(string text)
    {
        return text.ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token != "~" && token != "TX")
            .Select(token => token.Trim('~', '*'))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static string FormatQsoStage(QsoStage stage)
    {
        return stage switch
        {
            QsoStage.CallingInitial => "Calling Initial",
            QsoStage.TargetReportSeen => "Target Report Seen",
            QsoStage.MyReportSent => "My Report Sent",
            QsoStage.MyRReportSent => "My R Report Sent",
            QsoStage.WaitingForRrrOrRr73 => "Waiting For RRR/RR73",
            QsoStage.CompletionPending => "Completion Pending",
            QsoStage.QsoStuck => "QSO Stuck",
            QsoStage.Completed => "Complete",
            _ => "None"
        };
    }

    private static string ExtractPayload(string message)
    {
        var tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length >= 3 ? tokens[^1] : message;
    }

    private string BuildInitialCallMessage(string targetCall)
    {
        var myCall = Settings.Settings.MyCallsign.Trim();
        var homeGrid = Settings.Settings.HomeGrid.Trim();
        return string.IsNullOrWhiteSpace(homeGrid)
            ? $"{targetCall} {myCall}"
            : $"{targetCall} {myCall} {homeGrid}";
    }

    private void CompleteLockedTarget(string reason)
    {
        var completedCall = _lockedTarget?.Callsign ?? "";
        var adifConfirmed = reason.Contains("ADIF confirmed", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("newly logged", StringComparison.OrdinalIgnoreCase);
        if (_lockedTarget != null)
        {
            if (adifConfirmed)
            {
                _sessionWorked.Add(_lockedTarget.Callsign);
                TrackOpportunityWorked(_lockedTarget, reason);
            }
        }

        ClearLockedTarget(reason);
        _selectedIntendedTarget = null;
        _postQsoTransitionUntil = DateTime.Now.AddSeconds(8);
        _recoveryMode = "PostQsoTransition";
        if (!string.IsNullOrWhiteSpace(completedCall))
            AddAction($"Post-QSO transition started after {completedCall}.");
    }

    private void ClearLockedTarget(string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
            AddAction(reason);

        TrackOpportunityReleased(_lockedTarget, reason);

        _lockedTarget = null;
        _selectedIntendedTarget = null;
        _huntState = HuntState.Idle;
        _targetConfirmedInFeed = false;
        _targetConfirmedInJtdx = false;
        _jtdxShowsWrongTx = false;
        ResetWrongTargetState();
        _qsoStage = QsoStage.None;
        _callAttemptCount = 0;
        _acquisitionAttemptCount = 0;
        _reportAttemptCount = 0;
        _txMismatchCycleCount = 0;
        _completionGraceCycleCount = 0;
        _lastCallAttemptCycleKey = "";
        _lastCompletionGraceCycleKey = "";
        _myFinal73SeenDuringCompletion = false;
        _completionPendingStartedAt = DateTime.MinValue;
        _lastCallAttemptAt = DateTime.MinValue;
        _lastSelectionNudgeAt = DateTime.MinValue;
        _lastAcquisitionAttemptAt = DateTime.MinValue;
        _unconfirmedRecoveryStartedAt = DateTime.MinValue;
        _targetConfirmationWaitUntil = DateTime.MinValue;
        _manualTxOffDetectedAt = DateTime.MinValue;
        _targetStartedUtc = DateTime.MinValue;
        _lastQsoProgressAt = DateTime.MinValue;
        _lastTxMismatchCycleAt = DateTime.MinValue;
        _lastReportRepeatCycleKey = "";
        _lastObservedTransmitState = "Unknown";
        _txVerificationState = "Unknown";
        if (_recoveryMode != "PostQsoTransition")
            _recoveryMode = "None";
        _lastObservedQsoMessage = "";
        _lastObservedTxMessage = "Unknown";
        _lastObservedTxCycleTime = "";
        _lastIntendedTxMessage = "";
        _lastExpectedQsoStage = "None";
        _lastProgressMessageFromTarget = "";
        _lastProgressTime = DateTime.MinValue;
        _lastRepeatedStage = "";
        _lastStageChangeAt = DateTime.MinValue;
        _pendingLockedReplyWhenIdle = false;
        _pendingLockedReplyReason = "";
        _manualSuppressionOverrideCall = "";
        _targetSelectionCancellation?.Cancel();
        _targetSelectionCancellation?.Dispose();
        _targetSelectionCancellation = null;
        if (!reason.Contains("stuck", StringComparison.OrdinalIgnoreCase)
            && !reason.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
        {
            _stuckReason = "";
        }
        UpdateHuntStateDisplay();
    }

    private void SuppressTarget(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        if (string.IsNullOrWhiteSpace(call))
            return;

        var until = DateTime.Now.AddMinutes(Math.Max(1, Settings.Settings.SuppressFailedTargetMinutes));
        _suppressedTargets[call] = until;
        TrackOpportunitySuppressed(call, until, "Target suppressed");
        AddAction($"{call} suppressed until {until:HH:mm:ss}.");
        RemoveWantedItemsForCall(call, "suppressed after retry limit");
        ReleaseSuppressionCommand.RaiseCanExecuteChanged();
    }

    private bool IsSuppressed(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        if (!string.IsNullOrWhiteSpace(_manualSuppressionOverrideCall)
            && call.Equals(_manualSuppressionOverrideCall, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsPermanentlySuppressed(callsign)
            || _suppressedTargets.TryGetValue(call, out var until) && until > DateTime.Now;
    }

    private bool IsPermanentlySuppressed(string callsign)
    {
        var normal = CallsignNormalizer.Normalize(callsign);
        return !string.IsNullOrWhiteSpace(normal) && _permanentlySuppressedCallsigns.Contains(normal);
    }

    private void ExpireSuppressedTargets()
    {
        var expired = _suppressedTargets.Where(kvp => kvp.Value <= DateTime.Now).Select(kvp => kvp.Key).ToList();
        foreach (var call in expired)
            _suppressedTargets.Remove(call);
        if (expired.Count > 0)
            ReleaseSuppressionCommand.RaiseCanExecuteChanged();
    }

    private bool IsWorked(string callsign)
    {
        return !string.IsNullOrWhiteSpace(callsign)
            && _logbook.Any(q => q.Call.Equals(callsign, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasFreshLiveQso(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign) || _targetStartedUtc == DateTime.MinValue)
            return false;

        var cutoffUtc = _targetStartedUtc.AddMinutes(-2);
        return HasRecentLiveQso(callsign, cutoffUtc);
    }

    private bool IsRecentlyWorkedLive(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return false;

        var suppressHours = Math.Max(1, Settings.Settings.SuccessfulQsoSuppressHours);
        var cutoffUtc = DateTime.UtcNow.AddHours(-suppressHours);
        return HasRecentLiveQso(callsign, cutoffUtc);
    }

    private bool HasRecentLiveQso(string callsign, DateTime cutoffUtc)
    {
        return _liveLogbook.Any(q =>
            q.Call.Equals(callsign, StringComparison.OrdinalIgnoreCase)
            && TryGetQsoDateTimeUtc(q, out var qsoUtc)
            && qsoUtc >= cutoffUtc);
    }

    private static bool TryGetQsoDateTimeUtc(AdifQso qso, out DateTime qsoUtc)
    {
        qsoUtc = DateTime.MinValue;
        if (!qso.QsoDate.HasValue)
            return false;

        var date = qso.QsoDate.Value.Date;
        var timeText = new string((qso.TimeOn ?? "").Where(char.IsDigit).ToArray());
        var hour = 0;
        var minute = 0;
        var second = 0;

        if (timeText.Length >= 4)
        {
            if (!int.TryParse(timeText[..2], out hour) || !int.TryParse(timeText.Substring(2, 2), out minute))
                return false;
            if (timeText.Length >= 6 && !int.TryParse(timeText.Substring(4, 2), out second))
                return false;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
            return false;

        qsoUtc = DateTime.SpecifyKind(date.AddHours(hour).AddMinutes(minute).AddSeconds(second), DateTimeKind.Utc);
        return true;
    }

    private void LoadAdifSources()
    {
        LoadFullAdif();
        LoadLiveAdif();
        RebuildCombinedAdifIndex("ADIF sources loaded");
    }

    private void LoadFullAdif()
    {
        _fullLogbook.Clear();
        var path = Settings.Settings.FullAdifPath;
        if (!Settings.Settings.AutoLoadFullAdifOnStartup || string.IsNullOrWhiteSpace(path))
        {
            AddAction("Full ADIF not configured.");
            return;
        }

        if (!_adifReader.TryLoad(path, out var loaded))
        {
            AddAction($"Full ADIF missing or unreadable: {path}");
            return;
        }

        _fullLogbook.AddRange(WithSource(loaded, "Full"));
        _lastFullAdifLoadedAt = DateTime.Now;
        AddAction($"Full ADIF loaded: {_fullLogbook.Count} QSOs from {path}");
    }

    private void LoadLiveAdif()
    {
        _liveLogbook.Clear();
        var path = LiveAdifPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            AddAction("Live JTDX ADIF path is blank.");
            return;
        }

        if (!_adifReader.TryLoad(path, out var loaded))
        {
            AddAction($"Live JTDX ADIF missing or unreadable: {path}");
            return;
        }

        _liveLogbook.AddRange(WithSource(loaded, "Live"));
        _lastLiveAdifReloadAt = DateTime.Now;
        _lastLiveAdifWriteUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        AddAction($"Live JTDX ADIF loaded: {_liveLogbook.Count} QSOs from {path}");
    }

    private void RebuildCombinedAdifIndex(string reason)
    {
        _adifMergeResult = _adifStatusBuilder.Build(_fullLogbook, _liveLogbook, Settings.Settings);
        _logbook.Clear();
        _logbook.AddRange(_adifMergeResult.UniqueQsos);
        RebuildWorkedCallDisplayIndex();

        foreach (var decode in _decodeHistory)
            _targetScorer.EnrichDecode(decode, _logbook, _adifMergeResult.Indexes, Settings.Settings);

        RefreshRecentDecodeRows();
        UpdateAdifDiagnostics();
        AddAction($"{reason}: {_logbook.Count} unique QSOs, {_adifMergeResult.DuplicateCount} duplicates merged.");
        AddAction($"Confirmation modes applied: DXCC {Settings.Settings.DxccConfirmationMode}, grid {Settings.Settings.GridConfirmationMode}, state {Settings.Settings.StateConfirmationMode}, IOTA {Settings.Settings.IotaConfirmationMode}.");
        ExpireWantedItems();
        UpdateNextBestTargets();
    }

    private void ReloadAdifIfChanged()
    {
        if (!Settings.Settings.WatchLiveJtdxAdif)
            return;

        var path = LiveAdifPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (lastWrite <= _lastLiveAdifWriteUtc)
            return;

        LoadLiveAdif();
        RebuildCombinedAdifIndex("Live JTDX ADIF changed");
    }

    private void StartAdifWatcher()
    {
        StopAdifWatcher();

        if (!Settings.Settings.WatchLiveJtdxAdif)
        {
            AddAction("Live JTDX ADIF watching is disabled.");
            return;
        }

        var path = LiveAdifPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AddAction($"Live JTDX ADIF watcher not started; file missing: {path}");
            return;
        }

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return;

        _adifWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _adifWatcher.Changed += (_, _) => Dispatch(RequestLiveAdifReload);
        _adifWatcher.Created += (_, _) => Dispatch(RequestLiveAdifReload);
        _adifWatcher.Renamed += (_, _) => Dispatch(RequestLiveAdifReload);
        AddAction($"Watching live JTDX ADIF: {path}");
    }

    private void StartAllTxtMonitor(bool forceRestart = false)
    {
        if (!Settings.Settings.WatchJtdxAllTxt)
        {
            _allTxtMonitor.Stop();
            AllTxtDiagnostics = "JTDX ALL.TXT outgoing-message monitoring is disabled.";
            return;
        }

        var resolvedPath = JtdxAllTxtMonitor.ResolveCurrentPath(Settings.Settings.JtdxAllTxtPath);
        Settings.Settings.JtdxAllTxtPath = resolvedPath;
        if (!forceRestart
            && _allTxtMonitor.IsRunning
            && _allTxtMonitor.ActivePath.Equals(resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _allTxtMonitor.Start(resolvedPath);
    }

    private void StopAdifWatcher()
    {
        if (_adifWatcher == null)
            return;

        _adifWatcher.Dispose();
        _adifWatcher = null;
    }

    private string LiveAdifPath()
    {
        var path = Settings.Settings.LiveJtdxAdifPath;
        if (string.IsNullOrWhiteSpace(path))
            path = Settings.Settings.AdifFilePath;

        Settings.Settings.LiveJtdxAdifPath = path;
        Settings.Settings.AdifFilePath = path;
        return path;
    }

    private static IEnumerable<AdifQso> WithSource(IEnumerable<AdifQso> qsos, string source)
    {
        foreach (var qso in qsos)
        {
            qso.Source = source;
            yield return qso;
        }
    }

    private void UpdateAdifDiagnostics()
    {
        var fullPath = Settings.Settings.FullAdifPath;
        var livePath = LiveAdifPath();
        var dxccConfirmed = _adifMergeResult.Indexes.Dxcc.Values.Count(s => s.ConfirmedAny);
        var wasSatisfiedStates = UsStateValidator.StandardStateCodes
            .Where(state => _adifMergeResult.Indexes.States.TryGetValue(state, out var status) && status.ConfirmedAny)
            .ToList();
        var wasMissingStates = UsStateValidator.StandardStateCodes
            .Except(wasSatisfiedStates, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dcStatus = Settings.Settings.IncludeDistrictOfColumbia
            ? _adifMergeResult.Indexes.States.TryGetValue("DC", out var dc) && dc.ConfirmedAny
                ? "satisfied"
                : "needed"
            : "not included";

        LogbookStatus = $"Full {_adifMergeResult.FullQsoCount} + live {_adifMergeResult.LiveQsoCount} = {_logbook.Count} unique QSOs.";
        AdifDiagnostics =
            $"Full ADIF path: {DisplayPath(fullPath)}\n"
            + $"Full ADIF loaded: {_fullLogbook.Count > 0}  QSOs: {_adifMergeResult.FullQsoCount}  Last loaded: {DisplayTime(_lastFullAdifLoadedAt)}  Exists: {FileExists(fullPath)}\n"
            + $"Live JTDX ADIF path: {DisplayPath(livePath)}\n"
            + $"Live JTDX ADIF watched: {Settings.Settings.WatchLiveJtdxAdif}  QSOs: {_adifMergeResult.LiveQsoCount}  Last loaded: {DisplayTime(_lastLiveAdifReloadAt)}  Exists: {FileExists(livePath)}\n"
            + $"Combined unique QSOs: {_logbook.Count}  Duplicates merged: {_adifMergeResult.DuplicateCount}\n"
            + $"DXCC worked: {_adifMergeResult.Indexes.Dxcc.Count}  DXCC confirmed: {dxccConfirmed}  Grids worked: {_adifMergeResult.Indexes.Grids.Count}  States worked: {_adifMergeResult.Indexes.States.Count}  IOTA worked: {_adifMergeResult.Indexes.Iotas.Count}\n"
            + $"WAS progress ({Settings.Settings.StateConfirmationMode}): {wasSatisfiedStates.Count}/50 satisfied; missing: {(wasMissingStates.Count == 0 ? "none" : string.Join(", ", wasMissingStates))}; DC: {dcStatus}\n"
            + $"Confirmation modes: DXCC {Settings.Settings.DxccConfirmationMode}, Grid {Settings.Settings.GridConfirmationMode}, State {Settings.Settings.StateConfirmationMode}, IOTA {Settings.Settings.IotaConfirmationMode}";
    }

    private static string DisplayPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "(not set)" : path;
    }

    private static string FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? "yes" : "no";
    }

    private static string DisplayTime(DateTime value)
    {
        return value == DateTime.MinValue ? "never" : value.ToString("HH:mm:ss");
    }

    private static string DisplaySource(string? source)
    {
        return string.IsNullOrWhiteSpace(source) ? "None" : source.Replace("Full", "FullADIF", StringComparison.OrdinalIgnoreCase).Replace("Live", "LiveJTDX", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSet(IEnumerable<string>? values)
    {
        if (values == null)
            return "";

        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).OrderBy(v => v).ToList();
        return list.Count == 0 ? "" : string.Join(", ", list);
    }

    private static string FormatQso(AdifQso? qso)
    {
        if (qso == null)
            return "none";

        var date = qso.QsoDate?.ToString("yyyy-MM-dd") ?? qso.QsoDateText;
        return $"{qso.Call} {date} {qso.TimeOn} {qso.Band} {qso.Mode} {qso.Country} DXCC {qso.Dxcc} Source {DisplaySource(qso.Source)}";
    }

    private void UpdateBestTarget(DxTarget best)
    {
        var entity = string.IsNullOrWhiteSpace(best.Decode.EntityName) ? "" : $"  {best.Decode.EntityName}";
        Dashboard.BestTarget = $"{best.Callsign}{entity}  score {best.Score}";
        Dashboard.BestReason = best.PrimaryReason;
        DxAssist.TargetSourceRowText = TargetSourceRowText(best);
    }

    private string WrongTargetStatusText()
    {
        var baseText = $"TX Mismatch {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)}";
        if (string.IsNullOrWhiteSpace(_observedWrongTargetCall))
            return baseText;

        var expected = _lockedTarget?.Callsign ?? "None";
        var progress = _wrongTargetQsoProgress ? "Yes" : "No";
        var action = _wrongTargetQsoProgress
            ? Settings.Settings.WrongTargetActiveQsoPolicy.Equals("AdoptAndMonitor", StringComparison.OrdinalIgnoreCase) ? "Adopt and monitor" : "Wait"
            : _wrongTargetNoProgressCount >= Math.Max(1, Settings.Settings.MaxWrongTargetNoProgressCycles) ? "Release and hunt" : _wrongTargetNudgeSent ? "Correction queued/sent" : "Nudge original target";
        return $"{baseText}; Expected {expected}; Observed {_observedWrongTargetCall}; Wrong target no-progress {_wrongTargetNoProgressCount}/{Math.Max(1, Settings.Settings.MaxWrongTargetNoProgressCycles)}; Progress from {_observedWrongTargetCall}: {progress}; Action: {action}";
    }

    private static string TargetDisplay(DxTarget? target)
    {
        if (target == null)
            return "None";

        var entity = string.IsNullOrWhiteSpace(target.Decode.EntityName) ? "" : $" {target.Decode.EntityName}";
        return $"{target.Callsign}{entity}";
    }

    private string TargetSourceRowText(DxTarget? target)
    {
        if (target == null)
            return "JTDX Row: -  Visible: No";

        var row = _visibleRowModel.FindDecode(target.Decode);
        var rowText = row == null ? "-" : row.ScreenRowIndex.ToString();
        var visible = row == null ? "No" : "Yes";
        var source = string.IsNullOrWhiteSpace(target.Decode.RawText) ? "None" : target.Decode.RawText;
        return $"JTDX Row: {rowText}  Visible: {visible}  Type: {target.Decode.MessageTypeText}  Source: {source}";
    }

    private string TargetStateWarning()
    {
        var best = DxAssist.BestTarget?.Callsign ?? "";
        var locked = _lockedTarget?.Callsign ?? "";
        if (!string.IsNullOrWhiteSpace(best)
            && !string.IsNullOrWhiteSpace(locked)
            && !best.Equals(locked, StringComparison.OrdinalIgnoreCase))
        {
            return $"Best candidate differs from active QSO target. DX Pilot is monitoring {locked} and will not call {best} until this QSO completes.";
        }

        return "";
    }

    private void UpdateHuntStateDisplay()
    {
        Dashboard.HuntState = _lockedTarget == null
            ? $"{_huntState}"
            : $"{_huntState}: {_lockedTarget.Callsign} since {_targetStartedAt:HH:mm:ss}"
                + (_targetConfirmedInFeed ? " in exchange" : _targetConfirmedInJtdx ? " selected in JTDX" : _jtdxShowsWrongTx ? " correcting JTDX CQ/wrong target" : " awaiting JTDX select");

        if (_lockedTarget == null)
        {
            DxAssist.CallingElapsed = "Not calling.";
            DxAssist.MoveOnAt = "";
            DxAssist.QsoStageText = "";
            DxAssist.LockedTargetText = "Locked Target: None";
            DxAssist.TargetSourceText = $"Target Source: {_targetSource}";
            DxAssist.TargetSourceRowText = TargetSourceRowText(DxAssist.BestTarget);
            DxAssist.WantedReasonText = "";
            DxAssist.BestCandidateText = $"Best Candidate: {TargetDisplay(DxAssist.BestTarget)}";
            DxAssist.SelectedIntendedTargetText = $"Selected Intended Target: {TargetDisplay(_selectedIntendedTarget)}";
            DxAssist.ActiveLockedTargetText = "Active / Locked QSO Target: None";
            DxAssist.ActualJtdxDxCallText = $"Actual JTDX DX Call: {(string.IsNullOrWhiteSpace(_actualJtdxDxCall) ? "None" : _actualJtdxDxCall)}";
            DxAssist.TargetStateWarningText = "";
            DxAssist.CallAttemptsText = $"Call Attempts {CallAttemptProgressText()}";
            DxAssist.ReportRepeatsText = $"Report Repeats {_reportAttemptCount}/{Math.Max(1, Settings.Settings.MaxReportAttempts)}";
            DxAssist.TxMismatchText = WrongTargetStatusText();
            DxAssist.TxVerificationText = $"TX Verification: {_txVerificationState}";
            DxAssist.RecoveryModeText = $"Recovery Mode: {_recoveryMode}";
            DxAssist.LastProgressFromTarget = "Last Progress From Target: None";
            DxAssist.LastIntendedTx = "Last Intended TX: Unknown";
            DxAssist.LastMyTx = "Last My TX: Unknown";
            DxAssist.LastStageChange = "Last Stage Change: None";
            DxAssist.StuckReasonText = string.IsNullOrWhiteSpace(_stuckReason) ? "" : $"Stuck Reason: {_stuckReason}";
            DxAssist.LastCorrectiveAction = $"Last Corrective Action: {_lastCorrectiveAction}";
            DxAssist.LastObservedTransmitState = $"Last observed JTDX state: {_lastObservedTransmitState}";
            UpdateTargetStatusSummary();
            return;
        }

        var elapsed = DateTime.Now - _targetStartedAt;
        DxAssist.CallingElapsed = $"Calling for {elapsed:mm\\:ss}";
        DxAssist.QsoStageText = _huntState == HuntState.InQso
            ? $"QSO Stage: {FormatQsoStage(_qsoStage)}{(_qsoStage == QsoStage.CompletionPending ? $" ({_completionGraceCycleCount}/{Math.Max(1, Settings.Settings.CompletionGraceCycles)} grace cycles)" : "")}"
            : $"{(_targetConfirmedInJtdx ? "JTDX target selected" : _jtdxShowsWrongTx ? "Correcting JTDX CQ/wrong target" : "Waiting for JTDX to select target")}. {CurrentDigitalMode} call cycles {CallAttemptProgressText()}.";
        DxAssist.MoveOnAt = _huntState == HuntState.InQso
            ? "Holding while QSO progresses; repeated/stuck stages will move on at the report limit."
            : KeepCallingActiveNewDxccUntilStale()
                ? "New DXCC persistence is active; move-on occurs when the station goes stale."
                : "Move-on is based on call attempts, not a timer.";
        DxAssist.LockedTargetText = $"Locked Target: {_lockedTarget.Callsign}";
        DxAssist.TargetSourceText = $"Target Source: {_targetSource}";
        DxAssist.TargetSourceRowText = TargetSourceRowText(_lockedTarget ?? _selectedIntendedTarget ?? DxAssist.BestTarget);
        DxAssist.WantedReasonText = string.IsNullOrWhiteSpace(_wantedReason)
            ? ""
            : $"Wanted Reason: {_wantedReason}{(string.IsNullOrWhiteSpace(_wantedSourceBlock) ? "" : $" ({_wantedSourceBlock})")}";
        DxAssist.BestCandidateText = $"Best Candidate: {TargetDisplay(DxAssist.BestTarget)}";
        DxAssist.SelectedIntendedTargetText = $"Selected Intended Target: {TargetDisplay(_selectedIntendedTarget)}";
        DxAssist.ActiveLockedTargetText = $"Active / Locked QSO Target: {TargetDisplay(_lockedTarget)}";
        DxAssist.ActualJtdxDxCallText = $"Actual JTDX DX Call: {(string.IsNullOrWhiteSpace(_actualJtdxDxCall) ? "None" : _actualJtdxDxCall)}";
        DxAssist.TargetStateWarningText = TargetStateWarning();
        DxAssist.CallAttemptsText = $"Call Attempts {CallAttemptProgressText()}";
        DxAssist.ReportRepeatsText = $"Report Repeats {_reportAttemptCount}/{Math.Max(1, Settings.Settings.MaxReportAttempts)}";
        DxAssist.TxMismatchText = WrongTargetStatusText();
        DxAssist.TxVerificationText = $"TX Verification: {_txVerificationState}";
        DxAssist.RecoveryModeText = $"Recovery Mode: {_recoveryMode}";
        DxAssist.LastProgressFromTarget = string.IsNullOrWhiteSpace(_lastProgressMessageFromTarget)
            ? "Last Progress From Target: None"
            : $"Last Progress From Target: {_lastProgressMessageFromTarget} at {_lastProgressTime:HH:mm:ss}";
        DxAssist.LastIntendedTx = string.IsNullOrWhiteSpace(_lastIntendedTxMessage)
            ? "Last Intended TX: Unknown"
            : $"Last Intended TX: {_lastIntendedTxMessage}";
        DxAssist.LastMyTx = $"Last My TX: {_lastObservedTxMessage}{(string.IsNullOrWhiteSpace(_lastObservedTxCycleTime) ? "" : $" at {_lastObservedTxCycleTime}")}";
        DxAssist.LastStageChange = _lastStageChangeAt == DateTime.MinValue
            ? "Last Stage Change: None"
            : $"Last Stage Change: {FormatQsoStage(_qsoStage)} at {_lastStageChangeAt:HH:mm:ss}";
        DxAssist.StuckReasonText = string.IsNullOrWhiteSpace(_stuckReason) ? "" : $"Stuck Reason: {_stuckReason}";
        DxAssist.LastCorrectiveAction = $"Last Corrective Action: {_lastCorrectiveAction}";
        DxAssist.LastObservedTransmitState = $"Last observed JTDX state: {_lastObservedTransmitState}";
        UpdateTargetStatusSummary();
    }

    private void UpdateTargetStatusSummary()
    {
        var target = _lockedTarget ?? _selectedIntendedTarget;
        var operatingMode = !_autoResume.IsRunning
            ? "Stopped"
            : _huntState == HuntState.InQso
                ? "QSO In Progress"
                : _operatingMode switch
                {
                    HuntingOperatingMode.WantedSniper => "Wanted Sniper Active",
                    HuntingOperatingMode.LocationHunt => $"Location Hunt: {Location.SelectedAreasDisplay}",
                    _ => "DX Assist"
                };

        CurrentTargetStatus.OperatingMode = operatingMode;
        CurrentTargetStatus.SelectedTargetCall = target?.Callsign ?? "";
        CurrentTargetStatus.SelectedTargetEntity = target?.Decode.EntityName ?? "";
        CurrentTargetStatus.SelectedTargetDisplay = target == null ? "No target selected" : TargetDisplayWithDash(target);
        CurrentTargetStatus.TargetSource = string.IsNullOrWhiteSpace(_targetSource) ? "None" : _targetSource;
        CurrentTargetStatus.WantedReason = target == null
            ? "Reason unavailable - check diagnostics"
            : TargetReasonFormatter.FormatGeneral(string.IsNullOrWhiteSpace(_wantedReason) ? target.PrimaryReason : _wantedReason);
        CurrentTargetStatus.WantedCategory = target == null ? "None" : SessionCategory(target);
        CurrentTargetStatus.WantedScope = ScopeDisplay(target?.Ranking.WantedScope ?? WantedScope.Overall);
        CurrentTargetStatus.NeedStatus = NeedStatusDisplay(target);
        CurrentTargetStatus.TierName = target?.Ranking.PriorityTierName ?? "";
        CurrentTargetStatus.ScoreOrTier = target == null ? "" : $"Score {target.Score}";
        CurrentTargetStatus.SelectionMethod = SelectionMethodDisplay();
        CurrentTargetStatus.QsoState = QsoStateDisplay();
        CurrentTargetStatus.QsoStage = FormatQsoStage(_qsoStage);
        CurrentTargetStatus.ExpectedJtdxDxCall = target?.Callsign ?? "";
        CurrentTargetStatus.ActualJtdxDxCall = string.IsNullOrWhiteSpace(_actualJtdxDxCall) ? "None" : _actualJtdxDxCall;
        CurrentTargetStatus.JtdxMatchStatus = JtdxMatchStatusDisplay(target);
        CurrentTargetStatus.TxGateStatus = TxGateStatusDisplay(target);
        CurrentTargetStatus.AttemptCounterLabel = AttemptCounterLabel();
        CurrentTargetStatus.PlainStatusMessage = PlainStatusMessage(target);
        CurrentTargetStatus.DebugStatusMessage = $"State {_huntState}; stage {_qsoStage}; confirmed JTDX {_targetConfirmedInJtdx}; confirmed feed {_targetConfirmedInFeed}; recovery {_recoveryMode}; correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)}; GUI clicks {LockedTargetGuiSelectionClickCount()}/{MaxGuiSelectionClicks()}.";
    }

    private static string TargetDisplayWithDash(DxTarget target)
    {
        var entity = target.Decode.EntityName;
        return string.IsNullOrWhiteSpace(entity) ? target.Callsign : $"{target.Callsign} - {entity}";
    }

    private static string ScopeDisplay(WantedScope scope) => scope switch
    {
        WantedScope.CurrentBand => "Current Band",
        WantedScope.CurrentMode => "Current Mode",
        WantedScope.CurrentBandMode => "Current Band + Mode",
        _ => "Overall"
    };

    private static string NeedStatusDisplay(DxTarget? target)
    {
        if (target == null)
            return "Unknown";
        return target.Ranking.DxccStatus switch
        {
            DxccCandidateStatus.NotWorked => "Never worked",
            DxccCandidateStatus.WorkedUnconfirmed => "Worked, not LoTW confirmed",
            DxccCandidateStatus.Confirmed => "LoTW confirmed",
            _ => "Unknown"
        };
    }

    private string SelectionMethodDisplay()
    {
        var text = DxAssist.SelectionMethodText ?? "";
        if (text.Contains("GuiGridDoubleClick", StringComparison.OrdinalIgnoreCase))
            return "GUI grid double-click";
        if (text.Contains("UdpReply", StringComparison.OrdinalIgnoreCase))
            return "UDP Reply";
        return _lockedTarget == null ? "None" : "Not checked";
    }

    private string QsoStateDisplay()
    {
        return _huntState switch
        {
            HuntState.Calling when _jtdxShowsWrongTx => "Correcting wrong target",
            HuntState.Calling => "Calling target",
            HuntState.InQso when _qsoStage == QsoStage.CompletionPending => "Completion pending",
            HuntState.InQso => "QSO in progress",
            _ => _autoResume.IsRunning ? "Idle" : "Stopped"
        };
    }

    private string JtdxMatchStatusDisplay(DxTarget? target)
    {
        if (target == null)
            return "Not applicable";
        if (string.IsNullOrWhiteSpace(_actualJtdxDxCall))
            return "Blank/unknown";
        return target.Callsign.Equals(_actualJtdxDxCall, StringComparison.OrdinalIgnoreCase)
            ? "Confirmed"
            : "Wrong target";
    }

    private string TxGateStatusDisplay(DxTarget? target)
    {
        if (!_autoResume.IsRunning)
            return "TX blocked - stopped";
        if (target == null)
            return "TX disabled - no target selected";
        if (!_targetConfirmedInJtdx)
            return $"TX blocked - waiting for JTDX confirmation of {target.Callsign}";
        return "TX allowed";
    }

    private string AttemptCounterLabel()
    {
        if (_huntState == HuntState.Calling
            && _lockedTarget != null
            && !ShouldUseUdpReplyForSource(_lockedTarget.Decode)
            && !_targetConfirmedInJtdx)
        {
            var correction = _jtdxShowsWrongTx
                ? $"; wrong-target cycles {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)}"
                : "";
            return $"GUI selection clicks {LockedTargetGuiSelectionClickCount()}/{MaxGuiSelectionClicks()}{correction}";
        }

        if (_huntState == HuntState.Calling && _jtdxShowsWrongTx)
            return $"Wrong target correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)}";
        if (_huntState == HuntState.Calling)
            return $"Call attempt {CallAttemptProgressText()}";
        if (_huntState == HuntState.InQso && _reportAttemptCount > 0)
            return $"Report repeat {_reportAttemptCount}/{Math.Max(1, Settings.Settings.MaxReportAttempts)}";
        if (_qsoStage == QsoStage.CompletionPending)
            return $"Completion grace {_completionGraceCycleCount}/{Math.Max(1, Settings.Settings.CompletionGraceCycles)}";
        return "";
    }

    private string PlainStatusMessage(DxTarget? target)
    {
        if (!_autoResume.IsRunning)
            return "DX Pilot monitoring is stopped.";
        if (target == null)
            return "No target selected.";
        if (_huntState == HuntState.Calling && _jtdxShowsWrongTx)
            return $"Wrong target correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)} - expected {target.Callsign}, JTDX currently shows {(string.IsNullOrWhiteSpace(_actualJtdxDxCall) ? "blank/unknown" : _actualJtdxDxCall)}.";
        if (_huntState == HuntState.Calling)
            return KeepCallingActiveNewDxccUntilStale()
                ? $"Calling New DXCC {target.Callsign} until it goes stale - call attempt {_callAttemptCount}."
                : $"Calling {target.Callsign} - call attempt {CallAttemptProgressText()}.";
        if (_huntState == HuntState.InQso)
            return _qsoStage == QsoStage.CompletionPending
                ? $"Completion pending with {target.Callsign} - waiting for ADIF/log confirmation."
                : $"QSO in progress with {target.Callsign} - {FormatQsoStage(_qsoStage)}.";
        return "No target selected.";
    }

    private void UpdateNextBestTargets()
    {
        var recent = CurrentCandidateDecodes();
        var selectable = recent
            .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
            .Where(d => !IsFailedReplySource(d))
            .Where(d => _lockedTarget == null || !DecodeTargetCall(d).Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)))
            .Where(d => !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .Where(d => !IsSuppressed(DecodeTargetCall(d)))
            .Where(IsSelectableDecodeForAcquisition)
            .ToList();

        var displayEligible = recent
            .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
            .Where(d => !IsFailedReplySource(d))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)))
            .Where(d => !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .ToList();

        var ranked = _targetSelector.SelectRanked(selectable, _logbook, _adifMergeResult.Indexes, Settings.Settings, 500, includeActiveQso: false);
        var displayRanked = _targetSelector.SelectRanked(displayEligible, _logbook, _adifMergeResult.Indexes, Settings.Settings, 50, includeActiveQso: false);

        // DX Assist is the sole display-rank authority. This list contains the
        // current non-stale table before checkbox filters, and it deliberately
        // includes the locked call at its natural score position. Sniper and
        // Location modes consume these ranks but never alter them.
        UpdateSharedDisplayRanks(displayRanked);
        UpdateSessionStationFields();

        TrackOpportunitiesSeen(displayRanked);
        DxAssist.NextBestTargets.Clear();
        foreach (var target in ranked.Take(8))
            DxAssist.NextBestTargets.Add(target);

        var candidateTargets = new List<DxTarget>();
        if (_lockedTarget != null)
            candidateTargets.Add(_lockedTarget);
        candidateTargets.AddRange(displayRanked.Where(t => _lockedTarget == null || !t.Callsign.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase)));

        var allRows = candidateTargets
            .Select((target, index) => BuildCandidateRow(target, index + 1))
            .ToList();
        var rows = allRows
            .Where(PassesCandidateFilters)
            .OrderBy(r => r.TargetStatus == "Locked" || r.TargetStatus == "Calling" || r.TargetStatus == "In QSO" ? 0 : 1)
            .ThenBy(r => r.Rank)
            .ToList();

        DxAssist.CandidateRows.Clear();
        foreach (var row in rows)
            DxAssist.CandidateRows.Add(row);
        ApplyCandidateRankSort();

        if (DxAssist.SelectedCandidate == null || rows.All(r => !r.Call.Equals(DxAssist.SelectedCandidate.Call, StringComparison.OrdinalIgnoreCase)))
            DxAssist.SelectedCandidate = rows.FirstOrDefault();

        if (_lockedTarget == null)
            UpdatePreviewBestTarget(rows.FirstOrDefault(row => IsSelectableDecodeForAcquisition(row.Target.Decode)));

        UpdateUniversalStationFields(allRows);
        UpdateLocationPanels();
    }

    private void UpdateSharedDisplayRanks(IReadOnlyList<DxTarget> ranked)
    {
        _displayRankByCall.Clear();
        var rank = 1;
        foreach (var target in ranked)
        {
            var call = CallsignNormalizer.Normalize(target.Callsign);
            if (!string.IsNullOrWhiteSpace(call) && _displayRankByCall.TryAdd(call, rank))
                rank++;
        }
    }

    private string DisplayRankText(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        return _displayRankByCall.TryGetValue(call, out var rank) ? rank.ToString() : "—";
    }

    private int? DisplayRankValue(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        return _displayRankByCall.TryGetValue(call, out var rank) ? rank : null;
    }

    private void UpdateUniversalStationFields(IReadOnlyList<DxCandidateRow> candidateRows)
    {
        var rowsByCall = candidateRows
            .GroupBy(row => CallsignNormalizer.Normalize(row.Call), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var decode in DxAssist.RecentDecodes)
        {
            var call = CallsignNormalizer.Normalize(DecodeTargetCall(decode));
            rowsByCall.TryGetValue(call, out var row);
            var workedCall = WorkedCallDisplay(call);
            decode.RankText = DisplayRankText(call);
            decode.JtdxRow = JtdxRowText(decode);
            decode.AgeText = FormatAge(DateTime.UtcNow - LastHeardUtc(call, decode));
            decode.WantedReasonDisplay = row?.WantedReason ?? DecodeWantedReason(decode);
            decode.StationStatusDisplay = row?.TargetStatus ?? DecodeStationStatus(decode);
            decode.WasCallWorkedBefore = workedCall.Worked;
            decode.WorkedCallToolTip = workedCall.ToolTip;
        }

        foreach (var item in Wanted.WantedDxcc
                     .Concat(Wanted.WantedGrids)
                     .Concat(Wanted.WantedStates)
                     .Concat(Wanted.WantedBandMode))
        {
            var call = WantedItemTargetCall(item);
            var workedCall = WorkedCallDisplay(call);
            item.RankText = DisplayRankText(call);
            item.WasCallWorkedBefore = workedCall.Worked;
            item.WorkedCallToolTip = workedCall.ToolTip;
            item.RefreshVisualFields();
        }

        System.Windows.Data.CollectionViewSource.GetDefaultView(DxAssist.RecentDecodes).Refresh();
    }

    private void UpdateSessionStationFields()
    {
        foreach (var item in SessionHistory.AllOpportunities)
        {
            var workedCall = WorkedCallDisplay(item.Call);
            item.RankText = DisplayRankText(item.Call);
            item.WasCallWorkedBefore = workedCall.Worked;
            item.WorkedCallToolTip = workedCall.ToolTip;
            var latestDecode = _decodeHistory
                .Where(decode => DecodeTargetCall(decode).Equals(item.Call, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(DecodeSeenUtc)
                .FirstOrDefault();
            item.JtdxRow = latestDecode == null ? "—" : JtdxRowText(latestDecode);
        }
    }

    private (bool Worked, string ToolTip) WorkedCallDisplay(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        if (string.IsNullOrWhiteSpace(call)
            || !_workedCallDisplayByCall.TryGetValue(call, out var status))
        {
            return (false, "");
        }

        var qsoLabel = status.QsoCount == 1 ? "QSO" : "QSOs";
        var lastWorked = status.LastWorkedDate?.ToString("dd MMM yyyy") ?? "Date unavailable";
        var lotw = status.LoTWConfirmedAny ? "Confirmed" : "Not confirmed";
        var otherConfirmations = new List<string>();
        if (status.PaperConfirmedAny)
            otherConfirmations.Add("paper QSL");
        if (status.EqslConfirmedAny)
            otherConfirmations.Add("eQSL");
        var other = otherConfirmations.Count == 0
            ? "None recorded"
            : string.Join(", ", otherConfirmations);

        return (true,
            $"Worked before: {status.QsoCount} {qsoLabel}\n"
            + $"Last worked: {lastWorked}\n"
            + $"LoTW: {lotw}\n"
            + $"Other confirmations: {other}\n"
            + $"Log source: {string.Join(" + ", status.Sources.OrderBy(source => source).Select(DisplaySource))}\n"
            + "Visual marker only — ranking and targeting are unchanged.");
    }

    private void RebuildWorkedCallDisplayIndex()
    {
        _workedCallDisplayByCall.Clear();
        foreach (var qso in _logbook)
        {
            var call = CallsignNormalizer.Normalize(qso.Call);
            if (string.IsNullOrWhiteSpace(call))
                continue;

            if (!_workedCallDisplayByCall.TryGetValue(call, out var status))
            {
                status = new WorkedCallDisplayInfo();
                _workedCallDisplayByCall[call] = status;
            }

            status.QsoCount++;
            status.LoTWConfirmedAny |= qso.LotwConfirmed;
            status.PaperConfirmedAny |= qso.PaperConfirmed;
            status.EqslConfirmedAny |= qso.EqslConfirmed;
            if (qso.QsoDate.HasValue
                && (!status.LastWorkedDate.HasValue || qso.QsoDate.Value > status.LastWorkedDate.Value))
            {
                status.LastWorkedDate = qso.QsoDate;
            }

            foreach (var source in qso.Source.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                status.Sources.Add(source);
        }
    }

    private static string DecodeWantedReason(DecodeMessage decode)
    {
        if (decode.IsNewDxcc)
            return "New DXCC";
        if (decode.IsUnconfirmedDxcc)
            return "Unconfirmed DXCC";
        if (decode.IsNewGrid)
            return string.IsNullOrWhiteSpace(decode.Grid) ? "New grid" : $"New grid {decode.Grid}";
        if (decode.IsNewState)
            return string.IsNullOrWhiteSpace(decode.State) ? "New state" : $"New state {decode.State}";
        return "";
    }

    private int LockedTargetGuiSelectionClickCount()
    {
        if (_lockedTarget == null || ShouldUseUdpReplyForSource(_lockedTarget.Decode))
            return 0;

        return GuiSelectionClickCount(ReplySourceKey(_lockedTarget.Decode));
    }

    private string DecodeStationStatus(DecodeMessage decode)
    {
        var call = DecodeTargetCall(decode);
        if (IsPermanentlySuppressed(call) || IsSuppressed(call))
            return "Suppressed";
        if (!RadioContextReadyForSelection())
            return "Rows settling";
        if (!decode.Targetable || decode.ParseConfidence == ParseConfidence.Low)
            return "Not targetable";
        if (!IsSelectableDecodeForAcquisition(decode))
            return "Off JTDX grid";
        return IsFreshDecode(decode) ? "Candidate" : "Stale";
    }

    private void UpdateLocationPanels()
    {
        var targets = _targetSelector.SelectLocationRanked(
            CurrentCandidateDecodes(),
            _logbook,
            _adifMergeResult.Indexes,
            Settings.Settings,
            300);
        var definitions = LocationPanelDefinitions(Location.SelectedAreaKeys);

        var panelLayoutChanged = Location.Panels.Count != definitions.Count
            || Location.Panels
                .Select((panel, index) => !panel.Key.Equals(definitions[index].Key, StringComparison.Ordinal))
                .Any(changed => changed);
        if (panelLayoutChanged)
        {
            Location.Panels.Clear();
            foreach (var definition in definitions)
                Location.Panels.Add(new LocationPanelViewModel(definition.Key, definition.Title));
        }

        for (var panelIndex = 0; panelIndex < definitions.Count; panelIndex++)
        {
            var definition = definitions[panelIndex];
            var panel = Location.Panels[panelIndex];
            var matching = targets.Where(target => MatchesLocationRegion(target, definition.Key)).Take(35).ToList();
            var rank = 1;
            var desiredRows = matching
                .Select(target =>
                {
                    var row = BuildCandidateRow(target, rank++);
                    row.LocationDetail = definition.Key.Equals("IOTA", StringComparison.OrdinalIgnoreCase)
                        ? row.Iota
                        : row.State;
                    return row;
                })
                .ToList();
            SynchronizeLocationCandidates(panel.Candidates, desiredRows);

            var actionable = matching.Count(target => !IsSuppressed(target.Callsign) && IsSelectableDecodeForAcquisition(target.Decode));
            panel.Summary = matching.Count == 0
                ? "No recent decodes."
                : $"{matching.Count} station{(matching.Count == 1 ? "" : "s")}; {actionable} actionable.";
        }

        var total = targets.Count;
        Location.Status = _operatingMode == HuntingOperatingMode.LocationHunt && _autoResume.IsRunning
            ? $"Location Hunt active: {Location.SelectedAreasDisplay}. Monitoring {total} recent station{(total == 1 ? "" : "s")}."
            : $"Passive view: {total} recent station{(total == 1 ? "" : "s")}. Selected hunt areas: {Location.SelectedAreasDisplay}.";
    }

    private static void SynchronizeLocationCandidates(
        ObservableCollection<DxCandidateRow> currentRows,
        IReadOnlyList<DxCandidateRow> desiredRows)
    {
        for (var desiredIndex = 0; desiredIndex < desiredRows.Count; desiredIndex++)
        {
            var desired = desiredRows[desiredIndex];
            var currentIndex = -1;
            for (var searchIndex = desiredIndex; searchIndex < currentRows.Count; searchIndex++)
            {
                if (currentRows[searchIndex].Call.Equals(desired.Call, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = searchIndex;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                currentRows.Insert(desiredIndex, desired);
                continue;
            }

            if (currentIndex != desiredIndex)
                currentRows.Move(currentIndex, desiredIndex);

            currentRows[desiredIndex].UpdateFrom(desired);
        }

        while (currentRows.Count > desiredRows.Count)
            currentRows.RemoveAt(currentRows.Count - 1);
    }

    private static IReadOnlyList<(string Key, string Title)> LocationPanelDefinitions(IEnumerable<string> selectedAreaKeys)
    {
        var selected = selectedAreaKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new (string Key, string Title)[]
        {
            ("USA", "USA"),
            ("AF", "Africa"),
            ("AS", "Asia"),
            ("EU", "Europe"),
            ("NA", "North America (outside USA)"),
            ("SA", "South America"),
            ("OC", "Oceania"),
            ("IOTA", "Known IOTA stations"),
            ("OTHER", "Antarctica / unresolved")
        }
        .Where(definition => selected.Contains(definition.Key))
        .ToList();
    }

    private bool MatchesSelectedLocationAreas(DxTarget target)
    {
        return LocationPanelDefinitions(Location.SelectedAreaKeys)
            .Any(definition => MatchesLocationRegion(target, definition.Key));
    }

    private static bool MatchesLocationRegion(DxTarget target, string region)
    {
        var decode = target.Decode;
        var isUsa = WasStateEligibility.IsEligible(decode);
        var continent = (decode.Continent ?? "").Trim().ToUpperInvariant();

        return region switch
        {
            "USA" => isUsa,
            "IOTA" => !string.IsNullOrWhiteSpace(decode.Iota),
            "NA" => continent == "NA" && !isUsa,
            "AF" or "AS" or "EU" or "SA" or "OC" => continent == region,
            "OTHER" => continent is "AN" or "" || !new[] { "AF", "AS", "EU", "NA", "SA", "OC" }.Contains(continent),
            _ => false
        };
    }

    private void RequestNextBestTargetsUpdate()
    {
        if (_candidateRefreshTimer.IsEnabled)
            return;

        _candidateRefreshTimer.Start();
    }

    private void RequestLiveAdifReload()
    {
        _adifReloadTimer.Stop();
        _adifReloadTimer.Start();
    }

    private void UpdatePreviewBestTarget(DxCandidateRow? row)
    {
        if (row == null)
        {
            DxAssist.BestTarget = null;
            Dashboard.BestTarget = "No target selected.";
            Dashboard.BestReason = "";
            return;
        }

        DxAssist.BestTarget = row.Target;
        UpdateBestTarget(row.Target);
    }

    private void ApplyCandidateRankSort()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(DxAssist.CandidateRows);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(DxCandidateRow.Rank), ListSortDirection.Ascending));
        view.Refresh();
    }

    private DxCandidateRow BuildCandidateRow(DxTarget target, int rank)
    {
        var decode = target.Decode;
        var ranking = target.Ranking;
        var displayRank = DisplayRankValue(target.Callsign);
        var workedCall = WorkedCallDisplay(target.Callsign);
        var age = DateTime.UtcNow - LastHeardUtc(target.Callsign, decode);
        var dxccStatus = FormatDxccStatus(ranking.DxccStatus);
        var gridStatus = GridStatus(decode);
        var stateStatus = StateStatus(decode);
        var targetStatus = TargetStatus(target, decode, age);
        var opportunityClass = CandidateOpportunityClass(ranking);
        var wantedReason = string.IsNullOrWhiteSpace(ranking.PrimaryWantedReason)
            ? FriendlyWantedReason(target, dxccStatus, gridStatus, stateStatus)
            : ranking.PrimaryWantedReason;

        return new DxCandidateRow
        {
            JtdxRow = JtdxRowText(decode),
            Rank = displayRank ?? rank,
            RankText = displayRank?.ToString() ?? "—",
            Call = target.Callsign,
            WasCallWorkedBefore = workedCall.Worked,
            WorkedCallToolTip = workedCall.ToolTip,
            Country = string.IsNullOrWhiteSpace(decode.EntityName) ? decode.PrimaryDisplayEntity : decode.EntityName,
            Continent = decode.Continent,
            Iota = decode.Iota,
            Dxcc = decode.Dxcc,
            Tier = ranking.PriorityTierName,
            WantedReason = wantedReason,
            DxccStatus = dxccStatus,
            RarityRank = ranking.RarityRank,
            RarityScore = ranking.RarityScore,
            Grid = decode.Grid,
            GridSource = string.IsNullOrWhiteSpace(decode.GridSource) ? "" : decode.GridSource,
            GridStatus = gridStatus,
            State = decode.State,
            StateSource = string.IsNullOrWhiteSpace(decode.StateSource) ? "" : decode.StateSource,
            StateStatus = stateStatus,
            QrzStatus = decode.CallsignLookupStatus.ToString(),
            Rarity = ranking.RarityRank.HasValue ? $"#{ranking.RarityRank}" : "default",
            DistanceMiles = decode.DistanceMiles,
            Age = FormatAge(age),
            Snr = decode.Snr,
            SourceType = decode.MessageTypeText,
            Score = target.Score,
            TargetStatus = targetStatus,
            PriorityClass = opportunityClass,
            OpportunityClass = opportunityClass,
            ActionStateClass = CandidateActionStateClass(targetStatus, IsPermanentlySuppressed(target.Callsign)),
            IsPermanentlySuppressed = IsPermanentlySuppressed(target.Callsign),
            Details = BuildCandidateDetails(target, dxccStatus, gridStatus, stateStatus, age),
            Target = target
        };
    }

    private bool PassesCandidateFilters(DxCandidateRow row)
    {
        if (DxAssist.ShowOnlyTargetable && row.TargetStatus is "Watch only" or "Not targetable" or "Off JTDX grid")
            return false;
        if (row.TargetStatus == "Worked live")
            return false;
        if (DxAssist.ShowWantedOnly && row.DxccStatus is "Confirmed" && row.GridStatus is not "New" && row.StateStatus is not "New")
            return false;
        if (!DxAssist.ShowWorkedConfirmed && row.DxccStatus is "Worked, unconfirmed" or "Confirmed")
            return false;
        if (!DxAssist.ShowStale && row.TargetStatus == "Stale")
            return false;
        if (!DxAssist.ShowSuppressed && row.TargetStatus == "Suppressed")
            return false;
        return true;
    }

    private static string FormatDxccStatus(DxccCandidateStatus status) => status switch
    {
        DxccCandidateStatus.NotWorked => "Not worked",
        DxccCandidateStatus.WorkedUnconfirmed => "Worked, unconfirmed",
        DxccCandidateStatus.Confirmed => "Confirmed",
        _ => "Unknown"
    };

    private string GridStatus(DecodeMessage decode)
    {
        if (string.IsNullOrWhiteSpace(decode.Grid))
            return "No grid";
        if (!_adifMergeResult.Indexes.Grids.TryGetValue(decode.Grid, out var status))
            return "New";
        return status.ConfirmedAny ? "Confirmed" : "Worked";
    }

    private string StateStatus(DecodeMessage decode)
    {
        if (!WasStateEligibility.IsEligible(decode))
            return "Not USA";
        if (string.IsNullOrWhiteSpace(decode.State))
            return "Unknown";
        return _adifMergeResult.Indexes.States.ContainsKey(decode.State) ? "Worked" : "New";
    }

    private string TargetStatus(DxTarget target, DecodeMessage decode, TimeSpan age)
    {
        if (_lockedTarget != null && target.Callsign.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase))
            return _huntState == HuntState.InQso ? "In QSO" : _huntState == HuntState.Calling ? "Calling" : "Locked";
        if (_sessionWorked.Contains(target.Callsign) || IsRecentlyWorkedLive(target.Callsign))
            return "Worked live";
        if (IsSuppressed(target.Callsign))
            return "Suppressed";
        if (!RadioContextReadyForSelection())
            return "Rows settling";
        if (!decode.Targetable)
            return decode.ParseConfidence == ParseConfidence.Low ? "Not targetable" : "Watch only";
        if (!IsSelectableDecodeForAcquisition(decode))
            return "Off JTDX grid";
        if (age.TotalSeconds > CandidateStaleSeconds(decode))
            return "Stale";
        return "Candidate";
    }

    private static string FriendlyWantedReason(DxTarget target, string dxccStatus, string gridStatus, string stateStatus)
    {
        if (dxccStatus == "Not worked")
            return "New DXCC";
        if (dxccStatus == "Worked, unconfirmed")
            return "Unconfirmed DXCC";
        if (gridStatus == "New")
            return target.Decode.Grid.Length > 0 ? $"New grid: {target.Decode.Grid}" : "New grid";
        if (stateStatus == "New")
            return string.IsNullOrWhiteSpace(target.Decode.State) ? "New state" : $"New state: {target.Decode.State}";
        return target.PrimaryReason;
    }

    private static string CandidateOpportunityClass(CandidateRanking ranking)
    {
        return ranking.PriorityTier switch
        {
            10 => "NewDxcc",
            12 or 13 or 14 => "BandMode",
            15 => "UnconfirmedDxcc",
            20 => "RareDxcc",
            30 or 34 => "NewGrid",
            31 or 32 or 33 => "BandMode",
            40 or 44 => "NewState",
            41 or 42 or 43 => "BandMode",
            60 => "BandMode",
            _ => ""
        };
    }

    private static string CandidateActionStateClass(string targetStatus, bool permanentlySuppressed)
    {
        if (permanentlySuppressed)
            return "PermanentlySuppressed";

        return targetStatus switch
        {
            "Suppressed" => "Suppressed",
            "Off JTDX grid" => "NotContactable",
            "Stale" or "Watch only" or "Not targetable" or "Worked live" or "Rows settling" => "Muted",
            "Locked" or "Calling" => "Calling",
            "In QSO" => "InProgress",
            _ => "Actionable"
        };
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60)
            return $"{Math.Max(0, (int)age.TotalSeconds)}s";
        return $"{(int)age.TotalMinutes}m {age.Seconds:00}s";
    }

    private string BuildCandidateDetails(DxTarget target, string dxccStatus, string gridStatus, string stateStatus, TimeSpan age)
    {
        var decode = target.Decode;
        var ranking = target.Ranking;
        var distance = decode.DistanceMiles.HasValue ? $"{decode.DistanceMiles.Value:0} mi ({decode.DistanceSource})" : "Unknown";
        return $"{target.Callsign} - {decode.EntityName}\n"
            + $"{ranking.PriorityTierName}\n"
            + $"DXCC: {decode.Dxcc}  Status: {dxccStatus}  DXCC confirmation: {ranking.DxccConfirmationMode}\n"
            + $"Radio: {decode.Band} {decode.Mode}  Dial frequency: {(decode.DialFrequencyHz == 0 ? "Unknown" : $"{decode.DialFrequencyHz / 1_000_000d:0.000000} MHz")}\n"
            + $"Worked: {ranking.DxccWorked}  Confirmed: {ranking.DxccConfirmed}  Source: {DisplaySource(ranking.DxccConfirmationSource)}\n"
            + $"Rarity rank: {ranking.RarityRank?.ToString() ?? "default"}  Rarity score: {ranking.RarityScore}  Match: {ranking.RarityMatchSource}/{ranking.RarityMatchConfidence}\n"
            + $"Grid: {(string.IsNullOrWhiteSpace(decode.Grid) ? "None" : decode.Grid)}  Grid status: {gridStatus}\n"
            + $"Grid source: {(string.IsNullOrWhiteSpace(decode.GridSource) ? "Unknown" : decode.GridSource)}  QRZ grid: {(string.IsNullOrWhiteSpace(decode.QrzGrid) ? "None" : decode.QrzGrid)}\n"
            + $"State: {(string.IsNullOrWhiteSpace(decode.State) ? "None" : decode.State)}  State status: {stateStatus}  State source: {(string.IsNullOrWhiteSpace(decode.StateSource) ? "Unknown" : decode.StateSource)}\n"
            + $"QRZ status: {decode.CallsignLookupStatus}  Data source: {decode.CallsignDataSource}\n"
            + $"Last heard: {FormatAge(age)} ago  SNR: {decode.Snr}  DT: {decode.Dt:0.0}  Offset: {decode.AudioOffset}\n"
            + $"{TargetSourceRowText(target)}\n"
            + $"Distance: {distance}\n"
            + $"Source: {decode.RawText}\n"
            + $"Type: {decode.MessageTypeText}\n"
            + $"Score: {target.Score}  Breakdown: {ranking.ScoreBreakdown}\n"
            + $"Reason: {ranking.SelectionExplanation}\n"
            + $"Why: {string.Join("; ", target.Reasons)}";
    }

    private void TrackOpportunitiesSeen(IEnumerable<DxTarget> targets)
    {
        if (!Settings.Settings.EnableSessionDxHistory)
            return;

        ExpireSessionHistory();
        foreach (var target in targets)
        {
            if (!ShouldTrackOpportunity(target, selectedOrCalled: false, worked: false))
                continue;

            var item = UpsertSessionOpportunity(target);
            item.SeenCount = item.SeenCount <= 0 ? 1 : item.SeenCount + 1;
            item.DirectlyHeardCount++;
            item.Outcome = item.WasCalled ? item.Outcome : "Seen only";
            AddSessionTimeline(item, $"Seen: {target.Decode.RawText}");
        }

        SessionHistory.Refresh();
    }

    private void TrackOpportunitySelected(DxTarget target, bool manual)
    {
        if (!Settings.Settings.EnableSessionDxHistory)
            return;

        var item = UpsertSessionOpportunity(target);
        if (item.SeenCount <= 0)
            item.SeenCount = 1;
        item.WasCalled = true;
        item.WasAutoSelected |= !manual;
        item.WasManuallySelected |= manual;
        item.AttemptCount++;
        item.LastAttemptUtc = DateTime.UtcNow;
        item.Outcome = "Called";
        item.OutcomeReason = manual ? "Manual Wanted target selected" : "DX Pilot selected target";
        AddSessionTimeline(item, manual ? "Manual-selected" : "Auto-selected");
        AddSessionTimeline(item, "UDP Reply sent");
        SessionHistory.Refresh();
    }

    private void TrackOpportunityAttempt(DxTarget? target)
    {
        if (!Settings.Settings.EnableSessionDxHistory || target == null)
            return;

        var item = UpsertSessionOpportunity(target);
        if (item.SeenCount <= 0)
            item.SeenCount = 1;
        item.WasCalled = true;
        item.AttemptCount = Math.Max(item.AttemptCount + 1, _callAttemptCount);
        item.LastAttemptUtc = DateTime.UtcNow;
        item.Outcome = _huntState == HuntState.InQso ? "In progress" : "Called";
        item.OutcomeReason = $"Call attempt {CallAttemptProgressText()}";
        AddSessionTimeline(item, item.OutcomeReason);
        SessionHistory.Refresh();
    }

    private void TrackOpportunityWorked(DxTarget target, string reason)
    {
        if (!Settings.Settings.EnableSessionDxHistory)
            return;

        var item = UpsertSessionOpportunity(target);
        item.WasWorked = true;
        item.WorkedUtc = DateTime.UtcNow;
        item.WorkedSource = "LiveJTDXADIF";
        item.Outcome = "Worked/logged";
        item.OutcomeReason = reason;
        AddSessionTimeline(item, $"Worked: {reason}");
        SessionHistory.Refresh();
    }

    private void TrackOpportunitySuppressed(string callsign, DateTime until, string reason)
    {
        if (!Settings.Settings.EnableSessionDxHistory)
            return;

        var item = FindSessionOpportunity(callsign);
        if (item == null)
            return;

        item.Outcome = "Suppressed";
        item.OutcomeReason = reason;
        item.SuppressedUntilUtc = until.ToUniversalTime();
        AddSessionTimeline(item, $"Suppressed until {until:HH:mm:ss}: {reason}");
        SessionHistory.Refresh();
    }

    private void TrackOpportunityReleased(DxTarget? target, string reason)
    {
        if (!Settings.Settings.EnableSessionDxHistory || target == null)
            return;

        var item = FindSessionOpportunity(target.Callsign);
        if (item == null || item.WasWorked)
            return;

        if (reason.Contains("Abandoned - TX stopped", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Abandoned - TX stopped";
            item.OutcomeReason = reason;
        }
        else if (reason.Contains("Abandoned - DX Pilot stopped", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Abandoned - DX Pilot stopped";
            item.OutcomeReason = reason;
        }
        else if (reason.Contains("Missed - no progress", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Missed - no progress";
            item.OutcomeReason = reason;
        }
        else if (reason.Contains("Missed - no reply", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("call cycles", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("without QSO progress", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("Call attempts failed", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Missed - no reply";
            item.OutcomeReason = reason.Contains("Missed - no reply", StringComparison.OrdinalIgnoreCase) ? reason : "Call attempts exceeded";
        }
        else if (reason.Contains("report repeats", StringComparison.OrdinalIgnoreCase) || reason.Contains("stuck", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Missed - no reply";
            item.OutcomeReason = "Report repeats exceeded";
        }
        else if (reason.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Missed - wrong target / TX mismatch";
            item.OutcomeReason = "TX mismatch exceeded";
        }
        else if (reason.Contains("stopped", StringComparison.OrdinalIgnoreCase))
        {
            item.OutcomeReason = reason;
        }

        AddSessionTimeline(item, $"{item.Outcome}: {reason}");
        SessionHistory.Refresh();
    }

    private bool ShouldTrackOpportunity(DxTarget target, bool selectedOrCalled, bool worked)
    {
        if (worked || selectedOrCalled)
            return true;

        var ranking = target.Ranking;
        if (ranking.DxccStatus is DxccCandidateStatus.NotWorked or DxccCandidateStatus.WorkedUnconfirmed)
            return true;

        if (ranking.RarityRank.HasValue && ranking.RarityRank.Value <= Math.Max(1, Settings.Settings.RareDxccRankThreshold))
            return true;

        return ranking.PriorityTier <= 44
            || ranking.PriorityTier == 60
            || (ranking.WantedScope != WantedScope.Overall
                && ranking.NeedStatus is NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed);
    }

    private SessionDxOpportunity UpsertSessionOpportunity(DxTarget target)
    {
        var key = SessionOpportunityKey(target);
        var item = SessionHistory.AllOpportunities.FirstOrDefault(o => o.OpportunityId.Equals(key, StringComparison.OrdinalIgnoreCase));
        var sourceSeenUtc = DecodeSeenUtc(target.Decode);
        var lastHeardUtc = LastHeardUtc(target.Callsign, target.Decode);
        if (item == null)
        {
            item = new SessionDxOpportunity
            {
                OpportunityId = key,
                FirstSeenUtc = sourceSeenUtc,
                LastSeenUtc = lastHeardUtc,
                Call = target.Callsign
            };
            SessionHistory.AllOpportunities.Add(item);
        }

        var decode = target.Decode;
        var ranking = target.Ranking;
        if (lastHeardUtc > item.LastSeenUtc)
            item.LastSeenUtc = lastHeardUtc;
        if (item.FirstSeenUtc == DateTime.MinValue || sourceSeenUtc < item.FirstSeenUtc)
            item.FirstSeenUtc = sourceSeenUtc;
        item.Call = target.Callsign;
        var workedCall = WorkedCallDisplay(target.Callsign);
        item.WasCallWorkedBefore = workedCall.Worked;
        item.WorkedCallToolTip = workedCall.ToolTip;
        item.RankText = DisplayRankText(target.Callsign);
        item.JtdxRow = JtdxRowText(target.Decode);
        item.IsPermanentlySuppressed = IsPermanentlySuppressed(target.Callsign);
        item.Entity = string.IsNullOrWhiteSpace(decode.EntityName) ? ranking.Entity : decode.EntityName;
        item.DxccNumber = decode.Dxcc;
        item.DxccStatus = FormatSessionDxccStatus(ranking.DxccStatus);
        item.Category = SessionCategory(target);
        item.Need = SessionNeed(target);
        item.Scope = ScopeDisplay(ranking.WantedScope);
        item.Band = decode.Band;
        item.Mode = decode.Mode;
        item.DialFrequencyHz = decode.DialFrequencyHz;
        item.RarityRank = ranking.RarityRank;
        item.RarityScore = ranking.RarityScore;
        item.PriorityTier = ranking.PriorityTier;
        item.PriorityTierName = ranking.PriorityTierName;
        item.PrimaryReason = TargetReasonFormatter.FormatGeneral(ranking.PrimaryWantedReason);
        item.LastSnr = decode.Snr;
        item.BestSnr = item.BestSnr == int.MinValue ? decode.Snr : Math.Max(item.BestSnr, decode.Snr);
        item.BestDistance = item.BestDistance.HasValue && decode.DistanceMiles.HasValue
            ? Math.Max(item.BestDistance.Value, decode.DistanceMiles.Value)
            : decode.DistanceMiles ?? item.BestDistance;
        item.Grid = decode.Grid;
        item.State = decode.State;
        item.GridSource = decode.GridSource;
        item.SourceRawMessage = decode.RawText;
        item.SourceType = decode.MessageTypeText;
        if (!string.IsNullOrWhiteSpace(decode.RawText)
            && (item.RawMessages.Count == 0 || !item.RawMessages[^1].Equals(decode.RawText, StringComparison.Ordinal)))
        {
            item.RawMessages.Add(decode.RawText);
            while (item.RawMessages.Count > 20)
                item.RawMessages.RemoveAt(0);
        }

        return item;
    }

    private static DateTime DecodeSeenUtc(DecodeMessage decode)
    {
        var seen = decode.ReceivedAt.Kind == DateTimeKind.Utc
            ? decode.ReceivedAt
            : decode.ReceivedAt.ToUniversalTime();
        return seen == DateTime.MinValue ? DateTime.UtcNow : seen;
    }

    private void RecordLastHeard(DecodeMessage decode)
    {
        var call = DecodeTargetCall(decode);
        if (string.IsNullOrWhiteSpace(call))
            return;

        var seenUtc = DecodeSeenUtc(decode);
        if (!_lastHeardUtcByCall.TryGetValue(call, out var current) || seenUtc > current)
            _lastHeardUtcByCall[call] = seenUtc;
    }

    private DateTime LastHeardUtc(string callsign, DecodeMessage fallback)
    {
        var call = string.IsNullOrWhiteSpace(callsign) ? DecodeTargetCall(fallback) : callsign.Trim();
        return !string.IsNullOrWhiteSpace(call) && _lastHeardUtcByCall.TryGetValue(call, out var lastHeard)
            ? lastHeard
            : DecodeSeenUtc(fallback);
    }

    private SessionDxOpportunity? FindSessionOpportunity(string callsign)
    {
        return SessionHistory.AllOpportunities
            .Where(o => o.Call.Equals(callsign, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.LastSeenUtc)
            .FirstOrDefault();
    }

    private string SessionOpportunityKey(DxTarget target)
    {
        var dxcc = string.IsNullOrWhiteSpace(target.Decode.Dxcc) ? "UNKNOWN" : target.Decode.Dxcc;
        var radioContext = $"{target.Decode.Band}:{target.Decode.Mode}".ToUpperInvariant();
        return Settings.Settings.SessionHistoryGroupMode.Equals("ByDXCC", StringComparison.OrdinalIgnoreCase)
            ? $"{dxcc}:{radioContext}"
            : $"{dxcc}:{target.Callsign.ToUpperInvariant()}:{radioContext}";
    }

    private static string FormatSessionDxccStatus(DxccCandidateStatus status) => status switch
    {
        DxccCandidateStatus.NotWorked => "New DXCC",
        DxccCandidateStatus.WorkedUnconfirmed => "Worked unconfirmed",
        DxccCandidateStatus.Confirmed => "Confirmed",
        _ => "Unknown"
    };

    private static string SessionCategory(DxTarget target)
    {
        var reason = target.Ranking.PrimaryWantedReason;
        if (target.Ranking.DxccStatus is DxccCandidateStatus.NotWorked or DxccCandidateStatus.WorkedUnconfirmed)
            return "DXCC";
        if (reason.Contains("DXCC", StringComparison.OrdinalIgnoreCase))
            return "DXCC";
        if (reason.Contains("grid", StringComparison.OrdinalIgnoreCase))
            return "Grid";
        if (reason.Contains("USA state", StringComparison.OrdinalIgnoreCase) || reason.Contains("state", StringComparison.OrdinalIgnoreCase))
            return "USA State";
        if (reason.Contains("Rare confirmed DXCC", StringComparison.OrdinalIgnoreCase))
            return "Rare confirmed DXCC";
        return "General";
    }

    private static string SessionNeed(DxTarget target)
    {
        var reason = target.Ranking.PrimaryWantedReason;
        if (reason.StartsWith("New ", StringComparison.OrdinalIgnoreCase))
            return "New";
        if (reason.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase))
            return "Unconfirmed";
        if (target.Ranking.DxccStatus == DxccCandidateStatus.Confirmed)
            return "Confirmed";
        return "Unknown";
    }

    private static void AddSessionTimeline(SessionDxOpportunity item, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var line = $"{DateTime.Now:HH:mm:ss} {text}";
        if (item.Timeline.Count == 0 || !item.Timeline[^1].Equals(line, StringComparison.Ordinal))
            item.Timeline.Add(line);
        while (item.Timeline.Count > 40)
            item.Timeline.RemoveAt(0);
    }

    private void ExpireSessionHistory()
    {
        var expiry = Settings.Settings.SessionHistoryExpiryMinutes;
        if (expiry <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddMinutes(-expiry);
        for (var i = SessionHistory.AllOpportunities.Count - 1; i >= 0; i--)
        {
            if (SessionHistory.AllOpportunities[i].LastSeenUtc < cutoff)
                SessionHistory.AllOpportunities.RemoveAt(i);
        }
    }

    private void ExportSessionHistory()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"DXPilot-Session-History-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("FirstSeen,LastSeen,Age,Call,Country,DXCC,DXCCStatus,Band,Mode,DialFrequencyHz,Scope,RarityRank,RarityScore,Reason,BestSNR,LastSNR,Grid,SeenCount,Attempts,Outcome,OutcomeReason,Worked,WorkedSource,SourceType,SourceRawMessage");
        foreach (var item in SessionHistory.AllOpportunities.OrderBy(o => o.FirstSeenUtc))
        {
            sb.AppendLine(string.Join(",",
                Csv(item.FirstSeenText),
                Csv(item.LastSeenText),
                Csv(item.AgeText),
                Csv(item.Call),
                Csv(item.Entity),
                Csv(item.DxccNumber),
                Csv(item.DxccStatus),
                Csv(item.Band),
                Csv(item.Mode),
                Csv(item.DialFrequencyHz.ToString()),
                Csv(item.Scope),
                Csv(item.RarityRankText),
                Csv(item.RarityScore.ToString()),
                Csv(item.PrimaryReason),
                Csv(item.BestSnrText),
                Csv(item.LastSnr.ToString()),
                Csv(item.Grid),
                Csv(item.SeenCount.ToString()),
                Csv(item.AttemptCount.ToString()),
                Csv(item.Outcome),
                Csv(item.OutcomeReason),
                Csv(item.WorkedText),
                Csv(item.WorkedSource),
                Csv(item.SourceType),
                Csv(item.SourceRawMessage)));
        }

        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        SessionHistory.Status = $"Session history exported: {dialog.FileName}";
    }

    private void ExportRecentActions()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"DXPilot-Recent-Actions-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("DX Pilot for JTDX Recent Actions Export");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Overall: {Dashboard.OverallStatus}");
        sb.AppendLine($"UDP: {Dashboard.UdpStatus}");
        sb.AppendLine($"DX Pilot: {Dashboard.AutoResumeStatus}");
        sb.AppendLine($"Hunt State: {Dashboard.HuntState}");
        sb.AppendLine($"Radio Context: {_radioContext?.Display ?? "Unknown"}");
        sb.AppendLine($"Dial Frequency Hz: {_radioContext?.DialFrequencyHz.ToString() ?? "Unknown"}");
        sb.AppendLine($"Band: {CurrentBand}");
        sb.AppendLine($"Digital Mode: {CurrentDigitalMode}");
        sb.AppendLine($"TR Period: {(_radioContext?.TrPeriodSeconds > 0 ? $"{_radioContext.TrPeriodSeconds} s" : "Not reported")}");
        sb.AppendLine($"Radio Context Started: {(_radioContext?.StartedAt.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown")}");
        sb.AppendLine($"Best Target: {Dashboard.BestTarget}");
        sb.AppendLine($"Best Reason: {Dashboard.BestReason}");
        sb.AppendLine($"Pixel State: {Dashboard.PixelState}");
        sb.AppendLine($"Logbook: {LogbookStatus}");
        sb.AppendLine();
        sb.AppendLine("Recent Actions");
        foreach (var action in RecentActions)
            sb.AppendLine(action);

        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        AddAction($"Recent actions exported: {dialog.FileName}");
    }

    private void ClearSessionHistory()
    {
        if (System.Windows.MessageBox.Show("Clear Session History for this app session?", "DX Pilot for JTDX", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        SessionHistory.AllOpportunities.Clear();
        SessionHistory.Refresh();
    }

    private static string Csv(string value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void UpdateWantedItems(DecodeMessage decode)
    {
        if (string.IsNullOrWhiteSpace(decode.Callsign)
            || string.IsNullOrWhiteSpace(decode.ContactableCall)
            || decode.ParseConfidence == ParseConfidence.Low
            || string.IsNullOrWhiteSpace(decode.RawText))
        {
            return;
        }

        var decodeAge = DateTime.Now - decode.ReceivedAt;
        if (decodeAge.TotalSeconds > NewDxccStaleSeconds())
            return;

        if (IsRecentlyWorkedLive(decode.ContactableCall))
        {
            RemoveWantedItemsForCall(decode.ContactableCall, "recent live ADIF QSO");
            return;
        }

        RefreshWantedLastHeard(decode);
        ExpireWantedItems();

        var scored = _targetScorer.Score(decode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
        RefreshExistingWantedRowsFromLatestDecode(decode, scored);

        if (!string.IsNullOrWhiteSpace(decode.Dxcc)
            && !decode.EntityName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            var selected = SelectWantedScope(scope => EvaluateDxccNeed(decode.Dxcc, decode.Band, decode.Mode, scope));
            if (selected.HasValue)
            {
                var (scope, need) = selected.Value;
                UpsertWanted(Wanted.WantedDxcc, decode, scored, "DXCC", "Wanted DXCC",
                    BuildWantedReason(need, "DXCC", decode.EntityName, decode.Band, decode.Mode, scope),
                    need, scope, decode.Dxcc);
            }
        }

        if (IsValidGrid(decode.Grid))
        {
            var normalized = MaidenheadGrid.Normalize(decode.Grid);
            var grid4 = normalized.IsValid ? normalized.Grid4 : decode.Grid.Trim().ToUpperInvariant();
            var status = _adifMergeResult.Indexes.Grids.GetValueOrDefault(grid4);
            var selected = SelectWantedScope(scope => EvaluateSimpleNeed(status, decode.Band, decode.Mode, scope));
            if (selected.HasValue)
            {
                var (scope, need) = selected.Value;
                LogGridWantedDecision(decode, normalized, scope, status, need);
                UpsertWanted(Wanted.WantedGrids, decode, scored, "Grid", "Wanted Grids",
                    BuildWantedReason(need, "grid", grid4, decode.Band, decode.Mode, scope),
                    need, scope, grid4);
            }
        }

        if (WasStateEligibility.IsEligible(decode)
            && IsValidState(decode.State))
        {
            var stateStatus = _adifMergeResult.Indexes.States.GetValueOrDefault(decode.State);
            var selected = SelectWantedScope(scope => EvaluateSimpleNeed(stateStatus, decode.Band, decode.Mode, scope));
            if (selected.HasValue)
            {
                var (scope, need) = selected.Value;
                UpsertWanted(Wanted.WantedStates, decode, scored, "USA State", "Wanted USA States",
                    BuildWantedReason(need, "state", decode.State, decode.Band, decode.Mode, scope),
                    need, scope, decode.State);
            }
        }

        if (CurrentWantedSniperMode() == WantedSniperMode.Active || KeepCallingActiveNewDxccUntilStale())
            _ = TryUpgradeLockedTargetSourceAsync(decode);

        if (!_rebuildingWantedScopes && CurrentWantedSniperMode() == WantedSniperMode.Active)
            _ = TryWantedSniperAsync();
    }

    private (WantedScope Scope, NeedStatus Need)? SelectWantedScope(Func<WantedScope, NeedStatus> evaluate)
    {
        var overall = evaluate(WantedScope.Overall);
        if (overall == NeedStatus.NeverWorked)
            return (WantedScope.Overall, overall);

        var enabledScopes = new List<WantedScope>();
        if (Settings.Settings.IncludeBandWanted && !string.IsNullOrWhiteSpace(_radioContext?.Band))
            enabledScopes.Add(WantedScope.CurrentBand);
        if (Settings.Settings.IncludeModeWanted && !string.IsNullOrWhiteSpace(_radioContext?.Mode))
            enabledScopes.Add(WantedScope.CurrentMode);
        if (Settings.Settings.IncludeBandModeWanted
            && !string.IsNullOrWhiteSpace(_radioContext?.Band)
            && !string.IsNullOrWhiteSpace(_radioContext?.Mode))
            enabledScopes.Add(WantedScope.CurrentBandMode);

        foreach (var scope in enabledScopes)
        {
            var need = evaluate(scope);
            if (need == NeedStatus.NeverWorked)
                return (scope, need);
        }

        if (overall == NeedStatus.WorkedNotLoTWConfirmed)
            return (WantedScope.Overall, overall);

        foreach (var scope in enabledScopes)
        {
            var need = evaluate(scope);
            if (need == NeedStatus.WorkedNotLoTWConfirmed)
                return (scope, need);
        }

        return null;
    }

    private void ApplyWantedScopeSettingsChange()
    {
        SaveAll();
        Wanted.WantedDxcc.Clear();
        Wanted.WantedGrids.Clear();
        Wanted.WantedStates.Clear();
        Wanted.WantedBandMode.Clear();
        _rebuildingWantedScopes = true;
        try
        {
            foreach (var decode in CurrentCandidateDecodes().OrderBy(decode => decode.ReceivedAt))
                UpdateWantedItems(decode);
        }
        finally
        {
            _rebuildingWantedScopes = false;
        }

        var scopes = new List<string> { "overall" };
        if (Settings.Settings.IncludeBandWanted)
            scopes.Add("current band");
        if (Settings.Settings.IncludeModeWanted)
            scopes.Add("current mode");
        if (Settings.Settings.IncludeBandModeWanted)
            scopes.Add("current band + mode");
        Wanted.Status = $"Wanted scopes updated: {string.Join(", ", scopes)}.";
        AddAction($"Wanted scopes updated: {string.Join(", ", scopes)}. Overall New DXCC remains absolute priority.");
        RequestNextBestTargetsUpdate();
        if (CurrentWantedSniperMode() == WantedSniperMode.Active)
            _ = TryWantedSniperAsync();
    }

    private void ApplySniperCategorySettingsChange()
    {
        SaveAll();
        var enabled = new List<string>();
        if (Settings.Settings.EnableWantedDxcc)
            enabled.Add("DXCC");
        if (Settings.Settings.EnableWantedGrids)
            enabled.Add("grids");
        if (Settings.Settings.EnableWantedStates)
            enabled.Add("USA states");

        Wanted.Status = enabled.Count == 0
            ? "Wanted Sniper has no target categories enabled. Observation tables continue updating."
            : $"Wanted Sniper targets: {string.Join(", ", enabled)}. All observation tables continue updating.";

        if (CurrentWantedSniperMode() == WantedSniperMode.Active && _huntState == HuntState.Idle)
            _ = TryWantedSniperAsync();
    }

    private WantedSniperMode CurrentWantedSniperMode()
    {
        return _operatingMode == HuntingOperatingMode.WantedSniper
            ? WantedSniperMode.Active
            : WantedSniperMode.Off;
    }

    private string OperatingModeLabel()
    {
        return _operatingMode switch
        {
            HuntingOperatingMode.WantedSniper => "Wanted Sniper",
            HuntingOperatingMode.LocationHunt => "Location Hunt",
            _ => "DX Assist"
        };
    }

    private void UpsertWanted(ObservableCollection<WantedItem> list, DecodeMessage decode, DxTarget scored, string section, string block, string detail, NeedStatus needStatus, WantedScope scope, string detailValue)
    {
        var key = WantedKey(section, decode.ContactableCall, detailValue, scope, decode.Band, decode.Mode);
        var existing = list.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var item = existing ?? new WantedItem();
        item.Key = key;
        item.Section = section;
        item.Block = block;
        item.Call = decode.Callsign;
        item.ContactableCall = decode.ContactableCall;
        item.Entity = decode.EntityName;
        item.DxccNumber = decode.Dxcc;
        var normalizedGrid = MaidenheadGrid.Normalize(decode.Grid);
        item.WantedValue = section.Equals("DXCC", StringComparison.OrdinalIgnoreCase)
            ? decode.EntityName
            : section.Equals("Grid", StringComparison.OrdinalIgnoreCase) && normalizedGrid.IsValid
                ? normalizedGrid.Grid4
                : section.Equals("USA State", StringComparison.OrdinalIgnoreCase)
                    ? decode.State
                    : detailValue;
        item.WantedDetail = detail;
        item.WantedReason = detail;
        item.NeedStatus = needStatus;
        item.WantedScope = scope;
        item.Grid = section.Equals("Grid", StringComparison.OrdinalIgnoreCase) && normalizedGrid.IsValid ? normalizedGrid.Grid4 : decode.Grid;
        item.GridSource = string.IsNullOrWhiteSpace(decode.GridSource) ? "Unknown" : decode.GridSource;
        if (section.Equals("Grid", StringComparison.OrdinalIgnoreCase))
        {
            var gridStatus = normalizedGrid.IsValid ? _adifMergeResult.Indexes.Grids.GetValueOrDefault(normalizedGrid.Grid4) : null;
            item.NormalizedGrid4 = normalizedGrid.Grid4;
            item.NormalizedGrid6 = normalizedGrid.Grid6;
            item.MatchingWorkedQsoCount = gridStatus?.WorkedQsoCount ?? 0;
            item.MatchingLoTWConfirmedQsoCount = gridStatus?.LoTWConfirmedQsoCount ?? 0;
            item.GridNeedStatus = needStatus.ToString();
            item.GridDiagnosticReason = GridWantedDiagnostic(decode, normalizedGrid, scope, gridStatus, needStatus);
        }
        item.State = decode.State;
        item.StateSource = string.IsNullOrWhiteSpace(decode.State) ? "" : string.IsNullOrWhiteSpace(decode.StateSource) ? "Decode" : decode.StateSource;
        item.QrzStatus = decode.CallsignLookupStatus.ToString();
        item.IsPermanentlySuppressed = IsPermanentlySuppressed(decode.ContactableCall);
        item.Band = decode.Band;
        item.Mode = decode.Mode;
        item.LastSeenUtc = LastHeardUtc(decode.ContactableCall, decode);
        ApplyLatestWantedObservation(item, decode, scored);
        UpdateWantedActionability(item);
        item.RefreshVisualFields();
        if (existing == null)
        {
            list.Insert(0, item);
            AddAction($"{block} added: {item.Call} {item.Entity} - {item.WantedDetail}; {item.ActionabilityText}.");
        }
    }

    private void RefreshWantedLastHeard(DecodeMessage decode)
    {
        var call = DecodeTargetCall(decode);
        if (string.IsNullOrWhiteSpace(call))
            return;

        var lastHeardUtc = LastHeardUtc(call, decode);
        foreach (var item in Wanted.WantedDxcc.Concat(Wanted.WantedGrids).Concat(Wanted.WantedStates)
                     .Where(item => WantedItemTargetCall(item).Equals(call, StringComparison.OrdinalIgnoreCase)))
        {
            if (lastHeardUtc > item.LastSeenUtc)
                item.LastSeenUtc = lastHeardUtc;
        }
    }

    private void RefreshExistingWantedRowsFromLatestDecode(DecodeMessage decode, DxTarget scored)
    {
        var call = DecodeTargetCall(decode);
        if (string.IsNullOrWhiteSpace(call))
            return;

        var lastHeardUtc = LastHeardUtc(call, decode);
        foreach (var item in Wanted.WantedDxcc.Concat(Wanted.WantedGrids).Concat(Wanted.WantedStates)
                     .Where(item => WantedItemTargetCall(item).Equals(call, StringComparison.OrdinalIgnoreCase)))
        {
            item.LastSeenUtc = lastHeardUtc;
            ApplyLatestWantedObservation(item, decode, scored);
            UpdateWantedActionability(item);
        }
    }

    private void ApplyLatestWantedObservation(WantedItem item, DecodeMessage decode, DxTarget scored)
    {
        var hasSource = !string.IsNullOrWhiteSpace(item.SourceRawMessage);
        if (hasSource && DecodeSeenUtc(decode) < DecodeSeenUtc(item.SourceDecode))
            return;

        item.Snr = decode.Snr;
        item.Dt = decode.Dt;
        item.Offset = decode.AudioOffset;
        item.MessageType = decode.MessageTypeText;
        item.PriorityTier = scored.Ranking.PriorityTier;
        item.AdjustedDxValueScore = scored.Ranking.AdjustedDxValueScore;
        item.ClubLogRank = scored.Ranking.RarityRank;
        item.UKDesirability = scored.Ranking.UKDesirability;
        item.DistanceMiles = scored.Ranking.DistanceMiles;
        item.SourceRawMessage = decode.RawText;
        item.SourceDecode = decode;
        item.IsPermanentlySuppressed = IsPermanentlySuppressed(item.ContactableCall);
        item.JtdxRow = JtdxRowText(decode);
    }

    private void UpdateWantedActionability(WantedItem item)
    {
        item.IsActionable = false;
        item.SelectionMethod = "NotSelectable";
        item.ActionabilityStatus = WantedActionabilityStatus.Other;
        item.NotActionableReason = "";

        if (_huntState == HuntState.InQso)
        {
            item.ActionabilityStatus = WantedActionabilityStatus.QsoInProgress;
            item.NotActionableReason = "QSO in progress";
            return;
        }

        var itemTargetCall = WantedItemTargetCall(item);
        if (IsSuppressed(itemTargetCall))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.Suppressed;
            item.NotActionableReason = "Suppressed";
            return;
        }

        if (IsRecentlyWorkedLive(itemTargetCall))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.Suppressed;
            item.NotActionableReason = $"Worked in live ADIF within {Math.Max(1, Settings.Settings.SuccessfulQsoSuppressHours)}h";
            return;
        }

        if (IsFailedReplySource(item.SourceDecode))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.FailedSource;
            item.NotActionableReason = _visibleRowModel.FindDecode(item.SourceDecode) != null
                ? "Previous selection failed; the visible row will retry after one receive period"
                : "Waiting for a newer decode; previous source row is no longer visible";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.SourceRawMessage) || string.IsNullOrWhiteSpace(item.SourceDecode.ContactableCall))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.SourceDecodeMissing;
            item.NotActionableReason = "Source decode missing";
            return;
        }

        if (item.SourceDecode.ParseConfidence == ParseConfidence.Low)
        {
            item.ActionabilityStatus = WantedActionabilityStatus.InvalidParse;
            item.NotActionableReason = "Low parse confidence";
            return;
        }

        var useUdpReply = ShouldUseUdpReplyForSource(item.SourceDecode);
        var visibleRow = _visibleRowModel.FindDecode(item.SourceDecode) != null;
        if (!item.SourceDecode.Targetable || (!useUdpReply && !visibleRow))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.NotTargetable;
            item.NotActionableReason = item.SourceDecode.Targetable ? "Not visible in JTDX grid and not CQ/UDP-selectable" : "Not targetable";
            return;
        }

        item.ActionabilityStatus = WantedActionabilityStatus.Actionable;
        item.IsActionable = true;
        item.SelectionMethod = useUdpReply ? "UdpReply" : "GuiGridDoubleClick";
        item.NotActionableReason = "";
    }

    private bool IsSelectableDecodeForAcquisition(DecodeMessage decode)
    {
        return RadioContextReadyForSelection()
            && decode.Targetable
            && decode.ParseConfidence != ParseConfidence.Low
            && !string.IsNullOrWhiteSpace(decode.ContactableCall)
            && (ShouldUseUdpReplyForSource(decode) || _visibleRowModel.FindDecode(decode) != null);
    }

    private bool IsVisibleTargetableDecode(DecodeMessage decode)
    {
        return decode.Targetable
            && decode.ParseConfidence != ParseConfidence.Low
            && !string.IsNullOrWhiteSpace(decode.ContactableCall)
            && _visibleRowModel.FindDecode(decode) != null;
    }

    private static string WantedKey(string section, string call, string detailValue, WantedScope scope, string band, string mode)
    {
        var scopeBand = scope is WantedScope.CurrentBand or WantedScope.CurrentBandMode ? band : "";
        var scopeMode = scope is WantedScope.CurrentMode or WantedScope.CurrentBandMode ? mode : "";
        return $"{section}|{call}|{detailValue}|{scope}|{scopeBand}|{scopeMode}".ToUpperInvariant();
    }

    private NeedStatus EvaluateDxccNeed(string dxcc, string band, string mode, WantedScope scope)
    {
        return _adifMergeResult.Indexes.Dxcc.TryGetValue(dxcc, out var status)
            ? EvaluateNeed(status.WorkedAny, status.LoTWConfirmedAny, status.WorkedBands, status.WorkedModes, status.WorkedBandModes, status.LoTWConfirmedBands, status.LoTWConfirmedModes, status.LoTWConfirmedBandModes, band, mode, scope)
            : NeedStatus.NeverWorked;
    }

    private static NeedStatus EvaluateSimpleNeed(SimpleWorkedStatus? status, string band, string mode, WantedScope scope)
    {
        return status == null
            ? NeedStatus.NeverWorked
            : EvaluateNeed(status.WorkedAny, status.LoTWConfirmedAny, status.WorkedBands, status.WorkedModes, status.WorkedBandModes, status.LoTWConfirmedBands, status.LoTWConfirmedModes, status.LoTWConfirmedBandModes, band, mode, scope);
    }

    private void LogGridWantedDecision(DecodeMessage decode, NormalizedGrid normalized, WantedScope scope, SimpleWorkedStatus? status, NeedStatus need)
    {
        var diagnostic = GridWantedDiagnostic(decode, normalized, scope, status, need);
        if (need is NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed)
            AddAction($"Wanted grid added: {normalized.Grid4} - {NeedStatusText(need)}, worked count {status?.WorkedQsoCount ?? 0}, LoTW count {status?.LoTWConfirmedQsoCount ?? 0}. {diagnostic}");
        else
            AddAction($"Grid {normalized.Grid4}: {diagnostic} not wanted.");
    }

    private static string GridWantedDiagnostic(DecodeMessage decode, NormalizedGrid normalized, WantedScope scope, SimpleWorkedStatus? status, NeedStatus need)
    {
        return $"decoded {decode.Grid}, Grid4 {normalized.Grid4}, Grid6 {(string.IsNullOrWhiteSpace(normalized.Grid6) ? "none" : normalized.Grid6)}, source {decode.GridSource}, scope {scope}, band {decode.Band}, mode {decode.Mode}, effective mode {decode.Mode}, worked QSOs {status?.WorkedQsoCount ?? 0}, LoTW confirmed QSOs {status?.LoTWConfirmedQsoCount ?? 0}, status {NeedStatusText(need)}, reason {GridNeedReason(need)}.";
    }

    private static string NeedStatusText(NeedStatus need)
    {
        return need switch
        {
            NeedStatus.NeverWorked => "NeverWorked",
            NeedStatus.WorkedNotLoTWConfirmed => "WorkedNotLoTWConfirmed",
            NeedStatus.LoTWConfirmed => "LoTWConfirmed",
            _ => "Unknown"
        };
    }

    private static string GridNeedReason(NeedStatus need)
    {
        return need switch
        {
            NeedStatus.NeverWorked => "no matching Grid4 QSOs found",
            NeedStatus.WorkedNotLoTWConfirmed => "matching Grid4 worked, no matching LoTW confirmation",
            NeedStatus.LoTWConfirmed => "matching Grid4 LoTW confirmed",
            _ => "unknown grid status"
        };
    }

    private static NeedStatus EvaluateNeed(
        bool workedAny,
        bool lotwConfirmedAny,
        HashSet<string> workedBands,
        HashSet<string> workedModes,
        HashSet<string> workedBandModes,
        HashSet<string> lotwBands,
        HashSet<string> lotwModes,
        HashSet<string> lotwBandModes,
        string band,
        string mode,
        WantedScope scope)
    {
        var worked = scope switch
        {
            WantedScope.CurrentBand => !string.IsNullOrWhiteSpace(band) && workedBands.Contains(band),
            WantedScope.CurrentMode => !string.IsNullOrWhiteSpace(mode) && workedModes.Contains(mode),
            WantedScope.CurrentBandMode => !string.IsNullOrWhiteSpace(band) && !string.IsNullOrWhiteSpace(mode) && workedBandModes.Contains(BandModeKey(band, mode)),
            _ => workedAny
        };

        var lotw = scope switch
        {
            WantedScope.CurrentBand => !string.IsNullOrWhiteSpace(band) && lotwBands.Contains(band),
            WantedScope.CurrentMode => !string.IsNullOrWhiteSpace(mode) && lotwModes.Contains(mode),
            WantedScope.CurrentBandMode => !string.IsNullOrWhiteSpace(band) && !string.IsNullOrWhiteSpace(mode) && lotwBandModes.Contains(BandModeKey(band, mode)),
            _ => lotwConfirmedAny
        };

        if (!worked)
            return NeedStatus.NeverWorked;
        return lotw ? NeedStatus.LoTWConfirmed : NeedStatus.WorkedNotLoTWConfirmed;
    }

    private static string BuildWantedReason(NeedStatus need, string category, string value, string band, string mode, WantedScope scope)
    {
        return TargetReasonFormatter.FormatWantedReason(category, need, scope, value, band, mode);
    }

    private static string BandModeKey(string band, string mode)
    {
        return $"{band.Trim().ToUpperInvariant()}|{mode.Trim().ToUpperInvariant()}";
    }

    private static bool IsValidGrid(string grid)
    {
        return MaidenheadGrid.Normalize(grid).IsValid;
    }

    private bool IsValidState(string state)
    {
        return !string.IsNullOrWhiteSpace(UsStateValidator.Normalize(state, Settings.Settings.IncludeDistrictOfColumbia));
    }

    private void ExpireWantedItems()
    {
        TrimWanted(Wanted.WantedDxcc, 100);
        TrimWanted(Wanted.WantedGrids, 50);
        TrimWanted(Wanted.WantedStates, 50);
        RemoveRecentlyWorkedWanted(Wanted.WantedDxcc);
        RemoveRecentlyWorkedWanted(Wanted.WantedGrids);
        RemoveRecentlyWorkedWanted(Wanted.WantedStates);

        foreach (var item in Wanted.WantedDxcc.Concat(Wanted.WantedGrids).Concat(Wanted.WantedStates))
            UpdateWantedActionability(item);
    }

    private void RefreshWantedTimeColumns()
    {
        ExpireWantedItems();
        foreach (var item in Wanted.WantedDxcc.Concat(Wanted.WantedGrids).Concat(Wanted.WantedStates))
        {
            item.JtdxRow = JtdxRowText(item.SourceDecode);
            item.RefreshTimeFields();
        }
    }

    private string JtdxRowText(DecodeMessage decode)
    {
        var row = _visibleRowModel.FindDecode(decode);
        return row == null ? "—" : row.ScreenRowIndex.ToString();
    }

    private void RemoveRecentlyWorkedWanted(ObservableCollection<WantedItem> list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var call = string.IsNullOrWhiteSpace(list[i].ContactableCall) ? list[i].Call : list[i].ContactableCall;
            if (!IsRecentlyWorkedLive(call))
                continue;

            AddAction($"{list[i].Block} removed: {call} worked in live ADIF within {Math.Max(1, Settings.Settings.SuccessfulQsoSuppressHours)}h.");
            list.RemoveAt(i);
        }
    }

    private void RemoveWantedItemsForCall(string callsign, string reason)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return;

        RemoveWantedItemsForCall(Wanted.WantedDxcc, callsign, reason);
        RemoveWantedItemsForCall(Wanted.WantedGrids, callsign, reason);
        RemoveWantedItemsForCall(Wanted.WantedStates, callsign, reason);
    }

    private void RemoveWantedItemsForCall(ObservableCollection<WantedItem> list, string callsign, string reason)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var itemCall = string.IsNullOrWhiteSpace(list[i].ContactableCall) ? list[i].Call : list[i].ContactableCall;
            if (!itemCall.Equals(callsign, StringComparison.OrdinalIgnoreCase))
                continue;

            AddAction($"{list[i].Block} removed: {itemCall} {reason}.");
            list.RemoveAt(i);
        }
    }

    private void TrimWanted(ObservableCollection<WantedItem> list, int maxCount)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var staleCutoff = DateTime.UtcNow.AddSeconds(-WantedStaleSeconds(list[i]));
            if (list[i].LastSeenUtc < staleCutoff || list.Count > maxCount)
                list.RemoveAt(i);
        }
    }

    private async Task CallWantedItemAsync(WantedItem? item)
    {
        if (item == null)
            return;

        UpdateWantedActionability(item);
        if (!item.IsActionable)
        {
            Wanted.Status = $"Wanted item is not actionable: {item.NotActionableReason}";
            AddAction($"Manual Wanted selection rejected for {item.ContactableCall}: {item.NotActionableReason}.");
            return;
        }

        if (_lockedTarget != null && _huntState == HuntState.Calling)
            AddAction("Manual target replaced previous locked target.");

        var target = _targetScorer.Score(item.SourceDecode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
        target.Reasons.Insert(0, item.WantedDetail);
        Wanted.Status = $"Manual Wanted target selected: {item.Call}";
        AddAction($"Manual Wanted selection: {item.Call} from {item.Block}");
        AddAction($"Source decode: {item.SourceRawMessage}");
        await LockAndReplyAsync(target, "Manual Wanted selection", item.WantedDetail, item.Block);
        AddAction($"Selection sent for manual wanted target {item.Call}");
    }

    private bool CanCallNow(object? row)
    {
        return RadioContextReadyForSelection() && !string.IsNullOrWhiteSpace(RowCallsign(row));
    }

    private async Task CallNowAsync(object? row)
    {
        var call = RowCallsign(row);
        if (string.IsNullOrWhiteSpace(call))
            return;

        if (!_udpListener.IsRunning)
        {
            AddAction($"CALL NOW rejected for {call}: UDP listener is stopped.");
            Dashboard.OverallStatus = $"CALL NOW cannot call {call}: start UDP first.";
            return;
        }

        var selectionWaitUntil = DateTime.UtcNow.AddSeconds(2);
        while (_targetSelectionInProgress && DateTime.UtcNow < selectionWaitUntil)
            await Task.Delay(25);
        if (_targetSelectionInProgress)
        {
            AddAction($"CALL NOW for {call} could not start because the previous JTDX selection had not finished.");
            Dashboard.OverallStatus = $"CALL NOW: previous selection is still finishing; select {call} again.";
            return;
        }

        var preferredDecode = RowDecode(row);
        var decode = FindFreshCallableDecode(call, preferredDecode);
        if (decode == null)
        {
            AddAction($"CALL NOW rejected for {call}: no fresh contactable decode is currently selectable in JTDX.");
            Dashboard.OverallStatus = $"CALL NOW cannot call {call}: no fresh selectable row or UDP reply source.";
            return;
        }

        if (_lockedTarget != null || _selectedIntendedTarget != null || _huntState != HuntState.Idle)
        {
            var previous = _lockedTarget?.Callsign ?? _selectedIntendedTarget?.Callsign ?? "current target";
            ClearLockedTarget($"CALL NOW override: released {previous} to call {call}.");
        }

        var suppressionBypassed = HasStoredSuppression(call);
        _postQsoTransitionUntil = DateTime.MinValue;
        _manualSuppressionOverrideCall = call;
        ClearReplySourceBlocks(call);

        if (!_autoResume.IsRunning)
        {
            _autoResume.Start(Settings.Settings, Scheduler.ScheduleItems);
            _huntTimer.Start();
            RefreshModeIndicators();
            AddAction("CALL NOW started DX Pilot target monitoring.");
        }

        var target = _targetScorer.Score(decode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
        target.Reasons.Insert(0, "Manual CALL NOW override");
        Dashboard.OverallStatus = $"CALL NOW: selecting {call}.";
        Wanted.Status = $"CALL NOW override: {call}.";
        Location.Status = $"CALL NOW override: {call}.";
        AddAction(suppressionBypassed
            ? $"CALL NOW override selected suppressed target {call}; suppression is bypassed for this manual call only."
            : $"CALL NOW override selected {call}; all previous target priority checks are bypassed for this call.");
        await LockAndReplyAsync(target, "Manual CALL NOW", "Manual absolute-priority selection", "Manual override");
        if (_lockedTarget?.Callsign.Equals(call, StringComparison.OrdinalIgnoreCase) != true)
            _manualSuppressionOverrideCall = "";
    }

    private bool CanPermanentlySuppressCallsign(object? row)
    {
        var call = RowCallsign(row);
        return !string.IsNullOrWhiteSpace(call) && !IsPermanentlySuppressed(call);
    }

    private bool CanReleaseSuppression(object? row)
    {
        return HasStoredSuppression(RowCallsign(row));
    }

    private void PermanentlySuppressCallsign(object? row)
    {
        var call = CallsignNormalizer.Normalize(RowCallsign(row));
        if (string.IsNullOrWhiteSpace(call) || !_permanentlySuppressedCallsigns.Add(call))
            return;

        if (_lockedTarget?.Callsign.Equals(call, StringComparison.OrdinalIgnoreCase) == true)
        {
            ClearLockedTarget($"Permanent suppression applied to {call}; active target released.");
            EnsureEnableTxOff("Permanent suppression");
        }

        AddAction($"{call} suppressed indefinitely. It will remain visible in red but will not be selected automatically.");
        RefreshSuppressionState();
        SaveAll();
    }

    private void ReleaseSuppression(object? row)
    {
        var call = CallsignNormalizer.Normalize(RowCallsign(row));
        if (string.IsNullOrWhiteSpace(call))
            return;

        var permanentReleased = _permanentlySuppressedCallsigns.Remove(call);
        var temporaryReleased = _suppressedTargets.Remove(call);
        var sourceBlocksReleased = ClearReplySourceBlocks(call);
        if (!permanentReleased && !temporaryReleased && !sourceBlocksReleased)
            return;

        AddAction($"Suppression released for {call}; temporary, permanent, and failed-source blocks were cleared where present.");
        RefreshSuppressionState();
        SaveAll();
        if (CurrentWantedSniperMode() == WantedSniperMode.Active && _huntState == HuntState.Idle)
            _ = TryWantedSniperAsync();
    }

    private bool HasStoredSuppression(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        if (string.IsNullOrWhiteSpace(call))
            return false;

        if (_permanentlySuppressedCallsigns.Contains(call)
            || _suppressedTargets.TryGetValue(call, out var until) && until > DateTime.Now)
        {
            return true;
        }

        var failedCutoff = DateTime.Now.AddSeconds(-NewDxccStaleSeconds());
        return _decodeHistory
            .Where(decode => DecodeTargetCall(decode).Equals(call, StringComparison.OrdinalIgnoreCase))
            .Select(ReplySourceKey)
            .Any(key => _failedReplySources.TryGetValue(key, out var failedAt) && failedAt >= failedCutoff);
    }

    private bool ClearReplySourceBlocks(string callsign)
    {
        var call = CallsignNormalizer.Normalize(callsign);
        if (string.IsNullOrWhiteSpace(call))
            return false;

        var removed = false;
        var keys = _decodeHistory
            .Where(decode => DecodeTargetCall(decode).Equals(call, StringComparison.OrdinalIgnoreCase))
            .Select(ReplySourceKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var key in keys)
        {
            removed |= _failedReplySources.Remove(key);
            removed |= _forceGuiSelectionSources.Remove(key);
            removed |= _guiSelectionClickCounts.Remove(key);
            removed |= _guiSelectionLastClickAt.Remove(key);
        }

        return removed;
    }

    private void RefreshSuppressionState()
    {
        Settings.Settings.PermanentlySuppressedCallsigns = _permanentlySuppressedCallsigns
            .OrderBy(call => call, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var decode in _decodeHistory)
            decode.IsPermanentlySuppressed = IsPermanentlySuppressed(DecodeTargetCall(decode));

        foreach (var item in Wanted.WantedDxcc.Concat(Wanted.WantedGrids).Concat(Wanted.WantedStates))
        {
            item.IsPermanentlySuppressed = IsPermanentlySuppressed(WantedItemTargetCall(item));
            UpdateWantedActionability(item);
        }

        foreach (var item in SessionHistory.AllOpportunities)
            item.IsPermanentlySuppressed = IsPermanentlySuppressed(item.Call);

        System.Windows.Data.CollectionViewSource.GetDefaultView(DxAssist.RecentDecodes).Refresh();
        UpdateNextBestTargets();
        SessionHistory.Refresh();
        PermanentlySuppressCallsignCommand.RaiseCanExecuteChanged();
        ReleaseSuppressionCommand.RaiseCanExecuteChanged();
    }

    private string RowCallsign(object? row)
    {
        return CallsignNormalizer.Normalize(row switch
        {
            WantedItem item => WantedItemTargetCall(item),
            DxCandidateRow candidate => candidate.Call,
            DecodeMessage decode => DecodeTargetCall(decode),
            SessionDxOpportunity opportunity => opportunity.Call,
            DxTarget target => target.Callsign,
            _ => ""
        });
    }

    private static DecodeMessage? RowDecode(object? row)
    {
        return row switch
        {
            WantedItem item => item.SourceDecode,
            DxCandidateRow candidate => candidate.Target.Decode,
            DecodeMessage decode => decode,
            DxTarget target => target.Decode,
            _ => null
        };
    }

    private DecodeMessage? FindFreshCallableDecode(string callsign, DecodeMessage? preferred)
    {
        var candidates = _decodeHistory
            .Where(decode => DecodeTargetCall(decode).Equals(callsign, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (preferred != null && !candidates.Contains(preferred))
            candidates.Add(preferred);

        return candidates
            .Where(IsFreshDecode)
            .Where(IsSelectableDecodeForAcquisition)
            .OrderByDescending(DecodeSeenUtc)
            .FirstOrDefault();
    }

    private DecodeMessage? FindFreshCallableDecodeForLockedTarget(DxTarget target)
    {
        var maxAge = Settings.Settings.KeepCallingNewDxccUntilStale
            && IsUnconfirmedDxccStatus(target.Ranking.DxccStatus)
                ? NewDxccStaleSeconds()
                : NormalStaleSeconds();
        var cutoff = DateTime.Now.AddSeconds(-maxAge);
        var candidates = _decodeHistory
            .Where(decode => DecodeTargetCall(decode).Equals(target.Callsign, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!candidates.Contains(target.Decode))
            candidates.Add(target.Decode);

        var selectable = candidates
            .Where(decode => decode.ReceivedAt >= cutoff)
            .Where(IsSelectableDecodeForAcquisition)
            .ToList();
        return _huntState == HuntState.InQso
            ? selectable.OrderByDescending(DecodeSeenUtc).FirstOrDefault()
            : selectable
                .OrderByDescending(ShouldUseUdpReplyForSource)
                .ThenByDescending(DecodeSeenUtc)
                .FirstOrDefault();
    }

    private async Task CallLocationTargetAsync(DxCandidateRow? row)
    {
        if (row == null)
            return;

        if (!IsSelectableDecodeForAcquisition(row.Target.Decode))
        {
            Location.Status = $"{row.Call} is visible but not currently actionable: {row.TargetStatus}.";
            AddAction($"Manual Location selection rejected for {row.Call}: {row.TargetStatus}.");
            return;
        }

        if (!IsFreshTarget(row.Target))
        {
            Location.Status = $"{row.Call} is stale; waiting for a fresh UDP decode.";
            AddAction($"Manual Location selection rejected for {row.Call}: stale source decode.");
            return;
        }

        Location.Status = $"Manual Location target selected: {row.Call} ({row.Country}).";
        AddAction($"Manual Location selection: {row.Call} from {Location.SelectedAreasDisplay}.");
        await LockAndReplyAsync(row.Target, "Manual Location selection", row.WantedReason, Location.SelectedAreasDisplay);
    }

    private static void CopyLocationCallsign(DxCandidateRow? row)
    {
        if (row != null && !string.IsNullOrWhiteSpace(row.Call))
            System.Windows.Clipboard.SetText(row.Call);
    }

    private void WatchWantedItem(WantedItem? item)
    {
        if (item == null)
            return;
        Wanted.Status = $"Watching {item.Call}.";
        AddAction($"Wanted watch only: {item.Call}.");
    }

    private void SuppressWantedItem(WantedItem? item)
    {
        if (item == null)
            return;
        var call = WantedItemTargetCall(item);
        _suppressedTargets[call] = DateTime.Now.AddMinutes(5);
        RemoveWantedItemsForCall(call, "suppressed from Wanted");
        Wanted.Status = $"{call} suppressed for 5 minutes.";
        AddAction($"{call} suppressed for 5 minutes from Wanted.");
    }

    private static string WantedItemTargetCall(WantedItem item)
    {
        return string.IsNullOrWhiteSpace(item.ContactableCall) ? item.Call : item.ContactableCall;
    }

    private static string DecodeTargetCall(DecodeMessage decode)
    {
        return string.IsNullOrWhiteSpace(decode.ContactableCall) ? decode.Callsign : decode.ContactableCall;
    }

    private static void CopyWantedCallsign(WantedItem? item)
    {
        if (item != null && !string.IsNullOrWhiteSpace(item.Call))
            System.Windows.Clipboard.SetText(item.Call);
    }

    private static void CopyWantedRawMessage(WantedItem? item)
    {
        if (item != null && !string.IsNullOrWhiteSpace(item.SourceRawMessage))
            System.Windows.Clipboard.SetText(item.SourceRawMessage);
    }

    private static DecodeMessage CopyDecodeForWanted(DecodeMessage decode)
    {
        return new DecodeMessage
        {
            ReceivedAt = decode.ReceivedAt,
            DecodeTime = decode.DecodeTime,
            Snr = decode.Snr,
            Dt = decode.Dt,
            AudioOffset = decode.AudioOffset,
            Mode = decode.Mode,
            ProtocolMode = decode.ProtocolMode,
            RadioContextGeneration = decode.RadioContextGeneration,
            DialFrequencyHz = decode.DialFrequencyHz,
            RawText = decode.RawText,
            SourceAppId = decode.SourceAppId,
            MessageType = decode.MessageType,
            IsCq = decode.IsCq,
            ContactableCall = decode.ContactableCall,
            Callsign = decode.Callsign,
            Grid = decode.Grid,
            GridSource = decode.GridSource,
            Dxcc = decode.Dxcc,
            EntityName = decode.EntityName,
            Band = decode.Band,
            Targetable = decode.Targetable,
            ParseConfidence = decode.ParseConfidence
        };
    }

    private void RefreshRecentDecodeRows()
    {
        var rows = DxAssist.RecentDecodes.ToList();
        DxAssist.RecentDecodes.Clear();
        foreach (var row in rows)
            DxAssist.RecentDecodes.Add(row);
    }

    private IReadOnlyList<DecodeMessage> CurrentCandidateDecodes()
    {
        return _decodeHistory
            .Where(decode => _radioContext == null || decode.RadioContextGeneration == _radioContext.Generation)
            .Where(IsFreshDecode)
            .ToList();
    }

    private bool IsFreshTarget(DxTarget? target)
    {
        if (target == null)
            return false;

        var persistentNewDxcc = Settings.Settings.KeepCallingNewDxccUntilStale
            && IsUnconfirmedDxccStatus(target.Ranking.DxccStatus);
        var maxAge = persistentNewDxcc ? NewDxccStaleSeconds() : NormalStaleSeconds();
        return target.Decode.ReceivedAt >= DateTime.Now.AddSeconds(-maxAge);
    }

    private void TrimLiveDecodeDisplay()
    {
        var cutoff = DateTime.Now.AddSeconds(-35);
        for (var i = DxAssist.RecentDecodes.Count - 1; i >= 0; i--)
        {
            if (DxAssist.RecentDecodes[i].ReceivedAt < cutoff || DxAssist.RecentDecodes.Count > 80)
                DxAssist.RecentDecodes.RemoveAt(i);
        }
    }

    private void AddSchedule()
    {
        Scheduler.ScheduleItems.Add(new BandScheduleItem());
        SaveAll();
    }

    private void RemoveSchedule()
    {
        if (SelectedScheduleItem == null)
            return;

        Scheduler.ScheduleItems.Remove(SelectedScheduleItem);
        SaveAll();
    }

    private async Task PickPointAsync(string label, Action<int, int> apply)
    {
        if (!TryBeginPick(label))
            return;

        try
        {
            AddAction($"Hover over {label}, then press Space or Enter. Esc cancels.");
            var point = await _clicker.PickPointAsync();
            if (point == null)
                return;

            apply(point.Value.x, point.Value.y);
            Settings.Refresh();
            SaveAll();
            AddAction($"Picked {label}: X={point.Value.x}, Y={point.Value.y}.");
        }
        catch (OperationCanceledException)
        {
            AddAction($"Pick {label} cancelled.");
        }
        finally
        {
            EndPick();
        }
    }

    private async Task PickWindowRelativePointAsync(string label, Action<int, int> apply)
    {
        if (!TryBeginPick(label))
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection: cannot pick {label}; another picker is already active.";
            return;
        }

        try
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection: starting pick for {label}.";
            Dashboard.OverallStatus = $"Calibration pick active: {label}.";
            var window = _jtdxWindowLocator.FindMainWindow(Settings.Settings.JtdxWindowTitleMatch);
            if (window == null)
            {
                DxAssist.GuiSelectionStatus = $"GUI Selection: cannot pick {label}; JTDX window not found using title match '{Settings.Settings.JtdxWindowTitleMatch}'.";
                Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
                AddAction(DxAssist.GuiSelectionStatus);
                return;
            }

            DxAssist.GuiSelectionStatus = $"GUI Selection: hover over {label} in JTDX, then press Space or Enter. Esc cancels.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction($"Calibration active: hover over {label} in JTDX, then press Space or Enter. Esc cancels.");
            var point = await _clicker.PickPointAsync();
            if (point == null)
                return;

            var relativeX = point.Value.x - window.Left;
            var relativeY = point.Value.y - window.Top;
            apply(relativeX, relativeY);
            Settings.Refresh();
            SaveAll();
            DxAssist.GuiSelectionStatus = $"GUI Selection: picked {label} at window-relative X={relativeX}, Y={relativeY}.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction($"Picked {label}: window-relative X={relativeX}, Y={relativeY} for '{window.Title}'.");
            UpdateGuiCalibrationStatus();
        }
        catch (OperationCanceledException)
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection: pick {label} cancelled.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction($"Pick {label} cancelled.");
        }
        catch (Exception ex)
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection: pick {label} failed: {ex.GetBaseException().Message}";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction(DxAssist.GuiSelectionStatus);
        }
        finally
        {
            EndPick();
        }
    }

    private async Task PickColorAsync(string label, Action<int> apply)
    {
        if (!TryBeginPick(label))
            return;

        try
        {
            AddAction($"Hover over {label}, then press Space or Enter. Esc cancels.");
            var point = await _clicker.PickPointAsync();
            if (point == null)
                return;

            var rgb = _pixels.GetScreenRgb(point.Value.x, point.Value.y);
            apply(rgb);
            Settings.Refresh();
            SaveAll();
            AddAction($"Picked {label}: 0x{rgb:X6}.");
        }
        catch (OperationCanceledException)
        {
            AddAction($"Pick {label} cancelled.");
        }
        finally
        {
            EndPick();
        }
    }

    private bool TryBeginPick(string label)
    {
        if (_isPicking)
        {
            AddAction($"Ignored {label} pick because another picker is already active.");
            return false;
        }

        _isPicking = true;
        return true;
    }

    private void EndPick()
    {
        _isPicking = false;
    }

    private void TestScheduleClick()
    {
        if (SelectedScheduleItem == null || SelectedScheduleItem.X == 0 && SelectedScheduleItem.Y == 0)
            return;

        _clicker.MoveClickRestore(SelectedScheduleItem.X, SelectedScheduleItem.Y);
        AddAction($"Test clicked schedule '{SelectedScheduleItem.Label}' at X={SelectedScheduleItem.X}, Y={SelectedScheduleItem.Y}.");
    }

    private void CaptureJtdxWindow()
    {
        CaptureJtdxWindow(resetGrid: false, source: "Manual capture");
    }

    private void CaptureJtdxWindow(bool resetGrid, string source)
    {
        DxAssist.GuiSelectionStatus = "GUI Selection: capturing JTDX window...";
        Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
        var window = _jtdxWindowLocator.FindMainWindow(Settings.Settings.JtdxWindowTitleMatch);
        if (window == null)
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection: JTDX window not found using title match '{Settings.Settings.JtdxWindowTitleMatch}'.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction(DxAssist.GuiSelectionStatus);
            return;
        }

        Settings.Settings.JtdxBandDpiScale = 1.0;
        var calibration = JtdxBandActivityGridCalibration.FromSettings(Settings.Settings);
        var loadedDefaultGrid = resetGrid || !calibration.IsUsable;
        if (loadedDefaultGrid)
        {
            calibration = JtdxBandActivityGridCalibration.CreateDefault(
                window,
                Settings.Settings.JtdxBandVisibleRowCount);
        }
        else
        {
            UpdateCalibrationWindow(calibration, window);
        }

        calibration.SaveTo(Settings.Settings);
        Settings.Refresh();
        OnPropertyChanged(nameof(JtdxVisibleRowCount));
        SaveAll();
        var gridText = loadedDefaultGrid
            ? $"Loaded default {calibration.SafeVisibleFullRowCount}-row Band Activity grid."
            : $"Kept existing {calibration.SafeVisibleFullRowCount}-row grid calibration.";
        DxAssist.GuiSelectionStatus = $"GUI Selection: captured '{window.Title}' pid {window.ProcessId}, size {window.Width}x{window.Height}. {gridText}";
        Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
        AddAction($"{source}: {DxAssist.GuiSelectionStatus}");
    }

    private void ApplyBandActivityTopLeft(int x, int y)
    {
        Settings.Settings.JtdxBandActivityLeft = x;
        Settings.Settings.JtdxBandActivityTop = y;
        RecalculateBandActivityGridDefaults();
    }

    private void ApplyBandActivityBottomRight(int x, int y)
    {
        Settings.Settings.JtdxBandActivityRight = x;
        Settings.Settings.JtdxBandActivityBottom = y;
        RecalculateBandActivityGridDefaults();
    }

    private void RecalculateBandActivityGridDefaults()
    {
        var settings = Settings.Settings;
        settings.JtdxBandVisibleRowCount =
            JtdxBandActivityGridCalibration.NormalizeRowCount(settings.JtdxBandVisibleRowCount);

        var width = settings.JtdxBandActivityRight - settings.JtdxBandActivityLeft;
        var height = settings.JtdxBandActivityBottom - settings.JtdxBandActivityTop;
        if (width <= 0 || height <= 0)
            return;

        var partialTopAllowance = settings.JtdxBandIgnoredPartialTopRow ? 0.5 : 0;
        settings.JtdxBandRowHeight =
            height / (settings.JtdxBandVisibleRowCount + partialTopAllowance);
        settings.JtdxBandFirstRowCenterY = (int)Math.Round(
            settings.JtdxBandActivityTop
            + (settings.JtdxBandIgnoredPartialTopRow
                ? settings.JtdxBandRowHeight
                : settings.JtdxBandRowHeight / 2));
        if (settings.JtdxBandMessageClickX <= settings.JtdxBandActivityLeft || settings.JtdxBandMessageClickX >= settings.JtdxBandActivityRight)
            settings.JtdxBandMessageClickX = settings.JtdxBandActivityLeft + Math.Max(20, width / 2);

        settings.JtdxBandCalibrationVersion = $"grid-v2-{DateTime.Now:yyyyMMddHHmmss}";
        settings.JtdxBandCalibrationDate = DateTime.Now;
        _visibleRowModel.Rebuild(_decodeHistory, JtdxBandActivityGridCalibration.FromSettings(settings));
    }

    private void ShowBandActivityGridOverlay()
    {
        if (_bandActivityOverlay != null)
        {
            _bandActivityOverlay.Close();
            _bandActivityOverlay = null;
            GridOverlayButtonText = $"Show {JtdxVisibleRowCount}-Row Grid";
            DxAssist.GuiSelectionStatus = "Grid Overlay: hidden.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction(DxAssist.GuiSelectionStatus);
            return;
        }

        var window = _jtdxWindowLocator.FindMainWindow(Settings.Settings.JtdxWindowTitleMatch);
        if (window == null)
        {
            DxAssist.GuiSelectionStatus = $"Grid Overlay: JTDX window not found using title match '{Settings.Settings.JtdxWindowTitleMatch}'.";
            Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
            AddAction(DxAssist.GuiSelectionStatus);
            return;
        }

        var calibration = JtdxBandActivityGridCalibration.FromSettings(Settings.Settings);
        if (!calibration.IsUsable)
        {
            calibration = JtdxBandActivityGridCalibration.CreateDefault(
                window,
                Settings.Settings.JtdxBandVisibleRowCount);
            calibration.SaveTo(Settings.Settings);
            Settings.Refresh();
            SaveAll();
        }
        else
        {
            UpdateCalibrationWindow(calibration, window);
            calibration.SaveTo(Settings.Settings);
        }

        if (_bandActivityOverlay == null)
        {
            _bandActivityOverlay = new JtdxBandActivityOverlay();
            _bandActivityOverlay.CalibrationChanged += SaveOverlayCalibration;
            _bandActivityOverlay.Closed += (_, _) =>
            {
                _bandActivityOverlay = null;
                GridOverlayButtonText = $"Show {JtdxVisibleRowCount}-Row Grid";
            };
        }

        if (_bandActivityOverlay.Owner == null && System.Windows.Application.Current?.MainWindow != null)
            _bandActivityOverlay.Owner = System.Windows.Application.Current.MainWindow;

        _bandActivityOverlay.ShowCalibration(calibration, window.Left, window.Top);
        GridOverlayButtonText = $"Hide {calibration.SafeVisibleFullRowCount}-Row Grid";
        DxAssist.GuiSelectionStatus = $"Grid Overlay: showing {calibration.SafeVisibleFullRowCount} safe rows over '{window.Title}'. Drag the grid; mouse wheel adjusts height, Shift+wheel adjusts width.";
        Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
        AddAction(DxAssist.GuiSelectionStatus);
    }

    public void Dispose()
    {
        _allTxtMonitor.Dispose();
        StopAdifWatcher();
        _targetSelectionCancellation?.Cancel();
        _targetSelectionCancellation?.Dispose();
        _targetSelectionCancellation = null;

        if (_bandActivityOverlay != null)
        {
            try
            {
                _bandActivityOverlay.Close();
            }
            catch
            {
            }

            _bandActivityOverlay = null;
        }

        _callsignLocationService.Dispose();
    }

    private void SaveOverlayCalibration(JtdxBandActivityGridCalibration calibration)
    {
        var window = _jtdxWindowLocator.FindMainWindow(Settings.Settings.JtdxWindowTitleMatch);
        if (window != null)
            UpdateCalibrationWindow(calibration, window);

        calibration.SaveTo(Settings.Settings);
        Settings.Refresh();
        OnPropertyChanged(nameof(JtdxVisibleRowCount));
        SaveAll();
        _visibleRowModel.Rebuild(_decodeHistory, calibration);
        DxAssist.GuiSelectionStatus = $"Grid Overlay: saved {calibration.SafeVisibleFullRowCount} rows, left {calibration.BandActivityLeftRelative}, top {calibration.BandActivityTopRelative}, width {calibration.BandActivityWidth}, height {calibration.BandActivityHeight}, row height {calibration.RowHeight:0.00}.";
    }

    private void RefreshOpenBandActivityOverlay()
    {
        if (_bandActivityOverlay == null)
            return;

        var window = _jtdxWindowLocator.FindMainWindow(Settings.Settings.JtdxWindowTitleMatch);
        if (window == null)
            return;

        var calibration = JtdxBandActivityGridCalibration.FromSettings(Settings.Settings);
        UpdateCalibrationWindow(calibration, window);
        _bandActivityOverlay.ShowCalibration(calibration, window.Left, window.Top);
    }

    private static void UpdateCalibrationWindow(
        JtdxBandActivityGridCalibration calibration,
        JtdxWindowInfo window)
    {
        calibration.MonitorId = $"{window.Left},{window.Top}";
        calibration.JtdxWindowTitle = window.Title;
        calibration.JtdxWindowProcess = window.ProcessId.ToString();
        calibration.JtdxWindowLeft = window.Left;
        calibration.JtdxWindowTop = window.Top;
        calibration.JtdxWindowWidth = window.Width;
        calibration.JtdxWindowHeight = window.Height;
    }

    private async Task TestGuiSelectionAsync()
    {
        var requestedTarget = DxAssist.SelectedCandidate?.Target ?? DxAssist.BestTarget;
        if (requestedTarget == null)
        {
            AddAction("Test GUI selection skipped: select a candidate or best target first.");
            return;
        }

        var calibration = JtdxBandActivityGridCalibration.FromSettings(Settings.Settings);
        var previewRows = new JtdxVisibleRowModel();
        previewRows.Rebuild(_decodeHistory, calibration);
        if (previewRows.Rows.Count < calibration.SafeVisibleFullRowCount)
        {
            var fillMessage = $"Test GUI selection skipped: JTDX grid model is still filling ({previewRows.Rows.Count}/{calibration.SafeVisibleFullRowCount} rows). Wait until Band Activity has filled before testing grid clicks.";
            AddAction(fillMessage);
            DxAssist.GuiSelectionStatus = fillMessage;
            return;
        }

        var target = ResolveGridTestClickableTarget(requestedTarget, previewRows, out var resolutionMessage);
        if (target == null)
        {
            AddAction(resolutionMessage);
            DxAssist.GuiSelectionStatus = resolutionMessage;
            return;
        }

        if (!ReferenceEquals(target, requestedTarget) && !string.IsNullOrWhiteSpace(resolutionMessage))
            AddAction(resolutionMessage);

        var previewRow = previewRows.FindDecode(target.Decode);
        if (previewRow != null)
        {
            var window = _jtdxWindowLocator.FindMainWindow(Settings.Settings.JtdxWindowTitleMatch);
            var clickX = window == null ? 0 : window.Left + calibration.MessageClickXRelative;
            var clickY = window == null ? 0 : (int)Math.Round(window.Top + calibration.FirstFullRowCentreYRelative + previewRow.ScreenRowIndex * calibration.RowHeight);
            AddAction($"Test Grid Selection starting now: raw '{target.Decode.RawText}', expected {target.Callsign}, row {previewRow.ScreenRowIndex}, click {clickX},{clickY}. Overlay will close before click.");
        }
        else
        {
            AddAction($"Test Grid Selection starting now: raw '{target.Decode.RawText}', expected {target.Callsign}. Target is not in preview visible grid.");
        }

        var result = await _selectionController.SelectTargetByGridForTestAsync(target, Settings.Settings);
        DxAssist.SelectionMethodText = $"Selection Method: GUI diagnostic";
        if (result.Success)
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection confirmed: JTDX DX Call is {target.Callsign}.";
            AddAction($"Test GUI selection succeeded: raw '{result.TargetRawMessage}', row {result.ScreenRowIndex}, click {result.ClickX},{result.ClickY}, after DX '{result.JtdxDxCallAfter}'.");
        }
        else
        {
            DxAssist.GuiSelectionStatus = $"GUI Selection failed: {result.FailureText}";
            AddAction($"Test GUI selection failed: raw '{result.TargetRawMessage}', expected {target.Callsign}, method {result.SelectionMethod}, model v{result.VisibleRowModelVersion}, calibration {result.CalibrationVersion}, row {result.ScreenRowIndex?.ToString() ?? "n/a"}, click {result.ClickX?.ToString() ?? "n/a"},{result.ClickY?.ToString() ?? "n/a"}, before DX '{result.JtdxDxCallBefore}', after DX '{result.JtdxDxCallAfter}', failure {result.FailureText}.");
        }
    }

    private DxTarget? ResolveGridTestClickableTarget(DxTarget requestedTarget, JtdxVisibleRowModel previewRows, out string message)
    {
        message = "";
        var expectedCall = FirstNonBlank(requestedTarget.Callsign, requestedTarget.Decode.ContactableCall, requestedTarget.Decode.Callsign);
        if (string.IsNullOrWhiteSpace(expectedCall))
        {
            message = "Test GUI selection skipped: selected target has no callsign.";
            return null;
        }

        if (IsGridTestSelectableDecode(requestedTarget.Decode, expectedCall)
            && previewRows.FindDecode(requestedTarget.Decode) != null)
        {
            return requestedTarget;
        }

        var replacement = _decodeHistory
            .Where(decode => IsGridTestSelectableDecode(decode, expectedCall))
            .Select(decode => new { Decode = decode, Row = previewRows.FindDecode(decode) })
            .Where(candidate => candidate.Row != null)
            .OrderBy(candidate => IsInitialAcquisitionMessage(candidate.Decode) ? 0 : 1)
            .ThenByDescending(candidate => candidate.Decode.ReceivedAt)
            .ThenByDescending(candidate => candidate.Row!.ScreenRowIndex)
            .FirstOrDefault();

        if (replacement == null)
        {
            message = $"Test GUI selection skipped for {expectedCall}: no visible selectable row is available. Last selected source was '{requestedTarget.Decode.RawText}' ({requestedTarget.Decode.MessageTypeText}).";
            return null;
        }

        var resolved = _targetScorer.Score(replacement.Decode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
        if (!string.IsNullOrWhiteSpace(requestedTarget.PrimaryReason))
            resolved.Reasons.Insert(0, requestedTarget.PrimaryReason);

        message = $"Test GUI selection source changed for {expectedCall}: using visible selectable {replacement.Decode.MessageTypeText} row '{replacement.Decode.RawText}' instead of '{requestedTarget.Decode.RawText}'.";
        return resolved;
    }

    private static bool IsGridTestSelectableDecode(DecodeMessage decode, string expectedCall)
    {
        return decode.Targetable
            && decode.ParseConfidence != ParseConfidence.Low
            && !string.IsNullOrWhiteSpace(decode.ContactableCall)
            && decode.ContactableCall.Equals(expectedCall, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private string CurrentJtdxDxCall()
    {
        return (_udpListener.LastStatus?.DxCall ?? _actualJtdxDxCall).Trim();
    }

    private async Task<bool> WaitForJtdxDxCallAsync(string expectedCall, TimeSpan timeout)
    {
        var until = DateTime.Now + timeout;
        while (DateTime.Now < until)
        {
            var status = _udpListener.LastStatus;
            if (status != null && status.DxCall.Equals(expectedCall, StringComparison.OrdinalIgnoreCase))
                return true;

            await Task.Delay(150);
        }

        return false;
    }

    private void UpdateGuiCalibrationStatus()
    {
        var settings = Settings.Settings;
        var calibrated = settings.JtdxBandActivityRight > settings.JtdxBandActivityLeft
            && settings.JtdxBandActivityBottom > settings.JtdxBandActivityTop;
        DxAssist.GuiSelectionStatus = calibrated
            ? $"GUI Selection calibrated: {JtdxVisibleRowCount}-row grid set. DX Pilot uses UDP decodes as truth and calibrated row geometry for directed rows."
            : "GUI Selection: calibration incomplete.";
    }

    private void AddAction(string message)
    {
        var radio = _radioContext == null
            ? ""
            : $" [{_radioContext.BandDisplay} {_radioContext.ModeDisplay}]";
        var line = $"{DateTime.Now:HH:mm:ss}{radio}  {message}";
        RecentActions.Insert(0, line);
        while (RecentActions.Count > 500)
            RecentActions.RemoveAt(RecentActions.Count - 1);

        AddFilteredAction(line, message);
    }

    private void AddFilteredAction(string line, string message)
    {
        if (IsWantedAction(message))
            AddToLimited(WantedRecentActions, line, 120);
        if (IsSessionHistoryAction(message))
            AddToLimited(SessionHistoryRecentActions, line, 120);
        if (IsDxAssistAction(message))
            AddToLimited(DxAssistRecentActions, line, 160);
    }

    private static void AddToLimited(ObservableCollection<string> list, string line, int limit)
    {
        list.Insert(0, line);
        while (list.Count > limit)
            list.RemoveAt(list.Count - 1);
    }

    private static bool IsWantedAction(string message)
    {
        return message.Contains("Wanted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Sniper", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Manual Wanted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSessionHistoryAction(string message)
    {
        return message.Contains("Seen:", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Worked", StringComparison.OrdinalIgnoreCase)
            || message.Contains("suppressed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Missed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ADIF", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Call attempts failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDxAssistAction(string message)
    {
        return message.Contains("target", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Reply", StringComparison.OrdinalIgnoreCase)
            || message.Contains("JTDX", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TX", StringComparison.OrdinalIgnoreCase)
            || message.Contains("QSO", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Call attempt", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Report repeat", StringComparison.OrdinalIgnoreCase)
            || message.Contains("selection", StringComparison.OrdinalIgnoreCase);
    }

    private void Dispatch(Action action)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            try
            {
                AddAction($"UI update error: {error}");
            }
            catch
            {
                // Last resort: never let a UI/logging failure bubble into the UDP receive loop.
            }
        }
    }
}
