namespace JtdxAutoResume.V3.Views;

public partial class SessionHistoryView : System.Windows.Controls.UserControl
{
    public SessionHistoryView()
    {
        InitializeComponent();
    }

    private void StationGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DataGridContextMenuHelper.SelectRightClickedRow(sender, e);
    }
}
