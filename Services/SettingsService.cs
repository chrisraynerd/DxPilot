using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
            {
                var defaults = new AppSettings();
                NormalizeLocationHuntAreas(defaults);
                return defaults;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), _jsonOptions) ?? new AppSettings();
            NormalizeSettings(settings);
            settings.QrzPassword = UnprotectSecret(settings.QrzPasswordProtected);
            return settings;
        }
        catch
        {
            var defaults = new AppSettings();
            NormalizeLocationHuntAreas(defaults);
            return defaults;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            if (!string.IsNullOrWhiteSpace(settings.QrzPassword))
                settings.QrzPasswordProtected = ProtectSecret(settings.QrzPassword);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, _jsonOptions));
        }
        catch
        {
        }
    }

    public void ExportPortableSettings(
        string filePath,
        AppSettings settings,
        IEnumerable<BandScheduleItem> schedule)
    {
        var portableSettings = CloneSettings(settings);
        portableSettings.QrzPassword = "";
        portableSettings.QrzPasswordProtected = "";

        var package = new SettingsTransferPackage
        {
            ExportedAtUtc = DateTime.UtcNow,
            QrzPasswordExcluded = true,
            Settings = portableSettings,
            Schedule = schedule.Select(CloneScheduleItem).ToList()
        };

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, JsonSerializer.Serialize(package, _jsonOptions), Encoding.UTF8);
    }

    public bool TryReadSettingsImport(
        string filePath,
        out SettingsImportPayload payload,
        out string error)
    {
        payload = new SettingsImportPayload();
        error = "";

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The selected file does not contain a settings object.";
                return false;
            }

            var root = document.RootElement;
            if (TryGetProperty(root, "Format", out var formatElement))
            {
                var format = formatElement.GetString() ?? "";
                if (!format.Equals(SettingsTransferPackage.ExpectedFormat, StringComparison.Ordinal))
                {
                    error = $"Unsupported settings export format '{format}'.";
                    return false;
                }

                var package = JsonSerializer.Deserialize<SettingsTransferPackage>(root.GetRawText(), _jsonOptions);
                if (package == null
                    || package.FormatVersion is < 1 or > SettingsTransferPackage.CurrentFormatVersion
                    || package.Settings == null)
                {
                    error = "The settings export version is missing or unsupported.";
                    return false;
                }

                payload = new SettingsImportPayload
                {
                    Settings = package.Settings,
                    Schedule = package.Schedule?.Select(CloneScheduleItem).ToList(),
                    ExportedAtUtc = package.ExportedAtUtc,
                    QrzPasswordExcluded = package.QrzPasswordExcluded
                };
            }
            else
            {
                var looksLikeLegacySettings = new[]
                {
                    "JtdxBandVisibleRowCount",
                    "UdpListenPort",
                    "MyCallsign",
                    "EnableTxX"
                }.Any(name => TryGetProperty(root, name, out _));

                if (!looksLikeLegacySettings)
                {
                    error = "The selected JSON file is not a DX Pilot settings export.";
                    return false;
                }

                var legacy = JsonSerializer.Deserialize<AppSettings>(root.GetRawText(), _jsonOptions);
                if (legacy == null)
                {
                    error = "The legacy settings file could not be read.";
                    return false;
                }

                payload = new SettingsImportPayload
                {
                    Settings = legacy,
                    Schedule = null,
                    IsLegacySettingsFile = true,
                    QrzPasswordExcluded = string.IsNullOrWhiteSpace(legacy.QrzPasswordProtected)
                };
            }

            if (payload.Settings.JtdxBandVisibleRowCount != 0
                && payload.Settings.JtdxBandVisibleRowCount is < 5 or > 200)
            {
                error = "The visible JTDX row count must be between 5 and 200.";
                return false;
            }

            NormalizeSettings(payload.Settings);
            if (!ValidateImportedSettings(payload.Settings, payload.Schedule, out error))
                return false;

            payload.Settings.QrzPassword = UnprotectSecret(payload.Settings.QrzPasswordProtected);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"The selected file is not valid JSON: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"The selected settings file could not be read: {ex.GetBaseException().Message}";
            return false;
        }
    }

    public string BackupCurrentConfiguration()
    {
        Directory.CreateDirectory(AppFolder);
        var backupFolder = Path.Combine(
            AppFolder,
            "Settings Backups",
            $"PreImport-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        Directory.CreateDirectory(backupFolder);

        if (File.Exists(SettingsFile))
            File.Copy(SettingsFile, Path.Combine(backupFolder, Path.GetFileName(SettingsFile)));
        if (File.Exists(ScheduleFile))
            File.Copy(ScheduleFile, Path.Combine(backupFolder, Path.GetFileName(ScheduleFile)));

        return backupFolder;
    }

    public void ApplyImportedConfiguration(SettingsImportPayload payload)
    {
        var importedSettings = CloneSettings(payload.Settings);
        if (!string.IsNullOrWhiteSpace(importedSettings.QrzPassword))
            importedSettings.QrzPasswordProtected = ProtectSecret(importedSettings.QrzPassword);
        WriteTextAtomic(SettingsFile, JsonSerializer.Serialize(importedSettings, _jsonOptions));

        if (payload.Schedule != null)
        {
            var schedule = payload.Schedule.Select(CloneScheduleItem).ToList();
            WriteTextAtomic(ScheduleFile, JsonSerializer.Serialize(schedule, _jsonOptions));
        }
    }

    private static bool ValidateImportedSettings(
        AppSettings settings,
        IReadOnlyCollection<BandScheduleItem>? schedule,
        out string error)
    {
        error = "";
        if (settings.UdpListenPort is < 1 or > 65535
            || settings.UdpReplyFallbackPort is < 1 or > 65535
            || settings.UdpForwardPort is < 1 or > 65535
            || settings.DownstreamLoggerPort is < 1 or > 65535)
        {
            error = "The settings file contains an invalid UDP port.";
            return false;
        }

        if (settings.JtdxBandVisibleRowCount != 0
            && settings.JtdxBandVisibleRowCount is < 5 or > 200)
        {
            error = "The visible JTDX row count must be between 5 and 200.";
            return false;
        }

        if (settings.IntervalMs is < 50 or > 60_000
            || settings.CandidateMaxAgeSeconds is < 0 or > 86_400
            || settings.NewDxccStaleSeconds is < 0 or > 86_400)
        {
            error = "The settings file contains an invalid timing value.";
            return false;
        }

        if (schedule != null
            && schedule.Any(item => item.Hour is < 0 or > 23 || item.Minute is < 0 or > 59))
        {
            error = "The settings file contains an invalid scheduler time.";
            return false;
        }

        return true;
    }

    private void WriteTextAtomic(string destination, string content)
    {
        Directory.CreateDirectory(AppFolder);
        var temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content, Encoding.UTF8);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private AppSettings CloneSettings(AppSettings settings)
    {
        return JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, _jsonOptions),
            _jsonOptions) ?? new AppSettings();
    }

    private static BandScheduleItem CloneScheduleItem(BandScheduleItem item)
    {
        return new BandScheduleItem
        {
            Enabled = item.Enabled,
            Label = item.Label,
            Hour = item.Hour,
            Minute = item.Minute,
            X = item.X,
            Y = item.Y
        };
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        NormalizeCoordinateDefaults(settings);
        NormalizeUdpBridgeDefaults(settings);
        NormalizeTimingDefaults(settings);
        NormalizeAdifDefaults(settings);
        NormalizeWantedDefaults(settings);
        NormalizeLayoutDefaults(settings);
        NormalizeJtdxGuiSelectionDefaults(settings);
        NormalizeBandAnalysisDefaults(settings);
        NormalizeQrzDefaults(settings);
        NormalizePermanentSuppressions(settings);
        NormalizeLocationHuntAreas(settings);
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

        if (settings.DownstreamLoggerPort <= 0)
            settings.DownstreamLoggerPort = 2236;

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

        if (string.IsNullOrWhiteSpace(settings.AchievementCallsignProfile))
            settings.AchievementCallsignProfile = StationCallsignIdentity.AllCallsignsKey;
        else
            settings.AchievementCallsignProfile = settings.AchievementCallsignProfile.Trim().ToUpperInvariant();
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

        if (settings.CandidateMaxAgeSeconds <= 0 || settings.CandidateMaxAgeSeconds == 60)
            settings.CandidateMaxAgeSeconds = 90;

        if (settings.NewDxccStaleSeconds <= 0)
            settings.NewDxccStaleSeconds = 240;

        if (settings.WantedItemExpirySeconds <= 0)
            settings.WantedItemExpirySeconds = settings.CandidateMaxAgeSeconds;

        if (settings.ManualWantedMaxAgeSeconds <= 0)
            settings.ManualWantedMaxAgeSeconds = settings.CandidateMaxAgeSeconds;

        if (settings.MapDefaultsVersion < 2)
        {
            settings.MapStaleMinutes = 2;
            settings.MapDefaultsVersion = 2;
        }
        else if (settings.MapStaleMinutes <= 0)
        {
            settings.MapStaleMinutes = 2;
        }

        if (settings.MapLotwConfirmedGridOpacityPercent is < 5 or > 50)
            settings.MapLotwConfirmedGridOpacityPercent = 25;

        if (!Enum.TryParse<JtdxAutoResume.V3.Models.WantedScope>(settings.MapLotwConfirmedGridScope, true, out var lotwGridScope)
            || lotwGridScope is not (JtdxAutoResume.V3.Models.WantedScope.Overall
                or JtdxAutoResume.V3.Models.WantedScope.CurrentBand
                or JtdxAutoResume.V3.Models.WantedScope.CurrentMode))
        {
            settings.MapLotwConfirmedGridScope = JtdxAutoResume.V3.Models.WantedScope.Overall.ToString();
        }

        if (!Enum.TryParse<JtdxAutoResume.V3.Models.WantedScope>(settings.MapColourScope, true, out _))
            settings.MapColourScope = JtdxAutoResume.V3.Models.WantedScope.Overall.ToString();

        var supportedBasemaps = new[]
        {
            "OpenStreetMap", "EsriStreets", "EsriOutdoor", "EsriLightGray", "EsriStreetsNight"
        };
        if (!supportedBasemaps.Contains(settings.MapBasemapId, StringComparer.OrdinalIgnoreCase))
            settings.MapBasemapId = "OpenStreetMap";

        if (settings.QrzLookupTimeoutSeconds <= 0)
            settings.QrzLookupTimeoutSeconds = 3;

        if (settings.QrzSuccessCacheDays <= 0)
            settings.QrzSuccessCacheDays = 180;

        if (settings.QrzNotFoundCacheDays <= 0)
            settings.QrzNotFoundCacheDays = 14;

        if (settings.QrzDelayBetweenLookupsMs <= 0)
            settings.QrzDelayBetweenLookupsMs = 200;

        if (settings.QrzLookupQueueLimit <= 0)
            settings.QrzLookupQueueLimit = 2000;

        if (!Enum.TryParse<JtdxAutoResume.V3.Models.WantedScope>(settings.WantedScope, ignoreCase: true, out _))
            settings.WantedScope = JtdxAutoResume.V3.Models.WantedScope.Overall.ToString();

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

        if (string.IsNullOrWhiteSpace(settings.HuntingMode))
            settings.HuntingMode = "DXCC Hunter";

        if (string.IsNullOrWhiteSpace(settings.DxccRarityFilePath)
            || settings.DxccRarityFilePath.EndsWith("DXCC-Rankings.csv", StringComparison.OrdinalIgnoreCase))
        {
            settings.DxccRarityFilePath = Path.Combine(AppContext.BaseDirectory, "Data", "DXCC-UK-Desirability-G1CEC.csv");
        }
    }

    private static void NormalizeQrzDefaults(AppSettings settings)
    {
        if (settings.QrzCircuitBreakerFailureCount <= 0)
            settings.QrzCircuitBreakerFailureCount = 5;

        if (settings.QrzCircuitBreakerMinutes <= 0)
            settings.QrzCircuitBreakerMinutes = 5;

        if (string.IsNullOrWhiteSpace(settings.QrzTestCallsign))
            settings.QrzTestCallsign = settings.MyCallsign;
    }

    private static void NormalizePermanentSuppressions(AppSettings settings)
    {
        settings.PermanentlySuppressedCallsigns = (settings.PermanentlySuppressedCallsigns ?? new List<string>())
            .Select(CallsignNormalizer.Normalize)
            .Where(CallsignNormalizer.IsValidLookupCallsign)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(call => call, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void NormalizeLocationHuntAreas(AppSettings settings)
    {
        var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "USA", "AF", "AS", "EU", "NA", "SA", "OC", "IOTA", "OTHER"
        };

        if (settings.LocationHuntAreas == null)
        {
            settings.LocationHuntAreas = settings.LocationProfile switch
            {
                "USA" => new List<string> { "USA" },
                "Africa" => new List<string> { "AF" },
                "Asia" => new List<string> { "AS" },
                "Europe" => new List<string> { "EU" },
                "Americas" => new List<string> { "USA", "NA", "SA" },
                "Oceania" => new List<string> { "OC" },
                "USA + Africa" => new List<string> { "USA", "AF" },
                "Africa + Asia" => new List<string> { "AF", "AS" },
                "IOTA" => new List<string> { "IOTA" },
                _ => validKeys.ToList()
            };
        }

        settings.LocationHuntAreas = settings.LocationHuntAreas
            .Where(validKeys.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ProtectSecret(string secret)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return "";
        }
    }

    private static string UnprotectSecret(string protectedSecret)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(protectedSecret))
                return "";

            var bytes = Convert.FromBase64String(protectedSecret);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return "";
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

        settings.JtdxAllTxtPath = JtdxAllTxtMonitor.ResolveCurrentPath(settings.JtdxAllTxtPath);

        if (!ConfirmationModes.Contains(settings.DxccConfirmationMode))
            settings.DxccConfirmationMode = "LoTWOnly";
        if (!ConfirmationModes.Contains(settings.GridConfirmationMode))
            settings.GridConfirmationMode = "LoTWOnly";
        if (!ConfirmationModes.Contains(settings.StateConfirmationMode))
            settings.StateConfirmationMode = "LoTWOnly";
        if (!ConfirmationModes.Contains(settings.IotaConfirmationMode))
            settings.IotaConfirmationMode = "LoTWOnly";
    }

    private static void NormalizeLayoutDefaults(AppSettings settings)
    {
        settings.DxAssistSelectedTargetPanelWidth = Math.Clamp(
            settings.DxAssistSelectedTargetPanelWidth <= 0 ? 450 : settings.DxAssistSelectedTargetPanelWidth,
            300,
            900);
    }

    private static void NormalizeWantedDefaults(AppSettings settings)
    {
        if (string.Equals(settings.WantedScope, "CurrentBand", StringComparison.OrdinalIgnoreCase))
            settings.IncludeBandWanted = true;
        else if (string.Equals(settings.WantedScope, "CurrentMode", StringComparison.OrdinalIgnoreCase))
            settings.IncludeModeWanted = true;
        else if (string.Equals(settings.WantedScope, "CurrentBandMode", StringComparison.OrdinalIgnoreCase))
            settings.IncludeBandModeWanted = true;

        // Overall awards are now always evaluated. These independent optional
        // scopes replace the old mutually-exclusive scope selector.
        settings.WantedScope = "Overall";
    }

    private static void NormalizeJtdxGuiSelectionDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.JtdxWindowTitleMatch))
            settings.JtdxWindowTitleMatch = "JTDX";

        if (settings.JtdxBandDpiScale <= 0)
            settings.JtdxBandDpiScale = 1.0;

        settings.JtdxBandVisibleRowCount =
            JtdxAutoResume.V3.Controls.JtdxSelection.JtdxBandActivityGridCalibration.NormalizeRowCount(
                settings.JtdxBandVisibleRowCount);
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

    private static void NormalizeBandAnalysisDefaults(AppSettings settings)
    {
        var supportedBands = new HashSet<string>(
            ["160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "2m"],
            StringComparer.OrdinalIgnoreCase);
        settings.BandAnalysisEnabledBands = (settings.BandAnalysisEnabledBands ?? [])
            .Where(supportedBands.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (settings.BandAnalysisEnabledBands.Count == 0)
            settings.BandAnalysisEnabledBands = ["40m", "30m", "20m", "17m", "15m"];

        settings.BandAnalysisDwellMinutes = Math.Clamp(
            settings.BandAnalysisDwellMinutes <= 0 ? 2 : settings.BandAnalysisDwellMinutes,
            1,
            3);
        settings.BandAnalysisSurveyCycles = Math.Clamp(
            settings.BandAnalysisSurveyCycles <= 0 ? 1 : settings.BandAnalysisSurveyCycles,
            1,
            5);
        settings.PskPropagationProbeMinutes = Math.Clamp(
            settings.PskPropagationProbeMinutes <= 0 ? 1 : settings.PskPropagationProbeMinutes,
            1,
            5);
        settings.ConditionsSearchCooldownMinutes = Math.Clamp(
            settings.ConditionsSearchCooldownMinutes <= 0 ? 45 : settings.ConditionsSearchCooldownMinutes,
            15,
            180);
        settings.ConditionsSearchMinimumBandMinutes = Math.Clamp(
            settings.ConditionsSearchMinimumBandMinutes <= 0 ? 15 : settings.ConditionsSearchMinimumBandMinutes,
            5,
            60);
        settings.ConditionsSearchMonitoringWindowMinutes = Math.Clamp(
            settings.ConditionsSearchMonitoringWindowMinutes <= 0 ? 5 : settings.ConditionsSearchMonitoringWindowMinutes,
            3,
            15);
        settings.ConditionsSearchNoUsefulTargetMinutes = Math.Clamp(
            settings.ConditionsSearchNoUsefulTargetMinutes <= 0 ? 10 : settings.ConditionsSearchNoUsefulTargetMinutes,
            3,
            60);
        settings.ConditionsSearchLowStationThreshold = Math.Clamp(
            settings.ConditionsSearchLowStationThreshold <= 0 ? 5 : settings.ConditionsSearchLowStationThreshold,
            1,
            30);
        settings.ConditionsSearchLowActivityPersistMinutes = Math.Clamp(
            settings.ConditionsSearchLowActivityPersistMinutes <= 0 ? 3 : settings.ConditionsSearchLowActivityPersistMinutes,
            1,
            15);
        settings.ConditionsSearchPoorReplyAttempts = Math.Clamp(
            settings.ConditionsSearchPoorReplyAttempts <= 0 ? 8 : settings.ConditionsSearchPoorReplyAttempts,
            3,
            30);
        settings.ConditionsSearchPoorReplyDistinctStations = Math.Clamp(
            settings.ConditionsSearchPoorReplyDistinctStations <= 0 ? 3 : settings.ConditionsSearchPoorReplyDistinctStations,
            2,
            10);
        settings.ConditionsSearchNoCompletedQsoMinutes = Math.Clamp(
            settings.ConditionsSearchNoCompletedQsoMinutes <= 0 ? 20 : settings.ConditionsSearchNoCompletedQsoMinutes,
            5,
            120);
        settings.ConditionsSearchIncompleteQsoThreshold = Math.Clamp(
            settings.ConditionsSearchIncompleteQsoThreshold <= 0 ? 2 : settings.ConditionsSearchIncompleteQsoThreshold,
            1,
            10);
        settings.ConditionsSearchSilentMinutes = Math.Clamp(
            settings.ConditionsSearchSilentMinutes <= 0 ? 4 : settings.ConditionsSearchSilentMinutes,
            2,
            20);
        settings.ConditionsSearchSwitchImprovementPercent = Math.Clamp(
            settings.ConditionsSearchSwitchImprovementPercent <= 0 ? 20 : settings.ConditionsSearchSwitchImprovementPercent,
            5,
            100);
        settings.ConditionsSearchScheduleUtc ??= "";
        if (string.IsNullOrWhiteSpace(settings.JtdxBandButtonStripCalibrationVersion))
            settings.JtdxBandButtonStripCalibrationVersion = "band-strip-v1";
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
