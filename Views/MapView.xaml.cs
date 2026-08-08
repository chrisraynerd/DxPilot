using JtdxAutoResume.V3.ViewModels;

namespace JtdxAutoResume.V3.Views;

public partial class MapView : System.Windows.Controls.UserControl
{
    public MapView()
    {
        InitializeComponent();
    }

    private void WorldView_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        LiveMap.ShowWorld();
    }

    private void LiveMap_StationDoubleClicked(object? sender, MapStationDoubleClickedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!e.Station.IsContactable || !viewModel.CallNowCommand.CanExecute(e.Station))
        {
            viewModel.Map.ReportContactUnavailable(e.Station);
            return;
        }

        viewModel.CallNowCommand.Execute(e.Station);
    }
}
