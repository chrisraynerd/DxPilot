using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;
using JtdxAutoResume.V3.Views;
using JtdxAutoResume.V3.Controls.JtdxSelection;

internal static class Program
{
    private static int _checks;
    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        _checks++;
    }
    private static void Set(object obj, string name, object? value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void EnumField(object obj, string name, string value)
    {
        var field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(obj, Enum.Parse(field.FieldType, value));
    }

    private static void CheckClearedGrid()
    {
        var calibration = new JtdxBandActivityGridCalibration
        {
            SafeVisibleFullRowCount = 52, FirstFullRowCentreYRelative = 89,
            RowHeight = 16, IgnoredPartialTopRow = true, NewestRowsAtBottom = true
        };
        var rows = new JtdxVisibleRowModel();
        var time = new DateTime(2026, 9, 12, 20, 38, 44);
        DecodeMessage Decode(int i, int cycle, long generation = 7) => new()
        {
            Callsign = i == 3 ? "V51WH" : $"TEST{i}", RawText = $"AD5CA TEST{i} RR73",
            ReceivedAt = time.AddMilliseconds(i), DecodeTime = TimeSpan.FromSeconds(cycle),
            RadioContextGeneration = generation
        };
        var firstBatch = Enumerable.Range(0, 4).Select(i => Decode(i, 30)).ToArray();
        rows.Rebuild(firstBatch, calibration);
        Check(!rows.CanLocateRows(calibration), "Startup on an already populated pane must not pretend a clear was observed.");
        rows.ResetForContext(7, true);
        rows.Rebuild(firstBatch, calibration);
        Check(rows.CanLocateRows(calibration) && rows.UsesClearedPaneOrigin, "A confirmed band change must allow selection from the first partial batch.");
        var target = rows.FindDecode(firstBatch[3])!;
        Check(target.ScreenRowIndex == 4 && rows.RowCentreYRelative(target, calibration) == 137,
            "V51WH screenshot regression: initial marker plus four decodes puts target at Y137, not the next row.");
        var later = firstBatch.Concat(Enumerable.Range(4, 4).Select(i => Decode(i, 60))).Append(Decode(8, 90)).ToArray();
        rows.Rebuild(later, calibration);
        Check(rows.FindDecode(later[7])!.ScreenRowIndex == 9 && rows.RowCentreYRelative(rows.FindDecode(later[7])!, calibration) == 217,
            "Each receive-cycle separator must consume a physical row in a partially filled pane.");
        Check(rows.RowCentreYRelative(rows.FindDecode(later[8])!, calibration) == 249,
            "Third screenshot cycle remains top anchored after multiple batches.");
        var full = Enumerable.Range(0, 51).Select(i => Decode(i, 30)).ToArray();
        rows.Rebuild(full, calibration);
        Check(rows.UsesClearedPaneOrigin && rows.Rows.Count == 52, "Exactly 52 known rows still have the original top anchor.");
        var overflow = full.Append(Decode(51, 30)).ToArray();
        rows.Rebuild(overflow, calibration);
        Check(!rows.UsesClearedPaneOrigin && rows.RowCentreYRelative(rows.Rows[0], calibration) == 89,
            "Once rows overflow, retain the stable full-pane calibration.");
        rows.ResetForContext(8, true);
        var newBand = Decode(99, 90, 8);
        rows.Rebuild(overflow.Append(newBand).ToArray(), calibration);
        Check(rows.Rows.Count == 2 && rows.FindDecode(firstBatch[3]) == null && rows.FindDecode(newBand)!.ScreenRowIndex == 1,
            "Prior-band rows must not survive a context reset even if passed to the model.");
        rows.ResetForContext(9, false);
        rows.Rebuild([Decode(1, 30, 9)], calibration);
        Check(!rows.CanLocateRows(calibration), "A generic context reset must not manufacture evidence of a pane clear.");
        rows.ResetForContext(9, true);
        calibration.SafeVisibleFullRowCount = 34;
        calibration.RowHeight = 20;
        calibration.FirstFullRowCentreYRelative = 100;
        rows.Rebuild([Decode(1, 30, 9)], calibration);
        Check(rows.RowCentreYRelative(rows.Rows[1], calibration) == 100, "Custom row count and height must use the user's calibration.");
        calibration.IgnoredPartialTopRow = false;
        rows.Rebuild([Decode(1, 30, 9)], calibration);
        Check(rows.RowCentreYRelative(rows.Rows[1], calibration) == 120, "A calibration with no ignored top row must not receive a top-row offset.");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        CheckClearedGrid();
        var nonLotw = new DxTarget { Decode = new() { Callsign = "TEST1", IsLotwUser = false }, Ranking = new() { AdjustedDxValueScore = 9999 } };
        var lotw = new DxTarget { Decode = new() { Callsign = "TEST2", IsLotwUser = true }, Ranking = new() { AdjustedDxValueScore = 1 } };
        Check(ScavengerSearchPolicy.SelectTarget([nonLotw, lotw], _ => 30) == lotw, "LoTW grid must win over a higher-score non-LoTW grid in the same wanted tier.");
        Check(ScavengerSearchPolicy.SelectTarget([nonLotw, lotw], _ => 0) == lotw, "LoTW new DXCC must win between two new DXCCs.");
        Check(ScavengerSearchPolicy.SelectTarget([nonLotw, lotw], t => t == nonLotw ? 0 : 30) == nonLotw, "A new DXCC must still beat a LoTW grid.");
        Check(ScavengerSearchPolicy.SelectTarget([nonLotw, lotw], t => t == nonLotw ? 0 : 1) == nonLotw, "Never-worked DXCC priority must remain above worked-unconfirmed DXCC.");
        Check(ScavengerSearchPolicy.SelectTarget([], _ => 0) == null, "No eligible station must not invent a target.");
        var colourRow = new ScavengerOpportunityRow(new(), "New", "Eligible", false) { Category = "DXCC", Need = NeedStatus.NeverWorked };
        Check(colourRow.OpportunityClass == "NewDxcc", "New DXCC must use the application's purple coding.");
        Check((colourRow with { Need = NeedStatus.WorkedNotLoTWConfirmed }).OpportunityClass == "UnconfirmedDxcc", "Unconfirmed DXCC must use its lighter purple coding.");
        Check((colourRow with { Category = "grid" }).OpportunityClass == "NewGrid" && (colourRow with { Category = "state" }).OpportunityClass == "NewState", "Grid and state panels must retain their blue and teal coding.");
        Check((colourRow with { IsTarget = true }).ActionStateClass == "Calling" && (colourRow with { IsTarget = true, IsInQso = true }).ActionStateClass == "InProgress", "Active target outlines must remain orange/green without replacing opportunity colours.");
        Check((colourRow with { Status = "Resting · 2 rounds" }).ActionStateClass == "Suppressed", "Resting calls must use suppression coding.");
        var rests = new ScavengerTargetRestPolicy();
        var firstCall = new DateTime(2026, 9, 12, 21, 0, 0);
        Check(!rests.LimitReached("V51WH", firstCall.AddHours(1), 10, false), "Selecting without a transmitted call must not start the calling timer.");
        rests.RecordAttempt("V51WH", firstCall);
        rests.RecordAttempt("v51wh", firstCall.AddMinutes(9));
        Check(!rests.LimitReached("V51WH", firstCall.AddMinutes(9.99), 10, false), "Ten-minute cap must not expire early.");
        Check(rests.LimitReached("V51WH", firstCall.AddMinutes(10), 10, false), "Further calling attempts must not restart the original timer.");
        Check(!rests.LimitReached("V51WH", firstCall.AddHours(1), 10, true), "A progressing QSO must remain protected.");
        Check(rests.FinalReplyUntil("V51WH", TimeSpan.FromSeconds(30)) == firstCall.AddMinutes(9).AddSeconds(32), "Final reply window must cover TX/RX plus decode delivery.");
        rests.RestCall("V51WH", 3, 2);
        Check(rests.RoundsRemaining("v51wh", 3) == 2, "Remainder of release round must not count as a completed rest round.");
        Check(rests.RoundsRemaining("V51WH", 4) == 2 && rests.RoundsRemaining("V51WH", 5) == 1, "Call must rest through two complete subsequent search rounds.");
        Check(rests.RoundsRemaining("V51WH", 6) == 0, "Call must become eligible after the two rounds finish.");
        Check(rests.RoundsRemaining("V51WW", 3) == 0, "Resting one call must not suppress another from the same country.");
        Check(rests.Summary(3).Contains("V51WH") && rests.Summary(6) == "", "Rest summary must show calls with remaining rounds only.");
        rests.RecordAttempt("V51WH", firstCall.AddHours(1));
        Check(!rests.LimitReached("V51WH", firstCall.AddHours(1).AddMinutes(1), 10, false), "A later retry must receive a new calling allowance.");
        rests.ForgetAttempts("V51WH");
        Check(!rests.LimitReached("V51WH", firstCall.AddHours(2), 10, false), "A completed QSO must clear its timer.");
        var rotation = new ScavengerSearchPolicy();
        string[] bands = ["40m", "30m", "20m"];
        Check(rotation.Next(bands) == "40m", "Round must begin with first permitted band.");
        rotation.Complete("40m", 4);
        Check(rotation.Next(bands) == "30m", "A silent band must not skip the remainder of this round.");
        rotation.Complete("30m", 2);
        Check(rotation.Next(bands) == "20m", "All permitted bands must get their first visit.");
        rotation.Complete("20m", 0);
        Check(rotation.Next(bands) == "20m" && rotation.Round == 2, "Resting bands must be skipped in round two.");
        rotation.Complete("20m", 0);
        Check(rotation.Next(bands) == "20m" && rotation.Round == 3, "Quiet band must skip exactly two subsequent rounds.");
        rotation.Complete("20m", 0);
        Check(rotation.Next(bands) == "30m" && rotation.Round == 4, "Quiet band must return in round four.");
        rotation.Complete("30m", 0);
        Check(rotation.Next(bands) == "20m" && rotation.Round == 4, "Round cannot advance early.");
        rotation.Complete("20m", 0);
        Check(rotation.Next(bands) == "30m" && rotation.Round == 5, "Silent band must skip round five too.");
        rotation.Complete("30m", 0);
        rotation.Complete("20m", 0);
        Check(rotation.Next(bands) == "40m" && rotation.Round == 6, "Silent band must return after four skipped rounds.");
        Check(rotation.Next([]) == null, "No allowed bands must never choose a default band.");
        var single = new ScavengerSearchPolicy();
        Check(single.Next(["17m"]) == "17m", "Single permitted band must be supported.");
        single.Complete("17m", 4);
        Check(single.Next(["17m"]) == "17m" && single.Round == 2, "All-resting rotation must require a real visit, not spin empty rounds.");

        Check(ScavengerSearchPolicy.Classify(0, 0, 0, 4, 2) == 4, "Silent-band classification.");
        Check(ScavengerSearchPolicy.Classify(5, 5, 1500, 4, 2) == 2, "Few nearby stations classification.");
        Check(ScavengerSearchPolicy.Classify(1, 1, 3000, 4, 2) == 0, "One distant station must retain rotation priority.");
        Check(ScavengerSearchPolicy.Classify(5, 4, 100, 4, 2) == 0, "Unknown distance must not be assumed local.");
        Check(ScavengerSearchPolicy.Classify(6, 6, 100, 4, 2) == 0, "Busy nearby band is not a few-stations band.");
        var middle = new DateTime(2026, 9, 9, 23, 59, 52, DateTimeKind.Utc).ToLocalTime();
        var start = BandSurveyTiming.FirstEligibleSlotStart(middle, TimeSpan.FromSeconds(15));
        Check(start.ToUniversalTime() == new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), "Partial slot must be discarded across midnight.");
        Check(ScavengerSearchPolicy.SampleEnd(start, 1, TimeSpan.FromSeconds(15)) == start.AddSeconds(63), "One-minute FT8 sample must include four full slots and delivery tail.");
        Check(ScavengerSearchPolicy.SampleEnd(start, 2, TimeSpan.FromSeconds(7.5)) == start.AddSeconds(123), "FT4 timing must follow its own receive period.");
        Check(ScavengerSearchPolicy.ReturnWatchEnd(start) - start == TimeSpan.FromMinutes(5), "Return watch must be an extra five minutes.");
        var receiveStatus = new JtdxStatusMessage { ReceivedAt = DateTime.Now.AddSeconds(-10) };
        Check(ScavengerSearchPolicy.HasReceiveEvidence(receiveStatus, DateTime.Now, TimeSpan.FromSeconds(15)), "Ordinary gaps between FT8 status packets must not continually restart samples.");
        receiveStatus.ReceivedAt = DateTime.Now.AddSeconds(-45);
        Check(!ScavengerSearchPolicy.HasReceiveEvidence(receiveStatus, DateTime.Now, TimeSpan.FromSeconds(15)), "Loss of status across multiple receive periods must interrupt the sample.");
        receiveStatus.ReceivedAt = DateTime.Now;
        receiveStatus.TxEnabled = true;
        Check(!ScavengerSearchPolicy.HasReceiveEvidence(receiveStatus, DateTime.Now, TimeSpan.FromSeconds(15)), "Armed TX cannot count as receive-only listening.");
        Check(!ScavengerSearchPolicy.HasReceiveEvidence(null, DateTime.Now, TimeSpan.FromSeconds(15)), "Missing status cannot count as silence.");

        var settings = new AppSettings { KeepCallingNewDxccUntilStale = false, NewDxccStaleSeconds = 90, CandidateMaxAgeSeconds = 45 };
        var bandChoices = new[] { new BandAnalysisBandViewModel("40m", "40", 3, true), new BandAnalysisBandViewModel("30m", "30", 4, true) };
        var display = new ScavengerViewModel(() => settings, bandChoices);
        Check(display.MaxCallingMinutes == 10 && display.TargetRestRounds == 2, "Scavenger rest defaults must be ten minutes and two search rounds.");
        display.MaxCallingMinutes = 100;
        display.TargetRestRounds = 0;
        Check(display.MaxCallingMinutes == 60 && display.TargetRestRounds == 1, "Editable rest settings must be bounded.");
        display.MaxCallingMinutes = 10;
        display.TargetRestRounds = 2;
        var savedRestSettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(savedRestSettings.ScavengerMaxCallingMinutes == 10 && savedRestSettings.ScavengerTargetRestRounds == 2, "Rest settings must survive saving and loading.");
        Check(display.ListenMinutes == 1 && display.DeadSkipRounds == 4 && display.QuietSkipRounds == 2, "New settings defaults.");
        display.ListenMinutes = 9;
        display.DeadSkipRounds = -1;
        Check(display.ListenMinutes == 5 && display.DeadSkipRounds == 1, "Settings must be bounded.");
        settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        settings.ScavengerListenMinutes = 2;
        Check(display.ListenMinutes == 2, "View must follow replaced/imported settings.");
        display.Bands[0].BandChoice.Enabled = false;
        Check(!bandChoices[0].Enabled, "Allowed band choices must match the existing band permissions.");
        bandChoices[0].Enabled = true;

        // Deliberately bypass the application constructor. These tests must never
        // read/write the operator's settings, open UDP, start timers or click JTDX.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        Set(vm, "<Settings>k__BackingField", new SettingsViewModel { Settings = settings });
        Set(vm, "<Scavenger>k__BackingField", display);
        Set(vm, "_scavengerTargetRest", new ScavengerTargetRestPolicy());
        Set(vm, "_operatingMode", HuntingOperatingMode.Scavenger);
        Set(vm, "_lastHeardUtcByCall", new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase));
        var merged = new AdifMergeResult();
        Set(vm, "_adifMergeResult", merged);
        var decode = new DecodeMessage { Callsign = "FR4OM", ContactableCall = "FR4OM", Dxcc = "453", EntityName = "Reunion", Band = "40m", ReceivedAt = DateTime.Now };
        Check((bool)Call(vm, "IsScavengerNewDxcc", decode)!, "Never-worked DXCC must be eligible.");
        merged.OverallIndexes.Dxcc["453"] = new DxccWorkedStatus { WorkedAny = true, LoTWConfirmedAny = true };
        Check((bool)Call(vm, "IsScavengerNewDxcc", decode)!, "Old callsign achievements must not override the selected profile.");
        merged.Indexes.Dxcc["453"] = new DxccWorkedStatus { WorkedAny = true };
        Check((bool)Call(vm, "IsScavengerNewDxcc", decode)!, "Worked but unconfirmed DXCC must remain eligible (St. Martin/Sri Lanka regression).");
        int Priority(DecodeMessage d) => (int)Call(vm, "GetScavengerNeed", d)!.GetType().GetProperty("Priority")!.GetValue(Call(vm, "GetScavengerNeed", d))!;
        Check(Priority(decode) == 1, "Unconfirmed overall DXCC ranks immediately after never-worked.");
        foreach (var dxcc in new[] { "213", "315" })
        {
            merged.Indexes.Dxcc[dxcc] = new DxccWorkedStatus { WorkedAny = true, ConfirmedAny = false };
            Check(Call(vm, "GetScavengerNeed", new DecodeMessage { Dxcc = dxcc, EntityName = dxcc == "213" ? "St. Martin" : "Sri Lanka", Band = "20m", Mode = "FT8" }) != null,
                "Unconfirmed entity must pass the Scavenger selection filter.");
        }
        merged.Indexes.Dxcc["453"].ConfirmedAny = true;
        Check(Call(vm, "GetScavengerNeed", decode) == null, "Confirmed country without optional needs must be excluded.");
        settings.ScavengerWantedGrids = true;
        decode.Grid = "LG79";
        Check(Priority(decode) == 30, "New grid must be eligible when its country is confirmed.");
        Check(!(bool)Call(vm, "IsScavengerProtectedDxcc", decode)!, "Grid targets must not acquire unlimited DXCC calling protection.");
        merged.Indexes.Grids["LG79"] = new SimpleWorkedStatus { WorkedAny = true, ConfirmedAny = true };
        Check(Call(vm, "GetScavengerNeed", decode) == null, "Overall-confirmed grid is not wanted without added scopes.");
        settings.ScavengerIncludeBand = true;
        Check(Call(vm, "GetScavengerNeed", decode) != null, "Missing grid/country on this band must be eligible with band scope.");
        settings.ScavengerWantedDxcc = false;
        Check(Priority(decode) == 30, "Band-specific grid works independently of DXCC category checkbox.");
        settings.ScavengerWantedGrids = false;
        var displayNeeds = ((System.Collections.IEnumerable)Call(vm, "GetScavengerNeeds", decode, true)!).Cast<object>().ToArray();
        Check(displayNeeds.Any(n => (string)n.GetType().GetProperty("Category")!.GetValue(n)! == "grid"), "Scavenger display must show grid opportunities even when that category is off.");
        Check(Call(vm, "GetScavengerNeed", decode) == null, "Reading disabled display categories must not enable them for calling.");
        Check(!settings.ScavengerWantedGrids && !settings.ScavengerWantedDxcc, "Opportunity display must not mutate the operator's category choices.");
        var panelRow = new ScavengerOpportunityRow(decode, "New grid on current band", "Category off", false);
        display.OpportunityPanels[1].Rows.Add(panelRow);
        Check(display.OpportunityPanels[1].Heading == "Wanted Grids · 1" && !display.OpportunityPanels[1].IsEmpty, "Wanted panel heading must reflect actual rows.");
        display.ClearOpportunities();
        Check(display.OpportunityPanels.All(p => p.IsEmpty), "Band changes must clear all three Scavenger opportunity panels.");
        settings.ScavengerWantedStates = true;
        var stateDecode = new DecodeMessage { Dxcc = "291", EntityName = "United States", State = "TX", Band = "20m", Mode = "FT8" };
        merged.Indexes.Dxcc["291"] = new DxccWorkedStatus { WorkedAny = true, ConfirmedAny = true };
        merged.Indexes.States["TX"] = new SimpleWorkedStatus { WorkedAny = true, ConfirmedAny = true };
        Check(Priority(stateDecode) == 20, "New state on band must be eligible.");
        merged.Indexes.States["TX"].WorkedBands.Add("20m");
        merged.Indexes.States["TX"].ConfirmedBands.Add("20m");
        Check(Call(vm, "GetScavengerNeed", stateDecode) == null, "Confirmed state on band must be excluded.");
        stateDecode.Dxcc = "150";
        stateDecode.EntityName = "Australia";
        merged.Indexes.Dxcc["150"] = new DxccWorkedStatus { WorkedAny = true, ConfirmedAny = true };
        Check(Call(vm, "GetScavengerNeed", stateDecode) == null, "Non-USA state codes cannot be WAS targets.");
        settings.ScavengerIncludeBand = false;
        settings.ScavengerIncludeMode = true;
        stateDecode.Dxcc = "291";
        stateDecode.EntityName = "United States";
        Check(Priority(stateDecode) == 20, "Missing state in current mode must be eligible.");
        merged.Indexes.States["TX"].WorkedModes.Add("FT8");
        merged.Indexes.States["TX"].ConfirmedModes.Add("FT8");
        settings.ScavengerIncludeMode = false;
        settings.ScavengerIncludeBandMode = true;
        Check(Priority(stateDecode) == 20, "Separate band and mode credit cannot substitute for band-plus-mode credit.");
        settings.ScavengerIncludeBandMode = false;
        settings.ScavengerWantedStates = false;
        merged.Indexes.Dxcc.Clear();
        Check(Priority(decode) == 0, "Never-worked DXCC remains global priority even with the DXCC category unchecked.");
        settings.ScavengerWantedDxcc = true;
        Check(!(bool)Call(vm, "IsScavengerNewDxcc", new DecodeMessage())!, "Unresolved DXCC must not trigger calling.");
        var target = new DxTarget { Decode = decode, Ranking = new CandidateRanking { DxccStatus = DxccCandidateStatus.NotWorked } };
        Set(vm, "_lockedTarget", target);
        EnumField(vm, "_huntState", "Calling");
        Check((bool)Call(vm, "PersistentNewDxccLockApplies")!, "Scavenger must force until-stale protection even when ordinary mode setting is off.");
        Check(((string)Call(vm, "CallAttemptProgressText")!).Contains("until stale"), "Call counter must explain unlimited attempts.");
        EnumField(vm, "_huntState", "InQso");
        Set(vm, "_reportAttemptCount", 100);
        Check(!(bool)Call(vm, "ReportRepeatLimitShouldRelease", 6)!, "Reply must not remove the persistent QSO lock.");
        Check(!(bool)Call(vm, "InQsoNoProgressTimedOut")!, "Fresh persistent target must not time out during exchange.");
        decode.ReceivedAt = DateTime.Now.AddMinutes(-3);
        Check((bool)Call(vm, "PersistentNewDxccHasGoneStale")!, "Target should become stale after its configured last-heard threshold.");
        EnumField(vm, "_qsoStage", "CompletionPending");
        Check(!(bool)Call(vm, "InQsoNoProgressTimedOut")!, "Final-73 completion grace must retain existing protection.");
        Set(vm, "_operatingMode", HuntingOperatingMode.WantedSniper);
        Check(!(bool)Call(vm, "PersistentNewDxccLockApplies")!, "Ordinary mode must still respect its unchanged persistence setting.");
        Set(vm, "_operatingMode", HuntingOperatingMode.Scavenger);
        Check(!(bool)Call(vm, "TryQueueLateReplyRecovery", decode)!, "Scavenger must not adopt unrelated late replies.");

        var auto = (AutoResumeService)RuntimeHelpers.GetUninitializedObject(typeof(AutoResumeService));
        using var fakeRunning = new CancellationTokenSource();
        Set(auto, "_cts", fakeRunning);
        Set(vm, "_autoResume", auto);
        Set(vm, "_callNowSession", new CallNowSessionState());
        Set(vm, "_lockedTarget", null);
        Check(!(bool)Call(vm, "ShouldClickEnableTxRecovery")!, "Searching without a target must never arm Enable TX.");
        settings.AcceptIncomingCalls = true;
        Call(vm, "TryAdoptInboundQso", decode);
        Check(Get(vm, "_lockedTarget") == null, "Accept incoming QSOs cannot override Scavenger's exclusive target selection.");
        var analysis = new BandAnalysisViewModel(settings);
        Set(vm, "<BandAnalysis>k__BackingField", analysis);
        Set(vm, "_conditionsProductivityHandoverRequested", true);
        Set(vm, "_conditionsSafeHandoverRequested", true);
        ((Task)Call(vm, "EvaluateConditionsSearchAsync")!).GetAwaiter().GetResult();
        Check(!(bool)Get(vm, "_conditionsProductivityHandoverRequested")! && !(bool)Get(vm, "_conditionsSafeHandoverRequested")!, "Automatic analysis must be bypassed and handover cleared.");
        Check((bool)Call(vm, "RejectSurveyDuringScavenger")!, "Manual survey must not run concurrently with Scavenger.");
        Check(!vm.CanConfigureScavenger, "Configuration must be locked while searching.");
        Set(vm, "_lockedTarget", target);
        Set(vm, "_targetStartedAt", DateTime.Now.AddMinutes(-11));
        Set(vm, "_lastProgressTime", DateTime.MinValue);
        EnumField(vm, "_huntState", "Calling");
        EnumField(vm, "_qsoStage", "CallingInitial");
        var liveRestPolicy = (ScavengerTargetRestPolicy)Get(vm, "_scavengerTargetRest")!;
        liveRestPolicy.RecordAttempt(target.Callsign, DateTime.Now.AddMinutes(-11));
        Check((bool)Call(vm, "ScavengerCallingLimitReached", target)!, "Active Scavenger must enforce the configured timer.");
        Set(vm, "_operatingMode", HuntingOperatingMode.WantedSniper);
        Check(!(bool)Call(vm, "ScavengerCallingLimitReached", target)!, "Wanted Sniper calling behavior must remain unchanged.");
        Set(vm, "_operatingMode", HuntingOperatingMode.Scavenger);
        EnumField(vm, "_qsoStage", "TargetReportSeen");
        Check(!(bool)Call(vm, "ScavengerCallingLimitReached", target)!, "A target report must cancel the pending Scavenger time-limit release.");
        EnumField(vm, "_qsoStage", "CompletionPending");
        Check(!(bool)Call(vm, "ScavengerCallingLimitReached", target)!, "Final 73/QSO completion must not be interrupted by the timer.");
        Set(vm, "_lockedTarget", null);

        var samples = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        Set(vm, "_scavengerStations", samples);
        Set(vm, "_scavengerBand", display.Bands[0]);
        Set(vm, "_scavengerSampleStart", start);
        Set(vm, "_scavengerSampleEnd", start.AddSeconds(63));
        var heard = new DecodeMessage { ContactableCall = "G4ABC", Callsign = "G4ABC", Band = "40m", DecodeTime = start.ToUniversalTime().TimeOfDay, ReceivedAt = start.AddSeconds(15), DistanceKm = 160.9344 };
        Call(vm, "ObserveScavengerDecode", heard);
        Call(vm, "ObserveScavengerDecode", heard);
        Check(samples.Count == 1, "Repeated decodes must count as one station, not busy traffic.");
        Check(Math.Abs(samples["G4ABC"]!.Value - 100) < 0.1, "Nearby classification must use miles.");
        heard.ContactableCall = heard.Callsign = "K7ABC";
        heard.Band = "20m";
        Call(vm, "ObserveScavengerDecode", heard);
        Check(samples.Count == 1, "A decode from another band must not contaminate the sample.");
        heard.Band = "40m";
        heard.DecodeTime = start.AddSeconds(-15).ToUniversalTime().TimeOfDay;
        Call(vm, "ObserveScavengerDecode", heard);
        Check(samples.Count == 1, "Partial pre-entry slot must be excluded.");
        heard.DecodeTime = start.AddSeconds(60).ToUniversalTime().TimeOfDay;
        Call(vm, "ObserveScavengerDecode", heard);
        Check(samples.Count == 1, "A slot starting after the sample must be excluded.");
        heard.DecodeTime = start.AddSeconds(45).ToUniversalTime().TimeOfDay;
        Call(vm, "ObserveScavengerDecode", heard);
        Check(samples.Count == 2, "The last full receive slot must be included.");
        Set(vm, "_scavengerSearch", new ScavengerSearchPolicy());
        Set(vm, "_scavengerTrackedTarget", target);
        Set(vm, "_scavengerWatchEnd", DateTime.Now.AddMinutes(5));
        Call(vm, "CompleteScavengerTarget", true);
        Check(Get(vm, "_scavengerTrackedTarget") == null && Get(vm, "_scavengerWatchEnd") == null && samples.Count == 0, "Completed target must clear watch and discard incomparable interrupted listening sample.");
        Check((string?)Get(vm, "_scavengerPostQsoBand") == "40m" && ((ScavengerSearchPolicy)Get(vm, "_scavengerSearch")!).SkipsRemaining("40m") == 0, "Successful QSO must schedule fresh listening without marking the band visited.");
        Call(vm, "StartPendingScavengerPostQsoListen", "40m");
        var freshStart = (DateTime)Get(vm, "_scavengerSampleStart")!;
        var freshEnd = (DateTime)Get(vm, "_scavengerSampleEnd")!;
        Check(Get(vm, "_scavengerBand") == display.Bands[0] && freshEnd - freshStart == TimeSpan.FromSeconds(display.ListenMinutes * 60 + 3), "Post-QSO sample must use a full configured listen period on the same band.");
        Check((bool)Get(vm, "_scavengerPostQsoListening")! && Get(vm, "_scavengerPostQsoBand") == null, "Post-QSO restart must happen once, not extend the timer every tick.");
        Call(vm, "StartPendingScavengerPostQsoListen", "40m");
        Check((DateTime)Get(vm, "_scavengerSampleStart")! == freshStart, "Repeated tick must not restart an already running post-QSO sample.");
        Call(vm, "CompleteScavengerTarget", false);
        Check(Get(vm, "_scavengerPostQsoBand") == null && Get(vm, "_scavengerBand") == null, "Completion timeout without a logged QSO must retain existing onward-search behavior.");
        Set(vm, "_scavengerPostQsoBand", "40m");
        Call(vm, "StartPendingScavengerPostQsoListen", "30m");
        Check(Get(vm, "_scavengerBand") == null && Get(vm, "_scavengerPostQsoBand") == null, "Manual band change must cancel a pending post-QSO listen, never move the radio back.");
        var searchCancellation = new CancellationTokenSource();
        var searchToken = searchCancellation.Token;
        Set(vm, "_scavengerCancellation", searchCancellation);
        Call(vm, "StopScavengerSearch");
        Check(searchToken.IsCancellationRequested && Get(vm, "_scavengerCancellation") == null, "Stop must cancel any pending band movement token.");
        Set(auto, "_cts", null);
        Check(vm.CanConfigureScavenger, "Configuration must unlock after stop.");
        ((Task)Call(vm, "ScavengerTickAsync")!).GetAwaiter().GetResult();
        Check(Get(vm, "_lockedTarget") == null, "Stopped tick must remain a no-op without accessing radio services.");
        EnumField(vm, "_huntState", "Idle");
        Check(vm.CanChangeAchievementProfile, "Global profile selector must unlock when stopped and idle.");
        Set(auto, "_cts", fakeRunning);
        Check(!vm.CanChangeAchievementProfile, "Global profile must stay locked during every hunting mode.");
        Set(auto, "_cts", null);

        var achievements = new AchievementsViewModel();
        CallsignLogProfile[] profiles = [new("ALL", "", "All callsigns", 2, true, false, []), new("G1CEC", "G1CEC", "G1CEC", 1, false, true, [])];
        AdifQso[] profileQsos = [new() { Call = "4S7AB", Dxcc = "315", StationCallsign = "G1CEC" }, new() { Call = "FS4WBS", Dxcc = "213", StationCallsign = "2E0CCD" }];
        var resolver = new DxccResolver(Path.Combine(AppContext.BaseDirectory, "not-supplied.csv"));
        var rarity = new DxccRarityService();
        achievements.UpdateData(profileQsos, [], profiles, [], resolver, rarity, "ALL");
        Check(achievements.ProfileQsoCount == 2, "All-callsigns profile must include both identities in Achievements.");
        achievements.UpdateData(profileQsos, [], profiles, [], resolver, rarity, "G1CEC");
        Check(achievements.SelectedProfileKey == "G1CEC" && achievements.ProfileQsoCount == 1, "Global profile change must override old Achievements display selection.");
        achievements.UpdateData(profileQsos, [], profiles, [], resolver, rarity, "ALL");
        Check(achievements.ProfileQsoCount == 2 && achievements.SelectedProfileKey == "ALL", "Global All-callsigns selection must restore full Achievements totals.");

        if (args.Length > 0) RenderPreview(display, args[0]);
        Console.WriteLine($"Scavenger smoke tests passed: {_checks} checks. No live radio or operator settings accessed.");
    }

    private static void RenderPreview(ScavengerViewModel display, string output)
    {
        var app = new JtdxAutoResume.V3.App();
        app.InitializeComponent(); // resources only; never call Run or show a live application
        var previewSettings = new AppSettings();
        display = new ScavengerViewModel(() => previewSettings, new BandAnalysisViewModel(previewSettings).Bands);
        var previewDecodes = new DxAssistViewModel();
        for (var i = 0; i < 14; i++) previewDecodes.RecentDecodes.Add(new DecodeMessage
        {
            Callsign = $"TEST{i}", ContactableCall = $"TEST{i}", EntityName = "Layout test station", Grid = "JO01",
            Snr = -10, RawText = $"CQ TEST{i} JO01", Band = "40m", Mode = "FT8"
        });
        display.Phase = "Listening";
        display.Status = "40m · 42s remaining · 17 stations · round 3";
        display.TargetSummary = "TEST0 · Namibia — Unconfirmed DXCC: worked, not LoTW confirmed";
        display.FilterSummary = "Hunting: DXCC on · Grids on · USA states off. Scopes: overall + band. Never-worked overall DXCC always has priority.";
        display.WantedGrids = true;
        for (var p = 0; p < 3; p++)
        {
            for (var i = 0; i < (p == 0 ? 5 : 3); i++)
            {
                var sample = previewDecodes.RecentDecodes[i];
                display.OpportunityPanels[p].Rows.Add(new(sample,
                    p == 0 ? "Unconfirmed DXCC: Namibia" : p == 1 ? "New grid: JO01 on 40m" : "Unconfirmed state: TX",
                    p == 2 ? "Category off" : i == 0 ? "Current target" : i == 1 ? "Resting · 2 rounds" : "Eligible", p < 2 && i == 0)
                { Category = p == 0 ? "DXCC" : p == 1 ? "grid" : "state", Need = i == 0 ? NeedStatus.NeverWorked : NeedStatus.WorkedNotLoTWConfirmed });
            }
        }
        display.Round = 3;
        display.Bands[3].Status = "Listening";
        display.Bands[3].Activity = "17 stations this visit";
        display.Bands[3].Revisit = "Current band";
        display.Bands[7].Status = "Silent";
        display.Bands[7].Activity = "0 stations · distance unknown";
        display.Bands[7].Revisit = "Resting · 3 rounds until due";
        var view = new ScavengerView
        {
            Width = 1500, Height = 780,
            DataContext = new
            {
                Scavenger = display, CanConfigureScavenger = true,
                SelectedAchievementProfileDisplayLabel = "G1CEC",
                CurrentTargetStatus = new TargetStatusSummaryViewModel(),
                StartScavengerCommand = new RelayCommand(() => { }), StopAutoResumeCommand = new RelayCommand(() => { }),
                DxAssist = previewDecodes, CurrentBand = "40m", LookupQrzCommand = new RelayCommand(() => { })
            }
        };
        var surface = new System.Windows.Controls.Border { Child = view, Background = new SolidColorBrush(Color.FromRgb(238, 243, 247)) };
        ((System.Windows.Controls.Expander)((System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)((System.Windows.Controls.Grid)view.Content).Children[0]).Child).Children.OfType<System.Windows.Controls.Expander>().Single()).IsExpanded = false;
        surface.Measure(new Size(1500, 780));
        surface.Arrange(new Rect(0, 0, 1500, 780));
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1500, 780, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var file = File.Create(output);
        encoder.Save(file);
        RenderGlobalHeader(output + ".header.png");
    }

    private static void RenderGlobalHeader(string output)
    {
        var view = new OperatingHeader();
        var wanted = new WantedViewModel();
        wanted.AchievementProfiles.Add(new("G1CEC", "G1CEC", "G1CEC", 100, false, true, []));
        view.DataContext = new HeaderPreviewData
        {
            Wanted = wanted, SelectedAchievementProfileKey = "G1CEC", SelectedAchievementProfileDisplayLabel = "G1CEC",
            CanChangeAchievementProfile = false, AchievementProfileSummary = "Reviewing G1CEC", CurrentBand = "40m", CurrentDigitalMode = "FT8",
            CurrentTargetStatus = new TargetStatusSummaryViewModel { OperatingMode = "Scavenger", SelectedTargetDisplay = "Listening", TxGateStatus = "TX disabled — searching" },
            IsDxAssistActive = false, IsWantedSniperActive = false, IsLocationHuntActive = false,
            StartDxAssistCommand = new RelayCommand(() => { }), StartWantedSniperCommand = new RelayCommand(() => { }),
            StartLocationHuntCommand = new RelayCommand(() => { }), StopAutoResumeCommand = new RelayCommand(() => { })
        };
        view.Measure(new Size(1180, double.PositiveInfinity));
        var height = Math.Ceiling(view.DesiredSize.Height);
        view.Arrange(new Rect(0, 0, 1180, height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1180, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output);
        encoder.Save(file);
    }

    private sealed class HeaderPreviewData
    {
        public WantedViewModel Wanted { get; set; } = new();
        public string SelectedAchievementProfileKey { get; set; } = "G1CEC";
        public string SelectedAchievementProfileDisplayLabel { get; set; } = "G1CEC";
        public bool CanChangeAchievementProfile { get; set; }
        public string AchievementProfileSummary { get; set; } = "";
        public string CurrentBand { get; set; } = "40m";
        public string CurrentDigitalMode { get; set; } = "FT8";
        public TargetStatusSummaryViewModel CurrentTargetStatus { get; set; } = new();
        public bool IsDxAssistActive { get; set; }
        public bool IsWantedSniperActive { get; set; }
        public bool IsLocationHuntActive { get; set; }
        public bool IsScavengerActive { get; set; } = true;
        public bool CanConfigureScavenger { get; set; }
        public RelayCommand StartScavengerCommand { get; set; } = new(() => { });
        public RelayCommand StartDxAssistCommand { get; set; } = new(() => { });
        public RelayCommand StartWantedSniperCommand { get; set; } = new(() => { });
        public RelayCommand StartLocationHuntCommand { get; set; } = new(() => { });
        public RelayCommand StopAutoResumeCommand { get; set; } = new(() => { });
    }
}
