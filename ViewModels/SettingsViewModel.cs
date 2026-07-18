using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private AppSettings _settings = new();

    public AppSettings Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }

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
