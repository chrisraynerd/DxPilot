using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed record ConfirmationModeOption(string Value, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private AppSettings _settings = new();

    public AppSettings Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }

    public IReadOnlyList<ConfirmationModeOption> ConfirmationModeOptions { get; } =
    [
        new("LoTWOnly", "LoTW only (recommended)"),
        new("WorkedOnly", "Worked — QSO in log is enough"),
        new("PaperQslOnly", "Paper QSL only"),
        new("LoTWOrPaper", "LoTW or paper QSL"),
        new("LoTWOrPaperOrEqsl", "LoTW, paper QSL or eQSL")
    ];

    public string EnableTxOffRgbText
    {
        get => $"0x{Settings.EnableTxOffRgb:X6}";
        set
        {
            if (Services.PixelDetector.TryParseRgb(value, out var rgb))
            {
                Settings.EnableTxOffRgb = rgb;
                OnPropertyChanged();
            }
        }
    }

    public string RxGreenRgbText
    {
        get => $"0x{Settings.RxGreenRgb:X6}";
        set
        {
            if (Services.PixelDetector.TryParseRgb(value, out var rgb))
            {
                Settings.RxGreenRgb = rgb;
                OnPropertyChanged();
            }
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EnableTxOffRgbText));
        OnPropertyChanged(nameof(RxGreenRgbText));
    }
}
