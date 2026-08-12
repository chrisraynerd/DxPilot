namespace JtdxAutoResume.V3.Views;

public partial class SessionHistoryView : System.Windows.Controls.UserControl
{
    public SessionHistoryView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateViewActivity();
        DataContextChanged += (_, _) => UpdateViewActivity();
    }

    private void UpdateViewActivity()
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
            viewModel.SessionHistory.SetViewActive(IsVisible);
    }

    private void StationGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DataGridContextMenuHelper.SelectRightClickedRow(sender, e);
    }
}
