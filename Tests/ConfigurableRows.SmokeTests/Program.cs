using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;

var failures = new List<string>();

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
        QrzPasswordProtected = "protected-secret-must-not-export"
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

var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
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

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"PASS: CALL NOW one-shot/resume policy, configurable 5-200 row geometry/model/settings, secure settings/scheduler export-import validation, WAS-only state indexing with Alaska/Hawaii and optional DC, personal 52-row default migration, {bandCases.Length} band mappings, FT8/FT4 timing and Reply markers, binary JTDX parsing, stale-target policy, InQso CQ contradiction and no-progress safety, blank-status verification, band/mode resets, context inheritance, row-settling gate, optional scoped DXCC and new-grid priorities with normal-DX fallback, DX Assist opportunity colours independent of ranking tier, Session History DXCC-first universal-rank ordering, Full Archive search, semantic new-grid classification, and archive persistence round-trip.");
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
