namespace JtdxAutoResume.V3.Views;

public partial class AchievementsView : System.Windows.Controls.UserControl
{
    public AchievementsView()
    {
        InitializeComponent();
    }

    private void AchievementsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel mainViewModel
            || sender is not System.Windows.Controls.DataGrid grid
            || e.OriginalSource is not System.Windows.DependencyObject source
            || System.Windows.Controls.ItemsControl.ContainerFromElement(grid, source) is not System.Windows.Controls.DataGridRow row
            || row.Item is not Models.AchievementDxccRow achievement)
        {
            return;
        }

        OpenQsoDetails(mainViewModel, achievement);
        e.Handled = true;
    }

    private void QsoCount_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel mainViewModel
            && sender is System.Windows.FrameworkElement { DataContext: Models.AchievementDxccRow achievement })
        {
            OpenQsoDetails(mainViewModel, achievement);
            e.Handled = true;
        }
    }

    private void OpenQsoDetails(ViewModels.MainViewModel mainViewModel, Models.AchievementDxccRow achievement)
    {
        new AchievementDxccDetailWindow
        {
            Owner = System.Windows.Window.GetWindow(this),
            DataContext = mainViewModel.Achievements.BuildQsoDetails(achievement)
        }.ShowDialog();
    }
}
