using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class SchedulerViewModel : ObservableObject
{
    public ObservableCollection<BandScheduleItem> ScheduleItems { get; } = new();
}
