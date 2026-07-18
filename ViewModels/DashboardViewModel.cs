namespace JtdxAutoResume.V3.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private string _overallStatus = "Ready.";
    private string _udpStatus = "UDP listener stopped.";
    private string _autoResumeStatus = "AutoResume stopped.";
    private string _pixelState = "No pixel sample yet.";
    private string _bestTarget = "No target selected.";
    private string _bestReason = "";
    private string _huntState = "Idle.";
    private int _resumeCount;

    public string OverallStatus { get => _overallStatus; set => SetProperty(ref _overallStatus, value); }
    public string UdpStatus { get => _udpStatus; set => SetProperty(ref _udpStatus, value); }
    public string AutoResumeStatus { get => _autoResumeStatus; set => SetProperty(ref _autoResumeStatus, value); }
    public string PixelState { get => _pixelState; set => SetProperty(ref _pixelState, value); }
    public string BestTarget { get => _bestTarget; set => SetProperty(ref _bestTarget, value); }
    public string BestReason { get => _bestReason; set => SetProperty(ref _bestReason, value); }
    public string HuntState { get => _huntState; set => SetProperty(ref _huntState, value); }
    public int ResumeCount { get => _resumeCount; set => SetProperty(ref _resumeCount, value); }
}
