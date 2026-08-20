using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;

var failures = new List<string>();

var validOnAirCallsigns = new[] { "G1CEC", "4U1UN", "3D2USU", "EA8/G4ABC", "MM/F4MFS" };
foreach (var callsign in validOnAirCallsigns)
{
    if (!CallsignNormalizer.IsValidOnAirCallsign(callsign))
        failures.Add($"Valid on-air callsign '{callsign}' was rejected by the PSK survey priority guard.");
}

var invalidOnAirCallsigns = new[] { "AH4DCKZXJYR", "RR73", "IO83", "CQ", "<...>" };
foreach (var callsign in invalidOnAirCallsigns)
{
    if (CallsignNormalizer.IsValidOnAirCallsign(callsign))
        failures.Add($"Malformed callsign '{callsign}' passed the PSK survey priority guard.");
}

if (QrzProfileUrl.Build("k7pk") != "https://www.qrz.com/db/K7PK"
    || QrzProfileUrl.Build("EA8/G4ABC") != "https://www.qrz.com/db/EA8%2FG4ABC"
    || !string.IsNullOrWhiteSpace(QrzProfileUrl.Build("<...>")))
{
    failures.Add("QRZ table lookup did not create a safe normalized profile URL for ordinary and portable callsigns.");
}

var achievementResolver = new DxccResolver();
var achievementRarity = new DxccRarityService();
achievementRarity.Load(null, achievementResolver);
if (achievementRarity.Diagnostics.Unmatched != 0)
{
    failures.Add($"Club Log rank coverage left {achievementRarity.Diagnostics.Unmatched} current DXCC names unmatched: "
        + string.Join("; ", achievementRarity.Diagnostics.UnmatchedRows));
}
var englandDxcc = achievementResolver.Resolve("G1CEC")?.Code ?? "";
var achievementCollator = new DxccAchievementCollator();
var achievementRows = achievementCollator.Build(
    [
        new AdifQso
        {
            Call = "G4AAA", StationCallsign = "G1CEC", Dxcc = englandDxcc, Country = "England",
            Band = "20m", Mode = "FT8", QsoDate = new DateTime(2026, 8, 1), LotwConfirmed = false
        },
        new AdifQso
        {
            Call = "G4AAB", StationCallsign = "G1CEC", Dxcc = englandDxcc, Country = "England",
            Band = "40m", Mode = "FT8", QsoDate = new DateTime(2026, 8, 2), LotwConfirmed = true
        }
    ],
    [
        new SessionDxOpportunity
        {
            Call = "G4AAA", DxccNumber = englandDxcc, Entity = "England", SeenCount = 3, DirectlyHeardCount = 2
        },
        new SessionDxOpportunity
        {
            Call = "G4AAB", DxccNumber = englandDxcc, Entity = "England", SeenCount = 2, DirectlyHeardCount = 2
        }
    ],
    achievementResolver.EntityDefinitions(),
    achievementResolver,
    achievementRarity);
