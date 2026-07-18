using System.IO;
using System.Text.Json;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class SettingsService
{
    private const string DefaultAdifPath = @"C:\Users\Chris\AppData\Local\JTDX\wsjtx_log.adi";
    private const string OldWrongJtdxLogPath = @"C:\Users\Chris\AppData\Local\JTDX\wsjtx.log";
    private const string ImportedFullAdifPath = @"C:\Users\Chris\Downloads\FULL.adi";
    private static readonly HashSet<string> ConfirmationModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkedOnly",
        "LoTWOnly",
        "PaperQslOnly",
        "LoTWOrPaper",
        "LoTWOrPaperOrEqsl"
    };
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string AppFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JtdxAutoResume.V3");

    public string SettingsFile => Path.Combine(AppFolder, "app_settings.json");
    public string ScheduleFile => Path.Combine(AppFolder, "band_schedule.json");

    public AppSettings LoadSettings()
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
            NormalizeCoordinateDefaults(settings);
            NormalizeUdpBridgeDefaults(settings);
            NormalizeTimingDefaults(settings);
            NormalizeAdifDefaults(settings);
            NormalizeLayoutDefaults(settings);
            NormalizeJtdxGuiSelectionDefaults(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, _jsonOptions));
        }
        catch
        {
        }
    }

    private static void NormalizeUdpBridgeDefaults(AppSettings settings)
    {
        if (settings.UdpListenPort == 2240)
            settings.UdpListenPort = 2237;

        if (settings.UdpReplyFallbackPort <= 0)
            settings.UdpReplyFallbackPort = 2237;

        if (string.IsNullOrWhiteSpace(settings.UdpForwardHost))
            settings.UdpForwardHost = "127.0.0.1";

        if (settings.UdpForwardPort <= 0)
            settings.UdpForwardPort = 2238;

        settings.UdpForwardEnabled = true;
    }

    private static void NormalizeCoordinateDefaults(AppSettings settings)
    {
        if (settings.EnableTxX == 1433 && settings.EnableTxY == 785)
        {
            settings.EnableTxX = 1360;
            settings.EnableTxY = 781;
        }

        if (settings.CqTx6X == 1200 && settings.CqTx6Y == 860)
        {
            settings.CqTx6X = 1667;
            settings.CqTx6Y = 983;
        }

        if (settings.RxX == 0 && settings.RxY == 0)
        {
            settings.RxX = 110;
            settings.RxY = 1015;
        }

        if (string.IsNullOrWhiteSpace(settings.MyCallsign)
            || settings.MyCallsign.Equals("2E0CCD", StringComparison.OrdinalIgnoreCase)
            || settings.MyCallsign.Equals("G1CCD", StringComparison.OrdinalIgnoreCase))
        {
            settings.MyCallsign = "G1CEC";
        }
    }

    private static void NormalizeTimingDefaults(AppSettings settings)
    {
        if (settings.ReplyConfirmSeconds < 30)
            settings.ReplyConfirmSeconds = 30;

        if (settings.MaxCallAttempts <= 0)
            settings.MaxCallAttempts = 6;

        if (settings.MaxReportAttempts <= 0)
            settings.MaxReportAttempts = 6;

        if (settings.MaxTransmitMismatchCycles <= 0)
            settings.MaxTransmitMismatchCycles = 3;

        if (settings.MaxWrongTargetNoProgressCycles <= 0)
            settings.MaxWrongTargetNoProgressCycles = 2;

        if (string.IsNullOrWhiteSpace(settings.WrongTargetActiveQsoPolicy))
            settings.WrongTargetActiveQsoPolicy = "AdoptAndMonitor";

        if (settings.WantedItemExpirySeconds <= 0)
            settings.WantedItemExpirySeconds = 180;

        if (settings.ManualWantedMaxAgeSeconds <= 0)
            settings.ManualWantedMaxAgeSeconds = 90;

        if (!Enum.TryParse<JtdxAutoResume.V3.Models.WantedScope>(settings.WantedScope, ignoreCase: true, out _))
            settings.WantedScope = JtdxAutoResume.V3.Models.WantedScope.Overall.ToString();

        if (!Enum.TryParse<JtdxAutoResume.V3.Models.WantedSniperMode>(settings.WantedSniperMode, ignoreCase: true, out _))
            settings.WantedSniperMode = JtdxAutoResume.V3.Models.WantedSniperMode.Off.ToString();

        if (settings.CompletionGraceCycles <= 0)
            settings.CompletionGraceCycles = 2;
        if (settings.CompletionTimeoutSeconds < 30)
            settings.CompletionTimeoutSeconds = 120;

        if (settings.SuccessfulQsoSuppressHours <= 0)
            settings.SuccessfulQsoSuppressHours = 24;

        if (settings.RareDxccRankThreshold <= 0)
            settings.RareDxccRankThreshold = 150;

        if (settings.GlobalRarityWeight <= 0)
            settings.GlobalRarityWeight = 0.50;

        if (settings.UkDesirabilityWeight <= 0)
            settings.UkDesirabilityWeight = 0.35;

        if (settings.DistanceWeight <= 0)
            settings.DistanceWeight = 0.15;

        if (string.IsNullOrWhiteSpace(settings.AcceptIncomingCallsMode))
            settings.AcceptIncomingCallsMode = "OnlyIfNoBetterHunterTarget";

        if (settings.SessionHistoryExpiryMinutes < 0)
            settings.SessionHistoryExpiryMinutes = 0;

        if (string.IsNullOrWhiteSpace(settings.SessionHistoryGroupMode))
            settings.SessionHistoryGroupMode = "ByCall";

        if (settings.CandidateMaxAgeSeconds <= 0)
            settings.CandidateMaxAgeSeconds = 90;

        if (string.IsNullOrWhiteSpace(settings.HuntingMode))
            settings.HuntingMode = "DXCC Hunter";

        if (string.IsNullOrWhiteSpace(settings.DxccRarityFilePath)
            || settings.DxccRarityFilePath.EndsWith("DXCC-Rankings.csv", StringComparison.OrdinalIgnoreCase))
        {
            settings.DxccRarityFilePath = Path.Combine(AppContext.BaseDirectory, "Data", "DXCC-UK-Desirability-G1CEC.csv");
        }
    }

    private static void NormalizeAdifDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LiveJtdxAdifPath)
            || settings.LiveJtdxAdifPath.Equals(OldWrongJtdxLogPath, StringComparison.OrdinalIgnoreCase))
        {
            settings.LiveJtdxAdifPath = DefaultAdifPath;
        }

        if (string.IsNullOrWhiteSpace(settings.AdifFilePath)
            || settings.AdifFilePath.Equals(OldWrongJtdxLogPath, StringComparison.OrdinalIgnoreCase)
            || settings.AdifFilePath.Equals(ImportedFullAdifPath, StringComparison.OrdinalIgnoreCase))
        {
            settings.AdifFilePath = settings.LiveJtdxAdifPath;
        }

        if (string.IsNullOrWhiteSpace(settings.LiveJtdxAdifPath))
            settings.LiveJtdxAdifPath = settings.AdifFilePath;

        settings.AdifFilePath = settings.LiveJtdxAdifPath;

        if (!ConfirmationModes.Contains(settings.DxccConfirmationMode))
            settings.DxccConfirmationMode = "LoTWOnly";
        if (!ConfirmationModes.Contains(settings.GridConfirmationMode))
            settings.GridConfirmationMode = "WorkedOnly";
        if (!ConfirmationModes.Contains(settings.StateConfirmationMode))
            settings.StateConfirmationMode = "WorkedOnly";
        if (!ConfirmationModes.Contains(settings.IotaConfirmationMode))
            settings.IotaConfirmationMode = "WorkedOnly";
    }

    private static void NormalizeLayoutDefaults(AppSettings settings)
    {
        settings.DxAssistSelectedTargetPanelWidth = Math.Clamp(
            settings.DxAssistSelectedTargetPanelWidth <= 0 ? 450 : settings.DxAssistSelectedTargetPanelWidth,
            300,
            900);
    }

    private static void NormalizeJtdxGuiSelectionDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.JtdxWindowTitleMatch))
            settings.JtdxWindowTitleMatch = "JTDX";

        if (settings.JtdxBandDpiScale <= 0)
            settings.JtdxBandDpiScale = 1.0;

        if (settings.JtdxGuiMaxRowAgeSeconds <= 0)
            settings.JtdxGuiMaxRowAgeSeconds = 45;

        settings.JtdxBandVisibleRowCount = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.SafeFullRowCount;
        settings.JtdxBandIgnoredPartialTopRow = true;

        if (settings.JtdxBandActivityRight <= settings.JtdxBandActivityLeft
            || settings.JtdxBandActivityBottom <= settings.JtdxBandActivityTop
            || settings.JtdxBandFirstRowCenterY <= 0
            || settings.JtdxBandRowHeight <= 0
            || settings.JtdxBandMessageClickX <= 0)
        {
            settings.JtdxBandActivityLeft = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultBandActivityLeft;
            settings.JtdxBandActivityTop = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultBandActivityTop;
            settings.JtdxBandActivityRight = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultBandActivityRight;
            settings.JtdxBandActivityBottom = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultBandActivityBottom;
            settings.JtdxBandFirstRowCenterY = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultFirstFullRowCentreY;
            settings.JtdxBandRowHeight = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultRowHeight;
            settings.JtdxBandMessageClickX = JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.DefaultMessageClickX;
            settings.JtdxBandNewestRowsAtBottom = true;
        }

        if (string.IsNullOrWhiteSpace(settings.JtdxBandCalibrationVersion))
            settings.JtdxBandCalibrationVersion = "grid-v1";
    }

    public List<BandScheduleItem> LoadSchedule()
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            if (!File.Exists(ScheduleFile))
                return Enumerable.Range(0, 6).Select(_ => new BandScheduleItem()).ToList();

            return JsonSerializer.Deserialize<List<BandScheduleItem>>(File.ReadAllText(ScheduleFile))
                ?? new List<BandScheduleItem>();
        }
        catch
        {
            return Enumerable.Range(0, 6).Select(_ => new BandScheduleItem()).ToList();
        }
    }

    public void SaveSchedule(IEnumerable<BandScheduleItem> schedule)
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            File.WriteAllText(ScheduleFile, JsonSerializer.Serialize(schedule, _jsonOptions));
        }
        catch
        {
        }
    }
}
