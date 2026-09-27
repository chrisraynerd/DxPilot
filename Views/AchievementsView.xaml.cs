namespace JtdxAutoResume.V3.Views;

public partial class AchievementsView : System.Windows.Controls.UserControl
{
    public AchievementsView()
    {
        InitializeComponent();
        AddBandColumns(AchievementsGrid, 6);
        AddBandColumns(StatesGrid, 5);
        // WPF otherwise compresses fixed columns to make room for the trailing star column.
        // Keep labels/counts legible and let the table scroll horizontally on smaller screens.
        foreach (var grid in new[] { AchievementsGrid, StatesGrid })
            foreach (var column in grid.Columns)
                if (column.Width.IsAbsolute) column.MinWidth = column.Width.Value;
    }

    private void AddBandColumns(System.Windows.Controls.DataGrid grid, int insertionIndex)
    {
        for (var i = 0; i < Models.AchievementBandCell.BandOrder.Count; i++)
        {
            var presenter = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentControl));
            presenter.SetBinding(System.Windows.Controls.ContentControl.ContentProperty, new System.Windows.Data.Binding($"BandCells[{i}]"));
            presenter.SetValue(System.Windows.Controls.ContentControl.ContentTemplateProperty, FindResource("AchievementBandTemplate"));
            presenter.SetValue(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
            var cellStyle = new System.Windows.Style(typeof(System.Windows.Controls.DataGridCell));
            cellStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.PaddingProperty, new System.Windows.Thickness(0)));
            cellStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
            var headerStyle = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as System.Windows.Style);
            headerStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.PaddingProperty, new System.Windows.Thickness(2, 6, 2, 6)));
            headerStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center));
            grid.Columns.Insert(insertionIndex + i, new System.Windows.Controls.DataGridTemplateColumn
            {
                Header = Models.AchievementBandCell.BandOrder[i].TrimEnd('m'), Width = 43, MinWidth = 38,
                HeaderStyle = headerStyle, CellStyle = cellStyle,
                SortMemberPath = $"BandCells[{i}].LotwConfirmedCount",
                CellTemplate = new System.Windows.DataTemplate { VisualTree = presenter }
            });
        }
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
