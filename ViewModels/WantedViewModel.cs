using System.Collections.ObjectModel;
using System.Windows.Input;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class WantedViewModel : ObservableObject
{
    private string _status = "Wanted items will appear as fresh decodes arrive.";

    public ObservableCollection<WantedItem> WantedDxcc { get; } = new();
    public ObservableCollection<WantedItem> WantedGrids { get; } = new();
    public ObservableCollection<WantedItem> WantedStates { get; } = new();
    public ObservableCollection<WantedItem> WantedBandMode { get; } = new();

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand? CallWantedCommand { get; set; }
    public ICommand? WatchOnlyCommand { get; set; }
    public ICommand? SuppressWantedCommand { get; set; }
    public ICommand? CopyCallsignCommand { get; set; }
    public ICommand? CopyRawMessageCommand { get; set; }
}