var englandAchievement = achievementRows.FirstOrDefault(row => row.DxccNumber == englandDxcc);
if (achievementRows.Count != achievementResolver.EntityDefinitions().Select(entity => entity.DxccNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count()
    || englandAchievement == null
    || englandAchievement.QsoCount != 2
    || englandAchievement.UnconfirmedQsoCount != 1
    || englandAchievement.LotwConfirmedQsoCount != 1
    || englandAchievement.SeenCount != 5
    || englandAchievement.SeenCallCount != 2
    || englandAchievement.StatusKey != "LotwConfirmed")
{
    failures.Add("Achievements did not collate the complete DXCC list with independent history-seen, worked, unconfirmed and LoTW-confirmed counts.");
}

var bouvetAchievement = achievementRows.FirstOrDefault(row => row.DxccNumber == "24");
if (bouvetAchievement?.ClubLogRank != 38)
    failures.Add("Achievements did not reconcile the Club Log Bouvet Island name with DXCC 24 Bouvet and rank 38.");

var achievementDetails = achievementCollator.BuildQsoDetails(
    englandDxcc,
    [
        new AdifQso
        {
            Call = "G4AAA", StationCallsign = "G1CEC", Dxcc = englandDxcc,
            QsoDate = new DateTime(2026, 8, 1), TimeOn = "123045", Band = "20m", Mode = "FT8",
            Grid = "IO91", LotwConfirmed = true, Source = "Test ADIF"
        }
    ],
    achievementResolver.EntityDefinitions(),
    achievementResolver);
if (achievementDetails.Count != 1
    || achievementDetails[0].Call != "G4AAA"
    || achievementDetails[0].StationCallsign != "G1CEC"
    || achievementDetails[0].TimeDisplay != "12:30:45 UTC"
    || !achievementDetails[0].LotwConfirmed)
{
    failures.Add("Achievements did not provide one unified, callsign-aware QSO history for a selected DXCC row.");
}

if (args.Contains("--psk-live", StringComparer.OrdinalIgnoreCase))
{
    await using var pskClient = new PskReporterClient();
    pskClient.StatusChanged += Console.WriteLine;
    using var liveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var connected = await pskClient.StartLiveAsync("G1CEC", liveTimeout.Token);
    await pskClient.StopLiveAsync();
    Console.WriteLine(connected ? "PASS: PSK Reporter MQTT live feed connected and subscribed." : "FAIL: PSK Reporter MQTT live feed was unavailable.");
    return connected ? 0 : 1;
}

if (args.Contains("--psk-query", StringComparer.OrdinalIgnoreCase))
{
    await using var pskClient = new PskReporterClient();
    var result = await pskClient.QueryRecentAsync("G1CEC", TimeSpan.FromHours(1), CancellationToken.None);
    Console.WriteLine(result.Status);
    Console.WriteLine(result.Retrieved && result.Reports.Count > 0
        ? $"PASS: official PSK Reporter retrieval parsed {result.Reports.Count} reports."
        : "FAIL: official PSK Reporter retrieval returned no usable reports.");
    return result.Retrieved && result.Reports.Count > 0 ? 0 : 1;
}

var callNowSession = new CallNowSessionState();
if (!callNowSession.Begin(assistanceRunning: false)
    || !callNowSession.IsOneShot
    || !callNowSession.EndTarget()
    || callNowSession.IsOneShot)
{
    failures.Add("CALL NOW did not return to fully stopped after a one-station session started while assistance was off.");
}

if (callNowSession.Begin(assistanceRunning: true)
    || callNowSession.IsOneShot
    || callNowSession.EndTarget())
{
    failures.Add("CALL NOW did not preserve an assistance mode that was already running.");
}

callNowSession.Begin(assistanceRunning: false);
callNowSession.PromoteToAutomation();
if (callNowSession.EndTarget())
    failures.Add("Starting an assistance mode during one-shot CALL NOW did not promote the session to continuing automation.");

var bandCases = new (ulong Frequency, string Band)[]
{
    (1_840_000, "160m"),
    (3_573_000, "80m"),
    (7_074_000, "40m"),
    (10_136_000, "30m"),
    (14_074_000, "20m"),
    (18_100_000, "17m"),
    (21_074_000, "15m"),
    (24_915_000, "12m"),
    (28_074_000, "10m"),
    (50_313_000, "6m"),
    (70_154_000, "4m"),
    (144_174_000, "2m")
};

foreach (var (frequency, expectedBand) in bandCases)
{
    var actual = AmateurBandMapper.FromDialFrequency(frequency);
    if (!actual.Equals(expectedBand, StringComparison.Ordinal))
        failures.Add($"{frequency} mapped to '{actual}', expected '{expectedBand}'.");
}

if (JtdxBandActivityGridCalibration.NormalizeRowCount(0) != 52)
    failures.Add("An unset visible-row count did not preserve the personal 52-row default.");
var pskIsolationDefaults = new AppSettings();
if (!pskIsolationDefaults.PskStandaloneCleanupRequired)
    failures.Add("A newly upgraded installation did not require one Tx1 cleanup before normal hunting.");
pskIsolationDefaults.JtdxTxEvenRelativeX = 100;
pskIsolationDefaults.JtdxTxEvenRelativeY = 200;
pskIsolationDefaults.JtdxTxEvenCalibrationDate = DateTime.Now;
pskIsolationDefaults.JtdxTx1RelativeX = 300;
pskIsolationDefaults.JtdxTx1RelativeY = 400;
pskIsolationDefaults.JtdxTx1CalibrationDate = DateTime.Now;
if (!BandAnalysisViewModel.HasPskTransmitCalibration(pskIsolationDefaults))
    failures.Add("The standalone PSK sequence did not require both timing-selector and Tx1-reset mappings.");
if (JtdxBandActivityGridCalibration.NormalizeRowCount(34) != 34)
    failures.Add("A valid user-selected 34-row count was not preserved.");
if (JtdxBandActivityGridCalibration.NormalizeRowCount(4) != 5
    || JtdxBandActivityGridCalibration.NormalizeRowCount(201) != 200)
{
    failures.Add("Visible-row validation did not enforce the safe 5-200 range.");
}

var customWindow = new JtdxWindowInfo(IntPtr.Zero, "JTDX", 123, 100, 50, 1500, 850);
var customCalibration = JtdxBandActivityGridCalibration.CreateDefault(customWindow, 34);
var expectedCustomRowHeight =
    (JtdxBandActivityGridCalibration.DefaultBandActivityBottom
     - JtdxBandActivityGridCalibration.DefaultBandActivityTop) / 34.5;
if (!customCalibration.IsUsable
    || customCalibration.SafeVisibleFullRowCount != 34
    || Math.Abs(customCalibration.RowHeight - expectedCustomRowHeight) > 0.0001)
{
    failures.Add("A 34-row calibration did not produce usable dynamic row geometry.");
}

var customSettings = new AppSettings();
customCalibration.SaveTo(customSettings);
var restoredCustomCalibration = JtdxBandActivityGridCalibration.FromSettings(customSettings);
if (restoredCustomCalibration.SafeVisibleFullRowCount != 34
    || restoredCustomCalibration.JtdxWindowWidth != 1400
    || restoredCustomCalibration.JtdxWindowHeight != 800)
{
    failures.Add("The custom row count/window geometry did not survive settings round-trip.");
}

var normalizeGuiDefaults = typeof(SettingsService).GetMethod(
    "NormalizeJtdxGuiSelectionDefaults",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing GUI settings normalization method.");
normalizeGuiDefaults.Invoke(null, [customSettings]);
if (customSettings.JtdxBandVisibleRowCount != 34)
    failures.Add("Settings normalization overwrote the user's valid 34-row selection.");
var unsetRowSettings = new AppSettings { JtdxBandVisibleRowCount = 0 };
normalizeGuiDefaults.Invoke(null, [unsetRowSettings]);
if (unsetRowSettings.JtdxBandVisibleRowCount != 52)
    failures.Add("Settings normalization did not migrate an unset row count to the personal 52-row default.");

var windowMatchesCalibration = typeof(JtdxGuiGridSelector).GetMethod(
    "WindowMatchesCalibration",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing click-time window-size guard.");
var matchingWindow = new JtdxWindowInfo(IntPtr.Zero, "JTDX", 123, 0, 0, 1400, 800);
var resizedWindow = new JtdxWindowInfo(IntPtr.Zero, "JTDX", 123, 0, 0, 1200, 700);
if (!(bool)windowMatchesCalibration.Invoke(null, [matchingWindow, customCalibration])!
    || (bool)windowMatchesCalibration.Invoke(null, [resizedWindow, customCalibration])!)
{
    failures.Add("The click-time size guard did not accept matching geometry and reject resized geometry.");
}
if (!new JtdxWindowInfo(IntPtr.Zero, "JTDX", 123, 0, 0, 1400, 800, true).IsMinimized)
    failures.Add("The JTDX window model did not retain the click-time minimised state.");

var scoreWindowCandidate = typeof(JtdxWindowLocator).GetMethod(
    "ScoreCandidate",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing JTDX window candidate scorer.");
var dxPilotSelfScore = (int)scoreWindowCandidate.Invoke(
    null,
    ["DX Pilot for JTDX by G1CEC", "HwndWrapper", "DXPilot-for-JTDX-G1CEC", "JTDX"])!;
if (dxPilotSelfScore >= 0)
    failures.Add("The renamed DX Pilot window was not excluded from JTDX window discovery.");

if (!typeof(MainViewModel).Assembly.GetName().Name!.Equals("DXPilot-for-JTDX-G1CEC", StringComparison.Ordinal))
    failures.Add("The executable assembly does not use the DX Pilot/G1CEC identity.");
if (!new SettingsService().AppFolder.EndsWith("JtdxAutoResume.V3", StringComparison.OrdinalIgnoreCase))
    failures.Add("The DX Pilot rename changed the legacy settings folder and would hide existing configuration.");

var visibleModel = new JtdxVisibleRowModel();
var visibleDecodes = Enumerable.Range(0, 50)
    .Select(index => new DecodeMessage
    {
        ReceivedAt = DateTime.Now.AddSeconds(index),
        DecodeTime = TimeSpan.FromSeconds(15),
        Callsign = $"K1A{index:00}",
        ContactableCall = $"K1A{index:00}",
        RawText = $"CQ K1A{index:00} FN42",
        AudioOffset = 1000 + index
    })
    .ToList();
visibleModel.Rebuild(visibleDecodes, customCalibration);
var newestVisibleRow = visibleModel.FindDecode(visibleDecodes[^1]);
if (visibleModel.Rows.Count != 34 || newestVisibleRow?.ScreenRowIndex != 33)
{
    failures.Add($"The universal visible-row model did not use rows 0-33 (count {visibleModel.Rows.Count}, newest {newestVisibleRow?.ScreenRowIndex}).");
}

if (AmateurBandMapper.OwnTransmitCycle("FT8").TotalSeconds != 30)
    failures.Add("FT8 own-transmit cycle was not 30 seconds.");
if (AmateurBandMapper.OwnTransmitCycle("FT4").TotalSeconds != 15)
    failures.Add("FT4 own-transmit cycle was not 15 seconds.");
if (AmateurBandMapper.NormalizeMode("~") != "FT8")
    failures.Add("The JTDX FT8 Decode marker '~' was not normalized to FT8.");
if (AmateurBandMapper.NormalizeMode("+") != "FT4")
    failures.Add("The JTDX FT4 Decode marker '+' was not normalized to FT4.");

var packet = BuildStatusPacket(14_074_000, "FT8", 15);
if (!JtdxUdpListener.TryParseStatus(packet, out var status))
{
    failures.Add("Synthetic JTDX Status packet was not parsed.");
}
else
{
    if (status.DialFrequencyHz != 14_074_000)
        failures.Add($"Parsed dial frequency was {status.DialFrequencyHz}.");
    if (status.Band != "20m")
        failures.Add($"Parsed band was '{status.Band}'.");
    if (status.Mode != "FT8")
        failures.Add($"Parsed mode was '{status.Mode}'.");
    if (status.TrPeriodSeconds != 15)
        failures.Add($"Parsed TR period was {status.TrPeriodSeconds}.");
}

if (!JtdxUdpListener.TryParseDecode(
        BuildDecodePacket("~", "CQ K1ABC FN42"),
        out var ft8Decode,
        out var ft8DecodeWarning)
    || ft8Decode.Mode != "FT8"
    || ft8Decode.ProtocolMode != "~")
{
    failures.Add($"Synthetic JTDX FT8 Decode packet did not preserve both normalized and protocol modes: {ft8DecodeWarning}");
}

if (!JtdxUdpListener.TryParseDecode(
        BuildDecodePacket("+", "CQ K1ABC FN42"),
        out var ft4Decode,
        out var ft4DecodeWarning)
    || ft4Decode.Mode != "FT4"
    || ft4Decode.ProtocolMode != "+")
{
    failures.Add($"Synthetic JTDX FT4 Decode packet did not preserve both normalized and protocol modes: {ft4DecodeWarning}");
}

var buildReplyPacket = typeof(JtdxUdpClient).GetMethod(
    "BuildReplyPacket",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing UDP Reply packet builder.");
var ft8ReplyPacket = (byte[])buildReplyPacket.Invoke(null, [ft8Decode, "JTDX", Encoding.UTF8])!;
var ft4ReplyPacket = (byte[])buildReplyPacket.Invoke(null, [ft4Decode, "JTDX", Encoding.UTF8])!;
if (ReadReplyMode(ft8ReplyPacket) != "~" || ReadReplyMode(ft4ReplyPacket) != "+")
    failures.Add("UDP Reply packets did not echo the original JTDX FT8/FT4 protocol mode markers.");
var copyDecode = typeof(TargetSelector).GetMethod(
    "CopyDecode",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing target-selection decode copier.");
var copiedFt8Decode = (DecodeMessage)copyDecode.Invoke(null, [ft8Decode])!;
if (copiedFt8Decode.ProtocolMode != "~")
    failures.Add("Target ranking/selection lost the original UDP protocol mode marker.");

var mismatchCheck = typeof(MainViewModel).GetMethod(
    "LooksLikeCqOrWrongTarget",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing TX mismatch classifier.");
if (!(bool)mismatchCheck.Invoke(null, [new JtdxStatusMessage(), "K1ABC"])!)
    failures.Add("A blank DX Call and TX message did not identify that JTDX had cleared the locked target.");

var statusConfirmsTarget = typeof(MainViewModel).GetMethod(
    "StatusConfirmsTarget",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing locked-recovery target confirmation check.");
if (!(bool)statusConfirmsTarget.Invoke(
        null,
        [new JtdxStatusMessage { DxCall = "K9OM" }, "K9OM"])!
    || (bool)statusConfirmsTarget.Invoke(
        null,
        [new JtdxStatusMessage { DxCall = "VE2OPC" }, "K9OM"])!
    || (bool)statusConfirmsTarget.Invoke(
        null,
        [new JtdxStatusMessage { DxCall = "" }, "K9OM"])!)
{
    failures.Add("Locked recovery did not preserve a genuinely confirmed target or reject wrong/blank DX Calls.");
}

var freshSelectionStatusCheck = typeof(JtdxSelectionController).GetMethod(
    "IsFreshMatchingStatus",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing fresh post-selection Status check.");
var selectionActionAt = DateTime.Now;
var staleMatchingStatus = new JtdxStatusMessage
{
    ReceivedAt = selectionActionAt.AddMilliseconds(-1),
    DxCall = "8R1TM"
};
var freshMatchingStatus = new JtdxStatusMessage
{
    ReceivedAt = selectionActionAt.AddMilliseconds(1),
    DxCall = "8R1TM"
};
var freshWrongStatus = new JtdxStatusMessage
{
    ReceivedAt = selectionActionAt.AddMilliseconds(1),
    DxCall = "CR7BRV"
};
if ((bool)freshSelectionStatusCheck.Invoke(null, [staleMatchingStatus, "8R1TM", selectionActionAt])!
    || !(bool)freshSelectionStatusCheck.Invoke(null, [freshMatchingStatus, "8R1TM", selectionActionAt])!
    || (bool)freshSelectionStatusCheck.Invoke(null, [freshWrongStatus, "8R1TM", selectionActionAt])!)
{
    failures.Add("GUI confirmation did not reject stale/pre-click DX Call state and require a fresh matching JTDX Status.");
}

var confirmationTimeoutFor = typeof(JtdxSelectionController).GetMethod(
    "ConfirmationTimeoutFor",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing selection confirmation-timeout policy.");
var guiConfirmationTimeout = (TimeSpan)confirmationTimeoutFor.Invoke(
    null,
    [
        new SelectionResult { SelectionMethod = JtdxSelectionMethod.GuiGridDoubleClick },
        new AppSettings { ReplyConfirmSeconds = 30 },
        null
    ])!;
var udpConfirmationTimeout = (TimeSpan)confirmationTimeoutFor.Invoke(
    null,
    [
        new SelectionResult { SelectionMethod = JtdxSelectionMethod.UdpReply },
        new AppSettings { ReplyConfirmSeconds = 30 },
        null
    ])!;
if (guiConfirmationTimeout != TimeSpan.FromSeconds(4)
    || udpConfirmationTimeout != TimeSpan.FromSeconds(30))
{
    failures.Add("GUI selection still inherited the multi-cycle UDP confirmation timeout instead of using bounded CALL NOW confirmation.");
}

var guiAttemptGate = typeof(MainViewModel).GetMethod(
    "CanAttemptGuiSelection",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing bounded GUI selection-attempt gate.");
if (!(bool)guiAttemptGate.Invoke(null, [0, 3, false])!
    || (bool)guiAttemptGate.Invoke(null, [1, 3, false])!
    || !(bool)guiAttemptGate.Invoke(null, [1, 3, true])!
    || (bool)guiAttemptGate.Invoke(null, [3, 3, true])!)
{
    failures.Add("Bounded GUI recovery did not allow the first click, require an authorised correction, and stop at the real-click limit.");
}

var freshWrongTargetGate = typeof(MainViewModel).GetMethod(
    "IsFreshWrongTargetStatusForGuiCorrection",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing fresh wrong-target GUI correction gate.");
var lastGuiClickAt = DateTime.Now;
if ((bool)freshWrongTargetGate.Invoke(
        null,
        [new JtdxStatusMessage { ReceivedAt = lastGuiClickAt.AddMilliseconds(-1), DxCall = "CR7BRV" }, "8R1TM", lastGuiClickAt])!
    || (bool)freshWrongTargetGate.Invoke(
        null,
        [new JtdxStatusMessage { ReceivedAt = lastGuiClickAt.AddMilliseconds(1), DxCall = "8R1TM" }, "8R1TM", lastGuiClickAt])!
    || !(bool)freshWrongTargetGate.Invoke(
        null,
        [new JtdxStatusMessage { ReceivedAt = lastGuiClickAt.AddMilliseconds(1), DxCall = "" }, "8R1TM", lastGuiClickAt])!
    || !(bool)freshWrongTargetGate.Invoke(
        null,
        [new JtdxStatusMessage { ReceivedAt = lastGuiClickAt.AddMilliseconds(1), DxCall = "CR7BRV" }, "8R1TM", lastGuiClickAt])!)
{
    failures.Add("GUI correction did not require fresh post-click RX evidence of a wrong or cleared DX Call.");
}

var retriableGuiFailure = typeof(MainViewModel).GetMethod(
    "IsRetriableGuiSelectionFailure",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing retriable GUI-selection failure classifier.");
if (!(bool)retriableGuiFailure.Invoke(null, [SelectionFailureReason.ConfirmationTimedOut])!
    || !(bool)retriableGuiFailure.Invoke(null, [SelectionFailureReason.JtdxSelectedWrongCall])!
    || (bool)retriableGuiFailure.Invoke(null, [SelectionFailureReason.JtdxWindowMinimized])!)
{
    failures.Add("GUI retry classification did not limit retries to confirmation/wrong-call failures.");
}

var failedGuiSelection = new SelectionResult
{
    Success = false,
    SelectionMethod = JtdxSelectionMethod.GuiGridDoubleClick,
    SelectionActionAt = DateTime.Now,
    FailureReason = SelectionFailureReason.JtdxSelectedWrongCall
};
if (!GuiSelectionSafetyPolicy.RequiresReceiveOnlyBarrier(failedGuiSelection)
    || GuiSelectionSafetyPolicy.RequiresReceiveOnlyBarrier(new SelectionResult
    {
        Success = false,
        SelectionMethod = JtdxSelectionMethod.GuiGridDoubleClick,
        FailureReason = SelectionFailureReason.JtdxSelectedWrongCall
    })
    || GuiSelectionSafetyPolicy.RequiresReceiveOnlyBarrier(new SelectionResult
    {
        Success = false,
        SelectionMethod = JtdxSelectionMethod.UdpReply,
        SelectionActionAt = DateTime.Now,
        FailureReason = SelectionFailureReason.ConfirmationTimedOut
    }))
{
    failures.Add("Failed physical GUI selections were not isolated behind the receive-only safety barrier.");
}

var safetyNow = DateTime.Now;
if (!GuiSelectionSafetyPolicy.IsConfirmedReceiveOnly(
        new JtdxStatusMessage { ReceivedAt = safetyNow, TxEnabled = false, Transmitting = false },
        safetyNow)
    || GuiSelectionSafetyPolicy.IsConfirmedReceiveOnly(
        new JtdxStatusMessage { ReceivedAt = safetyNow, TxEnabled = true, Transmitting = false },
        safetyNow)
    || GuiSelectionSafetyPolicy.IsConfirmedReceiveOnly(
        new JtdxStatusMessage { ReceivedAt = safetyNow, TxEnabled = false, Transmitting = true },
        safetyNow)
    || GuiSelectionSafetyPolicy.IsConfirmedReceiveOnly(
        new JtdxStatusMessage { ReceivedAt = safetyNow.AddSeconds(-4), TxEnabled = false, Transmitting = false },
        safetyNow)
    || GuiSelectionSafetyPolicy.IsConfirmedReceiveOnly(
        new JtdxStatusMessage { ReceivedAt = safetyNow.AddMilliseconds(-1), TxEnabled = false, Transmitting = false },
        safetyNow,
        safetyNow))
{
    failures.Add("GUI receive-only barrier accepted enabled, transmitting, stale, or pre-click UDP state.");
}

var settingsTransferFolder = Path.Combine(
    Path.GetTempPath(),
    "JtdxAutoResume-SettingsTransfer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(settingsTransferFolder);
try
{
    var transferService = new SettingsService();
    var transferFile = Path.Combine(settingsTransferFolder, "settings-export.json");
    var transferSettings = new AppSettings
    {
        MyCallsign = "G1CEC",
        JtdxBandVisibleRowCount = 51,
        QrzUsername = "TESTUSER",
        QrzPassword = "plain-secret-must-not-export",
        QrzPasswordProtected = "protected-secret-must-not-export",
        MapBasemapId = "EsriStreets"
    };
    var transferSchedule = new[]
    {
        new BandScheduleItem
        {
            Enabled = true,
            Label = "15m",
            Hour = 12,
            Minute = 34,
            X = 100,
            Y = 200
        }
    };
    transferService.ExportPortableSettings(transferFile, transferSettings, transferSchedule);
    var exportedJson = File.ReadAllText(transferFile);
    if (exportedJson.Contains("plain-secret", StringComparison.Ordinal)
        || exportedJson.Contains("protected-secret", StringComparison.Ordinal))
    {
        failures.Add("Portable settings export exposed QRZ credentials.");
    }

    if (!transferService.TryReadSettingsImport(transferFile, out var importedTransfer, out var importError))
    {
        failures.Add($"A valid settings export was rejected: {importError}");
    }
    else if (importedTransfer.Settings.JtdxBandVisibleRowCount != 51
        || importedTransfer.Schedule?.Count != 1
        || importedTransfer.Schedule[0].Label != "15m"
        || importedTransfer.Settings.MapBasemapId != "EsriStreets"
        || !importedTransfer.QrzPasswordExcluded)
    {
        failures.Add("Settings/scheduler data did not survive the portable export/import round-trip.");
    }

    var invalidTransferFile = Path.Combine(settingsTransferFolder, "invalid-settings.json");
    File.WriteAllText(
        invalidTransferFile,
        JsonSerializer.Serialize(new AppSettings { JtdxBandVisibleRowCount = 999 }));
    if (transferService.TryReadSettingsImport(invalidTransferFile, out _, out _))
        failures.Add("An invalid 999-row settings file was accepted for import.");

    var unrelatedJsonFile = Path.Combine(settingsTransferFolder, "unrelated.json");
    File.WriteAllText(unrelatedJsonFile, "{\"example\":true}");
    if (transferService.TryReadSettingsImport(unrelatedJsonFile, out _, out _))
        failures.Add("An unrelated JSON file was accepted as DX Pilot settings.");
}
finally
{
    Directory.Delete(settingsTransferFolder, recursive: true);
}

if (UsStateValidator.StandardStateCodes.Count != 50)
    failures.Add("The WAS state validator does not expose exactly 50 standard states.");

var foreignStateOnly = new AdifWorkedStatusBuilder().Build(
    new[]
    {
        new AdifQso { Call = "PD0TEST", Dxcc = "263", Country = "Netherlands", State = "UT", LotwConfirmed = true },
        new AdifQso { Call = "YV5TEST", Dxcc = "148", Country = "Venezuela", State = "DC", LotwConfirmed = true },
        new AdifQso { Call = "VK6TEST", Dxcc = "150", Country = "Australia", State = "WA", LotwConfirmed = true }
    },
    Array.Empty<AdifQso>(),
    new AppSettings { StateConfirmationMode = "WorkedOnly", IncludeDistrictOfColumbia = true });
if (foreignStateOnly.Indexes.States.Count != 0)
    failures.Add("Foreign province codes were incorrectly indexed as WAS states.");

var wasQsos = new[]
{
    new AdifQso { Call = "K7UT", Dxcc = "291", Country = "United States", State = "UT", LotwConfirmed = false },
    new AdifQso { Call = "KL7AK", Dxcc = "6", Country = "Alaska", State = "AK", LotwConfirmed = true },
    new AdifQso { Call = "KH6HI", Dxcc = "110", Country = "Hawaii", State = "HI", LotwConfirmed = true },
    new AdifQso { Call = "W3DC", Dxcc = "291", Country = "United States", State = "DC", LotwConfirmed = true }
};
var workedOnlyWas = new AdifWorkedStatusBuilder().Build(
    wasQsos,
    Array.Empty<AdifQso>(),
    new AppSettings { StateConfirmationMode = "WorkedOnly", IncludeDistrictOfColumbia = true });
if (!workedOnlyWas.Indexes.States.ContainsKey("UT")
    || !workedOnlyWas.Indexes.States.ContainsKey("AK")
    || !workedOnlyWas.Indexes.States.ContainsKey("HI")
    || !workedOnlyWas.Indexes.States.ContainsKey("DC"))
{
    failures.Add("Valid contiguous-US, Alaska, Hawaii, or optional DC records were not indexed for WAS.");
}

var lotwOnlyWas = new AdifWorkedStatusBuilder().Build(
    wasQsos,
    Array.Empty<AdifQso>(),
    new AppSettings { StateConfirmationMode = "LoTWOnly", IncludeDistrictOfColumbia = true });
if (lotwOnlyWas.Indexes.States["UT"].ConfirmedAny
    || !lotwOnlyWas.Indexes.States["AK"].ConfirmedAny
    || !lotwOnlyWas.Indexes.States["HI"].ConfirmedAny)
{
    failures.Add("WAS state confirmation did not honour the LoTW-only setting.");
}

var noDcWas = new AdifWorkedStatusBuilder().Build(
    wasQsos,
    Array.Empty<AdifQso>(),
    new AppSettings { StateConfirmationMode = "WorkedOnly", IncludeDistrictOfColumbia = false });
if (noDcWas.Indexes.States.ContainsKey("DC"))
    failures.Add("District of Columbia was indexed while its optional setting was disabled.");

if (!WasStateEligibility.IsEligible("6", "Alaska")
    || !WasStateEligibility.IsEligible("110", "Hawaii")
    || !WasStateEligibility.IsEligible("291", "United States")
    || WasStateEligibility.IsEligible("263", "Netherlands"))
{
    failures.Add("Central WAS DXCC/entity eligibility is incorrect.");
}

using (var viewModel = new MainViewModel())
{
    var postPskCqTarget = new DecodeMessage
    {
        MessageType = Ft8MessageType.Cq,
        IsCq = true,
        ContactableCall = "SM7DXE",
        Callsign = "SM7DXE",
        RawText = "CQ SM7DXE JO89"
    };
    if (!(bool)(InvokePrivate(viewModel, "ShouldUseUdpReplyForSource", postPskCqTarget) ?? false))
        failures.Add("A normal CQ target did not retain the stable v3.4.3 UDP Reply selection path.");

    var noUsefulTargetBaseline = DateTime.UtcNow.AddMinutes(-12);
    SetPrivate(viewModel, "_conditionsLastUsefulTargetAtUtc", noUsefulTargetBaseline);
    InvokePrivate(viewModel, "ObserveConditionsSearchDecode", postPskCqTarget);
    if (PrivateDateTime(viewModel, "_conditionsLastUsefulTargetAtUtc") != noUsefulTargetBaseline)
        failures.Add("An ordinary CQ decode incorrectly reset the no-useful-target Conditions Search timer.");
    InvokePrivate(viewModel, "MarkConditionsUsefulTarget", postPskCqTarget.ContactableCall);
    if (PrivateDateTime(viewModel, "_conditionsLastUsefulTargetAtUtc") <= noUsefulTargetBaseline)
        failures.Add("An assistance target accepted by DX Pilot did not reset the no-useful-target Conditions Search timer.");

    var initialStatus = new JtdxStatusMessage
    {
        ReceivedAt = DateTime.Now.AddMilliseconds(-100),
        SourceAppId = "JTDX",
        DialFrequencyHz = 14_074_000,
        Band = "20m",
        Mode = "FT8",
        TrPeriodSeconds = 15
    };
    InvokePrivate(viewModel, "HandleRadioContextStatus", initialStatus);
    if (viewModel.CurrentBand != "20m" || viewModel.CurrentDigitalMode != "FT8")
        failures.Add("Main view model did not retain the initial 20m FT8 radio context.");

    var mapOverlayStatus = new SimpleWorkedStatus { Id = "FN42", LoTWConfirmedAny = true };
    mapOverlayStatus.LoTWConfirmedBands.UnionWith(["20m", "15m"]);
    mapOverlayStatus.LoTWConfirmedModes.UnionWith(["FT8", "FT4"]);
    var mapOverlayIndexes = new WorkedStatusIndexes();
    mapOverlayIndexes.Grids["FN42"] = mapOverlayStatus;
    SetPrivate(viewModel, "_adifMergeResult", new AdifMergeResult { Indexes = mapOverlayIndexes });
    InvokePrivate(viewModel, "RefreshMapLotwConfirmedGrids");
    viewModel.Map.ObserveDecode(new DecodeMessage
    {
        ContactableCall = "K1MAP",
        Grid = "FN42",
        ReceivedAt = DateTime.Now
    });

    viewModel.DxAssist.RecentDecodes.Add(new DecodeMessage());
    viewModel.Wanted.WantedDxcc.Add(new WantedItem());
    var panel = new LocationPanelViewModel("EU", "Europe");
    panel.Candidates.Add(new DxCandidateRow());
    viewModel.Location.Panels.Add(panel);
    PrivateDecodeHistory(viewModel).Add(new DecodeMessage());
    viewModel.SessionHistory.AllOpportunities.Add(new SessionDxOpportunity
    {
        SessionId = "radio-context-regression",
        OpportunityId = "291:K1HISTORY:20M:FT8",
        Call = "K1HISTORY",
        Band = "20m",
        Mode = "FT8",
        Category = "Heard",
        DxccStatus = "Confirmed",
        LastSeenUtc = DateTime.UtcNow,
        Outcome = "Seen only"
    });

    var changedStatus = new JtdxStatusMessage
    {
        ReceivedAt = DateTime.Now.AddMilliseconds(-100),
        SourceAppId = "JTDX",
        DialFrequencyHz = 21_074_000,
        Band = "15m",
        Mode = "FT8",
        TrPeriodSeconds = 15
    };
    InvokePrivate(viewModel, "HandleRadioContextStatus", changedStatus);
    if (viewModel.CurrentBand != "15m")
        failures.Add("Band change did not update the current band to 15m.");
    if (viewModel.DxAssist.RecentDecodes.Count != 0
        || viewModel.Wanted.WantedDxcc.Count != 0
        || panel.Candidates.Count != 0
        || PrivateDecodeHistory(viewModel).Count != 0)
    {
        failures.Add("Band change did not clear every tested live table/history.");
    }
    if (viewModel.Map.StationCount != 0
        || viewModel.Map.SelectedStation != null
        || viewModel.Map.LotwConfirmedGridCount != 1)
    {
        failures.Add("Band change did not clear only live map stations while retaining permanent LoTW grid shading.");
    }
    if (!viewModel.SessionHistory.AllOpportunities.Any(item => item.Call == "K1HISTORY" && item.Band == "20m"))
        failures.Add("Band change incorrectly cleared the full-run Session History.");

    var decode = new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        // JTDX transmits QTime as UTC even though the view model uses local DateTime.
        DecodeTime = DateTime.UtcNow.TimeOfDay,
        Mode = "FT8",
        Callsign = "K1ABC",
        ContactableCall = "K1ABC",
        RawText = "CQ K1ABC FN42"
    };
    var accepted = (bool)(InvokePrivate(viewModel, "PrepareDecodeForCurrentRadioContext", decode) ?? false);
    if (!accepted || decode.Band != "15m" || decode.DialFrequencyHz != 21_074_000)
        failures.Add("A current decode did not inherit the 15m dial-frequency context.");

    SetPrivate(viewModel, "_radioContextSettleUntil", DateTime.Now.AddSeconds(-1));
    InvokePrivate(viewModel, "CompleteRadioContextSettlingIfReady");
    if (!viewModel.RadioContextStatus.Contains("ready", StringComparison.OrdinalIgnoreCase))
        failures.Add("Radio context did not become ready after a settled current decode.");

    viewModel.DxAssist.RecentDecodes.Add(decode);
    viewModel.Map.ObserveDecode(new DecodeMessage
    {
        ContactableCall = "K1MODE",
        Grid = "FN31",
        ReceivedAt = DateTime.Now
    });
    InvokePrivate(viewModel, "HandleRadioContextStatus", new JtdxStatusMessage
    {
        ReceivedAt = DateTime.Now,
        SourceAppId = "JTDX",
        DialFrequencyHz = 21_074_000,
        Band = "15m",
        Mode = "FT4",
        TrPeriodSeconds = 7
    });
    if (viewModel.CurrentDigitalMode != "FT4" || viewModel.DxAssist.RecentDecodes.Count != 0)
        failures.Add("FT8-to-FT4 mode change did not clear the live table and update mode.");
    if (viewModel.Map.StationCount != 1)
        failures.Add("A mode-only change incorrectly cleared the band-specific live map.");

    SetPrivate(viewModel, "_adifMergeResult", new AdifMergeResult());
    PrivateLogbook(viewModel).Clear();
    viewModel.Settings.Settings.EnableWantedDxcc = false;
    viewModel.Settings.Settings.EnableWantedGrids = false;
    viewModel.Settings.Settings.EnableWantedStates = false;
    var observationOnlyDecode = new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        DecodeTime = DateTime.UtcNow.TimeOfDay,
        Mode = "FT4",
        Band = "15m",
        Callsign = "K1OBS",
        HeardCall = "K1OBS",
        ContactableCall = "K1OBS",
        Grid = "AA00",
        State = "WY",
        RawText = "CQ K1OBS AA00",
        MessageType = Ft8MessageType.Cq,
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    };
    InvokePrivate(viewModel, "UpdateWantedItems", observationOnlyDecode);
    if (viewModel.Wanted.WantedDxcc.Count != 1
        || viewModel.Wanted.WantedGrids.Count != 1
        || viewModel.Wanted.WantedStates.Count != 1)
    {
        failures.Add("Wanted observation tables did not populate while every Sniper target category was disabled.");
    }

    if (InvokePrivate(viewModel, "SelectWantedSniperTarget") != null)
        failures.Add("Wanted Sniper selected a row while every target category was disabled.");
    viewModel.Settings.Settings.EnableWantedGrids = true;
    if (InvokePrivate(viewModel, "SelectWantedSniperTarget") is not WantedItem gridSelection
        || !gridSelection.Section.Equals("Grid", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add("Wanted Sniper category filtering did not independently enable only grid selection.");
    }

    viewModel.Settings.Settings.CandidateMaxAgeSeconds = 30;
    viewModel.Settings.Settings.NewDxccStaleSeconds = 240;
    viewModel.Settings.Settings.KeepCallingNewDxccUntilStale = false;
    var staleNewDxcc = new DxTarget
    {
        Decode = new DecodeMessage
        {
            ReceivedAt = DateTime.Now.AddSeconds(-31),
            Callsign = "K1STALE",
            ContactableCall = "K1STALE",
            RawText = "CQ K1STALE FN42"
        },
        Ranking = new CandidateRanking { DxccStatus = DxccCandidateStatus.NotWorked }
    };
    SetPrivate(viewModel, "_lockedTarget", staleNewDxcc);
    SetPrivateEnum(viewModel, "_huntState", "Calling");
    var freshTargetCheck = typeof(MainViewModel).GetMethod(
        "IsFreshTarget",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing target freshness check.");
    var staleCheck = typeof(MainViewModel).GetMethod(
        "ActiveCallingTargetHasGoneStale",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing active-target stale check.");
    if ((bool)freshTargetCheck.Invoke(viewModel, [staleNewDxcc])!)
        failures.Add("A New DXCC used the longer acquisition window while optional persistence was disabled.");
    if (!(bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("An ordinary calling target was not stale at the normal threshold before QSO progress.");

    viewModel.Settings.Settings.KeepCallingNewDxccUntilStale = true;
    if (!(bool)freshTargetCheck.Invoke(viewModel, [staleNewDxcc])!)
        failures.Add("Optional New DXCC persistence did not extend the target acquisition window.");
    if ((bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("Optional New DXCC persistence did not use the longer New-DXCC stale threshold.");

    var staleUnconfirmedDxcc = new DxTarget
    {
        Decode = new DecodeMessage
        {
            ReceivedAt = DateTime.Now.AddSeconds(-31),
            Callsign = "VR2TEST",
            ContactableCall = "VR2TEST",
            Dxcc = "321",
            EntityName = "Hong Kong",
            RawText = "CQ VR2TEST OL72"
        },
        Ranking = new CandidateRanking { DxccStatus = DxccCandidateStatus.WorkedUnconfirmed }
    };
    SetPrivate(viewModel, "_lockedTarget", staleUnconfirmedDxcc);
    if (!(bool)freshTargetCheck.Invoke(viewModel, [staleUnconfirmedDxcc])!)
        failures.Add("A worked-but-unconfirmed DXCC did not receive the New-DXCC persistence window.");
    if ((bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("A worked-but-unconfirmed DXCC was treated as stale at the ordinary target threshold.");

    var staleGridOnlyTarget = new DxTarget
    {
        Decode = new DecodeMessage
        {
            ReceivedAt = DateTime.Now.AddSeconds(-31),
            Callsign = "G1GRID",
            ContactableCall = "G1GRID",
            Dxcc = "223",
            Grid = "IO91",
            RawText = "CQ G1GRID IO91"
        },
        Ranking = new CandidateRanking
        {
            DxccStatus = DxccCandidateStatus.Confirmed,
            NeedStatus = NeedStatus.NeverWorked
        }
    };
    SetPrivate(viewModel, "_lockedTarget", staleGridOnlyTarget);
    if ((bool)freshTargetCheck.Invoke(viewModel, [staleGridOnlyTarget])!)
        failures.Add("A new-grid-only target incorrectly received DXCC keep-calling persistence.");
    if (!(bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("A new-grid-only target incorrectly bypassed the ordinary stale threshold.");

    SetPrivate(viewModel, "_lockedTarget", staleNewDxcc);
    SetPrivate(viewModel, "_targetConfirmedInFeed", true);
    if ((bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("Source staleness incorrectly abandoned a target after genuine QSO progress.");

    viewModel.Settings.Settings.KeepCallingNewDxccUntilStale = false;
    SetPrivate(viewModel, "_targetConfirmedInFeed", false);
    SetPrivate(viewModel, "_targetConfirmedInJtdx", true);
    if ((bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("Source age incorrectly abandoned a target after JTDX had confirmed the selected DX Call.");

    SetPrivate(viewModel, "_targetConfirmedInJtdx", false);
    SetPrivate(viewModel, "_callAttemptCount", 1);
    if (!(bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("A stale target remained locked after JTDX lost confirmation merely because it had been called earlier.");

    SetPrivate(viewModel, "_callAttemptCount", 0);
    SetPrivate(viewModel, "_targetSelectionInProgress", true);
    if ((bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("Source age interrupted an in-progress target-selection confirmation.");

    SetPrivate(viewModel, "_targetSelectionInProgress", false);
    if (!(bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("A genuinely unselected, unheard stale target was not released during acquisition.");

    SetPrivate(viewModel, "_immediateTxRetargetInProgress", true);
    if ((bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("Source age interrupted an immediate same-slot target correction.");
    SetPrivate(viewModel, "_immediateTxRetargetInProgress", false);

    viewModel.Settings.Settings.KeepCallingNewDxccUntilStale = true;
    staleNewDxcc.Decode.ReceivedAt = DateTime.Now.AddSeconds(-241);
    SetPrivate(viewModel, "_targetConfirmedInJtdx", true);
    if (!(bool)staleCheck.Invoke(viewModel, null)!)
        failures.Add("Optional New DXCC persistence did not end after its explicit longer stale limit.");

    viewModel.Settings.Settings.KeepCallingNewDxccUntilStale = false;
    staleNewDxcc.Decode.ReceivedAt = DateTime.Now;
    SetPrivate(viewModel, "_targetConfirmedInJtdx", false);
    SetPrivate(viewModel, "_targetStartedAt", DateTime.Now);
    SetPrivate(viewModel, "_acquisitionAttemptCount", 0);
    SetPrivate(viewModel, "_unconfirmedRecoveryStartedAt", DateTime.Now.AddMinutes(-1));
    if (!(bool)(InvokePrivate(viewModel, "AcquisitionFailed") ?? false))
        failures.Add("A lost-target recovery did not expire when no confirming Status or retry arrived within the bounded RX window.");

    SetPrivate(viewModel, "_unconfirmedRecoveryStartedAt", DateTime.MinValue);
    if ((bool)(InvokePrivate(viewModel, "AcquisitionFailed") ?? true))
        failures.Add("Initial target acquisition expired without exhausting its configured attempts or bounded recovery window.");

    SetPrivate(viewModel, "_pendingLockedReplyWhenIdle", true);
    SetPrivate(viewModel, "_pendingLockedReplyReason", "obsolete K9OM recovery");
    InvokePrivate(viewModel, "ResetWrongTargetState");
    if (PrivateBool(viewModel, "_pendingLockedReplyWhenIdle"))
        failures.Add("Successful-target cleanup retained an obsolete queued recovery that could replace the confirmed target.");

    SetPrivate(viewModel, "_lockedTarget", null!);
    SetPrivateEnum(viewModel, "_huntState", "Idle");
    SetPrivateEnum(viewModel, "_qsoStage", "None");
    var failedSourceDecode = new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        DecodeTime = DateTime.UtcNow.TimeOfDay,
        Mode = "FT4",
        Band = "15m",
        Callsign = "HC5VF",
        HeardCall = "HC5VF",
        ContactableCall = "HC5VF",
        Dxcc = "120",
        EntityName = "Ecuador",
        Grid = "FI07",
        RawText = "CQ HC5VF FI07",
        MessageType = Ft8MessageType.Cq,
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    };
    InvokePrivate(viewModel, "UpdateWantedItems", failedSourceDecode);
    var hc5Wanted = viewModel.Wanted.WantedDxcc.FirstOrDefault(item =>
        item.ContactableCall.Equals("HC5VF", StringComparison.OrdinalIgnoreCase));
    if (hc5Wanted == null)
    {
        failures.Add("Failed-source recovery test could not create the HC5VF Wanted DXCC row.");
    }
    else
    {
        var replySourceKeyMethod = typeof(MainViewModel).GetMethod(
            "ReplySourceKey",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing reply-source key method.");
        var failedSourceKey = (string)replySourceKeyMethod.Invoke(null, [failedSourceDecode])!;
        PrivateFailedReplySources(viewModel)[failedSourceKey] = DateTime.Now;
        InvokePrivate(viewModel, "UpdateWantedActionability", hc5Wanted);
        if (hc5Wanted.IsActionable
            || !hc5Wanted.NotActionableReason.Contains("newer decode", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("A failed Wanted source did not clearly state that it was waiting for a newer decode.");
        }

        var newerHc5Decode = new DecodeMessage
        {
            ReceivedAt = failedSourceDecode.ReceivedAt.AddSeconds(1),
            DecodeTime = failedSourceDecode.DecodeTime?.Add(TimeSpan.FromSeconds(15)),
            Mode = "FT4",
            Band = "15m",
            Callsign = "HC5VF",
            HeardCall = "HC5VF",
            ContactableCall = "HC5VF",
            Dxcc = "120",
            EntityName = "Ecuador",
            Grid = "FI07",
            RawText = "CQ HC5VF FI07",
            MessageType = Ft8MessageType.Cq,
            Targetable = true,
            ParseConfidence = ParseConfidence.High
        };
        InvokePrivate(viewModel, "UpdateWantedItems", newerHc5Decode);
        if (!hc5Wanted.IsActionable
            || !ReferenceEquals(hc5Wanted.SourceDecode, newerHc5Decode))
        {
            failures.Add("A newer valid HC5VF decode did not automatically replace and clear the failed Wanted source.");
        }
    }

    var visibleRetryDecode = new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        DecodeTime = DateTime.UtcNow.TimeOfDay,
        Mode = "FT8",
        Band = "17m",
        Callsign = "K1VIS",
        HeardCall = "K1VIS",
        ContactableCall = "K1VIS",
        RawText = "CQ K1VIS FN42",
        MessageType = Ft8MessageType.Cq,
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    };
    PrivateDecodeHistory(viewModel).Insert(0, visibleRetryDecode);
    PrivateVisibleRowModel(viewModel).Rebuild(
        PrivateDecodeHistory(viewModel),
        JtdxBandActivityGridCalibration.FromSettings(viewModel.Settings.Settings));
    var visibleReplySourceKeyMethod = typeof(MainViewModel).GetMethod(
        "ReplySourceKey",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing reply-source key method.");
    var visibleRetryKey = (string)visibleReplySourceKeyMethod.Invoke(null, [visibleRetryDecode])!;
    var visibleRetryWanted = new WantedItem
    {
        Call = "K1VIS",
        ContactableCall = "K1VIS",
        SourceRawMessage = visibleRetryDecode.RawText,
        SourceDecode = visibleRetryDecode
    };
    PrivateFailedReplySources(viewModel)[visibleRetryKey] = DateTime.Now;
    PrivateGuiSelectionClickCounts(viewModel)[visibleRetryKey] = 3;
    InvokePrivate(viewModel, "UpdateWantedActionability", visibleRetryWanted);
    if (visibleRetryWanted.IsActionable
        || !visibleRetryWanted.NotActionableReason.Contains("visible row", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add("A just-failed visible source did not retain a one-receive-period retry cooldown.");
    }

    PrivateFailedReplySources(viewModel)[visibleRetryKey] = DateTime.Now.AddSeconds(-16);
    InvokePrivate(viewModel, "UpdateWantedActionability", visibleRetryWanted);
    if (!visibleRetryWanted.IsActionable
        || !visibleRetryWanted.SelectionMethod.Equals("GuiGridDoubleClick", StringComparison.Ordinal)
        || PrivateFailedReplySources(viewModel).ContainsKey(visibleRetryKey)
        || PrivateGuiSelectionClickCounts(viewModel).ContainsKey(visibleRetryKey)
        || !PrivateForcedGuiSelectionSources(viewModel).Contains(visibleRetryKey))
    {
        failures.Add("A failed source whose exact row remained visible was not re-armed as a controlled GUI grid retry.");
    }

    var activeGuyana = new DxTarget
    {
        Decode = new DecodeMessage
        {
            ReceivedAt = DateTime.Now,
            Callsign = "8R1TM",
            ContactableCall = "8R1TM",
            Dxcc = "129",
            EntityName = "Guyana",
            RawText = "CQ 8R1TM GJ06"
        },
        Ranking = new CandidateRanking { DxccStatus = DxccCandidateStatus.NotWorked }
    };
    SetPrivate(viewModel, "_lockedTarget", activeGuyana);
    SetPrivateEnum(viewModel, "_huntState", "Calling");
    SetPrivateEnum(viewModel, "_qsoStage", "None");
    viewModel.Settings.Settings.EnableWantedDxcc = true;
    var sameGuyanaCall = new WantedItem
    {
        Call = "8R1TM",
        ContactableCall = "8R1TM",
        DxccNumber = "129",
        SourceDecode = activeGuyana.Decode
    };
    if (!(bool)(InvokePrivate(viewModel, "WantedDxccMatchesLockedTarget", sameGuyanaCall) ?? false))
        failures.Add("Wanted DXCC self-preemption guard did not recognise the locked callsign.");

    var alternateGuyanaCall = new WantedItem
    {
        Call = "8R1XYZ",
        ContactableCall = "8R1XYZ",
        DxccNumber = "129",
        SourceDecode = new DecodeMessage { Callsign = "8R1XYZ", ContactableCall = "8R1XYZ", Dxcc = "129" }
    };
    if (!(bool)(InvokePrivate(viewModel, "WantedDxccMatchesLockedTarget", alternateGuyanaCall) ?? false))
        failures.Add("Wanted DXCC self-preemption guard did not recognise the locked DXCC through another callsign.");

    var differentNewDxcc = new WantedItem
    {
        Call = "PZ5DX",
        ContactableCall = "PZ5DX",
        DxccNumber = "140",
        SourceDecode = new DecodeMessage { Callsign = "PZ5DX", ContactableCall = "PZ5DX", Dxcc = "140" }
    };
    if ((bool)(InvokePrivate(viewModel, "WantedDxccMatchesLockedTarget", differentNewDxcc) ?? true))
        failures.Add("Wanted DXCC guard incorrectly blocked a genuinely different DXCC.");

    var newDxccPreemptAttempt = await (Task<bool>)(InvokePrivate(
        viewModel,
        "TryPreemptForWantedDxccAsync")
        ?? throw new InvalidOperationException("Missing Wanted DXCC preemption task."));
    if (newDxccPreemptAttempt || !ReferenceEquals(PrivateLockedTarget(viewModel), activeGuyana))
        failures.Add("An active New DXCC was released by the Wanted DXCC override.");

    var cqSafetyTarget = new DxTarget
    {
        Decode = new DecodeMessage
        {
            ReceivedAt = DateTime.Now,
            Callsign = "N4QEP",
            ContactableCall = "N4QEP",
            RawText = "CQ N4QEP EM60",
            Targetable = true,
            ParseConfidence = ParseConfidence.High
        },
        Ranking = new CandidateRanking()
    };
    SetPrivate(viewModel, "_lockedTarget", cqSafetyTarget);
    SetPrivateEnum(viewModel, "_huntState", "InQso");
    SetPrivateEnum(viewModel, "_qsoStage", "TargetReportSeen");
    viewModel.Settings.Settings.CallTimeoutMinutes = 1;
    SetPrivate(viewModel, "_lastQsoProgressAt", DateTime.Now.AddMinutes(-2));
    if (!(bool)(InvokePrivate(viewModel, "InQsoNoProgressTimedOut") ?? false))
        failures.Add("The independent InQso no-progress watchdog did not expire a stalled QSO.");

    SetPrivate(viewModel, "_lastQsoProgressAt", DateTime.Now);
    var firstCqStatus = new JtdxStatusMessage
    {
        ReceivedAt = DateTime.Now,
        DxCall = "N4QEP",
        TxMessage = "CQ G1CEC IO83",
        TxEnabled = false,
        Transmitting = false
    };
    var firstCqHandled = await (Task<bool>)(InvokePrivate(
        viewModel,
        "HandleInQsoCqContradictionAsync",
        firstCqStatus) ?? throw new InvalidOperationException("Missing CQ contradiction task."));
    if (!firstCqHandled || PrivateLockedTarget(viewModel) == null)
        failures.Add("The InQso CQ contradiction did not retain the locked target for immediate correction.");
    if ((bool)(InvokePrivate(viewModel, "ShouldClickEnableTxRecovery") ?? true))
        failures.Add("DX Pilot pixel recovery was allowed to re-enable TX during an InQso CQ contradiction.");

    InvokePrivate(viewModel, "ProcessDecodeForCurrentQso", new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        DecodeTime = DateTime.UtcNow.TimeOfDay,
        Callsign = "N4QEP",
        ContactableCall = "N4QEP",
        RawText = "G1CEC N4QEP -10",
        ParseConfidence = ParseConfidence.High
    });

    var repeatedCqHandled = await (Task<bool>)(InvokePrivate(
        viewModel,
        "HandleInQsoCqContradictionAsync",
        firstCqStatus) ?? throw new InvalidOperationException("Missing repeated CQ contradiction task."));
    if (!repeatedCqHandled || PrivateLockedTarget(viewModel) == null)
        failures.Add("A repeated InQso CQ contradiction released the target instead of immediately reloading it.");
}

var finalCallAt = new DateTime(2026, 8, 16, 21, 34, 15, DateTimeKind.Utc);
var finalGuardUntil = LateReplyRecoveryPolicy.FinalReplyGuardUntil(finalCallAt, TimeSpan.FromSeconds(30));
if (finalGuardUntil != finalCallAt.AddSeconds(32))
    failures.Add("The final-call guard did not retain the target through the complete TX/RX cycle plus decode allowance.");

var recentK5hq = new RecentCallAttempt
{
    Callsign = "K5HQ",
    Band = "15m",
    Mode = "FT8",
    LastAttemptUtc = finalCallAt,
    WantedReason = "New grid EM22",
    SourceBlock = "Wanted Grids"
};
var k5Reply = new DecodeMessage
{
    ReceivedAt = finalCallAt.AddMinutes(2),
    Band = "15m",
    Mode = "FT8",
    RawText = "G1CEC K5HQ -23"
};
if (!LateReplyRecoveryPolicy.TryMatch(
        k5Reply,
        "G1CEC",
        [recentK5hq],
        finalCallAt.AddMinutes(2),
        10,
        out var matchedLateReply)
    || !ReferenceEquals(matchedLateReply, recentK5hq))
{
    failures.Add("A fresh directed reply from a station genuinely called within the recovery window was not recognised.");
}

recentK5hq.Consumed = true;
if (LateReplyRecoveryPolicy.TryMatch(k5Reply, "G1CEC", [recentK5hq], finalCallAt.AddMinutes(2), 10, out _))
    failures.Add("Late-reply recovery reused an already-consumed call attempt.");
recentK5hq.Consumed = false;

var randomInbound = new DecodeMessage
{
    ReceivedAt = finalCallAt.AddMinutes(2),
    Band = "15m",
    Mode = "FT8",
    RawText = "G1CEC W1RANDOM -10"
};
if (LateReplyRecoveryPolicy.TryMatch(randomInbound, "G1CEC", [recentK5hq], finalCallAt.AddMinutes(2), 10, out _))
    failures.Add("Late-reply recovery accepted a random station that DX Pilot had not called.");

var staleK5Reply = new DecodeMessage
{
    ReceivedAt = finalCallAt.AddMinutes(11),
    Band = "15m",
    Mode = "FT8",
    RawText = "G1CEC K5HQ -23"
};
if (LateReplyRecoveryPolicy.TryMatch(staleK5Reply, "G1CEC", [recentK5hq], finalCallAt.AddMinutes(11), 10, out _))
    failures.Add("Late-reply recovery accepted a station after its ten-minute safety window expired.");

var wrongBandReply = new DecodeMessage
{
    ReceivedAt = finalCallAt.AddMinutes(2),
    Band = "20m",
    Mode = "FT8",
    RawText = "G1CEC K5HQ -23"
};
if (LateReplyRecoveryPolicy.TryMatch(wrongBandReply, "G1CEC", [recentK5hq], finalCallAt.AddMinutes(2), 10, out _))
    failures.Add("Late-reply recovery crossed radio bands instead of requiring the original band and mode.");

var nonProgressMessage = new DecodeMessage
{
    ReceivedAt = finalCallAt.AddMinutes(2),
    Band = "15m",
    Mode = "FT8",
    RawText = "G1CEC K5HQ EM22"
};
if (LateReplyRecoveryPolicy.TryMatch(nonProgressMessage, "G1CEC", [recentK5hq], finalCallAt.AddMinutes(2), 10, out _))
    failures.Add("Late-reply recovery treated a non-progress grid message as a qualifying delayed report.");
if (LateReplyRecoveryPolicy.CanInterruptCurrentTarget(true, true, false, false)
    || LateReplyRecoveryPolicy.CanInterruptCurrentTarget(true, false, true, false)
    || !LateReplyRecoveryPolicy.CanInterruptCurrentTarget(true, false, false, false)
    || !LateReplyRecoveryPolicy.CanInterruptCurrentTarget(true, false, true, true))
{
    failures.Add("Late-reply interruption did not protect active QSOs and current New DXCC priority correctly.");
}

var selectedHistory = new SessionDxOpportunity
{
    Call = "K5HQ",
    Category = "Heard",
    Need = "Heard",
    PrimaryReason = "General DX / distance",
    SelectionCategory = "Grid",
    SelectionNeed = "New",
    SelectionReason = "New grid EM22",
    SelectionValue = "EM22"
};
if (selectedHistory.WantedReasonDisplay != "New grid EM22"
    || selectedHistory.EffectiveCategory != "Grid"
    || selectedHistory.OpportunityClass != "NewGrid")
{
    failures.Add("Session History did not retain the immutable Wanted selection reason after later general observations.");
}

var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var mainWindowXaml = File.ReadAllText(Path.Combine(sourceRoot, "MainWindow.xaml"));
var locationViewXaml = File.ReadAllText(Path.Combine(sourceRoot, "Views", "LocationView.xaml"));
var locationViewCode = File.ReadAllText(Path.Combine(sourceRoot, "Views", "LocationView.xaml.cs"));
var achievementsViewXaml = File.ReadAllText(Path.Combine(sourceRoot, "Views", "AchievementsView.xaml"));
if (!mainWindowXaml.Contains("<TabItem Header=\"Achievements\">", StringComparison.Ordinal)
    || mainWindowXaml.IndexOf("<TabItem Header=\"Settings\">", StringComparison.Ordinal)
        >= mainWindowXaml.IndexOf("<TabItem Header=\"Achievements\">", StringComparison.Ordinal)
    || mainWindowXaml.IndexOf("<TabItem Header=\"Achievements\">", StringComparison.Ordinal)
        >= mainWindowXaml.IndexOf("<TabItem Header=\"Band Analysis\">", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("Achievements.SelectedProfileKey", StringComparison.Ordinal)
    || achievementsViewXaml.Contains("SelectedAchievementProfileKey", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("RefreshAchievementsCommand", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("Header=\"Decodes seen\"", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("Header=\"Unique calls\"", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("MouseDoubleClick=\"AchievementsGrid_MouseDoubleClick\"", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("Header=\"Unconfirmed QSOs\"", StringComparison.Ordinal)
    || !achievementsViewXaml.Contains("Header=\"LoTW QSOs\"", StringComparison.Ordinal)
    || achievementsViewXaml.Contains("CALL NOW", StringComparison.Ordinal)
    || achievementsViewXaml.Contains("StartWantedSniperCommand", StringComparison.Ordinal))
{
    failures.Add("Achievements was not kept as an independently filtered, manually refreshed, read-only ADIF/history display.");
}
if (!locationViewXaml.Contains("Columns=\"{Binding Location.PanelColumnCount}\"", StringComparison.Ordinal)
    || !locationViewXaml.Contains("<Setter Property=\"Height\" Value=\"260\" />", StringComparison.Ordinal)
    || !locationViewXaml.Contains("ColumnHeaderHeight=\"28\"", StringComparison.Ordinal)
    || !locationViewXaml.Contains("ItemsSource=\"{Binding Location.Panels}\"", StringComparison.Ordinal)
    || !locationViewXaml.Contains("Text=\"{Binding Candidates.Count}\"", StringComparison.Ordinal)
    || !locationViewXaml.Contains("CurrentTargetStatus.SelectedTargetDisplay", StringComparison.Ordinal)
    || !locationViewXaml.Contains("CurrentTargetStatus.AttemptCounterLabel", StringComparison.Ordinal)
    || !locationViewXaml.Contains("CurrentTargetStatus.TxGateStatus", StringComparison.Ordinal)
    || !locationViewCode.Contains("container.BringIntoView()", StringComparison.Ordinal)
    || !locationViewCode.Contains("LocationPanelsScrollViewer.ScrollToTop()", StringComparison.Ordinal))
{
    failures.Add("Location Hunt did not retain four-column overview cards with six-row height, region counts, jump/focus navigation and live target-cycle status.");
}

var mainViewModelSource = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "MainViewModel.cs"));
var statusControlMethodStart = mainViewModelSource.IndexOf(
    "private async Task ProcessJtdxStatusForCurrentTargetAsync",
    StringComparison.Ordinal);
var statusControlNoTargetBranch = statusControlMethodStart < 0
    ? -1
    : mainViewModelSource.IndexOf("if (_lockedTarget == null)", statusControlMethodStart, StringComparison.Ordinal);
var stoppedMonitorOnlyGate = statusControlMethodStart < 0
    ? -1
    : mainViewModelSource.IndexOf("if (!_autoResume.IsRunning)", statusControlMethodStart, StringComparison.Ordinal);
if (statusControlMethodStart < 0
    || stoppedMonitorOnlyGate < statusControlMethodStart
    || statusControlNoTargetBranch < 0
    || stoppedMonitorOnlyGate > statusControlNoTargetBranch)
{
    failures.Add("Stopped monitor-only mode was not gated before normal JTDX status control can prevent CQ or click Enable TX.");
}

var inboundAdoptionMethodStart = mainViewModelSource.IndexOf(
    "private void TryAdoptInboundQso",
    StringComparison.Ordinal);
var inboundAdoptionMethodEnd = inboundAdoptionMethodStart < 0
    ? -1
    : mainViewModelSource.IndexOf("private bool MessageInvolvesCurrentTarget", inboundAdoptionMethodStart, StringComparison.Ordinal);
var stoppedInboundAdoptionGate = inboundAdoptionMethodStart < 0
    ? -1
    : mainViewModelSource.IndexOf(
        "if (!_autoResume.IsRunning || !Settings.Settings.AcceptIncomingCalls)",
        inboundAdoptionMethodStart,
        StringComparison.Ordinal);
if (inboundAdoptionMethodStart < 0
    || inboundAdoptionMethodEnd < 0
    || stoppedInboundAdoptionGate < inboundAdoptionMethodStart
    || stoppedInboundAdoptionGate > inboundAdoptionMethodEnd)
{
    failures.Add("Stopped monitor-only mode could still adopt an inbound QSO lock.");
}

var compactStripXaml = File.ReadAllText(Path.Combine(sourceRoot, "Views", "CompactConditionsStrip.xaml"));
var compactStripCode = File.ReadAllText(Path.Combine(sourceRoot, "Views", "CompactConditionsStrip.xaml.cs"));
var compactStripCount = mainWindowXaml.Split("<views:CompactConditionsStrip", StringSplitOptions.None).Length - 1;
var dashboardStart = mainWindowXaml.IndexOf("<TabItem Header=\"Live Monitor\">", StringComparison.Ordinal);
var dxAssistStart = mainWindowXaml.IndexOf("<TabItem Header=\"DX Assist\">", StringComparison.Ordinal);
var dashboardTabXaml = dashboardStart >= 0 && dxAssistStart > dashboardStart
    ? mainWindowXaml[dashboardStart..dxAssistStart]
    : "";
if (compactStripCount != 7
    || dashboardTabXaml.Contains("CompactConditionsStrip", StringComparison.Ordinal)
    || !compactStripXaml.Contains("BandAnalysis.ConditionsIndicators", StringComparison.Ordinal)
    || !compactStripXaml.Contains("{Binding RemainingPercent, Mode=OneWay}", StringComparison.Ordinal)
    || !compactStripXaml.Contains("{Binding State}", StringComparison.Ordinal)
    || compactStripCode.Contains("DispatcherTimer", StringComparison.Ordinal))
{
    failures.Add("The compact global countdown strip was not shared across all seven non-Live-Monitor workspaces using the existing live Band Analysis indicators.");
}
if (!mainWindowXaml.Contains("AppNavigationTabControlStyle", StringComparison.Ordinal)
    || !mainWindowXaml.Contains("CurrentTargetStatus.SelectedTargetDisplay", StringComparison.Ordinal)
    || !mainWindowXaml.Contains("CurrentTargetStatus.TxGateStatus", StringComparison.Ordinal))
{
    failures.Add("The presentation-only application shell did not retain persistent target/TX state or the designed workspace navigation.");
}
var resolver = new DxccResolver(Path.Combine(sourceRoot, "Data", "cty.csv"));
var kg4ExpectedEntities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["KG4A"] = "United States",
    ["KG4ABC"] = "United States",
    ["KG4ZZZ"] = "United States",
    ["KG4AC"] = "Guantanamo Bay",
    ["KG4AA"] = "Guantanamo Bay",
    ["KG44WW"] = "Guantanamo Bay"
};
foreach (var expectation in kg4ExpectedEntities)
{
    var resolved = resolver.Resolve(expectation.Key);
    if (resolved == null || !resolved.Name.Equals(expectation.Value, StringComparison.OrdinalIgnoreCase))
    {
        failures.Add(
            $"{expectation.Key} resolved as {resolved?.Name ?? "Unknown"} instead of {expectation.Value}.");
    }
}

var rarity = new DxccRarityService();
rarity.Load("", resolver);
var scorer = new DxTargetScorer(resolver, rarity, new GridDistanceCalculator());

var stationCallsignAdif = Path.GetTempFileName();
try
{
    File.WriteAllText(stationCallsignAdif,
        "<CALL:5>VP8NO <BAND:3>20m <MODE:3>FT8 <DXCC:3>141 <COUNTRY:16>Falkland Islands <STATION_CALLSIGN:6>2E0CCD <OPERATOR:5>G1CEC <QSO_DATE:8>20200101 <TIME_ON:6>120000 <LOTW_QSL_RCVD:1>Y <EOR>");
    var parsedStationQso = new AdifLogbookReader().Load(stationCallsignAdif).SingleOrDefault();
    if (parsedStationQso == null
        || parsedStationQso.StationCallsign != "2E0CCD"
        || parsedStationQso.OperatorCallsign != "G1CEC")
    {
        failures.Add("ADIF STATION_CALLSIGN/OPERATOR identity fields were not preserved.");
    }
}
finally
{
    File.Delete(stationCallsignAdif);
}

var identityQsos = new List<AdifQso>
{
    new() { Call = "VP8NO", Band = "20m", Mode = "FT8", Dxcc = "141", Country = "Falkland Islands", StationCallsign = "2E0CCD", LotwConfirmed = true },
    new() { Call = "VP8NO", Band = "20m", Mode = "FT8", Dxcc = "141", Country = "Falkland Islands", StationCallsign = "2E0CCD/NHS", LotwConfirmed = true },
    new() { Call = "JA1AAA", Band = "20m", Mode = "FT8", Dxcc = "339", Country = "Japan", StationCallsign = "G1CEC", LotwConfirmed = true },
    new() { Call = "JA1AAB", Band = "17m", Mode = "FT8", Dxcc = "339", Country = "Japan", StationCallsign = "SV5/G1CEC", LotwConfirmed = true },
    new() { Call = "K1AAA", Band = "20m", Mode = "FT8", Dxcc = "291", Country = "United States", StationCallsign = "", LotwConfirmed = true }
};
var identitySettings = new AppSettings
{
    MyCallsign = "G1CEC",
    AchievementCallsignProfile = "G1CEC",
    DxccConfirmationMode = "LoTWOnly",
    GridConfirmationMode = "LoTWOnly",
    StateConfirmationMode = "LoTWOnly",
    IotaConfirmationMode = "LoTWOnly"
};
var identityResult = new AdifWorkedStatusBuilder().Build(identityQsos, [], identitySettings);
var g1cecProfile = identityResult.CallsignProfiles.SingleOrDefault(profile => profile.Key == "G1CEC");
var oldCallProfile = identityResult.CallsignProfiles.SingleOrDefault(profile => profile.Key == "2E0CCD");
if (g1cecProfile?.QsoCount != 2
    || oldCallProfile?.QsoCount != 2
    || !g1cecProfile.Variants.Contains("SV5/G1CEC")
    || !oldCallProfile.Variants.Contains("2E0CCD/NHS")
    || identityResult.UnassignedStationCallsignCount != 1
    || identityResult.Indexes.Dxcc.ContainsKey("141")
    || !identityResult.OverallIndexes.Dxcc.TryGetValue("141", out var overallFalklands)
    || !overallFalklands.LoTWConfirmedAny)
{
    failures.Add("Station callsign profiles did not group variants or separate active and overall award credit correctly.");
}

var newToG1cecTarget = scorer.Score(
    new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "VP8NO",
        ContactableCall = "VP8NO",
        Grid = "GD18",
        RawText = "CQ VP8NO GD18",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    },
    identityResult.ActiveQsos,
    identityResult.Indexes,
    [],
    identitySettings);
if (!newToG1cecTarget.Ranking.IsNewToCallsign
    || newToG1cecTarget.Ranking.PriorityTier != 11
    || !newToG1cecTarget.Ranking.PrimaryWantedReason.Contains("New DXCC for G1CEC", StringComparison.Ordinal))
{
    failures.Add("A DXCC confirmed under an old callsign was not classified as New DXCC for the selected current callsign.");
}

var unconfirmedDxccIndexes = new WorkedStatusIndexes();
unconfirmedDxccIndexes.Dxcc["321"] = new DxccWorkedStatus
{
    DxccNumber = "321",
    EntityName = "Hong Kong",
    WorkedAny = true,
    ConfirmedAny = false,
    LoTWConfirmedAny = false
};
var unconfirmedDxccTarget = scorer.Score(
    new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "VR2TEST",
        ContactableCall = "VR2TEST",
        Dxcc = "321",
        EntityName = "Hong Kong",
        Grid = "OL72",
        RawText = "CQ VR2TEST OL72",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    },
    [],
    unconfirmedDxccIndexes,
    [],
    new AppSettings());
if (unconfirmedDxccTarget.Ranking.DxccStatus != DxccCandidateStatus.WorkedUnconfirmed
    || unconfirmedDxccTarget.Ranking.PriorityTier != 10
    || unconfirmedDxccTarget.Ranking.NeedStatus != NeedStatus.WorkedNotLoTWConfirmed)
{
    failures.Add("A worked-but-unconfirmed DXCC did not receive the same absolute-priority tier as a never-worked DXCC.");
}

var hawaiiIndexes = new WorkedStatusIndexes();
hawaiiIndexes.Dxcc["110"] = new DxccWorkedStatus
{
    DxccNumber = "110",
    EntityName = "Hawaii",
    WorkedAny = true,
    ConfirmedAny = true,
    LoTWConfirmedAny = true
};
var hawaiiStateTarget = scorer.Score(
    new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "KH6ABC",
        ContactableCall = "KH6ABC",
        RawText = "CQ KH6ABC BL11",
        State = "HI",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    },
    [],
    hawaiiIndexes,
    [],
    new AppSettings { EnableWantedStates = true, PrioritizeNewUsStates = true });
if (!hawaiiStateTarget.Decode.IsNewState
    || hawaiiStateTarget.Ranking.PriorityTier != 40
    || !hawaiiStateTarget.Ranking.PrimaryWantedReason.Contains("HI", StringComparison.OrdinalIgnoreCase))
{
    failures.Add("A worked Hawaii DXCC decode was not ranked as a new HI WAS state.");
}

var foreignIndexes = new WorkedStatusIndexes();
foreignIndexes.Dxcc["263"] = new DxccWorkedStatus
{
    DxccNumber = "263",
    EntityName = "Netherlands",
    WorkedAny = true,
    ConfirmedAny = true,
    LoTWConfirmedAny = true
};
var foreignStateTarget = scorer.Score(
    new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "PD0TEST",
        ContactableCall = "PD0TEST",
        RawText = "CQ PD0TEST JO22",
        State = "UT",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    },
    [],
    foreignIndexes,
    [],
    new AppSettings { EnableWantedStates = true, PrioritizeNewUsStates = true });
if (!string.IsNullOrWhiteSpace(foreignStateTarget.Decode.State)
    || foreignStateTarget.Ranking.PriorityTier is 40 or 41 or 42 or 43 or 44)
{
    failures.Add("A foreign UT province survived decode enrichment or received a WAS state rank.");
}

var scopedIndexes = new WorkedStatusIndexes();
var workedUsa = new DxccWorkedStatus
{
    DxccNumber = "291",
    EntityName = "United States",
    WorkedAny = true,
    ConfirmedAny = true,
    LoTWConfirmedAny = true
};
workedUsa.WorkedBands.Add("20m");
workedUsa.LoTWConfirmedBands.Add("20m");
workedUsa.WorkedModes.Add("FT8");
workedUsa.LoTWConfirmedModes.Add("FT8");
workedUsa.WorkedBandModes.Add("20M|FT8");
workedUsa.LoTWConfirmedBandModes.Add("20M|FT8");
scopedIndexes.Dxcc["291"] = workedUsa;

var bandSettings = new AppSettings { IncludeBandWanted = true, EnableWantedDxcc = true };
var bandTarget = scorer.Score(Candidate("15m", "FT8"), [], scopedIndexes, [], bandSettings);
if (bandTarget.Ranking.PriorityTier != 12
    || bandTarget.Ranking.WantedScope != WantedScope.CurrentBand
    || !bandTarget.Ranking.PrimaryWantedReason.Contains("15m", StringComparison.OrdinalIgnoreCase))
{
    failures.Add($"Optional band-new DXCC classification failed: tier {bandTarget.Ranking.PriorityTier}, scope {bandTarget.Ranking.WantedScope}, reason '{bandTarget.Ranking.PrimaryWantedReason}'.");
}

var noScopedTarget = scorer.Score(Candidate("15m", "FT8"), [], scopedIndexes, [], new AppSettings { EnableWantedDxcc = true });
if (noScopedTarget.Ranking.PriorityTier is 12 or 13 or 14)
    failures.Add("A scoped DXCC tier was assigned while all optional scoped awards were disabled.");

var modeTarget = scorer.Score(Candidate("20m", "FT4"), [], scopedIndexes, [], new AppSettings { IncludeModeWanted = true, EnableWantedDxcc = true });
if (modeTarget.Ranking.PriorityTier != 13 || modeTarget.Ranking.WantedScope != WantedScope.CurrentMode)
    failures.Add("Optional mode-new DXCC classification failed.");

workedUsa.WorkedBands.Add("15m");
workedUsa.LoTWConfirmedBands.Add("15m");
workedUsa.WorkedModes.Add("FT4");
workedUsa.LoTWConfirmedModes.Add("FT4");
var bandModeTarget = scorer.Score(Candidate("15m", "FT4"), [], scopedIndexes, [], new AppSettings { IncludeBandModeWanted = true, EnableWantedDxcc = true });
if (bandModeTarget.Ranking.PriorityTier != 14 || bandModeTarget.Ranking.WantedScope != WantedScope.CurrentBandMode)
    failures.Add("Optional band-and-mode-new DXCC classification failed.");

if (new AppSettings().PrioritizeNewGridsInDxAssist)
    failures.Add("DX Assist new-grid priority must default to off.");

var franceEntity = resolver.Resolve("F4GRID");
var brazilEntity = resolver.Resolve("PY2DX");
if (franceEntity == null || brazilEntity == null)
{
    failures.Add("DX Assist grid-priority test calls did not resolve to France and Brazil.");
}
else
{
    var dxRankingIndexes = new WorkedStatusIndexes();
    dxRankingIndexes.Dxcc[franceEntity.Code] = new DxccWorkedStatus
    {
        DxccNumber = franceEntity.Code,
        EntityName = franceEntity.Name,
        WorkedAny = true,
        ConfirmedAny = true,
        LoTWConfirmedAny = true
    };
    dxRankingIndexes.Dxcc[brazilEntity.Code] = new DxccWorkedStatus
    {
        DxccNumber = brazilEntity.Code,
        EntityName = brazilEntity.Name,
        WorkedAny = true,
        ConfirmedAny = true,
        LoTWConfirmedAny = true
    };
    dxRankingIndexes.Grids["GG66"] = new SimpleWorkedStatus
    {
        Id = "GG66",
        WorkedAny = true,
        ConfirmedAny = true,
        LoTWConfirmedAny = true
    };

    var nearbyNewGrid = new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "F4GRID",
        ContactableCall = "F4GRID",
        HeardCall = "F4GRID",
        Grid = "JN18",
        DistanceKm = 500,
        RawText = "CQ F4GRID JN18",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    };
    var distantWorkedGrid = new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "PY2DX",
        ContactableCall = "PY2DX",
        HeardCall = "PY2DX",
        Grid = "GG66",
        DistanceKm = 9000,
        RawText = "CQ PY2DX GG66",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    };
    var dxRankingSettings = new AppSettings
    {
        PrioritizeNewUsStates = false,
        PrioritizeUnconfirmedUsStates = false
    };
    var selector = new TargetSelector(scorer);
    var normalDxRanking = selector.SelectRanked(
        [nearbyNewGrid, distantWorkedGrid], [], dxRankingIndexes, dxRankingSettings, 10, includeActiveQso: false);
    if (normalDxRanking.FirstOrDefault()?.Callsign != "PY2DX"
        || normalDxRanking.First(target => target.Callsign == "F4GRID").Ranking.PriorityTier != 80)
    {
        failures.Add("DX Assist let a nearby new grid outrank the more distant normal-DX candidate while new-grid priority was off.");
    }

    dxRankingSettings.PrioritizeNewGridsInDxAssist = true;
    var gridFirstRanking = selector.SelectRanked(
        [nearbyNewGrid, distantWorkedGrid], [], dxRankingIndexes, dxRankingSettings, 10, includeActiveQso: false);
    if (gridFirstRanking.FirstOrDefault()?.Callsign != "F4GRID"
        || gridFirstRanking[0].Ranking.PriorityTier != 30
        || gridFirstRanking.First(target => target.Callsign == "PY2DX").Ranking.PriorityTier != 80)
    {
        failures.Add("DX Assist did not promote only the globally new grid when new-grid priority was enabled.");
    }

    var gridPriorityFallback = selector.SelectRanked(
        [distantWorkedGrid], [], dxRankingIndexes, dxRankingSettings, 10, includeActiveQso: false);
    if (gridPriorityFallback.FirstOrDefault()?.Callsign != "PY2DX"
        || gridPriorityFallback[0].Ranking.PriorityTier != 80)
    {
        failures.Add("DX Assist did not fall back to normal DX ranking when grid priority was enabled but no new grid was available.");
    }

    var newDxccIndexes = new WorkedStatusIndexes();
    newDxccIndexes.Dxcc[brazilEntity.Code] = dxRankingIndexes.Dxcc[brazilEntity.Code];
    var newDxccStillWins = selector.SelectRanked(
        [nearbyNewGrid, distantWorkedGrid], [], newDxccIndexes, dxRankingSettings, 10, includeActiveQso: false);
    if (newDxccStillWins.FirstOrDefault()?.Callsign != "F4GRID"
        || newDxccStillWins[0].Ranking.PriorityTier != 10)
    {
        failures.Add("A new DXCC did not remain above optional new-grid priority.");
    }
}

var candidateOpportunityClass = typeof(MainViewModel).GetMethod(
    "CandidateOpportunityClass",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing DX Assist opportunity-colour classifier.");
string CandidateColour(DxccCandidateStatus dxccStatus, int tier, string gridStatus, string stateStatus) =>
    (string)(candidateOpportunityClass.Invoke(
        null,
        [new CandidateRanking { DxccStatus = dxccStatus, PriorityTier = tier }, gridStatus, stateStatus]) ?? "");
if (CandidateColour(DxccCandidateStatus.NotWorked, 10, "New", "New") != "NewDxcc"
    || CandidateColour(DxccCandidateStatus.WorkedUnconfirmed, 10, "New", "New") != "UnconfirmedDxcc"
    || CandidateColour(DxccCandidateStatus.Confirmed, 80, "New", "Not USA") != "NewGrid"
    || CandidateColour(DxccCandidateStatus.Confirmed, 80, "Worked", "New") != "NewState"
    || CandidateColour(DxccCandidateStatus.Confirmed, 20, "Worked", "Worked") != "RareDxcc")
{
    failures.Add("DX Assist colour classification did not follow opportunity status independently of ranking tier.");
}

var confirmationDefaults = new AppSettings();
if (confirmationDefaults.DxccConfirmationMode != "LoTWOnly"
    || confirmationDefaults.GridConfirmationMode != "LoTWOnly"
    || confirmationDefaults.StateConfirmationMode != "LoTWOnly"
    || confirmationDefaults.IotaConfirmationMode != "LoTWOnly")
{
    failures.Add("New installations did not default every confirmation category to LoTW only.");
}

var locationLayout = new LocationViewModel();
var usaLocationPanel = new LocationPanelViewModel("USA", "USA");
var europeLocationPanel = new LocationPanelViewModel("EU", "Europe");
locationLayout.Panels.Add(usaLocationPanel);
locationLayout.Panels.Add(europeLocationPanel);
locationLayout.TogglePanelFocusCommand.Execute(usaLocationPanel);
if (locationLayout.PanelColumnCount != 1
    || !usaLocationPanel.IsFocused
    || !usaLocationPanel.IsVisible
    || europeLocationPanel.IsVisible
    || locationLayout.VisiblePanels.Count != 1
    || !ReferenceEquals(locationLayout.VisiblePanels[0], usaLocationPanel))
{
    failures.Add("Location overview did not enter single-region focus mode correctly.");
}
locationLayout.TogglePanelFocusCommand.Execute(usaLocationPanel);
if (locationLayout.PanelColumnCount != 4
    || usaLocationPanel.IsFocused
    || !usaLocationPanel.IsVisible
    || !europeLocationPanel.IsVisible
    || locationLayout.VisiblePanels.Count != 2)
{
    failures.Add("Location focus mode did not restore the four-column all-region overview.");
}

var allLocationDefinitions = typeof(MainViewModel).GetMethod(
    "AllLocationPanelDefinitions",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing complete Location monitor definitions.");
var allLocationRegions = (System.Collections.IEnumerable)(allLocationDefinitions.Invoke(null, null)
    ?? throw new InvalidOperationException("Complete Location monitor definitions returned null."));
if (allLocationRegions.Cast<object>().Count() != 9)
    failures.Add("Location monitoring did not retain all nine geographical overview cards independently of hunt-area selection.");

var sessionOrdering = new SessionHistoryViewModel();
sessionOrdering.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "GRID1",
    Category = "Grid",
    DxccStatus = "Confirmed",
    UniversalRank = 1,
    PriorityTier = 30,
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Seen only"
});
sessionOrdering.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "NEW2",
    Category = "DXCC",
    DxccStatus = "New DXCC",
    UniversalRank = 2,
    PriorityTier = 10,
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Seen only"
});
sessionOrdering.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "UNCONF5",
    Category = "DXCC",
    DxccStatus = "Worked unconfirmed",
    UniversalRank = 5,
    PriorityTier = 10,
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Seen only"
});
sessionOrdering.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "GRID3",
    Category = "Grid",
    DxccStatus = "Confirmed",
    UniversalRank = 3,
    PriorityTier = 30,
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Seen only"
});
sessionOrdering.Refresh();
var orderedSessionCalls = sessionOrdering.Opportunities.Select(item => item.Call).ToArray();
if (!orderedSessionCalls.SequenceEqual(new[] { "NEW2", "UNCONF5", "GRID1", "GRID3" }))
    failures.Add($"Session History did not keep new/unconfirmed DXCC first and then follow universal rank: {string.Join(",", orderedSessionCalls)}.");

var sessionCategoryFilters = new SessionHistoryViewModel();
sessionCategoryFilters.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "DXCCCALLED",
    Category = "DXCC",
    DxccStatus = "Worked unconfirmed",
    WasCalled = true,
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Called"
});
sessionCategoryFilters.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "GRIDUNCONF",
    Category = "Grid",
    DxccStatus = "Confirmed",
    GridNeed = "Unconfirmed",
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Seen only"
});
sessionCategoryFilters.ShowDxcc = false;
sessionCategoryFilters.ShowRareConfirmed = false;
sessionCategoryFilters.ShowUsaStates = false;
sessionCategoryFilters.ShowHeard = false;
sessionCategoryFilters.Refresh();
if (sessionCategoryFilters.Opportunities.Count != 1
    || sessionCategoryFilters.Opportunities[0].Call != "GRIDUNCONF")
{
    failures.Add("Session History category filters were not strict, or an enabled unconfirmed-grid category was incorrectly hidden with DXCC disabled.");
}
sessionCategoryFilters.ShowGrids = false;
if (sessionCategoryFilters.Opportunities.Count != 0)
    failures.Add("Session History continued to show an unconfirmed grid after its Grids category was disabled.");

var evaluateSimpleNeed = typeof(MainViewModel).GetMethod(
    "EvaluateSimpleNeed",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing simple worked-status evaluator.");
var workedOnlyGrid = new SimpleWorkedStatus
{
    Id = "EN74",
    WorkedAny = true,
    ConfirmedAny = true,
    LoTWConfirmedAny = false
};
var workedOnlyGridNeed = (NeedStatus)(evaluateSimpleNeed.Invoke(
    null,
    [workedOnlyGrid, "40m", "FT8", WantedScope.Overall]) ?? NeedStatus.Unknown);
if (workedOnlyGridNeed != NeedStatus.LoTWConfirmed
    || TargetReasonFormatter.FormatGrid(workedOnlyGrid, "EN74") != TargetReasonFormatter.Unavailable)
{
    failures.Add("Worked-only grid confirmation was incorrectly treated as an unconfirmed/LoTW-only grid.");
}

var deferredSessionView = new SessionHistoryViewModel();
deferredSessionView.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "HIDDEN1",
    Category = "Heard",
    DxccStatus = "Confirmed",
    LastSeenUtc = DateTime.UtcNow,
    Outcome = "Seen only"
});
deferredSessionView.RequestRefresh();
deferredSessionView.RefreshIfDue(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(5));
if (deferredSessionView.Opportunities.Count != 0)
    failures.Add("Session History rebuilt its visible rows while its tab was inactive.");
deferredSessionView.SetViewActive(true);
deferredSessionView.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "VISIBLE2",
    Category = "Heard",
    DxccStatus = "Confirmed",
    LastSeenUtc = DateTime.UtcNow.AddSeconds(1),
    Outcome = "Seen only"
});
deferredSessionView.RequestRefresh();
deferredSessionView.RefreshIfDue(DateTime.UtcNow, TimeSpan.FromSeconds(5));
var remainedBatched = deferredSessionView.Opportunities.Count == 1;
deferredSessionView.RefreshIfDue(DateTime.UtcNow.AddSeconds(6), TimeSpan.FromSeconds(5));
if (!remainedBatched || deferredSessionView.Opportunities.Count != 2)
    failures.Add("Visible Session History updates were not batched while retaining a prompt refresh interval.");
deferredSessionView.SetViewActive(false);
deferredSessionView.AllOpportunities.Add(new SessionDxOpportunity
{
    Call = "RETURN3",
    Category = "Heard",
    DxccStatus = "Confirmed",
    LastSeenUtc = DateTime.UtcNow.AddSeconds(2),
    Outcome = "Seen only"
});
deferredSessionView.RequestRefresh();
deferredSessionView.RefreshIfDue(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(5));
deferredSessionView.SetViewActive(true);
if (deferredSessionView.Opportunities.Count != 3)
    failures.Add("Session History did not catch up immediately when its tab became active.");

var archiveView = new SessionHistoryViewModel();
var archiveEntries = new[]
{
    new SessionDxOpportunity
    {
        SessionId = "session-a",
        SessionStartedUtc = DateTime.UtcNow.AddHours(-2),
        OpportunityId = "13:DP0GVN:20M:FT8",
        FirstSeenUtc = DateTime.UtcNow.AddHours(-2),
        LastSeenUtc = DateTime.UtcNow.AddHours(-2).AddMinutes(3),
        Call = "DP0GVN",
        Entity = "Antarctica",
        DxccNumber = "13",
        DxccStatus = "Confirmed",
        Category = "Heard",
        Grid = "IB59",
        Band = "20m",
        Mode = "FT8",
        SeenCount = 4,
        AttemptCount = 2,
        WasCalled = true,
        Outcome = "Called",
        RawMessages = ["CQ DP0GVN IB59"],
        Timeline = ["20:00:00 Heard: CQ DP0GVN IB59"]
    },
    new SessionDxOpportunity
    {
        SessionId = "session-b",
        SessionStartedUtc = DateTime.UtcNow,
        OpportunityId = "339:JA6LCJ:15M:FT8",
        FirstSeenUtc = DateTime.UtcNow,
        LastSeenUtc = DateTime.UtcNow,
        Call = "JA6LCJ",
        Entity = "Japan",
        DxccNumber = "339",
        DxccStatus = "Confirmed",
        Category = "Grid",
        GridNeed = "New",
        Grid = "PM52",
        SeenCount = 1,
        Outcome = "Seen only"
    }
};
archiveView.LoadArchive(archiveEntries);
archiveView.ToggleArchiveCommand.Execute(null);
archiveView.SearchText = "Antarctica";
if (archiveView.Opportunities.Count != 1
    || archiveView.Opportunities[0].Call != "DP0GVN"
    || archiveView.Opportunities[0].SeenCount != 4
    || archiveView.Opportunities[0].AttemptCount != 2)
{
    failures.Add("Full Archive search did not retain/search an ordinary heard-and-called station with its seen and attempt counts.");
}
if (archiveEntries[1].OpportunityClass != "NewGrid")
    failures.Add("Session History did not apply the usual new-grid colour class independently of DX Assist priority settings.");

var archiveTestFolder = Path.Combine(Path.GetTempPath(), $"DXPilot-session-archive-test-{Guid.NewGuid():N}");
try
{
    var archiveStore = new SessionHistoryArchiveStore(archiveTestFolder);
    if (!archiveStore.Save(archiveEntries, out var archiveSaveError))
        failures.Add($"Full Archive could not be saved: {archiveSaveError}");
    else
    {
        var restoredArchive = archiveStore.Load(out var archiveLoadWarning);
        var restoredAntarctica = restoredArchive.SingleOrDefault(item => item.Call == "DP0GVN");
        if (!string.IsNullOrWhiteSpace(archiveLoadWarning)
            || restoredArchive.Count != 2
            || restoredAntarctica == null
            || restoredAntarctica.SeenCount != 4
            || restoredAntarctica.AttemptCount != 2
            || restoredAntarctica.RawMessages.SingleOrDefault() != "CQ DP0GVN IB59"
            || restoredAntarctica.Timeline.Count != 1)
        {
            failures.Add($"Full Archive JSON round-trip lost station history data: {archiveLoadWarning}");
        }
    }
}
finally
{
    if (Directory.Exists(archiveTestFolder))
        Directory.Delete(archiveTestFolder, recursive: true);
}

var timingConfirmation = new DateTime(2026, 8, 9, 12, 0, 7, DateTimeKind.Utc);
var firstFt8Slot = BandSurveyTiming.FirstEligibleSlotStart(timingConfirmation, TimeSpan.FromSeconds(15)).ToUniversalTime();
var silentFt8Fallback = BandSurveyTiming.SilentBandFallbackAt(timingConfirmation, TimeSpan.FromSeconds(15)).ToUniversalTime();
var firstFt4Slot = BandSurveyTiming.FirstEligibleSlotStart(timingConfirmation, TimeSpan.FromSeconds(7.5)).ToUniversalTime();
if (firstFt8Slot != new DateTime(2026, 8, 9, 12, 0, 15, DateTimeKind.Utc)
    || silentFt8Fallback != new DateTime(2026, 8, 9, 12, 0, 33, DateTimeKind.Utc)
    || firstFt4Slot != new DateTime(2026, 8, 9, 12, 0, 7, 500, DateTimeKind.Utc))
{
    failures.Add("Band Analysis did not align its observation window to the next complete FT8/FT4 receive slot.");
}

var oneMinutePassive = PskPropagationProbeTiming.PassiveListenDuration(1, TimeSpan.FromSeconds(15));
var fiveMinutePassive = PskPropagationProbeTiming.PassiveListenDuration(5, TimeSpan.FromSeconds(15));
var estimatedOccupancy = PskPropagationProbeTiming.EstimatedBandOccupancy(1, TimeSpan.FromSeconds(15));
if (oneMinutePassive != TimeSpan.FromSeconds(30)
    || fiveMinutePassive != TimeSpan.FromSeconds(270)
    || estimatedOccupancy != TimeSpan.FromSeconds(60)
    || PskPropagationProbeTiming.CqPeriodsPerBand != 2)
{
    failures.Add("PSK propagation timing did not preserve two fixed CQ periods or the configured passive-listening window.");
}

if (PskTxTransitionPolicy.OffConfirmationTimeout(TimeSpan.FromSeconds(4)) != TimeSpan.FromSeconds(18)
    || PskTxTransitionPolicy.OffConfirmationTimeout(TimeSpan.FromSeconds(25)) != TimeSpan.FromSeconds(25))
{
    failures.Add("PSK transition cleanup did not retain a full FT8-cycle acknowledgement window.");
}

if (!PskTxTransitionPolicy.CanSafelyRetryArm(
        startedFreshlyOff: true,
        latestReportsActive: false,
        freshOffReportedAfterClick: true,
        greyPercent: 70,
        activePercent: 0,
        configuredMinimumGreyPercent: 60,
        configuredMaximumActivePercent: 20)
    || !PskTxTransitionPolicy.CanSafelyRetryArm(
        startedFreshlyOff: true,
        latestReportsActive: false,
        freshOffReportedAfterClick: false,
        greyPercent: 90,
        activePercent: 2,
        configuredMinimumGreyPercent: 60,
        configuredMaximumActivePercent: 20)
    || PskTxTransitionPolicy.CanSafelyRetryArm(
        startedFreshlyOff: true,
        latestReportsActive: true,
        freshOffReportedAfterClick: true,
        greyPercent: 95,
        activePercent: 0,
        configuredMinimumGreyPercent: 60,
        configuredMaximumActivePercent: 20)
    || PskTxTransitionPolicy.CanSafelyRetryArm(
        startedFreshlyOff: true,
        latestReportsActive: false,
        freshOffReportedAfterClick: false,
        greyPercent: 70,
        activePercent: 15,
        configuredMinimumGreyPercent: 60,
        configuredMaximumActivePercent: 20))
{
    failures.Add("PSK Enable TX retry policy did not distinguish confirmed-off, strongly-grey and ambiguous/active toggle states.");
}

var firstProbeSlot = PskPropagationProbeTiming.SlotNumber(
    new DateTime(2026, 8, 9, 12, 0, 15, DateTimeKind.Utc),
    TimeSpan.FromSeconds(15));
if (!PskPropagationProbeTiming.AreImmediatelyConsecutive(firstProbeSlot, firstProbeSlot + 1)
    || PskPropagationProbeTiming.AreImmediatelyConsecutive(firstProbeSlot, firstProbeSlot + 2))
{
    failures.Add("PSK propagation timing accepted a same-parity CQ 30 seconds later as an immediately consecutive FT8 transmission.");
}

const string pskLiveJson = "{\"sq\":71550000001,\"f\":7076106,\"md\":\"FT8\",\"rp\":-10,\"t\":1786281870,\"t_tx\":1786281855,\"sc\":\"G1CEC\",\"sl\":\"IO83up\",\"rc\":\"K1ABC\",\"rl\":\"FN42aa\",\"sa\":223,\"ra\":291,\"b\":\"40m\"}";
const string pskQueryXml = "<receptionReports><receptionReport receiverCallsign=\"DL1ABC\" receiverLocator=\"JO31aa\" senderCallsign=\"G1CEC\" senderLocator=\"IO83up\" frequency=\"7076108\" flowStartSeconds=\"1786281870\" mode=\"FT8\" receiverDXCC=\"Fed. Rep. of Germany\" receiverDXCCCode=\"DL\" sNR=\"3\" /></receptionReports>";
var parsedLive = PskReporterParser.TryParseLiveJson(pskLiveJson, out var liveSpot);
var queriedSpots = PskReporterParser.ParseQueryXml(pskQueryXml);
var probeWindow = new PskProbeWindow(
    "40m",
    DateTimeOffset.FromUnixTimeSeconds(1786281855).UtcDateTime,
    DateTimeOffset.FromUnixTimeSeconds(1786281870).UtcDateTime);
if (!parsedLive
    || liveSpot.ReceiverCallsign != "K1ABC"
    || liveSpot.SignalReportDb != -10
    || queriedSpots.Count != 1
    || queriedSpots[0].SignalReportDb != 3
    || !probeWindow.Matches(liveSpot, TimeSpan.FromSeconds(6))
    || !probeWindow.Matches(queriedSpots[0], TimeSpan.FromSeconds(6)))
{
    failures.Add("PSK Reporter live JSON/query XML parsing or exact CQ-window matching failed.");
}
var pskAnalyzer = new PskReporterAnalyzer(new GridDistanceCalculator(), resolver);
var pskMetrics = pskAnalyzer.Analyze("40m", "IO83up", [liveSpot, queriedSpots[0]], measured: true);
if (!pskMetrics.Measured
    || pskMetrics.UniqueReceivers != 2
    || pskMetrics.FarthestDistanceMiles < 2_500
    || pskMetrics.StrongestSnr != 3
    || pskMetrics.PropagationScore <= 0)
{
    failures.Add("PSK Reporter metrics did not calculate receiver count, outward distance, SNR and propagation score.");
}

var workabilityAnalyzer = new BandWorkabilityAnalyzer();
var weakOutwardMetrics = new PskReporterMetrics
{
    Measured = true, UniqueReceivers = 10, PropagationScore = 23, FarthestDistanceMiles = 618
};
var strongOutwardMetrics = new PskReporterMetrics
{
    Measured = true, UniqueReceivers = 8, PropagationScore = 78, FarthestDistanceMiles = 4_200
};
var weakWantedDecodes = new[] { "FN31", "EM10", "EN50", "FM18", "DM79", "EL29", "CN87" }
    .Select((grid, index) => new DecodeMessage
    {
        Band = "30m", Callsign = $"K{index + 1}DX", ContactableCall = $"K{index + 1}DX",
        TransmittedGrid = grid, IsNewGrid = true, Snr = -12, DistanceKm = 5_000 + index * 100
    })
    .ToList();
var strongWantedDecodes = new[] { "FN31", "EM10", "EN50" }
    .Select((grid, index) => new DecodeMessage
    {
        Band = "20m", Callsign = $"W{index + 1}DX", ContactableCall = $"W{index + 1}DX",
        TransmittedGrid = grid, IsNewGrid = true, Snr = -12, DistanceKm = 5_000 + index * 100
    })
    .ToList();
var europeanPskReports = new[] { "JO31", "JN58", "JN18", "JO65", "JN45", "JO21", "JN88", "JO40", "JN06", "JO90" }
    .Select((grid, index) => new PskReporterSpot { Band = "30m", Mode = "FT8", ReceiverCallsign = $"DL{index}RX", ReceiverLocator = grid })
    .ToList();
var northAmericanPskReports = new[] { "FN31", "EM10", "EN50", "FM18", "DM79", "EL29", "CN87", "FN42" }
    .Select((grid, index) => new PskReporterSpot { Band = "20m", Mode = "FT8", ReceiverCallsign = $"K{index}RX", ReceiverLocator = grid })
    .ToList();
var weakWorkability = workabilityAnalyzer.Analyze(
    "30m", "IO83up", weakWantedDecodes, europeanPskReports,
    new BandQualitySnapshot { Band = "30m", WantedStations = 7, ActivityScore = 86, DxReachScore = 37 },
    weakOutwardMetrics, HuntingOperatingMode.WantedSniper, new BandPerformanceEvidence(8, 1, 0));
var strongWorkability = workabilityAnalyzer.Analyze(
    "20m", "IO83up", strongWantedDecodes, northAmericanPskReports,
    new BandQualitySnapshot { Band = "20m", WantedStations = 3, ActivityScore = 70, DxReachScore = 60 },
    strongOutwardMetrics, HuntingOperatingMode.WantedSniper, new BandPerformanceEvidence(6, 3, 1));
var absoluteNewDxccWorkability = workabilityAnalyzer.Analyze(
    "17m", "IO83up", [], [],
    new BandQualitySnapshot { Band = "17m", NewDxccStations = 1 },
    new PskReporterMetrics { Measured = true, UniqueReceivers = 0, PropagationScore = 0 },
    HuntingOperatingMode.WantedSniper, new BandPerformanceEvidence());
if (weakWorkability.PskViabilityPercent >= 50
    || weakWorkability.PathMatchPercent >= strongWorkability.PathMatchPercent
    || weakWorkability.Score >= strongWorkability.Score
    || weakWorkability.DistinctOpportunities != 7
    || strongWorkability.WorkableOpportunities != 3
    || absoluteNewDxccWorkability.Score < 10_000)
{
    failures.Add("Band workability did not gate busy/wanted receive results by outward PSK strength and matching geography while preserving absolute New DXCC priority.");
}

if (!PskBandRetryPolicy.CanRetryIncompleteBand(automatic: true, retryAlreadyUsed: false, verifiedCqTransmissions: 0, transmissionDefinitelyAbsent: true)
    || PskBandRetryPolicy.CanRetryIncompleteBand(automatic: false, retryAlreadyUsed: false, verifiedCqTransmissions: 0, transmissionDefinitelyAbsent: true)
    || PskBandRetryPolicy.CanRetryIncompleteBand(automatic: true, retryAlreadyUsed: true, verifiedCqTransmissions: 0, transmissionDefinitelyAbsent: true)
    || PskBandRetryPolicy.CanRetryIncompleteBand(automatic: true, retryAlreadyUsed: false, verifiedCqTransmissions: 1, transmissionDefinitelyAbsent: true)
    || PskBandRetryPolicy.CanRetryIncompleteBand(automatic: true, retryAlreadyUsed: false, verifiedCqTransmissions: 0, transmissionDefinitelyAbsent: false))
{
    failures.Add("Failed-band retry safety did not limit a retry to one automatic, definitely zero-transmission failure.");
}

var pskBandChoice = ConditionsSearchPolicy.ChoosePskSurveyBand(
[
    new PskSurveyBandCandidate("40m", 72, 0, 0, 68, 44, 30),
    new PskSurveyBandCandidate("20m", 86, 0, 1, 54, 70, 60)
],
"40m");
var pskNewDxccChoice = ConditionsSearchPolicy.ChoosePskSurveyBand(
[
    new PskSurveyBandCandidate("20m", 500, 0, 4, 90, 90, 90),
    new PskSurveyBandCandidate("40m", 5, 1, 0, 0, 2, 3)
],
"20m");
var pskNoEvidenceChoice = ConditionsSearchPolicy.ChoosePskSurveyBand(
[
    new PskSurveyBandCandidate("40m", 0, 0, 0, 0, 0, 0),
    new PskSurveyBandCandidate("20m", 0, 0, 0, 0, 0, 0)
],
"20m");
if (pskBandChoice?.Band != "20m"
    || pskNewDxccChoice?.Band != "40m"
    || pskNoEvidenceChoice?.Band != "20m")
{
    failures.Add("PSK survey selection did not combine evidence, preserve New DXCC priority, or stay on the current band when all evidence tied.");
}

var bandQualityAnalyzer = new BandQualityAnalyzer();
var quietLongDx = Enumerable.Range(0, 9)
    .Select(index => new DecodeMessage
    {
        Band = "17m",
        Callsign = $"DX{index}AA",
        ContactableCall = $"DX{index}AA",
        IsCq = true,
        Snr = -16 + index,
        DistanceKm = (5_200 + index * 225) * 1.609344,
        Continent = index % 3 == 0 ? "AS" : index % 3 == 1 ? "NA" : "OC",
        IsNewGrid = index == 0
    })
    .ToList();
quietLongDx.Add(new DecodeMessage
{
    Band = "17m",
    Callsign = "DX0AA",
    ContactableCall = "DX0AA",
    Snr = -12,
    DistanceKm = 5_200 * 1.609344,
    Continent = "AS"
});
var busyRegional = Enumerable.Range(0, 34)
    .Select(index => new DecodeMessage
    {
        Band = "20m",
        Callsign = $"EU{index}AA",
        ContactableCall = $"EU{index}AA",
        IsCq = index % 2 == 0,
        Snr = -8 + index % 10,
        DistanceKm = (300 + index * 30) * 1.609344,
        Continent = "EU"
    })
    .ToList();
var quietLongDxQuality = bandQualityAnalyzer.Analyze("17m", quietLongDx);
var busyRegionalQuality = bandQualityAnalyzer.Analyze("20m", busyRegional);
if (quietLongDxQuality.UniqueStations != 9
    || quietLongDxQuality.DxReachScore <= busyRegionalQuality.DxReachScore
    || busyRegionalQuality.ActivityScore <= quietLongDxQuality.ActivityScore
    || !quietLongDxQuality.Assessment.Contains("opening", StringComparison.OrdinalIgnoreCase)
    || busyRegionalQuality.Assessment != "Busy regional")
{
    failures.Add("Band Analysis did not prefer quiet long-DX reach while separately recognizing the busier regional band.");
}

var conditionsSettings = new AppSettings();
if (conditionsSettings.ConditionsSearchUsePskProbes)
    failures.Add("Automatic PSK CQ probing was not opt-in for an upgraded installation.");
var conditionsViewModel = new BandAnalysisViewModel(conditionsSettings);
if (conditionsViewModel.ConditionsIndicators.Count != 6
    || conditionsViewModel.ConditionsIndicators.Select(item => item.Key).Distinct().Count() != 6
    || conditionsViewModel.ConditionsIndicators.All(item => item.Key != "productivity"))
{
    failures.Add("The clearer dashboard did not expose all six distinct Conditions Search trigger indicators, including completed-QSO productivity.");
}
var unansweredIndicator = conditionsViewModel.ConditionsIndicators.First(item => item.Key == "unanswered");
unansweredIndicator.Update(25, "6/8 attempts · 2/3 different stations");
if (unansweredIndicator.State != "Near" || unansweredIndicator.RemainingPercent != 25)
    failures.Add("A nearly exhausted Conditions Search counter did not display its warning state.");
unansweredIndicator.Update(0, "8/8 attempts · 3/3 different stations");
if (unansweredIndicator.State != "Ready")
    failures.Add("An exhausted Conditions Search counter did not display its trigger-ready state.");
conditionsViewModel.ConditionsSearchUsePskProbes = true;
conditionsViewModel.ConditionsSearchLowActivityPersistMinutes = 4;
conditionsViewModel.ConditionsSearchNoCompletedQsoMinutes = 25;
conditionsViewModel.ConditionsSearchIncompleteQsoThreshold = 3;
conditionsViewModel.SaveTo(conditionsSettings);
if (!conditionsSettings.ConditionsSearchUsePskProbes
    || conditionsSettings.ConditionsSearchLowActivityPersistMinutes != 4
    || conditionsSettings.ConditionsSearchNoCompletedQsoMinutes != 25
    || conditionsSettings.ConditionsSearchIncompleteQsoThreshold != 3)
{
    failures.Add("Automatic full-analysis permission or the clearly exposed quiet-band/productivity settings did not survive settings save.");
}
var silentTrigger = ConditionsSearchPolicy.DetectTrigger(
    scheduledDue: false,
    startupDue: false,
    timeOnBand: TimeSpan.FromMinutes(5),
    sinceAnyDecode: TimeSpan.FromMinutes(4),
    sinceUsefulTarget: TimeSpan.FromMinutes(4),
    uniqueStations: 0,
    lowActivityDuration: TimeSpan.FromMinutes(3),
    unansweredAttempts: 0,
    distinctAttemptedStations: 0,
    conditionsSettings);
var oneHardTargetTrigger = ConditionsSearchPolicy.DetectTrigger(
    false, false, TimeSpan.FromMinutes(15), TimeSpan.Zero, TimeSpan.Zero, 20, TimeSpan.Zero, 12, 1, conditionsSettings);
var broadFailureTrigger = ConditionsSearchPolicy.DetectTrigger(
    false, false, TimeSpan.FromMinutes(15), TimeSpan.Zero, TimeSpan.Zero, 20, TimeSpan.Zero, 8, 3, conditionsSettings);
if (silentTrigger?.Priority != 80 || oneHardTargetTrigger != null || broadFailureTrigger?.Priority != 70)
    failures.Add("Conditions Search trigger policy did not distinguish a silent band or broad calling failure from repeated calls to one difficult station.");

var prematureProductivityTrigger = ConditionsSearchPolicy.DetectCompletedQsoTrigger(
    TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(24), 12, 3, conditionsSettings);
var insufficientProductivityEvidence = ConditionsSearchPolicy.DetectCompletedQsoTrigger(
    TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), 2, 2, conditionsSettings);
var incompleteQsoProductivityTrigger = ConditionsSearchPolicy.DetectCompletedQsoTrigger(
    TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), 2, 3, conditionsSettings);
var repeatedCallingProductivityTrigger = ConditionsSearchPolicy.DetectCompletedQsoTrigger(
    TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), 8, 0, conditionsSettings);
if (prematureProductivityTrigger != null
    || insufficientProductivityEvidence != null
    || incompleteQsoProductivityTrigger?.Priority != 65
    || repeatedCallingProductivityTrigger?.Priority != 65)
{
    failures.Add("Completed-QSO productivity did not require both elapsed time and sufficient calling or incomplete-exchange evidence.");
}

var newDxccQuality = bandQualityAnalyzer.Analyze("30m",
[
    new DecodeMessage
    {
        Band = "30m", Callsign = "RARE1", ContactableCall = "RARE1", IsCq = true,
        IsNewDxcc = true, DistanceKm = 1_000, Continent = "EU"
    }
]);
var bandChoice = ConditionsSearchPolicy.ChooseBand(
[
    (busyRegionalQuality, 0),
    (quietLongDxQuality, 5),
    (newDxccQuality, -10)
],
    "20m",
    HuntingOperatingMode.DxAssist,
    20);
if (!bandChoice.ShouldMove || bandChoice.Band != "30m" || newDxccQuality.NewDxccStations != 1)
    failures.Add("Conditions Search did not give an observed New DXCC absolute band-choice priority.");

var manualAssistanceDestination = ConditionsSearchPolicy.SurveyDestinationBand(
    new ConditionsBandChoice("20m", 75, 60, false, "20m was stronger but below the automatic margin."),
    "17m",
    automatic: false,
    automaticMovementEnabled: true);
var tiedManualDestination = ConditionsSearchPolicy.SurveyDestinationBand(
    new ConditionsBandChoice("20m", 0, 0, false, "No measured difference."),
    "17m",
    automatic: false,
    automaticMovementEnabled: true);
var disabledAutomaticDestination = ConditionsSearchPolicy.SurveyDestinationBand(
    new ConditionsBandChoice("20m", 100, 40, true, "20m was stronger."),
    "17m",
    automatic: true,
    automaticMovementEnabled: false);
if (manualAssistanceDestination != "20m"
    || tiedManualDestination != "17m"
    || disabledAutomaticDestination != "17m")
{
    failures.Add("Band Analysis destination policy did not move a manual assistance survey to its stronger winner while preserving tie and automatic-control safeguards.");
}

var trendHistory = new List<BandAnalysisHistoryEntry>
{
    new() { Band = "17m", ObservedAtUtc = DateTime.UtcNow.AddHours(-2), SecondsObserved = 60, ActivityScore = 15, DxReachScore = 10 },
    new() { Band = "17m", ObservedAtUtc = DateTime.UtcNow.AddHours(-1), SecondsObserved = 60, ActivityScore = 30, DxReachScore = 35 },
    new()
    {
        SurveyId = "survey-test", Band = "17m", ObservedAtUtc = DateTime.UtcNow,
        SecondsObserved = 60, ActivityScore = 55, DxReachScore = 70, StartingBand = "20m", SelectedBand = "17m",
        Decision = "Moved to 17m because DX reach improved.", CompletedComparableAnalysis = true,
        PskMeasured = true, WorkabilityScore = 68, PskViabilityPercent = 76, PathMatchPercent = 81,
        DistinctWantedOpportunities = 3, WorkableWantedOpportunities = 2, WorkabilityAssessment = "Good two-way prospects"
    }
};
var emergingTrend = ConditionsSearchPolicy.Trend("17m", trendHistory);
if (emergingTrend.Score <= 0 || !emergingTrend.Label.Contains("Emerging", StringComparison.OrdinalIgnoreCase))
    failures.Add("Band Analysis history did not identify a strongly emerging band trend.");

var trendNow = DateTime.UtcNow;
var comparableTrendHistory = new List<BandAnalysisHistoryEntry>
{
    new()
    {
        Band = "20m", ObservedAtUtc = trendNow.AddMinutes(-70), CompletedComparableAnalysis = true,
        PskMeasured = true, WorkabilityScore = 40
    },
    new()
    {
        Band = "20m", ObservedAtUtc = trendNow.AddHours(-24), CompletedComparableAnalysis = true,
        PskMeasured = true, WorkabilityScore = 90
    },
    new()
    {
        Band = "20m", ObservedAtUtc = trendNow.AddMinutes(-20), CompletedComparableAnalysis = false,
        PskMeasured = true, WorkabilityScore = 95
    }
};
var recentImprovement = ConditionsSearchPolicy.RecentTrendAgainstCurrent(
    "20m", 52, comparableTrendHistory, trendNow, comparisonWindowHours: 3);
var staleComparison = ConditionsSearchPolicy.RecentTrendAgainstCurrent(
    "20m", 52, comparableTrendHistory, trendNow, comparisonWindowHours: 1);
if (recentImprovement.Score <= 0
    || !recentImprovement.Label.Contains("30%", StringComparison.Ordinal)
    || !recentImprovement.Label.Contains("ago", StringComparison.OrdinalIgnoreCase)
    || staleComparison.Score != 0
    || !staleComparison.Label.Contains("No recent", StringComparison.OrdinalIgnoreCase))
{
    failures.Add("Band Analysis trends did not compare against the latest completed result inside the configured recent window while ignoring stale and incomplete surveys.");
}

var bandHistoryTestFolder = Path.Combine(Path.GetTempPath(), $"DXPilot-band-history-test-{Guid.NewGuid():N}");
try
{
    var bandHistoryStore = new BandAnalysisHistoryStore(bandHistoryTestFolder);
    bandHistoryStore.Save(trendHistory);
    var restoredBandHistory = bandHistoryStore.Load();
    if (restoredBandHistory.Count != 3
        || restoredBandHistory[^1].Band != "17m"
        || restoredBandHistory[^1].SelectedBand != "17m"
        || restoredBandHistory[^1].SurveyId != "survey-test"
        || restoredBandHistory[^1].WorkabilityScore != 68
        || restoredBandHistory[^1].PathMatchPercent != 81
        || !restoredBandHistory[^1].CompletedComparableAnalysis
        || !File.Exists(bandHistoryStore.HistoryFile)
        || !File.ReadAllText(bandHistoryStore.HistoryFile).Contains("Moved to 17m", StringComparison.Ordinal))
        failures.Add("Band Analysis history JSON round-trip lost trend observations.");
}
finally
{
    if (Directory.Exists(bandHistoryTestFolder))
        Directory.Delete(bandHistoryTestFolder, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"PASS: CALL NOW one-shot/resume policy, configurable 5-200 row geometry/model/settings, secure settings/scheduler export-import validation, WAS-only state indexing with Alaska/Hawaii and optional DC, personal 52-row default migration, {bandCases.Length} band mappings, FT8/FT4 timing and Reply markers, binary JTDX parsing, stale-target policy, InQso CQ contradiction and no-progress safety, blank-status verification, band/mode resets, context inheritance, row-settling gate, receive-only Band Analysis full-cycle synchronisation, verified PSK Reporter parsing/probe matching/outward metrics, two-way band workability with geographic path gating, recent comparable trends, safe failed-band-only retry, Conditions Search trigger/movement/New-DXCC priority, quiet-long-DX scoring, optional scoped DXCC and new-grid priorities with normal-DX fallback, DX Assist opportunity colours independent of ranking tier, Session History DXCC-first universal-rank ordering, Full Archive search, semantic new-grid classification, and archive persistence round-trip.");
return 0;

static byte[] BuildDecodePacket(string modeMarker, string message)
{
    using var stream = new MemoryStream();
    WriteUInt32(stream, 0xADBCCBDA);
    WriteUInt32(stream, 2);
    WriteUInt32(stream, 2);
    WriteString(stream, "JTDX");
    stream.WriteByte(1);
    WriteUInt32(stream, 35_000);
    WriteInt32(stream, -12);
    WriteDouble(stream, 0.2);
    WriteUInt32(stream, 1_250);
    WriteString(stream, modeMarker);
    WriteString(stream, message);
    stream.WriteByte(0);
    return stream.ToArray();
}

static byte[] BuildStatusPacket(ulong dialFrequencyHz, string mode, uint trPeriodSeconds)
{
    using var stream = new MemoryStream();
    WriteUInt32(stream, 0xADBCCBDA);
    WriteUInt32(stream, 2);
    WriteUInt32(stream, 1);
    WriteString(stream, "JTDX");
    WriteUInt64(stream, dialFrequencyHz);
    WriteString(stream, mode);
    WriteString(stream, "K1ABC");
    WriteString(stream, "-10");
    WriteString(stream, mode);
    stream.WriteByte(0);
    stream.WriteByte(0);
    stream.WriteByte(0);
    WriteUInt32(stream, 1_500);
    WriteUInt32(stream, 1_500);
    WriteString(stream, "G1CEC");
    WriteString(stream, "IO83");
    WriteString(stream, "FN42");
    stream.WriteByte(0);
    WriteString(stream, "");
    stream.WriteByte(0);
    stream.WriteByte(0);
    WriteUInt32(stream, 50);
    WriteUInt32(stream, trPeriodSeconds);
    WriteString(stream, "Default");
    WriteString(stream, "K1ABC G1CEC IO83");
    return stream.ToArray();
}

static string ReadReplyMode(byte[] packet)
{
    var offset = 0;
    _ = ReadUInt32(packet, ref offset); // magic
    _ = ReadUInt32(packet, ref offset); // schema
    _ = ReadUInt32(packet, ref offset); // message type
    _ = ReadString(packet, ref offset); // app ID
    offset += 4; // time
    offset += 4; // SNR
    offset += 8; // DT
    offset += 4; // audio offset
    return ReadString(packet, ref offset);
}

static uint ReadUInt32(byte[] packet, ref int offset)
{
    var value = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset, 4));
    offset += 4;
    return value;
}

static string ReadString(byte[] packet, ref int offset)
{
    var length = checked((int)ReadUInt32(packet, ref offset));
    var value = Encoding.UTF8.GetString(packet, offset, length);
    offset += length;
    return value;
}

static void WriteUInt32(Stream stream, uint value)
{
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    stream.Write(buffer);
}

static void WriteUInt64(Stream stream, ulong value)
{
    Span<byte> buffer = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
    stream.Write(buffer);
}

static void WriteInt32(Stream stream, int value)
{
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    stream.Write(buffer);
}

static void WriteDouble(Stream stream, double value)
{
    Span<byte> buffer = stackalloc byte[8];
    BinaryPrimitives.WriteInt64BigEndian(buffer, BitConverter.DoubleToInt64Bits(value));
    stream.Write(buffer);
}

static void WriteString(Stream stream, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    WriteUInt32(stream, (uint)bytes.Length);
    stream.Write(bytes);
}

static object? InvokePrivate(object target, string methodName, params object[] arguments)
{
    var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing private method {methodName}.");
    return method.Invoke(target, arguments);
}

static void SetPrivate(object target, string fieldName, object value)
{
    var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing private field {fieldName}.");
    field.SetValue(target, value);
}

static void SetPrivateEnum(object target, string fieldName, string value)
{
    var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing field {fieldName}.");
    field.SetValue(target, Enum.Parse(field.FieldType, value));
}

static List<DecodeMessage> PrivateDecodeHistory(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_decodeHistory", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing private decode history.");
    return (List<DecodeMessage>)field.GetValue(viewModel)!;
}

static List<AdifQso> PrivateLogbook(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_logbook", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing private logbook.");
    return (List<AdifQso>)field.GetValue(viewModel)!;
}

static Dictionary<string, DateTime> PrivateFailedReplySources(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_failedReplySources", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing failed reply-source dictionary.");
    return (Dictionary<string, DateTime>)field.GetValue(viewModel)!;
}

static Dictionary<string, int> PrivateGuiSelectionClickCounts(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_guiSelectionClickCounts", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing GUI selection click-count dictionary.");
    return (Dictionary<string, int>)field.GetValue(viewModel)!;
}

static HashSet<string> PrivateForcedGuiSelectionSources(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_forceGuiSelectionSources", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing forced GUI source set.");
    return (HashSet<string>)field.GetValue(viewModel)!;
}

static JtdxVisibleRowModel PrivateVisibleRowModel(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_visibleRowModel", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing visible-row model.");
    return (JtdxVisibleRowModel)field.GetValue(viewModel)!;
}

static DxTarget? PrivateLockedTarget(MainViewModel viewModel)
{
    var field = typeof(MainViewModel).GetField("_lockedTarget", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing private locked target.");
    return (DxTarget?)field.GetValue(viewModel);
}

static bool PrivateBool(MainViewModel viewModel, string fieldName)
{
    var field = typeof(MainViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing private Boolean field {fieldName}.");
    return (bool)field.GetValue(viewModel)!;
}

static DateTime PrivateDateTime(MainViewModel viewModel, string fieldName)
{
    var field = typeof(MainViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing private DateTime field {fieldName}.");
    return (DateTime)field.GetValue(viewModel)!;
}

static DecodeMessage Candidate(string band, string mode)
{
    return new DecodeMessage
    {
        ReceivedAt = DateTime.Now,
        Callsign = "K1ABC",
        ContactableCall = "K1ABC",
        HeardCall = "K1ABC",
        Grid = "FN42",
        Band = band,
        Mode = mode,
        RawText = "CQ K1ABC FN42",
        Targetable = true,
        ParseConfidence = ParseConfidence.High
    };
}
