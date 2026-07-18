using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class DxAssistViewModel : ObservableObject
{
    private DxTarget? _bestTarget;
    private bool _autoSelectBestCq;
    private string _callingElapsed = "Not calling.";
    private string _moveOnAt = "";
    private string _qsoStageText = "";
    private string _lockedTargetText = "None";
    private string _callAttemptsText = "Call Attempts 0/0";
    private string _reportRepeatsText = "Report Repeats 0/0";
    private string _txVerificationText = "TX Verification: Unknown";
    private string _recoveryModeText = "Recovery Mode: None";
    private string _lastCorrectiveAction = "Last Corrective Action: None";
    private string _lastObservedTransmitState = "Last observed JTDX state: Unknown";
    private string _targetSourceText = "Target Source: None";
    private string _targetSourceRowText = "JTDX Row: -  Visible: No";
    private string _wantedReasonText = "";
    private string _bestCandidateText = "Best Candidate: None";
    private string _selectedIntendedTargetText = "Selected Intended Target: None";
    private string _activeLockedTargetText = "Active / Locked QSO Target: None";
    private string _actualJtdxDxCallText = "Actual JTDX DX Call: None";
    private string _targetStateWarningText = "";
    private string _txMismatchText = "TX Mismatch 0/0";
    private string _lastProgressFromTarget = "Last Progress From Target: None";
    private string _lastIntendedTx = "Last Intended TX: Unknown";
    private string _lastMyTx = "Last My TX: Unknown";
    private string _lastStageChange = "Last Stage Change: None";
    private string _stuckReasonText = "";
    private string _selectionMethodText = "Selection Method: None";
    private string _guiSelectionStatus = "GUI Selection: Not calibrated.";
    private DxCandidateRow? _selectedCandidate;
    private bool _showOnlyTargetable = true;
    private bool _showWantedOnly;
    private bool _showWorkedConfirmed = true;
    private bool _showStale = true;
    private bool _showSuppressed = true;
    private bool _showRawDiagnostics;

    public ObservableCollection<DecodeMessage> RecentDecodes { get; } = new();
    public ObservableCollection<string> RecentActions { get; } = new();
    public ObservableCollection<DxTarget> NextBestTargets { get; } = new();
    public ObservableCollection<DxCandidateRow> CandidateRows { get; } = new();

    public DxTarget? BestTarget
    {
        get => _bestTarget;
        set => SetProperty(ref _bestTarget, value);
    }

    public bool AutoSelectBestCq
    {
        get => _autoSelectBestCq;
        set => SetProperty(ref _autoSelectBestCq, value);
    }

    public string CallingElapsed
    {
        get => _callingElapsed;
        set => SetProperty(ref _callingElapsed, value);
    }

    public string MoveOnAt
    {
        get => _moveOnAt;
        set => SetProperty(ref _moveOnAt, value);
    }

    public string QsoStageText
    {
        get => _qsoStageText;
        set => SetProperty(ref _qsoStageText, value);
    }

    public string LockedTargetText
    {
        get => _lockedTargetText;
        set => SetProperty(ref _lockedTargetText, value);
    }

    public string CallAttemptsText
    {
        get => _callAttemptsText;
        set => SetProperty(ref _callAttemptsText, value);
    }

    public string ReportRepeatsText
    {
        get => _reportRepeatsText;
        set => SetProperty(ref _reportRepeatsText, value);
    }

    public string TxVerificationText
    {
        get => _txVerificationText;
        set => SetProperty(ref _txVerificationText, value);
    }

    public string RecoveryModeText
    {
        get => _recoveryModeText;
        set => SetProperty(ref _recoveryModeText, value);
    }

    public string LastCorrectiveAction
    {
        get => _lastCorrectiveAction;
        set => SetProperty(ref _lastCorrectiveAction, value);
    }

    public string LastObservedTransmitState
    {
        get => _lastObservedTransmitState;
        set => SetProperty(ref _lastObservedTransmitState, value);
    }

    public string TargetSourceText
    {
        get => _targetSourceText;
        set => SetProperty(ref _targetSourceText, value);
    }

    public string TargetSourceRowText
    {
        get => _targetSourceRowText;
        set => SetProperty(ref _targetSourceRowText, value);
    }

    public string WantedReasonText
    {
        get => _wantedReasonText;
        set => SetProperty(ref _wantedReasonText, value);
    }

    public string BestCandidateText
    {
        get => _bestCandidateText;
        set => SetProperty(ref _bestCandidateText, value);
    }

    public string SelectedIntendedTargetText
    {
        get => _selectedIntendedTargetText;
        set => SetProperty(ref _selectedIntendedTargetText, value);
    }

    public string ActiveLockedTargetText
    {
        get => _activeLockedTargetText;
        set => SetProperty(ref _activeLockedTargetText, value);
    }

    public string ActualJtdxDxCallText
    {
        get => _actualJtdxDxCallText;
        set => SetProperty(ref _actualJtdxDxCallText, value);
    }

    public string TargetStateWarningText
    {
        get => _targetStateWarningText;
        set => SetProperty(ref _targetStateWarningText, value);
    }

    public string TxMismatchText
    {
        get => _txMismatchText;
        set => SetProperty(ref _txMismatchText, value);
    }

    public string LastProgressFromTarget
    {
        get => _lastProgressFromTarget;
        set => SetProperty(ref _lastProgressFromTarget, value);
    }

    public string LastMyTx
    {
        get => _lastMyTx;
        set => SetProperty(ref _lastMyTx, value);
    }

    public string LastIntendedTx
    {
        get => _lastIntendedTx;
        set => SetProperty(ref _lastIntendedTx, value);
    }

    public string LastStageChange
    {
        get => _lastStageChange;
        set => SetProperty(ref _lastStageChange, value);
    }

    public string StuckReasonText
    {
        get => _stuckReasonText;
        set => SetProperty(ref _stuckReasonText, value);
    }

    public string SelectionMethodText
    {
        get => _selectionMethodText;
        set => SetProperty(ref _selectionMethodText, value);
    }

    public string GuiSelectionStatus
    {
        get => _guiSelectionStatus;
        set => SetProperty(ref _guiSelectionStatus, value);
    }

    public DxCandidateRow? SelectedCandidate
    {
        get => _selectedCandidate;
        set => SetProperty(ref _selectedCandidate, value);
    }

    public bool ShowOnlyTargetable
    {
        get => _showOnlyTargetable;
        set => SetProperty(ref _showOnlyTargetable, value);
    }

    public bool ShowWantedOnly
    {
        get => _showWantedOnly;
        set => SetProperty(ref _showWantedOnly, value);
    }

    public bool ShowWorkedConfirmed
    {
        get => _showWorkedConfirmed;
        set => SetProperty(ref _showWorkedConfirmed, value);
    }

    public bool ShowStale
    {
        get => _showStale;
        set => SetProperty(ref _showStale, value);
    }

    public bool ShowSuppressed
    {
        get => _showSuppressed;
        set => SetProperty(ref _showSuppressed, value);
    }

    public bool ShowRawDiagnostics
    {
        get => _showRawDiagnostics;
        set => SetProperty(ref _showRawDiagnostics, value);
    }
}
