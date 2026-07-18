using System.Windows;
using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closed += (_, _) =>
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        };
    }
}
