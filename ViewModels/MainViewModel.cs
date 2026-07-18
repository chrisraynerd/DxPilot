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
    private static readonly TimeSpan Ft8AttemptCycle = TimeSpan.FromSeconds(30);

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

    private readonly SettingsService _settingsService;
    private readonly PixelDetector _pixels;
    private readonly ScreenClicker _clicker;
    private readonly AutoResumeService _autoResume;
    private readonly JtdxUdpListener _udpListener;
    private readonly JtdxUdpClient _udpClient;
    private readonly JtdxWindowLocator _jtdxWindowLocator;
    private readonly JtdxSelectionController _selectionController;
    private readonly JtdxVisibleRowModel _visibleRowModel = new();
    private readonly AdifLogbookReader _adifReader;
    private readonly AdifWorkedStatusBuilder _adifStatusBuilder;
    private readonly DxccResolver _dxccResolver;
    private readonly DxccRarityService _rarityService;
    private readonly DxTargetScorer _targetScorer;
    private readonly TargetSelector _targetSelector;
    private readonly List<DecodeMessage> _decodeHistory = new();
    private readonly List<AdifQso> _logbook = new();
    private readonly List<AdifQso> _fullLogbook = new();
    private readonly List<AdifQso> _liveLogbook = new();
    private readonly Dictionary<string, DateTime> _suppressedTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedReplySources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _guiSelectionAttemptedSources = new(StringComparer.OrdinalIgnoreCase);
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
    private DateTime _lastFullAdifLoadedAt = DateTime.MinValue;
    private DateTime _lastLiveAdifReloadAt = DateTime.MinValue;
    private DateTime _lastLiveAdifWriteUtc = DateTime.MinValue;
    private DateTime _lastAutoResumeStatusUiAt = DateTime.MinValue;
    private DateTime _lastPixelStateUiAt = DateTime.MinValue;
    private string _lastAutoResumeStatusUi = "";
    private string _lastPixelStateUi = "";
    private string _logbookStatus = "No ADIF loaded.";
    private string _adifDiagnostics = "No ADIF loaded.";
    private string _resolverDiagnostics = "";
    private string _rarityDiagnostics = "";
    private string _diagnosticCallsign = "";
    private string _diagnosticGrid = "";
    private string _diagnosticState = "";
    private string _diagnosticIota = "";
    private string _diagnosticLookupResult = "Enter a callsign, grid, state, or IOTA reference, then run lookup.";
    private AdifMergeResult _adifMergeResult = new();
    private bool _isPicking;
    private bool _wantedSniperBusy;
    private DateTime _lastWantedSniperNoTargetLogAt = DateTime.MinValue;
    private string _targetSource = "None";
    private string _wantedReason = "";
    private string _wantedSourceBlock = "";
    private JtdxBandActivityOverlay? _bandActivityOverlay;

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _pixels = new PixelDetector();
        _clicker = new ScreenClicker();
        var scheduler = new BandScheduler(_clicker, _pixels);
        _autoResume = new AutoResumeService(_pixels, _clicker, scheduler);
        _udpListener = new JtdxUdpListener();
        _udpClient = new JtdxUdpClient();
        _jtdxWindowLocator = new JtdxWindowLocator();
        var udpReplySelector = new JtdxUdpReplySelector(_udpClient);
        var guiGridSelector = new JtdxGuiGridSelector(_clicker, _jtdxWindowLocator, () => _visibleRowModel.Version);
        _selectionController = new JtdxSelectionController(udpReplySelector, guiGridSelector, _visibleRowModel, CurrentJtdxDxCall);
        _adifReader = new AdifLogbookReader();
        _adifStatusBuilder = new AdifWorkedStatusBuilder();
        Settings = new SettingsViewModel { Settings = _settingsService.LoadSettings() };
        _dxccResolver = new DxccResolver(Settings.Settings.CountryFilePath);
        _rarityService = new DxccRarityService();
        _rarityService.Load(Settings.Settings.DxccRarityFilePath, _dxccResolver);
        _targetScorer = new DxTargetScorer(_dxccResolver, _rarityService, new GridDistanceCalculator());
        _targetSelector = new TargetSelector(_targetScorer);
        _autoResume.ShouldUseCqReset = ShouldUseIdleRecovery;
        _autoResume.ShouldClickEnableTx = ShouldClickEnableTxRecovery;

        Dashboard = new DashboardViewModel();
        DxAssist = new DxAssistViewModel();
        Wanted = new WantedViewModel();
        SessionHistory = new SessionHistoryViewModel();
        Scheduler = new SchedulerViewModel();
        DxAssist.AutoSelectBestCq = Settings.Settings.AutoSelectBestCq;

        foreach (var item in _settingsService.LoadSchedule())
            Scheduler.ScheduleItems.Add(item);

        StartDxAssistCommand = new RelayCommand(StartDxAssist);
        StartWantedSniperCommand = new RelayCommand(StartWantedSniper);
        StartAutoResumeCommand = new RelayCommand(StartDxAssist);
        StopAutoResumeCommand = new RelayCommand(StopAll);
        StartUdpCommand = new RelayCommand(StartUdpAsync);
        StopUdpCommand = new RelayCommand(StopUdp);
        SelectBestTargetCommand = new RelayCommand(SelectBestTarget);
        ReplyToBestCommand = new RelayCommand(ReplyToBestAsync);
        LoadAdifCommand = new RelayCommand(LoadAdif);
        SaveSettingsCommand = new RelayCommand(SaveAll);
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
        Wanted.CallWantedCommand = new RelayCommand(async item => await CallWantedItemAsync(item as WantedItem));
        Wanted.WatchOnlyCommand = new RelayCommand(item => WatchWantedItem(item as WantedItem));
        Wanted.SuppressWantedCommand = new RelayCommand(item => SuppressWantedItem(item as WantedItem));
        Wanted.CopyCallsignCommand = new RelayCommand(item => CopyWantedCallsign(item as WantedItem));
        Wanted.CopyRawMessageCommand = new RelayCommand(item => CopyWantedRawMessage(item as WantedItem));
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
        _wantedRefreshTimer.Tick += (_, _) => RefreshWantedTimeColumns();
        _wantedRefreshTimer.Start();

        WireEvents();
        Dashboard.OverallStatus = "V3 ready. Start UDP and AutoResume when JTDX is open.";
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
        UpdateHuntStateDisplay();
        LoadAdifSources();
        StartAdifWatcher();
    }

    public DashboardViewModel Dashboard { get; }
    public DxAssistViewModel DxAssist { get; }
    public WantedViewModel Wanted { get; }
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

    public ICommand StartAutoResumeCommand { get; }
    public ICommand StartDxAssistCommand { get; }
    public ICommand StartWantedSniperCommand { get; }
    public ICommand StopAutoResumeCommand { get; }
    public ICommand StartUdpCommand { get; }
    public ICommand StopUdpCommand { get; }
    public ICommand SelectBestTargetCommand { get; }
    public ICommand ReplyToBestCommand { get; }
    public ICommand LoadAdifCommand { get; }
    public ICommand SaveSettingsCommand { get; }
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

    private void WireEvents()
    {
        _udpListener.StatusChanged += message => Dispatch(() =>
        {
            Dashboard.UdpStatus = message;
            AddAction(message);
        });

        _udpListener.DecodeReceived += decode => Dispatch(() =>
        {
            _targetScorer.EnrichDecode(decode, _logbook, _adifMergeResult.Indexes, Settings.Settings);
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
        });

        _udpListener.StatusMessageReceived += status => Dispatch(() =>
        {
            _ = ProcessJtdxStatusForCurrentTargetAsync(status);
        });

        _autoResume.StatusChanged += message => Dispatch(() =>
        {
            var statusAge = DateTime.Now - _lastAutoResumeStatusUiAt;
            if ((message.Equals(_lastAutoResumeStatusUi, StringComparison.Ordinal)
                    || message.StartsWith("AutoResume running.", StringComparison.Ordinal))
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

    private async void StartDxAssist()
    {
        Settings.Settings.WantedSniperMode = "Off";
        Settings.Refresh();
        AddAction("Mode selected: DX Assist. Wanted Sniper stopped.");
        await StartAutoResumeAsync();
    }

    private async void StartWantedSniper()
    {
        Settings.Settings.WantedSniperMode = "Armed";
        Settings.Refresh();
        AddAction("Mode selected: Wanted Sniper Armed. DX Assist hunting paused.");
        await StartAutoResumeAsync();
    }

    private async Task StartAutoResumeAsync()
    {
        SaveAll();
        LoadAdifSources();
        StartAdifWatcher();
        if (!_udpListener.IsRunning)
            await StartUdpAsync();
        _autoResume.Start(Settings.Settings, Scheduler.ScheduleItems);
        AddAction(CurrentWantedSniperMode() == WantedSniperMode.Off
            ? "Start mode: DX Assist."
            : $"Start mode: Wanted Sniper {CurrentWantedSniperMode()}; DX Assist hunting paused.");
        _huntTimer.Start();
        await HuntTickAsync();
        if (CurrentWantedSniperMode() == WantedSniperMode.Off)
        {
            ArmEnableTxForSelectedTarget("Start AutoResume");
        }
        else if (_lockedTarget == null)
        {
            EnsureEnableTxOff("Wanted Sniper active at start");
        }
        Dashboard.OverallStatus = CurrentWantedSniperMode() == WantedSniperMode.Off
            ? "DX Assist is running."
            : $"Wanted Sniper {CurrentWantedSniperMode()} is active; DX Assist hunting is paused.";
    }

    private async void StopAll()
    {
        _autoResume.Stop();
        _huntTimer.Stop();
        Settings.Settings.WantedSniperMode = "Off";
        Settings.Refresh();
        await ReleaseLockedTargetAndMaybeResumeAsync("AutoResume stopped", "Abandoned - AutoResume stopped", suppress: false, resumeSniper: false);
        EnsureEnableTxOff("Stop All");
        Dashboard.OverallStatus = "Stopped. DX Assist and Wanted Sniper are off.";
        AddAction("Stop All: DX Assist stopped, Wanted Sniper off, active target cleared.");
    }

    private async void StopUdp()
    {
        _udpListener.Stop();
        if (_autoResume.IsRunning)
        {
            _autoResume.Stop();
            _huntTimer.Stop();
            await ReleaseLockedTargetAndMaybeResumeAsync("UDP stopped; AutoResume stopped to avoid blind TX control", "Abandoned - AutoResume stopped", suppress: false, resumeSniper: false);
            Dashboard.OverallStatus = "AutoResume stopped because UDP listener was stopped.";
            AddAction("AutoResume stopped because UDP listener was stopped; UDP status is required before enabling TX.");
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
        _rarityService.Load(Settings.Settings.DxccRarityFilePath, _dxccResolver);
        RarityDiagnostics = _rarityService.Diagnostics.Summary;
        _settingsService.SaveSettings(Settings.Settings);
        _settingsService.SaveSchedule(Scheduler.ScheduleItems);
        UpdateAdifDiagnostics();
        AddAction("Settings saved.");
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

        if (_lockedTarget != null && HasFreshLiveQso(_lockedTarget.Callsign))
        {
            CompleteLockedTarget($"QSO released: ADIF confirmed {_lockedTarget.Callsign}.");
        }

        var sniperMode = CurrentWantedSniperMode();
        if (sniperMode != WantedSniperMode.Off)
        {
            if (_huntState is HuntState.Calling or HuntState.InQso)
            {
                if (_qsoStage == QsoStage.CompletionPending && _lockedTarget != null)
                    AddThrottledCompletionLog($"Retarget blocked: QSO completion pending with {_lockedTarget.Callsign}.");

                if (sniperMode == WantedSniperMode.Armed && await TryPreemptForWantedDxccAsync())
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

            if (sniperMode == WantedSniperMode.Armed)
                await TryWantedSniperAsync();
            else
                EnsureEnableTxOff("Wanted Sniper watch mode");

            Dashboard.OverallStatus = $"Wanted Sniper {sniperMode} active; DX Assist general hunting paused.";
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

        if (_huntState == HuntState.Calling && !_targetConfirmedInJtdx && !IsFreshTarget(_lockedTarget))
        {
            var call = _lockedTarget.Callsign;
            await FailCurrentReplySourceAndRetargetAsync($"source decode expired before JTDX accepted the UDP Reply for {call}");
            return;
        }

        if (_huntState == HuntState.Calling && !_targetConfirmedInJtdx && AcquisitionFailed())
        {
            await FailCurrentReplySourceAndRetargetAsync($"JTDX did not confirm {_lockedTarget.Callsign} within acquisition window");
            return;
        }

        var maxCallAttempts = Math.Max(1, Settings.Settings.MaxCallAttempts);
        var maxReportAttempts = Math.Max(1, Settings.Settings.MaxReportAttempts);

        if (_huntState == HuntState.Calling
            && _jtdxShowsWrongTx
            && DateTime.Now - _lastSelectionNudgeAt >= Ft8AttemptCycle)
        {
            AddAction($"JTDX is not aimed at {_lockedTarget.Callsign}; nudging UDP Reply without CQ/TX6 reset.");
            _lastCorrectiveAction = $"Sent UDP Reply nudge to {_lockedTarget.Callsign}";
            await SendReplyAsync(_lockedTarget, countAttempt: false);
            _lastSelectionNudgeAt = DateTime.Now;
            UpdateHuntStateDisplay();
            return;
        }

        if (_huntState == HuntState.Calling && _callAttemptCount >= maxCallAttempts)
        {
            await ReleaseLockedTargetAndMaybeResumeAsync(
                $"Target released: {_lockedTarget.Callsign} - call attempts exceeded {_callAttemptCount}/{maxCallAttempts}",
                "Missed - no reply",
                suppress: true,
                resumeSniper: true);
            return;
        }

        if (_huntState == HuntState.Calling && NoQsoProgressTimedOut())
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

        if (_huntState == HuntState.Calling && DateTime.Now - _lastCallAttemptAt >= Ft8AttemptCycle)
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
                AddAction($"Call attempt {_callAttemptCount + 1}/{maxCallAttempts} - calling {_lockedTarget.Callsign}.");
                RecordCallAttempt();
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

    private void LogPostQsoSelectingNext(string callsign)
    {
        if (_postQsoTransitionUntil == DateTime.MinValue || DateTime.Now < _postQsoTransitionUntil)
            return;

        AddAction($"Post-QSO transition: selecting next target {callsign}.");
        _postQsoTransitionUntil = DateTime.MinValue;
    }

    private async Task TryWantedSniperAsync()
    {
        if (_wantedSniperBusy || CurrentWantedSniperMode() != WantedSniperMode.Armed)
            return;
        if (!_autoResume.IsRunning)
        {
            Wanted.Status = "Wanted Sniper armed, but AutoResume is stopped.";
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
                Wanted.Status = "Wanted Sniper armed: watching, no actionable wanted target right now.";
                LogWantedSniperNoTarget();
                EnsureEnableTxOff("Wanted Sniper armed");
                return;
            }

            var target = _targetScorer.Score(best.SourceDecode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);
            target.Reasons.Insert(0, best.WantedDetail);
            LogPostQsoSelectingNext(target.Callsign);
            Wanted.Status = $"Wanted Sniper target selected: {best.ContactableCall} - {best.WantedDetail}";
            AddAction($"Wanted Sniper armed target: {best.ContactableCall} from {best.Block}; {best.WantedDetail}; method {best.SelectionMethod}.");
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
            || _wantedSourceBlock.Equals("Wanted DXCC", StringComparison.OrdinalIgnoreCase)
            || _qsoStage >= QsoStage.TargetReportSeen)
        {
            return false;
        }

        var dxcc = SelectWantedDxccOverride(requireActionable: false);
        if (dxcc == null)
            return false;

        var previous = _lockedTarget?.Callsign ?? "current target";
        UpdateWantedActionability(dxcc);
        AddAction($"Wanted DXCC override: releasing {previous} ({_wantedSourceBlock}) because {dxcc.ContactableCall} appeared; {dxcc.WantedDetail}; {dxcc.ActionabilityText}.");
        SuppressTarget(previous);
        ClearLockedTarget($"Released {previous} because Wanted DXCC {dxcc.ContactableCall} appeared.");
        if (dxcc.IsActionable)
        {
            await TryWantedSniperAsync();
        }
        else
        {
            Wanted.Status = $"Wanted DXCC seen: {dxcc.ContactableCall} - waiting for CQ/grid row. Lower-priority hunting paused.";
            AddAction($"Wanted DXCC hold: {dxcc.ContactableCall} is not actionable yet ({dxcc.NotActionableReason}); TX off while waiting for a usable row.");
            EnsureEnableTxOff("Wanted DXCC hold");
            UpdateHuntStateDisplay();
        }

        return true;
    }

    private WantedItem? SelectWantedDxccOverride(bool requireActionable = true)
    {
        foreach (var item in Wanted.WantedDxcc)
            UpdateWantedActionability(item);

        return Wanted.WantedDxcc
            .Where(item => requireActionable ? item.IsActionable : IsWantedDxccPriorityCandidate(item))
            .OrderBy(item => item.NeedStatus == NeedStatus.NeverWorked ? 0 : 1)
            .ThenBy(item => item.PriorityTier ?? int.MaxValue)
            .ThenByDescending(item => item.AdjustedDxValueScore ?? 0)
            .ThenByDescending(item => item.UKDesirability ?? 0)
            .ThenByDescending(item => item.LastSeenUtc)
            .ThenByDescending(item => item.Snr)
            .FirstOrDefault();
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

        if (Settings.Settings.EnableWantedDxcc)
        {
            var actionableDxcc = Wanted.WantedDxcc.Any(item => item.IsActionable);
            var blockingDxcc = Wanted.WantedDxcc
                .Where(IsWantedDxccPriorityCandidate)
                .OrderBy(item => item.NeedStatus == NeedStatus.NeverWorked ? 0 : 1)
                .ThenBy(item => item.PriorityTier ?? int.MaxValue)
                .ThenByDescending(item => item.AdjustedDxValueScore ?? 0)
                .ThenByDescending(item => item.LastSeenUtc)
                .FirstOrDefault();

            if (!actionableDxcc && blockingDxcc != null)
                return null;
        }

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

    private bool IsWantedDxccPriorityCandidate(WantedItem item)
    {
        UpdateWantedActionability(item);
        if (item.IsActionable)
            return true;

        if (item.NeedStatus is not (NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed))
            return false;

        var age = (DateTime.UtcNow - item.LastSeenUtc).TotalSeconds;
        if (age > Math.Max(15, Settings.Settings.ManualWantedMaxAgeSeconds))
            return false;

        return item.ActionabilityStatus == WantedActionabilityStatus.NotTargetable
            || item.ActionabilityStatus == WantedActionabilityStatus.SourceDecodeMissing
            || item.ActionabilityStatus == WantedActionabilityStatus.InvalidParse;
    }

    private async Task TryUpgradeLockedWantedSourceAsync(DecodeMessage decode)
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
            AddAction($"Wanted Sniper source upgraded for {upgraded.Callsign}: using fresh {decode.MessageTypeText} '{decode.RawText}' instead of stale/progress source.");
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

        AddAction($"Wanted Sniper armed: {wantedCount} wanted rows, {actionableCount} actionable.{detail}");
    }

    private async Task SendReplyAsync(DxTarget target, bool countAttempt = true)
    {
        if (!IsFreshTarget(target))
        {
            AddAction($"Selection blocked for {target.Callsign}: source decode is stale ({FormatAge(DateTime.Now - target.Decode.ReceivedAt)} old).");
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
        if (!JtdxSelectionController.ShouldUseUdpReply(target.Decode))
        {
            var sourceKey = ReplySourceKey(target.Decode);
            if (_guiSelectionAttemptedSources.Contains(sourceKey))
            {
                AddAction($"GUI double-click not repeated for {target.Callsign}: source row already had one automated click attempt.");
                return;
            }

            _guiSelectionAttemptedSources.Add(sourceKey);
        }

        var selection = await _selectionController.SelectTargetAsync(
            target,
            Settings.Settings,
            endpoint,
            fallbackEndpoint,
            destinationAppId,
            sendFallback);

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

        AddAction($"Selection attempt: raw '{selection.TargetRawMessage}', expected {selection.ExpectedCall}, type {selection.MessageType}, method {selection.SelectionMethod}, model v{selection.VisibleRowModelVersion}, calibration {selection.CalibrationVersion}, row {selection.ScreenRowIndex?.ToString() ?? "n/a"}, click {selection.ClickX?.ToString() ?? "n/a"},{selection.ClickY?.ToString() ?? "n/a"}, before DX '{selection.JtdxDxCallBefore}', after DX '{selection.JtdxDxCallAfter}', success {selection.Success}, failure {selection.FailureReason}.");

        if (!selection.Success)
        {
            _lastCorrectiveAction = selection.FailureText;
            AddAction($"{selection.SelectionMethod} selection failed for {target.Callsign}: {selection.FailureText}. TX remains blocked until JTDX confirms the expected DX Call.");
            if (_lockedTarget?.Callsign.Equals(target.Callsign, StringComparison.OrdinalIgnoreCase) == true
                && _huntState == HuntState.Calling
                && !_targetConfirmedInJtdx)
            {
                _failedReplySources[ReplySourceKey(target.Decode)] = DateTime.Now;
                await ReleaseLockedTargetAndMaybeResumeAsync(
                    $"Selection failed for {target.Callsign}: {selection.FailureText}",
                    "Abandoned - selection failed",
                    suppress: false,
                    resumeSniper: true);
            }
            return;
        }

        _targetConfirmedInJtdx = true;
        _lastCorrectiveAction = selection.SelectionMethod == JtdxSelectionMethod.GuiGridDoubleClick
            ? $"GUI grid double-click confirmed for {target.Callsign}"
            : $"UDP Reply confirmed for {target.Callsign}";
        AddAction($"{selection.SelectionMethod} selection confirmed for {target.Callsign}. {selection.Details}");
        ArmEnableTxForSelectedTarget($"{selection.SelectionMethod} selection confirmed");
    }

    private async Task LockAndReplyAsync(DxTarget target, string source, string wantedReason, string sourceBlock)
    {
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
        DxAssist.BestTarget = target;
        _huntState = HuntState.Calling;
        _targetStartedAt = DateTime.Now;
        _targetStartedUtc = DateTime.UtcNow;
        _lastReplyAt = DateTime.MinValue;
        _lastCallAttemptAt = DateTime.MinValue;
        _lastSelectionNudgeAt = DateTime.MinValue;
        _lastAcquisitionAttemptAt = DateTime.MinValue;
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
        AddAction($"{source} target locked {target.Callsign}: {wantedReason}.");
        AddAction($"Reply source selected: {target.Decode.RawText}, age {FormatAge(DateTime.Now - target.Decode.ReceivedAt)}, offset {target.Decode.AudioOffset?.ToString() ?? "unknown"}.");
        await SendReplyAsync(target, countAttempt: false);
        _lastCallAttemptAt = DateTime.Now;
        QueueReplyWhenIdleIfTransmitting($"{source} initial reply");
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

    private void EnsureEnableTxOff(string source)
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

        if (looksOff || DateTime.Now - _lastForcedTxOffAt < TimeSpan.FromSeconds(4))
            return;

        _lastForcedTxOffAt = DateTime.Now;
        _clicker.MoveClickRestore(settings.EnableTxX, settings.EnableTxY);
        AddAction($"{source}: no wanted target to hunt; clicked Enable TX off.");
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
        return _acquisitionAttemptCount >= maxNudges
            && elapsed >= TimeSpan.FromSeconds(15 * maxCycles);
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

        if (DateTime.Now - _manualTxOffDetectedAt < Ft8AttemptCycle)
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

        if (!resumeSniper || CurrentWantedSniperMode() != WantedSniperMode.Armed)
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
        AddAction($"Reply source failed: {failed.Decode.RawText}. Candidate {failed.Callsign} remains eligible if heard again.");
        ClearLockedTarget($"No usable confirmed reply from current source for {failed.Callsign}; retargeting.");

        if (CurrentWantedSniperMode() != WantedSniperMode.Off)
        {
            if (CurrentWantedSniperMode() == WantedSniperMode.Armed)
                await TryWantedSniperAsync();
            else
                EnsureEnableTxOff("Wanted Sniper recovery");
            UpdateHuntStateDisplay();
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

    private bool IsFailedReplySource(DecodeMessage decode)
    {
        ExpireFailedReplySources();
        return _failedReplySources.ContainsKey(ReplySourceKey(decode));
    }

    private void ExpireFailedReplySources()
    {
        var cutoff = DateTime.Now.AddSeconds(-Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds));
        foreach (var item in _failedReplySources.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList())
            _failedReplySources.Remove(item);
    }

    private static string ReplySourceKey(DecodeMessage decode)
    {
        return $"{decode.Callsign}|{decode.RawText}|{decode.AudioOffset}|{decode.DecodeTime?.TotalMilliseconds}|{decode.ReceivedAt:O}";
    }

    private async Task ProcessJtdxStatusForCurrentTargetAsync(JtdxStatusMessage status)
    {
        if (PreventUnwantedCq(status))
            return;

        if (_lockedTarget == null)
            return;

        if (!_autoResume.IsRunning)
        {
            ClearLockedTarget("AutoResume stopped; clearing locked target.");
            return;
        }

        var targetCall = _lockedTarget.Callsign.Trim().ToUpperInvariant();
        _actualJtdxDxCall = status.DxCall.Trim();
        _lastObservedTransmitState = BuildObservedTransmitState(status);
        if (_qsoStage == QsoStage.CompletionPending && _pendingLockedReplyWhenIdle)
        {
            _pendingLockedReplyWhenIdle = false;
            _pendingLockedReplyReason = "";
            AddThrottledCompletionLog($"Retarget blocked: QSO completion pending with {_lockedTarget.Callsign}.");
        }

        if (!status.Transmitting && _pendingLockedReplyWhenIdle)
        {
            _pendingLockedReplyWhenIdle = false;
            var reason = string.IsNullOrWhiteSpace(_pendingLockedReplyReason) ? "queued correction" : _pendingLockedReplyReason;
            _pendingLockedReplyReason = "";
            _lastCorrectiveAction = $"Sent queued UDP Reply to {targetCall}";
            AddAction($"JTDX is idle/RX; sending queued UDP Reply to {targetCall} ({reason}).");
            await SendReplyAsync(_lockedTarget, countAttempt: false);
            ArmEnableTxForSelectedTarget("Queued UDP Reply");
            _lastSelectionNudgeAt = DateTime.Now;
        }

        var statusMatchesTarget = status.DxCall.Equals(targetCall, StringComparison.OrdinalIgnoreCase);

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

        if (_huntState == HuntState.Calling && _targetConfirmedInJtdx && status.TxEnabled)
        {
            var counted = RecordCallAttempt(GetCycleKey(status.ReceivedAt));
            if (counted)
            {
                AddAction($"Observed JTDX TX-enabled call cycle {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)} for {_lockedTarget.Callsign}.");
                if (_callAttemptCount >= Math.Max(1, Settings.Settings.MaxCallAttempts))
                {
                    await ReleaseLockedTargetAndMaybeResumeAsync(
                        $"Target released: {_lockedTarget.Callsign} - call attempts exceeded {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}",
                        "Missed - no reply",
                        suppress: true,
                        resumeSniper: true);
                    return;
                }
            }
        }

        if (status.Transmitting)
            ObserveMyTransmitCycle(status);

        if (_targetConfirmedInJtdx)
        {
            UpdateHuntStateDisplay();
            return;
        }

        _targetConfirmedInJtdx = true;
        AddAction($"Target confirmed by JTDX Status DX Call = {_lockedTarget.Callsign}. TX gate may open.");
        UpdateHuntStateDisplay();
    }

    private bool PreventUnwantedCq(JtdxStatusMessage status)
    {
        var cq = LooksLikeCq(status.TxMessage);
        if (!cq || !status.TxEnabled || _huntState == HuntState.InQso)
            return false;

        var postQso = DateTime.Now < _postQsoTransitionUntil;
        var lockedButNotReady = _lockedTarget != null
            && !_targetConfirmedInJtdx
            && !_targetConfirmedInFeed
            && !status.TxMessage.Contains(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase);
        var noSafeTargetLoaded = _lockedTarget == null && (_selectedIntendedTarget != null || postQso);
        if (!postQso && !lockedButNotReady && !noSafeTargetLoaded)
            return false;

        if (DateTime.Now - _lastForcedTxOffAt < TimeSpan.FromSeconds(5))
            return true;

        _lastForcedTxOffAt = DateTime.Now;
        _lastCorrectiveAction = "Forced Enable TX off to prevent unwanted CQ";
        _recoveryMode = postQso ? "PostQsoTransition" : "WaitingForJtdxIdle";
        AddAction($"Prevented unwanted CQ '{status.TxMessage}'; clicked Enable TX off before next target is safely loaded.");
        _clicker.MoveClickRestore(Settings.Settings.EnableTxX, Settings.Settings.EnableTxY);
        return true;
    }

    private async Task NudgeLockedTargetAfterResumeAsync()
    {
        if (_lockedTarget == null)
        {
            var target = _selectedIntendedTarget ?? DxAssist.BestTarget;
            if (target != null && target.Decode.ReceivedAt > DateTime.Now.AddSeconds(-Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds)))
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
            _lastCorrectiveAction = "Clicked Enable TX only; UDP Reply nudge skipped during QSO";
            AddAction($"Locked recovery: Enable TX clicked only; QSO is already in progress with {_lockedTarget.Callsign}, so original UDP Reply was not resent.");
        }
        else
        {
            _targetConfirmedInJtdx = false;
            _jtdxShowsWrongTx = true;
            _lastCorrectiveAction = $"Clicked Enable TX only; sent UDP Reply nudge to {_lockedTarget.Callsign}";
            AddAction($"Locked recovery: Enable TX clicked only; nudging {_lockedTarget.Callsign} again.");
            await SendReplyAsync(_lockedTarget, countAttempt: false);
        }
        _lastSelectionNudgeAt = DateTime.Now;
        UpdateHuntStateDisplay();
    }

    private static bool LooksLikeCqOrWrongTarget(JtdxStatusMessage status, string targetCall)
    {
        if (!string.IsNullOrWhiteSpace(status.DxCall)
            && !status.DxCall.Equals(targetCall, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tx = status.TxMessage.Trim();
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
            if (hadActuallyAttemptedTarget)
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
        _jtdxShowsWrongTx = true;
        var isCqMismatch = LooksLikeCq(status.TxMessage);
        _txVerificationState = isCqMismatch ? "CQ mismatch - correcting" : "Mismatch";
        AddAction($"TX mismatch: expected target {targetCall} but detected {DescribeMismatch(status)}.");

        if (isCqMismatch && _huntState != HuntState.InQso)
        {
            _lastCorrectiveAction = status.Transmitting
                ? $"JTDX is transmitting CQ; queued UDP Reply to {targetCall} for RX/idle"
                : $"JTDX was calling CQ; resent UDP Reply to {targetCall}";
            if (status.Transmitting)
            {
                QueueReplyWhenIdle($"CQ mismatch while transmitting {status.TxMessage}");
                AddAction($"JTDX is transmitting CQ while {targetCall} is locked; queued UDP Reply for the next RX/idle moment.");
                ClickEnableTxOffForWrongTransmit(status, $"CQ while {targetCall} is locked");
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
                if (hadActuallyAttemptedTarget)
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
            QueueReplyWhenIdle($"{reason}; observed '{status.TxMessage}' / DX Call {status.DxCall}");
            _wrongTargetNudgeSent = true;
            _lastCorrectiveAction = $"Queued UDP Reply to {targetCall} for next RX/idle";
            AddAction($"JTDX is transmitting the wrong target/CQ while {targetCall} is locked; queued UDP Reply for next RX/idle.");
            ClickEnableTxOffForWrongTransmit(status, reason);
            return;
        }

        if (DateTime.Now - _lastSelectionNudgeAt < TimeSpan.FromSeconds(3))
        {
            QueueReplyWhenIdle($"{reason}; retry throttled");
            _lastCorrectiveAction = $"Retry queued for {targetCall}";
            return;
        }

        _wrongTargetNudgeSent = true;
        _lastCorrectiveAction = $"Sent UDP Reply correction to {targetCall}";
        AddAction($"UDP Reply correction sent to locked target {targetCall} ({reason}).");
        await SendReplyAsync(_lockedTarget, countAttempt: false);
        _lastSelectionNudgeAt = DateTime.Now;
        ArmEnableTxForSelectedTarget("Wrong-target correction");
    }

    private void ClickEnableTxOffForWrongTransmit(JtdxStatusMessage status, string reason)
    {
        if (!status.TxEnabled || DateTime.Now - _lastForcedTxOffAt < TimeSpan.FromSeconds(4))
            return;

        _lastForcedTxOffAt = DateTime.Now;
        _lastCorrectiveAction = $"Clicked Enable TX off during wrong transmit: {reason}";
        AddAction($"Clicked Enable TX off during wrong transmit ({reason}); correct UDP Reply is queued for RX/idle.");
        _clicker.MoveClickRestore(Settings.Settings.EnableTxX, Settings.Settings.EnableTxY);
    }

    private bool HasActuallyAttemptedLockedTarget()
    {
        return _targetConfirmedInJtdx
            || _targetConfirmedInFeed
            || _callAttemptCount > 0;
    }

    private void QueueReplyWhenIdleIfTransmitting(string reason)
    {
        if (_udpListener.LastStatus?.Transmitting == true)
            QueueReplyWhenIdle(reason);
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
        if (DateTime.Now - _lastWrongTargetNoProgressAt < Ft8AttemptCycle)
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
        if (CurrentWantedSniperMode() != WantedSniperMode.Off)
        {
            _recoveryMode = "WantedSniper";
            _lastCorrectiveAction = "CQ/TX6 idle recovery blocked because Wanted Sniper is active";
            return false;
        }

        var freshBestCandidate = DxAssist.BestTarget != null && DxAssist.BestTarget.Decode.ReceivedAt > DateTime.Now.AddSeconds(-Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds));
        var postQsoTransition = DateTime.Now < _postQsoTransitionUntil;
        var idleRecovery = _lockedTarget == null
            && _selectedIntendedTarget == null
            && !Settings.Settings.AutoHuntEnabled
            && _huntState == HuntState.Idle
            && _qsoStage == QsoStage.None
            && !postQsoTransition
            && !freshBestCandidate;

        if (idleRecovery)
        {
            _recoveryMode = "IdleRecovery";
            return true;
        }

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

        return idleRecovery;
    }

    private bool ShouldClickEnableTxRecovery()
    {
        var sniperMode = CurrentWantedSniperMode();
        if (sniperMode == WantedSniperMode.Watch || sniperMode == WantedSniperMode.Armed && _lockedTarget == null)
        {
            _recoveryMode = "WantedSniper";
            _lastCorrectiveAction = sniperMode == WantedSniperMode.Watch
                ? "Enable TX blocked because Wanted Sniper is watch-only"
                : "Enable TX blocked because Wanted Sniper has no locked wanted target";
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
        if (DateTime.Now - _lastTxMismatchCycleAt < Ft8AttemptCycle)
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

        if (_huntState == HuntState.Calling && IsInitialCallTransmitForLockedTarget(status.TxMessage))
        {
            if (RecordCallAttempt(cycleKey))
                AddAction($"Observed TX call attempt {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)} for {_lockedTarget.Callsign}: {txMessage}.");
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

    private static string GetCycleKey(DecodeMessage decode)
    {
        if (decode.DecodeTime.HasValue)
        {
            var seconds = (int)decode.DecodeTime.Value.TotalSeconds;
            return $"decode:{seconds / 30}";
        }

        return GetCycleKey(decode.ReceivedAt);
    }

    private static string GetCycleKey(DateTime timestamp)
    {
        return $"clock:{timestamp.Ticks / TimeSpan.FromSeconds(30).Ticks}";
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
                AddAction($"Observed decode TX call attempt {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)} for {_lockedTarget.Callsign}: {decode.RawText}.");
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
        if (!reason.Contains("stuck", StringComparison.OrdinalIgnoreCase)
            && !reason.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
        {
            _stuckReason = "";
        }
        UpdateHuntStateDisplay();
    }

    private void SuppressTarget(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return;

        var until = DateTime.Now.AddMinutes(Math.Max(1, Settings.Settings.SuppressFailedTargetMinutes));
        _suppressedTargets[callsign] = until;
        TrackOpportunitySuppressed(callsign, until, "Target suppressed");
        AddAction($"{callsign} suppressed until {until:HH:mm:ss}.");
        RemoveWantedItemsForCall(callsign, "suppressed after retry limit");
    }

    private bool IsSuppressed(string callsign)
    {
        return _suppressedTargets.TryGetValue(callsign, out var until) && until > DateTime.Now;
    }

    private void ExpireSuppressedTargets()
    {
        foreach (var call in _suppressedTargets.Where(kvp => kvp.Value <= DateTime.Now).Select(kvp => kvp.Key).ToList())
            _suppressedTargets.Remove(call);
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

        LogbookStatus = $"Full {_adifMergeResult.FullQsoCount} + live {_adifMergeResult.LiveQsoCount} = {_logbook.Count} unique QSOs.";
        AdifDiagnostics =
            $"Full ADIF path: {DisplayPath(fullPath)}\n"
            + $"Full ADIF loaded: {_fullLogbook.Count > 0}  QSOs: {_adifMergeResult.FullQsoCount}  Last loaded: {DisplayTime(_lastFullAdifLoadedAt)}  Exists: {FileExists(fullPath)}\n"
            + $"Live JTDX ADIF path: {DisplayPath(livePath)}\n"
            + $"Live JTDX ADIF watched: {Settings.Settings.WatchLiveJtdxAdif}  QSOs: {_adifMergeResult.LiveQsoCount}  Last loaded: {DisplayTime(_lastLiveAdifReloadAt)}  Exists: {FileExists(livePath)}\n"
            + $"Combined unique QSOs: {_logbook.Count}  Duplicates merged: {_adifMergeResult.DuplicateCount}\n"
            + $"DXCC worked: {_adifMergeResult.Indexes.Dxcc.Count}  DXCC confirmed: {dxccConfirmed}  Grids worked: {_adifMergeResult.Indexes.Grids.Count}  States worked: {_adifMergeResult.Indexes.States.Count}  IOTA worked: {_adifMergeResult.Indexes.Iotas.Count}\n"
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
            return $"Best candidate differs from active QSO target. AutoResume is monitoring {locked} and will not call {best} until this QSO completes.";
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
            DxAssist.CallAttemptsText = $"Call Attempts {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}";
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
            : $"{(_targetConfirmedInJtdx ? "JTDX target selected" : _jtdxShowsWrongTx ? "Correcting JTDX CQ/wrong target" : "Waiting for JTDX to select target")}. FT8 call cycles {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}.";
        DxAssist.MoveOnAt = _huntState == HuntState.InQso
            ? "Holding while QSO progresses; repeated/stuck stages will move on at the report limit."
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
        DxAssist.CallAttemptsText = $"Call Attempts {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}";
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
        var sniperMode = CurrentWantedSniperMode();
        var operatingMode = !_autoResume.IsRunning
            ? "Stopped"
            : _huntState == HuntState.InQso
                ? "QSO In Progress"
                : sniperMode == WantedSniperMode.Armed
                    ? "Wanted Sniper Armed"
                    : sniperMode == WantedSniperMode.Watch
                        ? "Wanted Sniper Watch"
                        : "DX Assist";

        CurrentTargetStatus.OperatingMode = operatingMode;
        CurrentTargetStatus.SelectedTargetCall = target?.Callsign ?? "";
        CurrentTargetStatus.SelectedTargetEntity = target?.Decode.EntityName ?? "";
        CurrentTargetStatus.SelectedTargetDisplay = target == null ? "No target selected" : TargetDisplayWithDash(target);
        CurrentTargetStatus.TargetSource = string.IsNullOrWhiteSpace(_targetSource) ? "None" : _targetSource;
        CurrentTargetStatus.WantedReason = target == null
            ? "Reason unavailable - check diagnostics"
            : TargetReasonFormatter.FormatGeneral(string.IsNullOrWhiteSpace(_wantedReason) ? target.PrimaryReason : _wantedReason);
        CurrentTargetStatus.WantedCategory = target == null ? "None" : SessionCategory(target);
        CurrentTargetStatus.WantedScope = ScopeDisplay(CurrentWantedScope());
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
        CurrentTargetStatus.DebugStatusMessage = $"State {_huntState}; stage {_qsoStage}; confirmed JTDX {_targetConfirmedInJtdx}; confirmed feed {_targetConfirmedInFeed}; recovery {_recoveryMode}; correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)}.";
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
        if (CurrentWantedSniperMode() == WantedSniperMode.Watch)
            return "TX blocked - Wanted Sniper watch mode";
        if (target == null)
            return "TX disabled - no target selected";
        if (!_targetConfirmedInJtdx)
            return $"TX blocked - waiting for JTDX confirmation of {target.Callsign}";
        return "TX allowed";
    }

    private string AttemptCounterLabel()
    {
        if (_huntState == HuntState.Calling && _jtdxShowsWrongTx)
            return $"Wrong target correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)}";
        if (_huntState == HuntState.Calling)
            return $"Call attempt {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}";
        if (_huntState == HuntState.InQso && _reportAttemptCount > 0)
            return $"Report repeat {_reportAttemptCount}/{Math.Max(1, Settings.Settings.MaxReportAttempts)}";
        if (_qsoStage == QsoStage.CompletionPending)
            return $"Completion grace {_completionGraceCycleCount}/{Math.Max(1, Settings.Settings.CompletionGraceCycles)}";
        return "";
    }

    private string PlainStatusMessage(DxTarget? target)
    {
        if (!_autoResume.IsRunning)
            return "AutoResume monitoring is stopped.";
        if (target == null)
            return "No target selected.";
        if (_huntState == HuntState.Calling && _jtdxShowsWrongTx)
            return $"Wrong target correction {_txMismatchCycleCount}/{Math.Max(1, Settings.Settings.MaxTransmitMismatchCycles)} - expected {target.Callsign}, JTDX currently shows {(string.IsNullOrWhiteSpace(_actualJtdxDxCall) ? "blank/unknown" : _actualJtdxDxCall)}.";
        if (_huntState == HuntState.Calling)
            return $"Calling {target.Callsign} - call attempt {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}.";
        if (_huntState == HuntState.InQso)
            return _qsoStage == QsoStage.CompletionPending
                ? $"Completion pending with {target.Callsign} - waiting for ADIF/log confirmation."
                : $"QSO in progress with {target.Callsign} - {FormatQsoStage(_qsoStage)}.";
        return "No target selected.";
    }

    private void UpdateNextBestTargets()
    {
        var recent = CurrentCandidateDecodes();
        var eligible = recent
            .Where(d => !string.IsNullOrWhiteSpace(d.Callsign))
            .Where(d => !IsFailedReplySource(d))
            .Where(d => _lockedTarget == null || !DecodeTargetCall(d).Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)))
            .Where(d => !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .Where(d => !IsSuppressed(DecodeTargetCall(d)))
            .Where(IsSelectableDecodeForAcquisition)
            .ToList();

        var ranked = _targetSelector.SelectRanked(eligible, _logbook, _adifMergeResult.Indexes, Settings.Settings, 50, includeActiveQso: false);
        TrackOpportunitiesSeen(ranked);
        DxAssist.NextBestTargets.Clear();
        foreach (var target in ranked.Take(8))
            DxAssist.NextBestTargets.Add(target);

        var candidateTargets = new List<DxTarget>();
        if (_lockedTarget != null)
            candidateTargets.Add(_lockedTarget);
        candidateTargets.AddRange(ranked.Where(t => _lockedTarget == null || !t.Callsign.Equals(_lockedTarget.Callsign, StringComparison.OrdinalIgnoreCase)));

        var rows = candidateTargets
            .Select((target, index) => BuildCandidateRow(target, index + 1))
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
            UpdatePreviewBestTarget(rows.FirstOrDefault());
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
        var age = DateTime.Now - decode.ReceivedAt;
        var dxccStatus = FormatDxccStatus(ranking.DxccStatus);
        var gridStatus = GridStatus(decode);
        var stateStatus = StateStatus(decode);
        var targetStatus = TargetStatus(target, decode, age);
        var wantedReason = string.IsNullOrWhiteSpace(ranking.PrimaryWantedReason)
            ? FriendlyWantedReason(target, dxccStatus, gridStatus, stateStatus)
            : ranking.PrimaryWantedReason;

        return new DxCandidateRow
        {
            JtdxRow = JtdxRowText(decode),
            Rank = rank,
            Call = target.Callsign,
            Country = string.IsNullOrWhiteSpace(decode.EntityName) ? decode.PrimaryDisplayEntity : decode.EntityName,
            Dxcc = decode.Dxcc,
            Tier = ranking.PriorityTierName,
            WantedReason = wantedReason,
            DxccStatus = dxccStatus,
            RarityRank = ranking.RarityRank,
            RarityScore = ranking.RarityScore,
            Grid = decode.Grid,
            GridStatus = gridStatus,
            State = decode.State,
            StateStatus = stateStatus,
            Rarity = ranking.RarityRank.HasValue ? $"#{ranking.RarityRank}" : "default",
            DistanceMiles = decode.DistanceMiles,
            Age = FormatAge(age),
            Snr = decode.Snr,
            SourceType = decode.MessageTypeText,
            Score = target.Score,
            TargetStatus = targetStatus,
            PriorityClass = CandidatePriorityClass(targetStatus, dxccStatus, gridStatus, stateStatus),
            Details = BuildCandidateDetails(target, dxccStatus, gridStatus, stateStatus, age),
            Target = target
        };
    }

    private bool PassesCandidateFilters(DxCandidateRow row)
    {
        if (DxAssist.ShowOnlyTargetable && row.TargetStatus is "Watch only" or "Not targetable")
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
        if (!decode.EntityName.Equals("United States", StringComparison.OrdinalIgnoreCase))
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
        if (!decode.Targetable)
            return decode.ParseConfidence == ParseConfidence.Low ? "Not targetable" : "Watch only";
        if (age.TotalSeconds > Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds))
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

    private static string CandidatePriorityClass(string targetStatus, string dxccStatus, string gridStatus, string stateStatus)
    {
        if (targetStatus is "Locked" or "Calling" or "In QSO")
            return "Locked";
        if (targetStatus == "Suppressed")
            return "Suppressed";
        if (targetStatus is "Stale" or "Watch only" or "Not targetable" or "Worked live")
            return "Muted";
        if (dxccStatus is "Not worked" or "Worked, unconfirmed")
            return "DxccWanted";
        if (stateStatus == "New")
            return "StateWanted";
        if (gridStatus == "New")
            return "GridWanted";
        return "";
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
            + $"DXCC: {decode.Dxcc}  Status: {dxccStatus}  Mode: {ranking.DxccConfirmationMode}\n"
            + $"Worked: {ranking.DxccWorked}  Confirmed: {ranking.DxccConfirmed}  Source: {DisplaySource(ranking.DxccConfirmationSource)}\n"
            + $"Rarity rank: {ranking.RarityRank?.ToString() ?? "default"}  Rarity score: {ranking.RarityScore}  Match: {ranking.RarityMatchSource}/{ranking.RarityMatchConfidence}\n"
            + $"Grid: {(string.IsNullOrWhiteSpace(decode.Grid) ? "None" : decode.Grid)}  Grid status: {gridStatus}\n"
            + $"State: {(string.IsNullOrWhiteSpace(decode.State) ? "None" : decode.State)}  State status: {stateStatus}\n"
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
        item.OutcomeReason = manual ? "Manual Wanted target selected" : "AutoResume selected target";
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
        item.OutcomeReason = $"Call attempt {_callAttemptCount}/{Math.Max(1, Settings.Settings.MaxCallAttempts)}";
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
        else if (reason.Contains("Abandoned - AutoResume stopped", StringComparison.OrdinalIgnoreCase))
        {
            item.Outcome = "Abandoned - AutoResume stopped";
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

        return ranking.PriorityTier is <= 40 or 60;
    }

    private SessionDxOpportunity UpsertSessionOpportunity(DxTarget target)
    {
        var key = SessionOpportunityKey(target);
        var item = SessionHistory.AllOpportunities.FirstOrDefault(o => o.OpportunityId.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            item = new SessionDxOpportunity
            {
                OpportunityId = key,
                FirstSeenUtc = DecodeSeenUtc(target.Decode),
                LastSeenUtc = DecodeSeenUtc(target.Decode),
                Call = target.Callsign
            };
            SessionHistory.AllOpportunities.Add(item);
        }

        var decode = target.Decode;
        var ranking = target.Ranking;
        var seenUtc = DecodeSeenUtc(decode);
        if (seenUtc > item.LastSeenUtc)
            item.LastSeenUtc = seenUtc;
        if (item.FirstSeenUtc == DateTime.MinValue || seenUtc < item.FirstSeenUtc)
            item.FirstSeenUtc = seenUtc;
        item.Call = target.Callsign;
        item.Entity = string.IsNullOrWhiteSpace(decode.EntityName) ? ranking.Entity : decode.EntityName;
        item.DxccNumber = decode.Dxcc;
        item.DxccStatus = FormatSessionDxccStatus(ranking.DxccStatus);
        item.Category = SessionCategory(target);
        item.Need = SessionNeed(target);
        item.Scope = "Overall";
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
        return Settings.Settings.SessionHistoryGroupMode.Equals("ByDXCC", StringComparison.OrdinalIgnoreCase)
            ? dxcc
            : $"{dxcc}:{target.Callsign.ToUpperInvariant()}";
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
            FileName = $"AutoResume-Session-History-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("FirstSeen,LastSeen,Age,Call,Country,DXCC,DXCCStatus,RarityRank,RarityScore,Reason,BestSNR,LastSNR,Grid,SeenCount,Attempts,Outcome,OutcomeReason,Worked,WorkedSource,SourceType,SourceRawMessage");
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
            FileName = $"AutoResume-Recent-Actions-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("AutoResume V3 Recent Actions Export");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Overall: {Dashboard.OverallStatus}");
        sb.AppendLine($"UDP: {Dashboard.UdpStatus}");
        sb.AppendLine($"AutoResume: {Dashboard.AutoResumeStatus}");
        sb.AppendLine($"Hunt State: {Dashboard.HuntState}");
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
        if (System.Windows.MessageBox.Show("Clear Session History for this app session?", "AutoResume", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
        ExpireWantedItems();
        if (string.IsNullOrWhiteSpace(decode.Callsign)
            || string.IsNullOrWhiteSpace(decode.ContactableCall)
            || decode.ParseConfidence == ParseConfidence.Low
            || string.IsNullOrWhiteSpace(decode.RawText))
        {
            return;
        }

        var decodeAge = DateTime.Now - decode.ReceivedAt;
        if (decodeAge.TotalSeconds > Math.Max(15, Settings.Settings.ManualWantedMaxAgeSeconds))
            return;

        if (IsRecentlyWorkedLive(decode.ContactableCall))
        {
            RemoveWantedItemsForCall(decode.ContactableCall, "recent live ADIF QSO");
            return;
        }

        var scope = CurrentWantedScope();
        var scored = _targetScorer.Score(decode, _logbook, _adifMergeResult.Indexes, _decodeHistory, Settings.Settings);

        if (!string.IsNullOrWhiteSpace(decode.Dxcc) && !decode.EntityName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            var need = EvaluateDxccNeed(decode.Dxcc, decode.Band, decode.Mode, scope);
            if (need is NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed)
                UpsertWanted(Wanted.WantedDxcc, decode, scored, "DXCC", "Wanted DXCC", BuildWantedReason(need, "DXCC", decode.EntityName, decode.Band, decode.Mode, scope), need, scope, decode.Dxcc);
        }

        if (IsValidGrid(decode.Grid))
        {
            var normalized = MaidenheadGrid.Normalize(decode.Grid);
            var grid4 = normalized.IsValid ? normalized.Grid4 : decode.Grid.Trim().ToUpperInvariant();
            var status = _adifMergeResult.Indexes.Grids.GetValueOrDefault(grid4);
            var need = EvaluateSimpleNeed(status, decode.Band, decode.Mode, scope);
            LogGridWantedDecision(decode, normalized, scope, status, need);
            if (need is NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed)
                UpsertWanted(Wanted.WantedGrids, decode, scored, "Grid", "Wanted Grids", BuildWantedReason(need, "grid", grid4, decode.Band, decode.Mode, scope), need, scope, grid4);
        }

        if (decode.EntityName.Equals("United States", StringComparison.OrdinalIgnoreCase) && IsValidState(decode.State))
        {
            var need = EvaluateSimpleNeed(_adifMergeResult.Indexes.States.GetValueOrDefault(decode.State), decode.Band, decode.Mode, scope);
            if (need is NeedStatus.NeverWorked or NeedStatus.WorkedNotLoTWConfirmed)
                UpsertWanted(Wanted.WantedStates, decode, scored, "USA State", "Wanted USA States", BuildWantedReason(need, "state", decode.State, decode.Band, decode.Mode, scope), need, scope, decode.State);
        }

        if (CurrentWantedSniperMode() == WantedSniperMode.Armed)
            _ = TryUpgradeLockedWantedSourceAsync(decode);

        if (CurrentWantedSniperMode() == WantedSniperMode.Armed)
            _ = TryWantedSniperAsync();
    }

    private WantedScope CurrentWantedScope()
    {
        return Enum.TryParse<WantedScope>(Settings.Settings.WantedScope, ignoreCase: true, out var scope)
            ? scope
            : WantedScope.Overall;
    }

    private WantedSniperMode CurrentWantedSniperMode()
    {
        return Enum.TryParse<WantedSniperMode>(Settings.Settings.WantedSniperMode, ignoreCase: true, out var mode)
            ? mode
            : WantedSniperMode.Off;
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
        item.WantedDetail = detail;
        item.WantedReason = detail;
        item.NeedStatus = needStatus;
        item.WantedScope = scope;
        var normalizedGrid = MaidenheadGrid.Normalize(decode.Grid);
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
        item.StateSource = string.IsNullOrWhiteSpace(decode.State) ? "" : "Decode";
        item.Band = decode.Band;
        item.Mode = decode.Mode;
        item.Snr = decode.Snr;
        item.Dt = decode.Dt;
        item.Offset = decode.AudioOffset;
        item.MessageType = decode.MessageTypeText;
        item.PriorityTier = scored.Ranking.PriorityTier;
        item.AdjustedDxValueScore = scored.Ranking.AdjustedDxValueScore;
        item.ClubLogRank = scored.Ranking.RarityRank;
        item.UKDesirability = scored.Ranking.UKDesirability;
        item.DistanceMiles = scored.Ranking.DistanceMiles;
        item.LastSeenUtc = DecodeSeenUtc(decode);
        item.SourceRawMessage = decode.RawText;
        item.SourceDecode = decode;
        item.JtdxRow = JtdxRowText(decode);
        UpdateWantedActionability(item);
        if (existing == null)
        {
            list.Insert(0, item);
            AddAction($"{block} added: {item.Call} {item.Entity} - {item.WantedDetail}; {item.ActionabilityText}.");
        }
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
            item.NotActionableReason = "Failed source row";
            return;
        }

        var age = (DateTime.UtcNow - item.LastSeenUtc).TotalSeconds;
        if (age > Math.Max(15, Settings.Settings.ManualWantedMaxAgeSeconds))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.Stale;
            item.NotActionableReason = "Stale";
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

        if (!IsSelectableDecodeForAcquisition(item.SourceDecode))
        {
            item.ActionabilityStatus = WantedActionabilityStatus.NotTargetable;
            item.NotActionableReason = item.SourceDecode.Targetable ? "Not visible in JTDX grid and not CQ/UDP-selectable" : "Not targetable";
            return;
        }

        var useUdpReply = JtdxSelectionController.ShouldUseUdpReply(item.SourceDecode);
        if (!useUdpReply)
        {
            var guiMaxAge = Math.Max(15, Settings.Settings.JtdxGuiMaxRowAgeSeconds);
            if ((DateTime.Now - item.SourceDecode.ReceivedAt).TotalSeconds > guiMaxAge)
            {
                item.ActionabilityStatus = WantedActionabilityStatus.Stale;
                item.NotActionableReason = "GUI row too old";
                return;
            }
        }

        item.ActionabilityStatus = WantedActionabilityStatus.Actionable;
        item.IsActionable = true;
        item.SelectionMethod = useUdpReply ? "UdpReply" : "GuiGridDoubleClick";
        item.NotActionableReason = "";
    }

    private bool IsSelectableDecodeForAcquisition(DecodeMessage decode)
    {
        return decode.Targetable
            && decode.ParseConfidence != ParseConfidence.Low
            && !string.IsNullOrWhiteSpace(decode.ContactableCall)
            && (JtdxSelectionController.ShouldUseUdpReply(decode) || _visibleRowModel.FindDecode(decode) != null);
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

    private static bool IsValidState(string state)
    {
        return state.Length == 2 && state.All(char.IsLetter);
    }

    private void ExpireWantedItems()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(30, Settings.Settings.WantedItemExpirySeconds));
        TrimWanted(Wanted.WantedDxcc, cutoff);
        TrimWanted(Wanted.WantedGrids, cutoff);
        TrimWanted(Wanted.WantedStates, cutoff);
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
        return row == null ? "-" : row.ScreenRowIndex.ToString();
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

    private static void TrimWanted(ObservableCollection<WantedItem> list, DateTime cutoff)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].LastSeenUtc < cutoff || list.Count > 50)
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
        var cutoff = DateTime.Now.AddSeconds(-Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds));
        return _decodeHistory.Where(d => d.ReceivedAt >= cutoff).ToList();
    }

    private bool IsFreshTarget(DxTarget? target)
    {
        if (target == null)
            return false;

        var maxAge = Math.Max(30, Settings.Settings.CandidateMaxAgeSeconds);
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
        Settings.Settings.JtdxBandMonitorId = $"{window.Left},{window.Top}";
        JtdxBandActivityGridCalibration.CreateDefault(window).SaveTo(Settings.Settings);
        Settings.Refresh();
        SaveAll();
        DxAssist.GuiSelectionStatus = $"GUI Selection: captured '{window.Title}' pid {window.ProcessId}, size {window.Width}x{window.Height}. Loaded default 52-row Band Activity grid.";
        Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
        AddAction(DxAssist.GuiSelectionStatus);
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
        settings.JtdxBandVisibleRowCount = JtdxBandActivityGridCalibration.SafeFullRowCount;
        settings.JtdxBandIgnoredPartialTopRow = true;

        var width = settings.JtdxBandActivityRight - settings.JtdxBandActivityLeft;
        var height = settings.JtdxBandActivityBottom - settings.JtdxBandActivityTop;
        if (width <= 0 || height <= 0)
            return;

        settings.JtdxBandRowHeight = height / (JtdxBandActivityGridCalibration.SafeFullRowCount + 0.5);
        settings.JtdxBandFirstRowCenterY = (int)Math.Round(settings.JtdxBandActivityTop + settings.JtdxBandRowHeight);
        if (settings.JtdxBandMessageClickX <= settings.JtdxBandActivityLeft || settings.JtdxBandMessageClickX >= settings.JtdxBandActivityRight)
            settings.JtdxBandMessageClickX = settings.JtdxBandActivityLeft + Math.Max(20, width / 2);

        settings.JtdxBandCalibrationVersion = $"grid-v1-{DateTime.Now:yyyyMMddHHmmss}";
        settings.JtdxBandCalibrationDate = DateTime.Now;
        _visibleRowModel.Rebuild(_decodeHistory, JtdxBandActivityGridCalibration.FromSettings(settings));
    }

    private void ShowBandActivityGridOverlay()
    {
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
            calibration = JtdxBandActivityGridCalibration.CreateDefault(window);
            calibration.SaveTo(Settings.Settings);
            Settings.Refresh();
            SaveAll();
        }

        if (_bandActivityOverlay == null)
        {
            _bandActivityOverlay = new JtdxBandActivityOverlay();
            _bandActivityOverlay.CalibrationChanged += SaveOverlayCalibration;
            _bandActivityOverlay.Closed += (_, _) => _bandActivityOverlay = null;
        }

        if (_bandActivityOverlay.Owner == null && System.Windows.Application.Current?.MainWindow != null)
            _bandActivityOverlay.Owner = System.Windows.Application.Current.MainWindow;

        _bandActivityOverlay.ShowCalibration(calibration, window.Left, window.Top);
        DxAssist.GuiSelectionStatus = $"Grid Overlay: showing 52 safe rows over '{window.Title}'. Drag the grid; mouse wheel adjusts height, Shift+wheel adjusts width.";
        Dashboard.OverallStatus = DxAssist.GuiSelectionStatus;
        AddAction(DxAssist.GuiSelectionStatus);
    }

    public void Dispose()
    {
        if (_bandActivityOverlay == null)
            return;

        try
        {
            _bandActivityOverlay.Close();
        }
        catch
        {
        }

        _bandActivityOverlay = null;
    }

    private void SaveOverlayCalibration(JtdxBandActivityGridCalibration calibration)
    {
        calibration.SaveTo(Settings.Settings);
        Settings.Refresh();
        SaveAll();
        _visibleRowModel.Rebuild(_decodeHistory, calibration);
        DxAssist.GuiSelectionStatus = $"Grid Overlay: saved left {calibration.BandActivityLeftRelative}, top {calibration.BandActivityTopRelative}, width {calibration.BandActivityWidth}, height {calibration.BandActivityHeight}, row height {calibration.RowHeight:0.00}.";
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

        var guiMaxAge = Math.Max(15, Settings.Settings.JtdxGuiMaxRowAgeSeconds);
        var cutoff = DateTime.Now.AddSeconds(-guiMaxAge);
        var replacement = _decodeHistory
            .Where(decode => decode.ReceivedAt >= cutoff)
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
            ? "GUI Selection calibrated: 52-row grid set. AutoResume uses UDP decodes as truth and calibrated row geometry for directed rows."
            : "GUI Selection: calibration incomplete.";
    }

    private void AddAction(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
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
