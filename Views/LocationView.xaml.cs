using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3.Views;

public partial class LocationView : System.Windows.Controls.UserControl
{
    public LocationView()
    {
        InitializeComponent();
    }

    private void LocationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not DataGrid grid || grid.SelectedItem is not DxCandidateRow row)
            return;

        if (viewModel.Location.CallTargetCommand?.CanExecute(row) == true)
            viewModel.Location.CallTargetCommand.Execute(row);
    }

    private void StationGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DataGridContextMenuHelper.SelectRightClickedRow(sender, e);
    }

    private void RegionOverview_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: LocationPanelViewModel panel })
            return;

        if (DataContext is MainViewModel viewModel && !viewModel.Location.VisiblePanels.Contains(panel))
            viewModel.Location.ClearPanelFocus();

        Dispatcher.BeginInvoke(() =>
        {
            if (LocationPanelsItemsControl.ItemContainerGenerator.ContainerFromItem(panel) is System.Windows.FrameworkElement container)
                container.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void RegionFocus_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            () => LocationPanelsScrollViewer.ScrollToTop(),
            DispatcherPriority.Loaded);
    }
}
