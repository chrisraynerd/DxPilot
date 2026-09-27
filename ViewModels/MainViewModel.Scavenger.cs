using System.Windows.Input;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.ViewModels;

public sealed partial class MainViewModel
{
    public ScavengerViewModel Scavenger { get; private set; } = null!;
    public ICommand StartScavengerCommand { get; private set; } = null!;
    public bool IsScavengerActive => _autoResume.IsRunning && _operatingMode == HuntingOperatingMode.Scavenger;
    public bool CanConfigureScavenger => !IsScavengerActive && !_scavengerMoving && !_scavengerStarting;
    private ScavengerSearchPolicy _scavengerSearch = new();
    private ScavengerTargetRestPolicy _scavengerTargetRest = new();
    private CancellationTokenSource? _scavengerCancellation;
    private bool _scavengerMoving;
    private bool _scavengerStarting;
    private int _scavengerMovementFailures;
    private ScavengerBandRow? _scavengerBand;
    private DateTime _scavengerSampleStart;
    private DateTime _scavengerSampleEnd;
    private long _scavengerSampleGeneration;
    private bool _scavengerSampleInterrupted;
    private readonly Dictionary<string, double?> _scavengerStations = new(StringComparer.OrdinalIgnoreCase);
    private DxTarget? _scavengerTrackedTarget;
    private DateTime? _scavengerWatchStarted;
    private DateTime? _scavengerWatchEnd;
    private DateTime? _scavengerReceivePausedAt;
    private string? _scavengerPostQsoBand;
    private bool _scavengerPostQsoListening;

    private async void StartScavenger()
    {
        if (IsScavengerActive || _scavengerMoving || _scavengerStarting || RejectHuntingWhileBandSurveyRuns()) return;
        if (_bandAnalysisOperationInProgress || _conditionsEvaluationRunning || _lockedTarget != null
            || _targetSelectionInProgress || _immediateTxRetargetInProgress || _lateReplyRecoveryInProgress)
        {
            Scavenger.Status = "Finish the current contact or operation (or press Stop All) before starting Scavenger.";
            return;
        }
        if (!BandAnalysisViewModel.HasUsableCalibration(Settings.Settings))
        {
            Scavenger.Status = "Map the band button strip in Band Analysis before starting Scavenger.";
            return;
        }
        if (!Scavenger.Bands.Any(b => b.BandChoice.Enabled))
        {
            Scavenger.Status = "Select at least one permitted band.";
            return;
        }
        StopScavengerSearch();
        _scavengerStarting = true;
        OnPropertyChanged(nameof(CanConfigureScavenger));
        _scavengerCancellation = new();
        var startToken = _scavengerCancellation.Token;
        // Finish the previous mode's ownership before enabling the scavenger gate.
        _autoResume.Stop();
        _huntTimer.Stop();
        _callNowSession.Reset();
        _recentCallAttempts.Clear();
        _operatingMode = HuntingOperatingMode.Scavenger;
        _conditionsPendingReason = "";
        _conditionsProductivityHandoverRequested = _conditionsSafeHandoverRequested = false;
        BandAnalysis.HideAnalysisBanner();
        _scavengerSearch = new();
        _scavengerMovementFailures = 0;
        Scavenger.Round = 1;
        foreach (var row in Scavenger.Bands)
        {
            row.Status = row.BandChoice.Enabled ? "Waiting" : "Disabled";
            row.Activity = "—";
            row.Revisit = "Next visit";
        }
        try
        {
            await ReleaseLockedTargetAndMaybeResumeAsync("Starting Scavenger", "Released for Scavenger", false, false);
            startToken.ThrowIfCancellationRequested();
            Scavenger.Phase = "Starting";
            Scavenger.Status = "Receive-only search for selected wanted categories. New DXCC has priority; confirmation follows the global profile and settings.";
            await StartAutoResumeAsync();
            if (!IsScavengerActive) StopScavengerSearch();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StopAll();
            Scavenger.Status = $"Scavenger stopped: {ex.GetBaseException().Message}";
            AddAction(Scavenger.Status);
        }
        finally { _scavengerStarting = false; }
        RefreshModeIndicators();
    }

