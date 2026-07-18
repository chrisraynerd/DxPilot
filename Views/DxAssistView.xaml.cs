using System.Windows;
using System.Windows.Controls.Primitives;
using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3.Views;

public partial class DxAssistView : System.Windows.Controls.UserControl
{
    public DxAssistView()
    {
        InitializeComponent();
        Loaded += DxAssistView_Loaded;
    }

    private void DxAssistView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var width = Math.Clamp(viewModel.Settings.Settings.DxAssistSelectedTargetPanelWidth, 300, 900);
        SelectedTargetColumn.Width = new GridLength(width);
    }

    private void SelectedTargetSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var width = ClampSelectedTargetWidth(SelectedTargetColumn.ActualWidth);
        viewModel.Settings.Settings.DxAssistSelectedTargetPanelWidth = width;
        SelectedTargetColumn.Width = new GridLength(width);

        if (viewModel.SaveSettingsCommand.CanExecute(null))
            viewModel.SaveSettingsCommand.Execute(null);
    }

    private void SelectedTargetSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var width = ClampSelectedTargetWidth(SelectedTargetColumn.ActualWidth + e.HorizontalChange);
        SelectedTargetColumn.Width = new GridLength(width);
    }

    private double ClampSelectedTargetWidth(double width)
    {
        var availableMax = Math.Max(300, DxAssistContentGrid.ActualWidth - 5 - 500 - 12);
        return Math.Clamp(width, 300, Math.Min(900, availableMax));
    }
}
