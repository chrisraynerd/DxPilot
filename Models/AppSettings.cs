using System.IO;
using System.Text.Json.Serialization;

namespace JtdxAutoResume.V3.Models;

public sealed class AppSettings
{
    public bool SetupWizardCompleted { get; set; }
    public int DownstreamLoggerPort { get; set; } = 2236;
    public DateTime EnableTxCalibrationDate { get; set; } = DateTime.MinValue;
    public string LocationProfile { get; set; } = "Worldwide";
    public List<string>? LocationHuntAreas { get; set; }
    public int EnableTxX { get; set; } = 1360;
    public int EnableTxY { get; set; } = 781;
    public int CqTx6X { get; set; } = 1667;
    public int CqTx6Y { get; set; } = 983;
    public int EnableTxOffRgb { get; set; } = 0xDCDCDC;
    public int EnableTxOnRgb { get; set; } = 0xFF3C3C;
    public int BoxRadius { get; set; } = 6;
    public int Tolerance { get; set; } = 25;
    public int IntervalMs { get; set; } = 400;
    public int CooldownMs { get; set; } = 6000;
    public int MinGreyPercent { get; set; } = 60;
    public int MaxRedPercent { get; set; } = 20;
    public int RxX { get; set; } = 110;
    public int RxY { get; set; } = 1015;
    public int RxGreenRgb { get; set; } = 0x00FF00;
    public int RxRadius { get; set; } = 4;
    public int RxTolerance { get; set; } = 30;
    public int MinGreenPercent { get; set; } = 60;
    public int UdpPort { get; set; } = 2237;
    public int UdpListenPort { get; set; } = 2237;
    public int UdpReplyFallbackPort { get; set; } = 2237;
    public bool UdpForwardEnabled { get; set; } = true;
    public string UdpForwardHost { get; set; } = "127.0.0.1";
    public int UdpForwardPort { get; set; } = 2238;
    public string UdpAppId { get; set; } = "JtdxAutoResume.V3";
    public bool AutoSelectBestCq { get; set; }
    public bool AutoHuntEnabled { get; set; } = true;
    public int CallTimeoutMinutes { get; set; } = 3;
    public int MaxCallAttempts { get; set; } = 6;
    public int MaxReportAttempts { get; set; } = 6;
    public int MaxTransmitMismatchCycles { get; set; } = 3;
    public int MaxWrongTargetNoProgressCycles { get; set; } = 2;
    public string WrongTargetActiveQsoPolicy { get; set; } = "AdoptAndMonitor";
    public int SuppressFailedTargetMinutes { get; set; } = 30;
    public List<string> PermanentlySuppressedCallsigns { get; set; } = new();
    public int ReplyRetrySeconds { get; set; } = 45;
    public int ReplyConfirmSeconds { get; set; } = 30;
    public int MaxTargetAcquisitionCycles { get; set; } = 1;
    public int MaxUdpReplyNudgesBeforeConfirmed { get; set; } = 2;
    public string MyCallsign { get; set; } = "G1CEC";
    public string HomeGrid { get; set; } = "";
    public string AdifFilePath { get; set; } = @"C:\Users\Chris\AppData\Local\JTDX\wsjtx_log.adi";
    public string FullAdifPath { get; set; } = "";
    public string LiveJtdxAdifPath { get; set; } = @"C:\Users\Chris\AppData\Local\JTDX\wsjtx_log.adi";
    public bool AutoLoadFullAdifOnStartup { get; set; } = true;
    public bool WatchLiveJtdxAdif { get; set; } = true;
    public string JtdxAllTxtPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JTDX",
        $"{DateTime.UtcNow:yyyyMM}_ALL.TXT");
    public bool WatchJtdxAllTxt { get; set; } = true;
    public string DxccConfirmationMode { get; set; } = "LoTWOnly";
    public string GridConfirmationMode { get; set; } = "WorkedOnly";
    public string StateConfirmationMode { get; set; } = "WorkedOnly";
    public string IotaConfirmationMode { get; set; } = "WorkedOnly";
    public string DxccRarityFilePath { get; set; } = "";
    public double GlobalRarityWeight { get; set; } = 0.50;
    public double UkDesirabilityWeight { get; set; } = 0.35;
    public double DistanceWeight { get; set; } = 0.15;
    public bool ChaseRareConfirmedDxcc { get; set; } = false;
    public string AcceptIncomingCallsMode { get; set; } = "OnlyIfNoBetterHunterTarget";
    public string HuntingMode { get; set; } = "DXCC Hunter";
    public bool AllowUnconfirmedDxccOverride { get; set; } = true;
    public bool AcceptIncomingCalls { get; set; } = false;
    public int CandidateMaxAgeSeconds { get; set; } = 90;
    public int NewDxccStaleSeconds { get; set; } = 240;
    public bool KeepCallingNewDxccUntilStale { get; set; }
    public double DxAssistSelectedTargetPanelWidth { get; set; } = 450;
    public string CountryFilePath { get; set; } = "";
    public int WantedItemExpirySeconds { get; set; } = 90;
    public int ManualWantedMaxAgeSeconds { get; set; } = 90;
    public string WantedScope { get; set; } = "Overall";
    public bool IncludeBandWanted { get; set; }
    public bool IncludeModeWanted { get; set; }
    public bool IncludeBandModeWanted { get; set; }
    public bool EnableWantedDxcc { get; set; } = true;
    public bool EnableWantedGrids { get; set; } = true;
    public bool EnableWantedStates { get; set; } = true;
    public bool EnableQrzCallsignLookup { get; set; }
    public string QrzUsername { get; set; } = "";
    public string QrzPasswordProtected { get; set; } = "";
    [JsonIgnore]
    public string QrzPassword { get; set; } = "";
    public string QrzTestCallsign { get; set; } = "K1ABC";
    public int QrzLookupTimeoutSeconds { get; set; } = 3;
    public int QrzSuccessCacheDays { get; set; } = 180;
    public int QrzNotFoundCacheDays { get; set; } = 14;
    public int QrzLookupQueueLimit { get; set; } = 2000;
    public int QrzDelayBetweenLookupsMs { get; set; } = 200;
    public int QrzCircuitBreakerFailureCount { get; set; } = 5;
    public int QrzCircuitBreakerMinutes { get; set; } = 5;
    public bool EnableQrzGridEnrichment { get; set; } = true;
    public bool UseQrzGridsForNewGridTargeting { get; set; } = true;
    public bool UseQrzGridsForUnconfirmedGridTargeting { get; set; } = true;
    public bool IgnoreQrzTargetingForPotentiallyPortableCalls { get; set; } = true;
    public bool PrioritizeNewUsStates { get; set; } = true;
    public bool PrioritizeUnconfirmedUsStates { get; set; } = true;
    public bool IncludeDistrictOfColumbia { get; set; } = true;
    public bool WantedShowActionableOnly { get; set; }
    public int CompletionGraceCycles { get; set; } = 2;
    public bool WaitForFinal73AfterRr73 { get; set; } = true;
    public bool PreferAdifConfirmation { get; set; } = true;
    public int CompletionTimeoutSeconds { get; set; } = 120;
    public int SuccessfulQsoSuppressHours { get; set; } = 24;
    public bool EnableSessionDxHistory { get; set; } = true;
    public int RareDxccRankThreshold { get; set; } = 150;
    public int SessionHistoryExpiryMinutes { get; set; } = 0;
    public string SessionHistoryGroupMode { get; set; } = "ByCall";
    public string JtdxWindowTitleMatch { get; set; } = "JTDX";
    public int JtdxBandActivityLeft { get; set; }
    public int JtdxBandActivityTop { get; set; }
    public int JtdxBandActivityRight { get; set; }
    public int JtdxBandActivityBottom { get; set; }
    public int JtdxBandFirstRowCenterY { get; set; }
    public double JtdxBandRowHeight { get; set; }
    public int JtdxBandMessageClickX { get; set; }
    public int JtdxBandVisibleRowCount { get; set; } = 52;
    public double JtdxBandDpiScale { get; set; } = 1.0;
    public string JtdxBandMonitorId { get; set; } = "";
    public bool JtdxBandIgnoredPartialTopRow { get; set; } = true;
    public bool JtdxBandNewestRowsAtBottom { get; set; } = true;
    public string JtdxBandCalibrationVersion { get; set; } = "grid-v1";
    public DateTime JtdxBandCalibrationDate { get; set; } = DateTime.MinValue;
    public string JtdxCalibratedWindowTitle { get; set; } = "";
    public string JtdxCalibratedWindowProcess { get; set; } = "";
    public int JtdxCalibratedWindowLeft { get; set; }
    public int JtdxCalibratedWindowTop { get; set; }
    public int JtdxCalibratedWindowWidth { get; set; }
    public int JtdxCalibratedWindowHeight { get; set; }
    public bool JtdxGuiSelectionEnabled { get; set; } = true;
}