    private void StopScavengerSearch()
    {
        _scavengerCancellation?.Cancel();
        _scavengerCancellation?.Dispose();
        _scavengerCancellation = null;
        _scavengerBand = null;
        _scavengerTrackedTarget = null;
        _scavengerWatchStarted = _scavengerWatchEnd = null;
        _scavengerReceivePausedAt = null;
        _scavengerPostQsoBand = null;
        _scavengerPostQsoListening = false;
        _scavengerStations.Clear();
        if (Scavenger != null)
        {
            _scavengerTargetRest = new();
            Scavenger.RestingTargets = "";
            Scavenger.TargetSummary = "Scavenger stopped — no target selected.";
            Scavenger.Phase = "Stopped";
            Scavenger.Status = "Scavenger stopped. Last visit results are retained.";
        }
        OnPropertyChanged(nameof(CanConfigureScavenger));
    }

    private bool IsScavengerNewDxcc(DecodeMessage decode) => !string.IsNullOrWhiteSpace(decode.Dxcc)
        && !decode.EntityName.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
        && IsUnconfirmedDxccNeed(EvaluateDxccNeed(decode.Dxcc, decode.Band, decode.Mode, WantedScope.Overall));

    private sealed record ScavengerNeed(int Priority, string Category, string Value, WantedScope Scope, NeedStatus Need, string Reason);
    private bool IsScavengerProtectedDxcc(DecodeMessage decode) => GetScavengerNeed(decode) is { Category: "DXCC", Scope: WantedScope.Overall };

    private ScavengerNeed? GetScavengerNeed(DecodeMessage decode) => GetScavengerNeeds(decode).FirstOrDefault();

    private IEnumerable<ScavengerNeed> GetScavengerNeeds(DecodeMessage decode, bool includeDisabledCategories = false)
    {
        var settings = Settings.Settings;
        var needs = new List<ScavengerNeed>();
        var scopes = new List<WantedScope> { WantedScope.Overall };
        if (settings.ScavengerIncludeBand && !string.IsNullOrWhiteSpace(decode.Band)) scopes.Add(WantedScope.CurrentBand);
        if (settings.ScavengerIncludeMode && !string.IsNullOrWhiteSpace(decode.Mode)) scopes.Add(WantedScope.CurrentMode);
        if (settings.ScavengerIncludeBandMode && !string.IsNullOrWhiteSpace(decode.Band) && !string.IsNullOrWhiteSpace(decode.Mode)) scopes.Add(WantedScope.CurrentBandMode);
        void Add(string category, string value, Func<WantedScope, NeedStatus> evaluate, int priority)
        {
            foreach (var scope in scopes)
            {
                var need = evaluate(scope);
                if (!IsUnconfirmedDxccNeed(need)) continue;
                var rank = category == "DXCC" && scope == WantedScope.Overall ? (need == NeedStatus.NeverWorked ? 0 : 1)
                    : priority + (need == NeedStatus.NeverWorked ? 0 : 1);
                needs.Add(new(rank, category, value, scope, need, BuildWantedReason(need, category, value, decode.Band, decode.Mode, scope)));
            }
        }
        if (!string.IsNullOrWhiteSpace(decode.Dxcc) && !decode.EntityName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            var overall = EvaluateDxccNeed(decode.Dxcc, decode.Band, decode.Mode, WantedScope.Overall);
            // Never-worked DXCC remains an explicit global override, just as in the other hunting modes.
            if (includeDisabledCategories || settings.ScavengerWantedDxcc || overall == NeedStatus.NeverWorked)
                Add("DXCC", decode.EntityName, scope => EvaluateDxccNeed(decode.Dxcc, decode.Band, decode.Mode, scope), 10);
        }
        if ((includeDisabledCategories || settings.ScavengerWantedStates) && WasStateEligibility.IsEligible(decode) && IsValidState(decode.State))
            Add("state", decode.State, scope => EvaluateSimpleNeed(_adifMergeResult.Indexes.States.GetValueOrDefault(decode.State), decode.Band, decode.Mode, scope), 20);
        if ((includeDisabledCategories || settings.ScavengerWantedGrids) && IsValidGrid(decode.Grid))
        {
            var grid = MaidenheadGrid.Normalize(decode.Grid).Grid4;
            Add("grid", grid, scope => EvaluateSimpleNeed(_adifMergeResult.Indexes.Grids.GetValueOrDefault(grid), decode.Band, decode.Mode, scope), 30);
        }
        return needs.OrderBy(n => n.Priority).ThenBy(n => n.Scope);
    }

