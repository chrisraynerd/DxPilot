namespace JtdxAutoResume.V3.ViewModels;

public sealed class ConditionsIndicatorViewModel : ObservableObject
{
    private double _remainingPercent = 100;
    private string _detail = "Waiting for monitoring to start.";
    private string _state = "Inactive";

    public ConditionsIndicatorViewModel(string key, string title, string explanation)
    {
        Key = key;
        Title = title;
        Explanation = explanation;
    }

    public string Key { get; }
    public string Title { get; }
    public string Explanation { get; }
    public double RemainingPercent { get => _remainingPercent; private set => SetProperty(ref _remainingPercent, Math.Clamp(value, 0, 100)); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string State { get => _state; private set => SetProperty(ref _state, value); }

    public void Update(double remainingPercent, string detail, bool active = true)
    {
        RemainingPercent = remainingPercent;
        Detail = detail;
        State = !active ? "Inactive" : remainingPercent <= 0 ? "Ready" : remainingPercent <= 35 ? "Near" : "Safe";
    }
}
