using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using Microsoft.Win32;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class SetupWizardViewModel : ObservableObject
{
    public const string GridTrackerAndLogger = "GridTracker + logging program";
    public const string GridTrackerOnly = "GridTracker only";
    public const string LoggerOnly = "Logging program only (no GridTracker)";
    public const string JtdxOnly = "JTDX only";
    private const int LastStep = 6;
    private static readonly Regex CallsignPattern = new(
        @"^(?=.{3,15}$)(?=.*[A-Z])(?=.*\d)[A-Z0-9/]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GridPattern = new(
        @"^[A-R]{2}\d{2}(?:[A-X]{2}(?:\d{2})?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private int _currentStep;
    private string _connectionSetup = GridTrackerAndLogger;
    private string _callsign;
    private string _homeGrid;
    private int _udpListenPort;
    private int _udpReplyFallbackPort;
    private bool _udpForwardEnabled;
    private string _udpForwardHost;
    private int _udpForwardPort;
    private int _loggingProgramPort;
    private string _fullAdifPath;
    private string _liveJtdxAdifPath;
    private bool _autoLoadFullAdifOnStartup;
    private bool _watchLiveJtdxAdif;
    private string _jtdxAllTxtPath;
    private bool _watchJtdxAllTxt;
    private bool _autoHuntEnabled;
    private bool _jtdxGuiSelectionEnabled;
    private int _enableTxX;
    private int _enableTxY;
    private int _enableTxOffRgb;
    private DateTime _enableTxCalibrationDate;
    private bool _isEnableTxCalibrated;
    private string _enableTxCalibrationStatus = "Enable TX has not been calibrated for this screen layout.";
    private int _jtdxVisibleRowCount;
    private bool _isGridCalibrated;
    private string _gridCalibrationStatus = "JTDX grid calibration has not been completed.";
    private string _errorMessage = "";
    private JtdxBandActivityGridCalibration? _pendingCalibration;

    public SetupWizardViewModel(AppSettings settings, bool isFirstRun)
    {
        TargetSettings = settings;
        IsFirstRun = isFirstRun;
        _callsign = settings.MyCallsign;
        _homeGrid = settings.HomeGrid;
        _udpListenPort = settings.UdpListenPort;
        _udpReplyFallbackPort = settings.UdpReplyFallbackPort;
        _udpForwardEnabled = settings.UdpForwardEnabled;
        _udpForwardHost = settings.UdpForwardHost;
        _udpForwardPort = settings.UdpForwardPort;
        _loggingProgramPort = settings.DownstreamLoggerPort;
        _fullAdifPath = settings.FullAdifPath;
        _liveJtdxAdifPath = settings.LiveJtdxAdifPath;
        _autoLoadFullAdifOnStartup = settings.AutoLoadFullAdifOnStartup;
        _watchLiveJtdxAdif = settings.WatchLiveJtdxAdif;
        _jtdxAllTxtPath = settings.JtdxAllTxtPath;
        _watchJtdxAllTxt = settings.WatchJtdxAllTxt;
        _autoHuntEnabled = settings.AutoHuntEnabled;
        _jtdxGuiSelectionEnabled = settings.JtdxGuiSelectionEnabled;
        _enableTxX = settings.EnableTxX;
        _enableTxY = settings.EnableTxY;
        _enableTxOffRgb = settings.EnableTxOffRgb;
        _enableTxCalibrationDate = settings.EnableTxCalibrationDate;
        _isEnableTxCalibrated = settings.EnableTxCalibrationDate != DateTime.MinValue;
        if (_isEnableTxCalibrated)
            _enableTxCalibrationStatus = $"Enable TX was calibrated at X={settings.EnableTxX}, Y={settings.EnableTxY} on {settings.EnableTxCalibrationDate:g}.";
        _jtdxVisibleRowCount = JtdxBandActivityGridCalibration.NormalizeRowCount(settings.JtdxBandVisibleRowCount);
        var existingCalibration = JtdxBandActivityGridCalibration.FromSettings(settings);
        _isGridCalibrated = existingCalibration.IsUsable && settings.JtdxBandCalibrationDate != DateTime.MinValue;
        if (_isGridCalibrated)
            _gridCalibrationStatus = $"Existing calibration from {settings.JtdxBandCalibrationDate:g} is ready. Recalibrate if JTDX has moved or changed size.";

        BackCommand = new RelayCommand(Back);
        NextCommand = new RelayCommand(Next);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
        BrowseFullAdifCommand = new RelayCommand(BrowseFullAdif);
        BrowseLiveAdifCommand = new RelayCommand(BrowseLiveAdif);
        BrowseAllTxtCommand = new RelayCommand(BrowseAllTxt);
        UseJtdxDefaultsCommand = new RelayCommand(UseJtdxDefaults);
        StartGridCalibrationCommand = new RelayCommand(() => CalibrationRequested?.Invoke(this, EventArgs.Empty));
        CaptureEnableTxCommand = new RelayCommand(() => EnableTxCaptureRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler<bool>? CloseRequested;
    public event EventHandler? CalibrationRequested;
    public event EventHandler? EnableTxCaptureRequested;

    private AppSettings TargetSettings { get; }
    public bool IsFirstRun { get; }
    public ICommand BackCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseFullAdifCommand { get; }
    public ICommand BrowseLiveAdifCommand { get; }
    public ICommand BrowseAllTxtCommand { get; }
    public ICommand UseJtdxDefaultsCommand { get; }
    public ICommand StartGridCalibrationCommand { get; }
    public ICommand CaptureEnableTxCommand { get; }
    public IReadOnlyList<string> ConnectionSetupOptions { get; } =
        [GridTrackerAndLogger, GridTrackerOnly, LoggerOnly, JtdxOnly];

    public string ConnectionSetup
    {
        get => _connectionSetup;
        set
        {
            if (!SetProperty(ref _connectionSetup, value))
                return;
            UdpForwardEnabled = UsesForwarding;
            OnPropertyChanged(nameof(UsesGridTracker));
            OnPropertyChanged(nameof(UsesLoggingProgram));
            OnPropertyChanged(nameof(UsesForwarding));
            OnPropertyChanged(nameof(JtdxPortInstructions));
            OnPropertyChanged(nameof(GridTrackerPortInstructions));
            OnPropertyChanged(nameof(LoggingProgramInstructions));
            OnPropertyChanged(nameof(ConnectionSummary));
        }
    }

    public bool UsesGridTracker => ConnectionSetup is GridTrackerAndLogger or GridTrackerOnly;
    public bool UsesLoggingProgram => ConnectionSetup is GridTrackerAndLogger or LoggerOnly;
    public bool UsesForwarding => UsesGridTracker || UsesLoggingProgram;

    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (!SetProperty(ref _currentStep, value))
                return;
            ErrorMessage = "";
            OnPropertyChanged(nameof(StepNumber));
            OnPropertyChanged(nameof(StepTitle));
            OnPropertyChanged(nameof(StepDescription));
            OnPropertyChanged(nameof(NextButtonText));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(ProgressWidth));
        }
    }

    public int StepNumber => CurrentStep + 1;
    public string StepTitle => CurrentStep switch
    {
        0 => "Welcome to DX Pilot",
        1 => "Your station",
        2 => "Connect to JTDX",
        3 => "Choose your log files",
        4 => "Calibrate Enable TX",
        5 => "Calibrate the JTDX grid",
        _ => "Review and finish"
    };
    public string StepDescription => CurrentStep switch
    {
        0 => "A few essentials are all DX Pilot needs to get started.",
        1 => "These identify your station and make distance and worked-status calculations accurate.",
        2 => "Use the same UDP values in JTDX Reporting settings.",
        3 => "DX Pilot combines your main log with JTDX's live files.",
        4 => "DX Pilot must know the exact safe button position and its OFF colour on this computer.",
        5 => "Align the overlay with every complete Band Activity row before enabling GUI selection.",
        _ => "You can change every option later from Settings."
    };
    public string NextButtonText => CurrentStep == LastStep ? "Finish setup" : "Continue";
    public string CancelButtonText => IsFirstRun ? "Skip for now" : "Cancel";
    public bool CanGoBack => CurrentStep > 0;
    public double ProgressWidth => 112 * StepNumber;

    public string Callsign
    {
        get => _callsign;
        set => SetProperty(ref _callsign, value);
    }

    public string HomeGrid
    {
        get => _homeGrid;
        set => SetProperty(ref _homeGrid, value);
    }

    public int UdpListenPort
    {
        get => _udpListenPort;
        set
        {
            if (SetProperty(ref _udpListenPort, value))
            {
                OnPropertyChanged(nameof(JtdxPortInstructions));
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }
    }

    public int UdpReplyFallbackPort
    {
        get => _udpReplyFallbackPort;
        set => SetProperty(ref _udpReplyFallbackPort, value);
    }

    public bool UdpForwardEnabled
    {
        get => _udpForwardEnabled;
        set => SetProperty(ref _udpForwardEnabled, value);
    }

    public string UdpForwardHost
    {
        get => _udpForwardHost;
        set => SetProperty(ref _udpForwardHost, value);
    }

    public int UdpForwardPort
    {
        get => _udpForwardPort;
        set
        {
            if (SetProperty(ref _udpForwardPort, value))
            {
                OnPropertyChanged(nameof(GridTrackerPortInstructions));
                OnPropertyChanged(nameof(LoggingProgramInstructions));
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }
    }

    public int LoggingProgramPort
    {
        get => _loggingProgramPort;
        set
        {
            if (SetProperty(ref _loggingProgramPort, value))
            {
                OnPropertyChanged(nameof(GridTrackerPortInstructions));
                OnPropertyChanged(nameof(LoggingProgramInstructions));
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }
    }

    public string FullAdifPath
    {
        get => _fullAdifPath;
        set
        {
            if (SetProperty(ref _fullAdifPath, value))
                OnPropertyChanged(nameof(FullAdifStatus));
        }
    }

    public string LiveJtdxAdifPath
    {
        get => _liveJtdxAdifPath;
        set
        {
            if (SetProperty(ref _liveJtdxAdifPath, value))
                OnPropertyChanged(nameof(LiveAdifStatus));
        }
    }

    public bool AutoLoadFullAdifOnStartup
    {
        get => _autoLoadFullAdifOnStartup;
        set => SetProperty(ref _autoLoadFullAdifOnStartup, value);
    }

    public bool WatchLiveJtdxAdif
    {
        get => _watchLiveJtdxAdif;
        set => SetProperty(ref _watchLiveJtdxAdif, value);
    }

    public string JtdxAllTxtPath
    {
        get => _jtdxAllTxtPath;
        set
        {
            if (SetProperty(ref _jtdxAllTxtPath, value))
                OnPropertyChanged(nameof(AllTxtStatus));
        }
    }

    public bool WatchJtdxAllTxt
    {
        get => _watchJtdxAllTxt;
        set => SetProperty(ref _watchJtdxAllTxt, value);
    }

    public bool AutoHuntEnabled
    {
        get => _autoHuntEnabled;
        set => SetProperty(ref _autoHuntEnabled, value);
    }

    public bool JtdxGuiSelectionEnabled
    {
        get => _jtdxGuiSelectionEnabled;
        set => SetProperty(ref _jtdxGuiSelectionEnabled, value);
    }

    public bool IsEnableTxCalibrated
    {
        get => _isEnableTxCalibrated;
        private set => SetProperty(ref _isEnableTxCalibrated, value);
    }

    public string EnableTxCalibrationStatus
    {
        get => _enableTxCalibrationStatus;
        private set => SetProperty(ref _enableTxCalibrationStatus, value);
    }

    public string EnableTxCoordinates => IsEnableTxCalibrated
        ? $"X={_enableTxX}, Y={_enableTxY}, OFF colour #{_enableTxOffRgb:X6}"
        : "Not calibrated";

    public int JtdxVisibleRowCount
    {
        get => _jtdxVisibleRowCount;
        set => SetProperty(ref _jtdxVisibleRowCount, value);
    }

    public bool IsGridCalibrated
    {
        get => _isGridCalibrated;
        private set => SetProperty(ref _isGridCalibrated, value);
    }

    public string GridCalibrationStatus
    {
        get => _gridCalibrationStatus;
        private set => SetProperty(ref _gridCalibrationStatus, value);
    }

    public string FullAdifStatus => FileStatus(FullAdifPath, "Optional — no main log selected");
    public string LiveAdifStatus => FileStatus(LiveJtdxAdifPath, "No live ADIF selected");
    public string AllTxtStatus => FileStatus(JtdxAllTxtPath, "No ALL.TXT selected");
    public string JtdxPortInstructions =>
        $"Set UDP Server to 127.0.0.1 and UDP Server port number to {UdpListenPort}. Tick Accept UDP requests. "
        + (UsesForwarding ? "Also tick Enable sending logged QSO ADIF data for the selected companion-program setup. " : "")
        + "Notify on accepted UDP request is optional; Accepted UDP request restores window is helpful if JTDX may be minimised. "
        + "The separate 2nd UDP server is not used by DX Pilot, so leave it unchanged if another program uses it.";
    public string GridTrackerPortInstructions =>
        $"Under Receive UDP Messages Received from JTDX, set Port to {UdpForwardPort} and leave Multicast? off. "
        + (UsesLoggingProgram
            ? $"Under Forward UDP Messages e.g. GridTracker on another host, set IP to 127.0.0.1, Port to {LoggingProgramPort}, and tick Enabled?"
            : "A Forward UDP Messages destination is not needed for the selected setup.");
    public string LoggingProgramInstructions =>
        $"Configure the program to receive JTDX/WSJT-X messages on port {LoggingProgramPort}. "
        + $"Log4OM example: Settings → Configuration → Software integration → Connections → UDP. Add or enable UDP INBOUND with Service type JT_MESSAGE and Port {LoggingProgramPort}. "
        + (UsesGridTracker
            ? "GridTracker supplies this feed. Other ADIF_MESSAGE connections are unrelated."
            : "DX Pilot sends directly to this port; GridTracker settings are not required. Other ADIF_MESSAGE connections are unrelated.");
    public string ConnectionSummary => ConnectionSetup switch
    {
        GridTrackerAndLogger => $"JTDX {UdpListenPort} → DX Pilot → GridTracker {UdpForwardPort} → logging program {LoggingProgramPort}",
        GridTrackerOnly => $"JTDX {UdpListenPort} → DX Pilot → GridTracker {UdpForwardPort}",
        LoggerOnly => $"JTDX {UdpListenPort} → DX Pilot → logging program {LoggingProgramPort}",
        _ => $"JTDX {UdpListenPort} → DX Pilot"
    };

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private void Back()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    private void Next()
    {
        if (!ValidateCurrentStep())
            return;

        if (CurrentStep < LastStep)
        {
            CurrentStep++;
            return;
        }

        ApplyToTarget();
        CloseRequested?.Invoke(this, true);
    }

    private bool ValidateCurrentStep()
    {
        ErrorMessage = "";
        if (CurrentStep == 1)
        {
            var callsign = Callsign.Trim();
            var grid = HomeGrid.Trim();
            if (!CallsignPattern.IsMatch(callsign))
                ErrorMessage = "Enter a valid amateur-radio callsign, for example G1CEC.";
            else if (!GridPattern.IsMatch(grid))
                ErrorMessage = "Enter a 4, 6, or 8 character Maidenhead grid, for example IO91 or IO91WM.";
        }
        else if (CurrentStep == 2)
        {
            if (!ValidPort(UdpListenPort) || !ValidPort(UdpReplyFallbackPort))
                ErrorMessage = "The JTDX UDP Server and DX Pilot reply fallback ports must be between 1 and 65535.";
            else if (UsesForwarding && string.IsNullOrWhiteSpace(UdpForwardHost))
                ErrorMessage = "Enter a forwarding host, or turn UDP forwarding off.";
            else if (UsesGridTracker && !ValidPort(UdpForwardPort))
                ErrorMessage = "The GridTracker Receive UDP port must be between 1 and 65535.";
            else if (UsesLoggingProgram && !ValidPort(LoggingProgramPort))
                ErrorMessage = "The logging-program port must be between 1 and 65535.";
            else if (UsedUdpPorts().Distinct().Count() != UsedUdpPorts().Count)
                ErrorMessage = "Each selected program must use a different port so they do not compete for the same UDP messages.";
        }
        else if (CurrentStep == 3)
        {
            if (WatchLiveJtdxAdif && string.IsNullOrWhiteSpace(LiveJtdxAdifPath))
                ErrorMessage = "Choose the live JTDX ADIF file, or turn off live-log watching.";
            else if (WatchJtdxAllTxt && string.IsNullOrWhiteSpace(JtdxAllTxtPath))
                ErrorMessage = "Choose JTDX ALL.TXT, or turn off outgoing-message monitoring.";
        }
        else if (CurrentStep == 4 && !IsEnableTxCalibrated)
        {
            ErrorMessage = "Capture the OFF/grey Enable TX button before continuing.";
        }
        else if (CurrentStep == 5 && !IsGridCalibrated)
        {
            ErrorMessage = "Calibrate the Band Activity grid before continuing. Open JTDX, choose Start calibration, align the overlay, then press Esc.";
        }

        return !HasError;
    }

    private void ApplyToTarget()
    {
        TargetSettings.MyCallsign = Callsign.Trim().ToUpperInvariant();
        TargetSettings.HomeGrid = HomeGrid.Trim().ToUpperInvariant();
        TargetSettings.UdpListenPort = UdpListenPort;
        TargetSettings.UdpReplyFallbackPort = UdpReplyFallbackPort;
        TargetSettings.UdpForwardEnabled = UsesForwarding;
        TargetSettings.UdpForwardHost = UdpForwardHost.Trim();
        TargetSettings.UdpForwardPort = UsesGridTracker ? UdpForwardPort : LoggingProgramPort;
        TargetSettings.DownstreamLoggerPort = LoggingProgramPort;
        TargetSettings.FullAdifPath = FullAdifPath.Trim();
        TargetSettings.LiveJtdxAdifPath = LiveJtdxAdifPath.Trim();
        TargetSettings.AdifFilePath = TargetSettings.LiveJtdxAdifPath;
        TargetSettings.AutoLoadFullAdifOnStartup = AutoLoadFullAdifOnStartup;
        TargetSettings.WatchLiveJtdxAdif = WatchLiveJtdxAdif;
        TargetSettings.JtdxAllTxtPath = JtdxAllTxtPath.Trim();
        TargetSettings.WatchJtdxAllTxt = WatchJtdxAllTxt;
        TargetSettings.AutoHuntEnabled = AutoHuntEnabled;
        TargetSettings.JtdxGuiSelectionEnabled = JtdxGuiSelectionEnabled;
        TargetSettings.EnableTxX = _enableTxX;
        TargetSettings.EnableTxY = _enableTxY;
        TargetSettings.EnableTxOffRgb = _enableTxOffRgb;
        TargetSettings.EnableTxCalibrationDate = _enableTxCalibrationDate;
        TargetSettings.JtdxBandVisibleRowCount = JtdxBandActivityGridCalibration.NormalizeRowCount(JtdxVisibleRowCount);
        _pendingCalibration?.SaveTo(TargetSettings);
        TargetSettings.SetupWizardCompleted = true;
    }

    public bool TryPrepareCalibration(
        out JtdxBandActivityGridCalibration calibration,
        out JtdxWindowInfo? window)
    {
        ErrorMessage = "";
        window = new JtdxWindowLocator().FindMainWindow(TargetSettings.JtdxWindowTitleMatch);
        if (window == null)
        {
            calibration = new JtdxBandActivityGridCalibration();
            ErrorMessage = $"JTDX was not found. Open and restore its main window, then try again (title match: '{TargetSettings.JtdxWindowTitleMatch}').";
            return false;
        }

        calibration = _pendingCalibration ?? JtdxBandActivityGridCalibration.FromSettings(TargetSettings);
        calibration = calibration.IsUsable
            ? calibration
            : JtdxBandActivityGridCalibration.CreateDefault(window, JtdxVisibleRowCount);
        calibration.SafeVisibleFullRowCount = JtdxBandActivityGridCalibration.NormalizeRowCount(JtdxVisibleRowCount);
        return true;
    }

    public void BeginEnableTxCapture()
    {
        ErrorMessage = "";
        EnableTxCalibrationStatus = "In JTDX, make sure Enable TX is OFF and grey. Hover over a clear grey part of the button (not its lettering), then press Space or Enter. Press Esc to cancel.";
    }

    public void AcceptEnableTxCapture(int x, int y, int rgb)
    {
        _enableTxX = x;
        _enableTxY = y;
        _enableTxOffRgb = rgb;
        _enableTxCalibrationDate = DateTime.Now;
        IsEnableTxCalibrated = true;
        EnableTxCalibrationStatus = $"Enable TX captured at X={x}, Y={y}; OFF colour #{rgb:X6}.";
        OnPropertyChanged(nameof(EnableTxCoordinates));
        ErrorMessage = "";
    }

    public void CancelEnableTxCapture()
    {
        EnableTxCalibrationStatus = IsEnableTxCalibrated
            ? $"Capture cancelled. Keeping {EnableTxCoordinates}."
            : "Capture cancelled; Enable TX still needs calibration.";
    }

    public void AcceptCalibration(JtdxBandActivityGridCalibration calibration)
    {
        _pendingCalibration = calibration;
        JtdxVisibleRowCount = calibration.SafeVisibleFullRowCount;
        IsGridCalibrated = true;
        GridCalibrationStatus = $"Calibrated {calibration.SafeVisibleFullRowCount} rows at {DateTime.Now:t}. Reopen calibration if the lines do not match JTDX.";
        ErrorMessage = "";
    }

    public void CalibrationClosedWithoutChange()
    {
        if (IsGridCalibrated)
            return;
        GridCalibrationStatus = "The overlay closed without being aligned. Drag or resize it at least once, then press Esc.";
        ErrorMessage = GridCalibrationStatus;
    }

    private void UseJtdxDefaults()
    {
        var jtdxFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JTDX");
        LiveJtdxAdifPath = Path.Combine(jtdxFolder, "wsjtx_log.adi");
        JtdxAllTxtPath = Path.Combine(jtdxFolder, $"{DateTime.UtcNow:yyyyMM}_ALL.TXT");
        WatchLiveJtdxAdif = true;
        WatchJtdxAllTxt = true;
    }

    private void BrowseFullAdif()
    {
        var selected = ChooseFile("Choose your main ADIF log", "ADIF logs (*.adi;*.adif)|*.adi;*.adif|All files (*.*)|*.*", FullAdifPath);
        if (selected != null)
            FullAdifPath = selected;
    }

    private void BrowseLiveAdif()
    {
        var selected = ChooseFile("Choose JTDX's live ADIF log", "ADIF logs (*.adi;*.adif)|*.adi;*.adif|All files (*.*)|*.*", LiveJtdxAdifPath);
        if (selected != null)
            LiveJtdxAdifPath = selected;
    }

    private void BrowseAllTxt()
    {
        var selected = ChooseFile("Choose JTDX ALL.TXT", "JTDX text log (*_ALL.TXT)|*_ALL.TXT|Text files (*.txt)|*.txt|All files (*.*)|*.*", JtdxAllTxtPath);
        if (selected != null)
            JtdxAllTxtPath = selected;
    }

    private static string? ChooseFile(string title, string filter, string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            dialog.FileName = Path.GetFileName(currentPath);
            var folder = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                dialog.InitialDirectory = folder;
        }
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static bool ValidPort(int port) => port is >= 1 and <= 65535;

    private List<int> UsedUdpPorts()
    {
        var ports = new List<int> { UdpListenPort };
        if (UsesGridTracker)
            ports.Add(UdpForwardPort);
        if (UsesLoggingProgram)
            ports.Add(LoggingProgramPort);
        return ports;
    }

    private static string FileStatus(string path, string emptyText)
    {
        if (string.IsNullOrWhiteSpace(path))
            return emptyText;
        return File.Exists(path) ? "File found" : "Path saved — file not found yet";
    }
}