    private void RefreshScavengerOpportunities()
    {
        if (Scavenger == null) return;
        var settings = Settings.Settings;
        Scavenger.FilterSummary = $"Hunting: DXCC {(settings.ScavengerWantedDxcc ? "on" : "off")} · Grids {(settings.ScavengerWantedGrids ? "on" : "off")} · USA states {(settings.ScavengerWantedStates ? "on" : "off")}. "
            + "Scopes: overall" + (settings.ScavengerIncludeBand ? " + band" : "")
            + (settings.ScavengerIncludeMode ? " + mode" : "") + (settings.ScavengerIncludeBandMode ? " + band/mode" : "")
            + ". Never-worked overall DXCC always has priority.";
        var active = IsScavengerActive;
        var target = active ? _lockedTarget : null;
        Scavenger.TargetSummary = target != null
            ? $"{target.Callsign} · {target.Decode.EntityName} — {_wantedReason}"
            : active && _scavengerTrackedTarget != null
                ? $"Watching {_scavengerTrackedTarget.Callsign} · {_scavengerTrackedTarget.Decode.EntityName} — no confirmed selection in JTDX."
                : active ? "Searching — no station currently locked." : "Scavenger stopped — opportunities shown for the current band.";
        var snapshots = Scavenger.OpportunityPanels.ToDictionary(p => p.Category, _ => new List<ScavengerOpportunityRow>());
        foreach (var group in CurrentCandidateDecodes().Where(d => !string.IsNullOrWhiteSpace(DecodeTargetCall(d)))
                     .GroupBy(DecodeTargetCall, StringComparer.OrdinalIgnoreCase))
        {
            var decode = group.OrderByDescending(IsSelectableDecodeForAcquisition).ThenByDescending(d => d.ReceivedAt).First();
            var call = DecodeTargetCall(decode);
            var chosenNeed = GetScavengerNeed(decode);
            foreach (var need in GetScavengerNeeds(decode, true).GroupBy(n => n.Category).Select(g => g.First()))
            {
                var enabled = need.Category switch
                {
                    "DXCC" => settings.ScavengerWantedDxcc || (need.Scope == WantedScope.Overall && need.Need == NeedStatus.NeverWorked),
                    "grid" => settings.ScavengerWantedGrids,
                    _ => settings.ScavengerWantedStates
                };
                var isTarget = target?.Callsign.Equals(call, StringComparison.OrdinalIgnoreCase) == true;
                var resting = _scavengerTargetRest.RoundsRemaining(call, _scavengerSearch.Round);
                var state = !enabled ? "Category off"
                    : isTarget ? (chosenNeed?.Category == need.Category ? "Current target" : "Target · also needed")
                    : resting > 0 ? $"Resting · {resting} rounds"
                    : IsPermanentlySuppressed(call) ? "Suppressed indefinitely"
                    : IsSuppressed(call) ? "Temporarily suppressed"
                    : _sessionWorked.Contains(call) || IsRecentlyWorkedLive(call) ? "Worked recently"
                    : IsFailedReplySource(decode) ? "Waiting for fresh source"
                    : !Scavenger.Bands.Any(b => b.BandChoice.Enabled && b.Band.Equals(decode.Band, StringComparison.OrdinalIgnoreCase)) ? "Band not permitted"
                    : !IsSelectableDecodeForAcquisition(decode) ? "Not selectable yet"
                    : "Eligible";
                snapshots[need.Category].Add(new(decode, need.Reason, state, isTarget)
                {
                    Category = need.Category, Need = need.Need,
                    IsInQso = isTarget && _huntState == HuntState.InQso
                });
            }
        }
        foreach (var panel in Scavenger.OpportunityPanels)
        {
            var rows = snapshots[panel.Category].OrderByDescending(r => r.IsTarget)
                .ThenByDescending(r => r.Status == "Eligible").ThenByDescending(r => r.Decode.IsLotwUser)
                .ThenBy(r => DecodeTargetCall(r.Decode), StringComparer.OrdinalIgnoreCase).ToArray();
            // Avoid rebuilding unchanged tables every hunting tick.
            if (panel.Rows.SequenceEqual(rows)) continue;
            panel.Rows.Clear();
            foreach (var row in rows) panel.Rows.Add(row);
        }
    }

    private bool RejectSurveyDuringScavenger()
    {
        if (!IsScavengerActive && !_scavengerMoving && !_scavengerStarting) return false;
        BandAnalysis.Status = BandAnalysis.PskProbeStatus = "Stop Scavenger before starting Band Analysis or testing band movement.";
        return true;
    }

