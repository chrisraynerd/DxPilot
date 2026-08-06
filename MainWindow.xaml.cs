using System.Windows;
using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                await vm.StartUdpOnLaunchAsync();
        };
        Closed += (_, _) =>
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        };
    }
}
