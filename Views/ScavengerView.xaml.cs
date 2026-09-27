namespace JtdxAutoResume.V3.Views;

public partial class ScavengerView : System.Windows.Controls.UserControl
{
    public ScavengerView() => InitializeComponent();
    private void StationGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DataGridContextMenuHelper.SelectRightClickedRow(sender, e);
}