    private void CompleteScavengerTarget(bool successfullyLogged)
    {
        var completedTarget = _lockedTarget ?? _scavengerTrackedTarget;
        if (completedTarget != null) _scavengerTargetRest.ForgetAttempts(completedTarget.Callsign);
        var visitedBand = _scavengerBand ?? Scavenger.Bands.FirstOrDefault(b => b.Band == completedTarget?.Band);
        _scavengerTrackedTarget = null;
        _scavengerWatchStarted = _scavengerWatchEnd = null;
        _scavengerPostQsoBand = successfullyLogged ? visitedBand?.Band : null;
        _scavengerPostQsoListening = false;
        if (visitedBand != null)
        {
            visitedBand.Status = successfullyLogged ? "QSO completed — listening next" : "Target sequence completed";
            if (!successfullyLogged) _scavengerSearch.Complete(visitedBand.Band, 0);
        }
        _scavengerBand = null;
        _scavengerStations.Clear();
    }

    private void ObserveScavengerDecode(DecodeMessage decode)
    {
        if (!IsScavengerActive || _scavengerBand == null || _scavengerMoving
            || !decode.Band.Equals(_scavengerBand.Band, StringComparison.OrdinalIgnoreCase)
            || !decode.DecodeTime.HasValue) return;
        var slot = DecodeSlotStart(decode.DecodeTime.Value, decode.ReceivedAt);
        if (slot < _scavengerSampleStart || slot >= _scavengerSampleEnd.AddSeconds(-3)) return;
        var call = DecodeTargetCall(decode);
        if (string.IsNullOrWhiteSpace(call)) return;
        _scavengerStations.TryGetValue(call, out var distance);
        _scavengerStations[call] = decode.DistanceMiles.HasValue
            ? Math.Max(distance ?? 0, decode.DistanceMiles.Value) : distance;
    }

    private DxTarget? FindScavengerTarget(string? onlyCall = null)
    {
        if (!Scavenger.Bands.Any(b => b.BandChoice.Enabled && b.Band.Equals(CurrentReportedBand(), StringComparison.OrdinalIgnoreCase)))
            return null;
        var candidates = CurrentCandidateDecodes()
            .Where(d => GetScavengerNeed(d) != null)
            .Where(d => _scavengerTargetRest.RoundsRemaining(DecodeTargetCall(d), _scavengerSearch.Round) == 0)
            .Where(d => onlyCall == null || DecodeTargetCall(d).Equals(onlyCall, StringComparison.OrdinalIgnoreCase))
            .Where(IsSelectableDecodeForAcquisition)
            .Where(d => !IsFailedReplySource(d) && !IsPermanentlySuppressed(DecodeTargetCall(d)))
            .Where(d => !_sessionWorked.Contains(DecodeTargetCall(d)) && !IsRecentlyWorkedLive(DecodeTargetCall(d)))
            .Where(d => onlyCall != null || !IsSuppressed(DecodeTargetCall(d)))
            .ToList();
        return ScavengerSearchPolicy.SelectTarget(
            _targetSelector.SelectRanked(candidates, _logbook, _adifMergeResult.Indexes, Settings.Settings, 500, false),
            t => GetScavengerNeed(t.Decode)!.Priority);
    }

    private async Task ScavengerTickAsync()
    {
        if (!IsScavengerActive || _scavengerMoving || _scavengerCancellation == null) return;
        try { await ScavengerTickCoreAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StopAll();
            Scavenger.Status = $"Scavenger stopped: {ex.GetBaseException().Message}";
            AddAction(Scavenger.Status);
        }
    }

