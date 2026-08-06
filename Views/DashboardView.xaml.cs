namespace JtdxAutoResume.V3.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void StationGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DataGridContextMenuHelper.SelectRightClickedRow(sender, e);
    }
}
