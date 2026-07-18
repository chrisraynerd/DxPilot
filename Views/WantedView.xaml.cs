using System.Windows.Controls;
using System.Windows.Input;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3.Views;

public partial class WantedView : System.Windows.Controls.UserControl
{
    public WantedView()
    {
        InitializeComponent();
    }

    private void WantedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not DataGrid grid || grid.SelectedItem is not WantedItem item)
            return;

        if (viewModel.Wanted.CallWantedCommand?.CanExecute(item) == true)
            viewModel.Wanted.CallWantedCommand.Execute(item);
    }
}