    private async Task ScavengerTickCoreAsync()
    {
        if (!IsScavengerActive || _scavengerMoving || _scavengerCancellation == null) return;
        Scavenger.RestingTargets = _scavengerTargetRest.Summary(_scavengerSearch.Round);
        if (await RestScavengerTargetIfDueAsync()) return;
        if (_scavengerTrackedTarget != null
            && !_scavengerTrackedTarget.Decode.Band.Equals(CurrentReportedBand(), StringComparison.OrdinalIgnoreCase))
        {
            StopAll();
            Scavenger.Status = "Scavenger stopped: the band changed while a New DXCC was protected.";
            AddAction(Scavenger.Status);
            return;
        }
        if (_lockedTarget != null)
        {
            var persistentDxcc = IsScavengerProtectedDxcc(_lockedTarget.Decode);
            if (persistentDxcc) _scavengerTrackedTarget ??= _lockedTarget;
            if (!persistentDxcc && _huntState == HuntState.Calling && _qsoStage < QsoStage.TargetReportSeen
                && !_targetSelectionInProgress && !_immediateTxRetargetInProgress)
            {
                var priorityTarget = FindScavengerTarget();
                if (priorityTarget != null && IsScavengerProtectedDxcc(priorityTarget.Decode)
                    && priorityTarget.Callsign != _lockedTarget.Callsign)
                {
                    ClearLockedTarget("Scavenger: wanted DXCC takes priority before the current grid/state target has replied.");
                    await AcquireScavengerTargetAsync(priorityTarget);
                    return;
                }
            }
            Scavenger.Phase = !_targetConfirmedInJtdx ? "Waiting for target selection"
                : persistentDxcc ? "Wanted DXCC protected" : "Calling wanted target";
            Scavenger.Status = !_targetConfirmedInJtdx
                ? $"{_lockedTarget.Callsign} · Waiting for JTDX to confirm selection."
                : $"{_lockedTarget.Callsign} · {_lockedTarget.Decode.EntityName} · {CallAttemptProgressText()} calls. "
                    + (persistentDxcc ? $"Unanswered calling limit: {Scavenger.MaxCallingMinutes} minutes." : "Normal call limits apply; search resumes after release.");
            var targetCard = Scavenger.Bands.FirstOrDefault(b => b.Band == _lockedTarget.Band);
            if (targetCard != null) targetCard.Status = _targetConfirmedInJtdx
                ? $"Calling {_lockedTarget.Callsign}" : $"Selecting {_lockedTarget.Callsign}";
            // Preserve final-73 completion handling in the established QSO engine.
            if (_qsoStage != QsoStage.CompletionPending && PersistentNewDxccHasGoneStale())
            {
                await BeginScavengerReturnWatchAsync();
                return;
            }
            await MaintainLockedTargetAsync();
            return;
        }

        EnsureEnableTxOff("Scavenger listening");
        if (_targetSelectionInProgress || _immediateTxRetargetInProgress || _lateReplyRecoveryInProgress) return;
        var status = _udpListener.LastStatus;
        if (!ScavengerSearchPolicy.HasReceiveEvidence(status, DateTime.Now, ActiveReceivePeriod()))
        {
            if (_scavengerBand != null) _scavengerSampleInterrupted = true;
            _scavengerReceivePausedAt ??= DateTime.Now;
            Scavenger.Phase = "Waiting for receive status";
            Scavenger.Status = "Listening paused: waiting for JTDX receive-only status. Incomplete samples will not be scored as silence.";
            return;
        }
        if (_scavengerReceivePausedAt.HasValue)
        {
            if (_scavengerWatchEnd.HasValue)
                _scavengerWatchEnd = _scavengerWatchEnd.Value + (DateTime.Now - _scavengerReceivePausedAt.Value);
            _scavengerReceivePausedAt = null;
        }
        if (_scavengerTrackedTarget != null)
        {
            var tracked = _scavengerTrackedTarget;
            if (HasFreshLiveQso(tracked.Callsign))
            {
                CompleteScavengerTarget(successfullyLogged: true);
            }
            else
            {
                var lastHeard = LastHeardUtc(tracked.Callsign, tracked.Decode).ToLocalTime();
                if (!_scavengerWatchEnd.HasValue && DateTime.Now - lastHeard > TimeSpan.FromSeconds(NewDxccStaleSeconds()))
                    await BeginScavengerReturnWatchAsync();
                if (_scavengerWatchEnd.HasValue && lastHeard > _scavengerWatchStarted)
                {
                    AddAction($"Scavenger: {tracked.Callsign} returned; resuming protected acquisition.");
                    _scavengerWatchStarted = _scavengerWatchEnd = null;
                }
                if (_scavengerWatchEnd.HasValue && DateTime.Now >= _scavengerWatchEnd)
                {
                    AddAction($"Scavenger: five-minute return watch for {tracked.Callsign} ended; continuing search.");
                    _scavengerTrackedTarget = null;
                    _scavengerWatchStarted = _scavengerWatchEnd = null;
                    // A long target hold is not a comparable listening sample.
                    var visitedBand = _scavengerBand ?? Scavenger.Bands.FirstOrDefault(b => b.Band == tracked.Band);
                    if (visitedBand != null)
                    {
                        visitedBand.Status = "Return watch ended";
                        _scavengerSearch.Complete(visitedBand.Band, 0);
                    }
                    _scavengerBand = null;
                }
                else
                {
                    Scavenger.Phase = _scavengerWatchEnd.HasValue ? "Waiting for return" : "Recovering target selection";
                    Scavenger.Status = _scavengerWatchEnd.HasValue
                        ? $"Staying on {CurrentReportedBand()} for {tracked.Callsign} · {Math.Ceiling((_scavengerWatchEnd.Value - DateTime.Now).TotalSeconds)}s remaining. Receive only."
                        : $"Staying with {tracked.Callsign}; waiting for a usable fresh decode. Target remains protected until stale.";
                    if (!_scavengerWatchEnd.HasValue && RadioContextReadyForSelection())
                    {
                        var recovered = FindScavengerTarget(tracked.Callsign);
                        if (recovered != null) await AcquireScavengerTargetAsync(recovered);
                    }
                    UpdateHuntStateDisplay();
                    return;
                }
            }
        }
        if (DateTime.Now < _postQsoTransitionUntil) return;
        StartPendingScavengerPostQsoListen(CurrentReportedBand());
        if (RadioContextReadyForSelection())
        {
            var target = FindScavengerTarget();
            if (target != null)
            {
                await AcquireScavengerTargetAsync(target);
                return;
            }
        }

        if (_scavengerBand != null && !CurrentReportedBand().Equals(_scavengerBand.Band, StringComparison.OrdinalIgnoreCase))
        {
            // User band changes restart observation and never mix two bands.
            _scavengerBand.Status = "Visit interrupted";
            _scavengerBand = null;
            _scavengerStations.Clear();
        }
        if (_scavengerBand != null && (_scavengerSampleInterrupted || _scavengerSampleGeneration != _radioContextGeneration))
        {
            StartScavengerSample(_scavengerBand);
            AddAction($"Scavenger: restarting {_scavengerBand.Band} listening sample after receive status/context was interrupted; incomplete data is not classified as silence.");
        }
        if (_scavengerBand != null && DateTime.Now < _scavengerSampleEnd)
        {
            Scavenger.Phase = DateTime.Now < _scavengerSampleStart ? "Synchronising"
                : _scavengerPostQsoListening ? "QSO completed — listening again" : "Listening";
            Scavenger.Status = $"{_scavengerBand.Band} · {Math.Ceiling((_scavengerSampleEnd - DateTime.Now).TotalSeconds)}s remaining · {_scavengerStations.Count} stations · round {_scavengerSearch.Round}";
            _scavengerBand.Activity = $"{_scavengerStations.Count} stations this visit";
            return;
        }
        if (!IsFreshTxStatus(status))
        {
            Scavenger.Phase = "Waiting for movement clearance";
            Scavenger.Status = "Listening visit finished; waiting for fresh JTDX receive-only confirmation before moving.";
            return;
        }
        if (_scavengerBand != null)
        {
            var located = _scavengerStations.Values.Count(d => d.HasValue);
            var farthest = _scavengerStations.Values.DefaultIfEmpty().Max() ?? 0;
            var skip = ScavengerSearchPolicy.Classify(_scavengerStations.Count, located, farthest, Scavenger.DeadSkipRounds, Scavenger.QuietSkipRounds);
            _scavengerBand.Status = _scavengerStations.Count == 0 ? "Silent" : skip > 0 ? "Few nearby stations" : "In rotation";
            _scavengerBand.Activity = $"{_scavengerStations.Count} stations · {(located == 0 ? "distance unknown" : $"furthest {farthest:0} mi")}";
            _scavengerSearch.Complete(_scavengerBand.Band, skip);
            AddAction($"Scavenger round {_scavengerSearch.Round}: {_scavengerBand.Band}, {_scavengerBand.Activity}; {_scavengerBand.Status}; skip {skip} rounds.");
            _scavengerBand = null;
        }
        var enabled = Scavenger.Bands.Where(b => b.BandChoice.Enabled).Select(b => b.Band).ToList();
        var next = _scavengerSearch.Next(enabled);
        if (next == null)
        {
            StopAll();
            Scavenger.Status = "Scavenger stopped: no permitted bands remain.";
            return;
        }
        var row = Scavenger.Bands.First(b => b.Band == next);
        var token = _scavengerCancellation.Token;
        _scavengerMoving = true;
        OnPropertyChanged(nameof(CanConfigureScavenger));
        try
        {
            Scavenger.Round = _scavengerSearch.Round;
            Scavenger.RestingTargets = _scavengerTargetRest.Summary(_scavengerSearch.Round);
            Scavenger.Phase = "Changing band";
            Scavenger.Status = $"Moving to {next} · round {Scavenger.Round}. All-resting rounds still include one real listening visit.";
            token.ThrowIfCancellationRequested();
            if (!await MoveToBandAndConfirmAsync(row.BandChoice, token))
            {
                row.Status = "Movement failed";
                _scavengerSearch.Complete(next, 1);
                if (++_scavengerMovementFailures >= enabled.Count)
                {
                    StopAll();
                    Scavenger.Status = "Scavenger stopped: consecutive movement failures. Check JTDX status and band mapping before restarting.";
                    AddAction(Scavenger.Status);
                    return;
                }
                Scavenger.Status = $"Could not confirm {next}; continuing with the other bands.";
                AddAction($"Scavenger: {Scavenger.Status}");
                return;
            }
            token.ThrowIfCancellationRequested();
            _scavengerMovementFailures = 0;
            StartScavengerSample(row);
            AddAction($"Scavenger: listening on {next}, full slots from {_scavengerSampleStart:HH:mm:ss} until {_scavengerSampleEnd:HH:mm:ss}. No CQ probes.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StopAll();
            Scavenger.Status = $"Scavenger stopped: {ex.GetBaseException().Message}";
            AddAction(Scavenger.Status);
        }
        finally
        {
            _scavengerMoving = false;
            foreach (var band in Scavenger.Bands)
                band.Revisit = !band.BandChoice.Enabled ? "Disabled" : ReferenceEquals(band, _scavengerBand) ? "Current band" : _scavengerSearch.SkipsRemaining(band.Band) > 0
                    ? $"Resting · {_scavengerSearch.SkipsRemaining(band.Band)} rounds until due" : "In rotation";
            OnPropertyChanged(nameof(CanConfigureScavenger));
        }
    }

    private bool ScavengerCallingLimitReached(DxTarget? target)
    {
        if (!IsScavengerActive || target == null || !IsScavengerProtectedDxcc(target.Decode)) return false;
        var progress = _lockedTarget != null && (_huntState == HuntState.InQso
            || _qsoStage >= QsoStage.TargetReportSeen || _lastProgressTime >= _targetStartedAt);
        return _scavengerTargetRest.LimitReached(target.Callsign, DateTime.Now, Scavenger.MaxCallingMinutes, progress);
    }

    private async Task<bool> RestScavengerTargetIfDueAsync()
    {
        var target = _lockedTarget ?? _scavengerTrackedTarget;
        if (target == null || !ScavengerCallingLimitReached(target)) return false;
        Scavenger.Phase = "Calling limit reached — final reply window";
        Scavenger.Status = $"{target.Callsign} · {Scavenger.MaxCallingMinutes}-minute limit. Finishing the current cycle and listening for a final reply before resting for {Scavenger.TargetRestRounds} search rounds.";
        // Never truncate TX, race an acquisition, or release on stale TX state.
        var status = _udpListener.LastStatus;
        if (_targetSelectionInProgress || _immediateTxRetargetInProgress || !IsFreshTxStatus(status) || status!.Transmitting)
            return true;
        if (status.TxEnabled)
        {
            EnsureEnableTxOff("Scavenger unanswered calling limit", true);
            return true;
        }
        if (DateTime.Now < _scavengerTargetRest.FinalReplyUntil(target.Callsign, ActiveAttemptCycle()))
            return true;
        if (HasFreshLiveQso(target.Callsign)) return false;
        var round = _scavengerSearch.Round;
        var rounds = Scavenger.TargetRestRounds;
        _scavengerTargetRest.RestCall(target.Callsign, round, rounds);
        await ReleaseLockedTargetAndMaybeResumeAsync(
            $"Scavenger unanswered calling limit: {target.Callsign}; resting for {rounds} complete search rounds",
            "Missed - Scavenger calling time limit", false, false);
        _scavengerTrackedTarget = null;
        _scavengerWatchStarted = _scavengerWatchEnd = null;
        var card = _scavengerBand ?? Scavenger.Bands.FirstOrDefault(b => b.Band == target.Band);
        if (card != null)
        {
            card.Status = $"{target.Callsign} resting";
            _scavengerSearch.Complete(card.Band, 0);
        }
        _scavengerBand = null;
        _scavengerStations.Clear();
        Scavenger.Phase = "Resuming search";
        Scavenger.RestingTargets = _scavengerTargetRest.Summary(round);
        Scavenger.Status = $"{target.Callsign} temporarily resting; other wanted stations remain eligible.";
        return true;
    }

    private async Task AcquireScavengerTargetAsync(DxTarget target)
    {
        var need = GetScavengerNeed(target.Decode);
        if (need == null || _scavengerTargetRest.RoundsRemaining(target.Callsign, _scavengerSearch.Round) > 0) return;
        _scavengerPostQsoListening = false;
        _scavengerPostQsoBand = null;
        _scavengerSampleInterrupted = true;
        _scavengerTrackedTarget = IsScavengerProtectedDxcc(target.Decode) ? target : null;
        _scavengerWatchStarted = _scavengerWatchEnd = null;
        Scavenger.Phase = "Wanted target found";
        Scavenger.Status = $"{target.Callsign} · {need.Reason}. Selecting in JTDX…";
        var bandCard = Scavenger.Bands.FirstOrDefault(b => b.Band == target.Band);
        if (bandCard != null) { bandCard.Status = $"Selecting {target.Callsign}"; bandCard.Revisit = "Current band"; }
        AddAction($"Scavenger: {Scavenger.Status}");
        target.Reasons.Insert(0, need.Reason);
        await LockAndReplyAsync(target, "Scavenger", need.Reason, $"Scavenger {need.Category}");
        if (!IsScavengerActive) return;
        if (_lockedTarget?.Callsign == target.Callsign && _targetConfirmedInJtdx)
        {
            Scavenger.Status = $"{target.Callsign} selected in JTDX. " + (_scavengerTrackedTarget != null ? $"Calling until stale or the {Scavenger.MaxCallingMinutes}-minute unanswered limit." : "Normal call limits apply.");
            if (bandCard != null) bandCard.Status = $"Calling {target.Callsign}";
        }
        else
        {
            Scavenger.Phase = "Waiting for target selection";
            Scavenger.Status = $"{target.Callsign} has not been confirmed in JTDX. See Recent Actions for the selection result.";
            if (bandCard != null) bandCard.Status = $"Selection pending: {target.Callsign}";
        }
    }

    private void StartScavengerSample(ScavengerBandRow row)
    {
        _scavengerPostQsoListening = false;
        _scavengerBand = row;
        _scavengerStations.Clear();
        _scavengerSampleStart = BandSurveyTiming.FirstEligibleSlotStart(DateTime.Now, ActiveReceivePeriod());
        _scavengerSampleEnd = ScavengerSearchPolicy.SampleEnd(_scavengerSampleStart, Scavenger.ListenMinutes, ActiveReceivePeriod());
        _scavengerSampleGeneration = _radioContextGeneration;
        _scavengerSampleInterrupted = false;
        row.Status = "Listening";
        row.Activity = "0 stations this visit";
    }

    private void StartPendingScavengerPostQsoListen(string currentBand)
    {
        if (_scavengerPostQsoBand == null) return;
        var band = _scavengerPostQsoBand;
        _scavengerPostQsoBand = null;
        // Never undo an operator's band change to return to the completed QSO.
        var row = Scavenger.Bands.FirstOrDefault(b => b.BandChoice.Enabled
            && b.Band.Equals(band, StringComparison.OrdinalIgnoreCase)
            && b.Band.Equals(currentBand, StringComparison.OrdinalIgnoreCase));
        if (row == null) return;
        StartScavengerSample(row);
        _scavengerPostQsoListening = true;
        row.Revisit = "Current band";
        row.Status = "QSO completed — listening again";
        Scavenger.Phase = "QSO completed — listening again";
        Scavenger.Status = $"Fresh {Scavenger.ListenMinutes}-minute listening period on {row.Band}. Other wanted stations can be called immediately.";
    }

    private async Task BeginScavengerReturnWatchAsync()
    {
        if (_scavengerWatchEnd.HasValue) return;
        _scavengerTrackedTarget ??= _lockedTarget;
        _scavengerWatchStarted = DateTime.Now;
        _scavengerWatchEnd = ScavengerSearchPolicy.ReturnWatchEnd(DateTime.Now);
        EnsureEnableTxOff("Scavenger target stale; five-minute receive-only return watch");
        await ReleaseLockedTargetAndMaybeResumeAsync("Scavenger target stale; listening for its return for five minutes", "Stale - return watch", false, false);
        Scavenger.Phase = "Waiting for return";
        var bandCard = Scavenger.Bands.FirstOrDefault(b => b.Band == CurrentReportedBand());
        if (bandCard != null) bandCard.Status = "Listening for return";
        AddAction($"Scavenger: return watch for {_scavengerTrackedTarget?.Callsign} on {CurrentReportedBand()} until {_scavengerWatchEnd:HH:mm:ss}.");
    }
}
