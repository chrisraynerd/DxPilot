using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JtdxAutoResume.V3.Views;

internal static class DataGridContextMenuHelper
{
    public static void SelectRightClickedRow(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
            return;

        if (ItemsControl.ContainerFromElement(grid, source) is not DataGridRow row)
            return;

        grid.SelectedItem = row.Item;
        row.IsSelected = true;
        row.Focus();
    }
}
