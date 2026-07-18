namespace JtdxAutoResume.V3.ViewModels;

public sealed class TargetStatusSummaryViewModel : ObservableObject
{
    private string _mode = "Stopped";
    private string _operatingMode = "Stopped";
    private string _selectedTargetCall = "";
    private string _selectedTargetEntity = "";
    private string _selectedTargetDisplay = "No target selected";
    private string _targetSource = "None";
    private string _wantedReason = "";
    private string _category = "";
    private string _wantedCategory = "None";
    private string _scope = "";
    private string _wantedScope = "Overall";
    private string _needStatus = "";
    private string _tierName = "";
    private string _scoreOrTier = "";
    private string _selectionMethod = "";
    private string _qsoState = "";
    private string _qsoStage = "";
    private string _expectedJtdxDxCall = "";
    private string _actualJtdxDxCall = "";
    private string _jtdxMatchStatus = "Not checked";
    private string _txGateStatus = "";
    private string _plainStatusMessage = "";
    private string _debugStatusMessage = "";
    private string _attemptCounterLabel = "";

    public string Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public string OperatingMode { get => _operatingMode; set { if (SetProperty(ref _operatingMode, value)) Mode = value; } }
    public string SelectedTargetCall { get => _selectedTargetCall; set => SetProperty(ref _selectedTargetCall, value); }
    public string SelectedTargetEntity { get => _selectedTargetEntity; set => SetProperty(ref _selectedTargetEntity, value); }
    public string SelectedTargetDisplay { get => _selectedTargetDisplay; set => SetProperty(ref _selectedTargetDisplay, value); }
    public string TargetSource { get => _targetSource; set => SetProperty(ref _targetSource, value); }
    public string WantedReason { get => _wantedReason; set => SetProperty(ref _wantedReason, value); }
    public string Category { get => _category; set => SetProperty(ref _category, value); }
    public string WantedCategory { get => _wantedCategory; set { if (SetProperty(ref _wantedCategory, value)) Category = value; } }
    public string Scope { get => _scope; set => SetProperty(ref _scope, value); }
    public string WantedScope { get => _wantedScope; set { if (SetProperty(ref _wantedScope, value)) Scope = value; } }
    public string NeedStatus { get => _needStatus; set => SetProperty(ref _needStatus, value); }
    public string TierName { get => _tierName; set => SetProperty(ref _tierName, value); }
    public string ScoreOrTier { get => _scoreOrTier; set => SetProperty(ref _scoreOrTier, value); }
    public string SelectionMethod { get => _selectionMethod; set => SetProperty(ref _selectionMethod, value); }
    public string QsoState { get => _qsoState; set => SetProperty(ref _qsoState, value); }
    public string QsoStage { get => _qsoStage; set => SetProperty(ref _qsoStage, value); }
    public string ExpectedJtdxDxCall { get => _expectedJtdxDxCall; set => SetProperty(ref _expectedJtdxDxCall, value); }
    public string ActualJtdxDxCall { get => _actualJtdxDxCall; set => SetProperty(ref _actualJtdxDxCall, value); }
    public string JtdxMatchStatus { get => _jtdxMatchStatus; set => SetProperty(ref _jtdxMatchStatus, value); }
    public string TxGateStatus { get => _txGateStatus; set => SetProperty(ref _txGateStatus, value); }
    public string PlainStatusMessage { get => _plainStatusMessage; set => SetProperty(ref _plainStatusMessage, value); }
    public string DebugStatusMessage { get => _debugStatusMessage; set => SetProperty(ref _debugStatusMessage, value); }
    public string AttemptCounterLabel { get => _attemptCounterLabel; set => SetProperty(ref _attemptCounterLabel, value); }
}
