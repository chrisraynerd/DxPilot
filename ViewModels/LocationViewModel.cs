using System.Collections.ObjectModel;
using System.Windows.Input;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

public sealed class LocationHuntAreaViewModel : ObservableObject
{
    private bool _isSelected;

    public LocationHuntAreaViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class LocationPanelViewModel : ObservableObject
{
    private string _summary = "No recent decodes.";

    public LocationPanelViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }
    public string LocationDetailHeader => Key.Equals("IOTA", StringComparison.OrdinalIgnoreCase) ? "IOTA" : "State";
    public ObservableCollection<DxCandidateRow> Candidates { get; } = new();

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }
}

public sealed class LocationViewModel : ObservableObject
{
    private string _status = "Location panels update whenever UDP decodes arrive.";
    private bool _isApplyingAreaSelection;

    public LocationViewModel()
    {
        Areas = new ObservableCollection<LocationHuntAreaViewModel>
        {
            new("USA", "USA"),
            new("AF", "Africa"),
            new("AS", "Asia"),
            new("EU", "Europe"),
            new("NA", "North America (outside USA)"),
            new("SA", "South America"),
            new("OC", "Oceania"),
            new("IOTA", "IOTA"),
            new("OTHER", "Antarctica / unresolved")
        };

        foreach (var area in Areas)
            area.PropertyChanged += (_, args) =>
            {
                if (!_isApplyingAreaSelection && args.PropertyName == nameof(LocationHuntAreaViewModel.IsSelected))
                    RaiseSelectedAreasChanged();
            };

        SelectAllAreasCommand = new RelayCommand(() => SetSelectedAreas(Areas.Select(area => area.Key)));
        ClearAllAreasCommand = new RelayCommand(() => SetSelectedAreas(Array.Empty<string>()));
    }

    public ObservableCollection<LocationHuntAreaViewModel> Areas { get; }
    public ObservableCollection<LocationPanelViewModel> Panels { get; } = new();
    public event EventHandler? SelectedAreasChanged;
    public ICommand SelectAllAreasCommand { get; }
    public ICommand ClearAllAreasCommand { get; }
    public IReadOnlyList<string> SelectedAreaKeys => Areas.Where(area => area.IsSelected).Select(area => area.Key).ToList();
    public string SelectedAreasDisplay
    {
        get
        {
            var selected = Areas.Where(area => area.IsSelected).Select(area => area.Title).ToList();
            if (selected.Count == 0)
                return "No areas (Global New DXCC only)";
            if (selected.Count == Areas.Count)
                return "All areas";
            return string.Join(" + ", selected);
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand? CallTargetCommand { get; set; }
    public ICommand? CopyCallsignCommand { get; set; }

    public bool IsAreaSelected(string key)
    {
        return Areas.Any(area => area.IsSelected && area.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public void SetSelectedAreas(IEnumerable<string> keys)
    {
        var selected = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _isApplyingAreaSelection = true;
        try
        {
            foreach (var area in Areas)
                area.IsSelected = selected.Contains(area.Key);
        }
        finally
        {
            _isApplyingAreaSelection = false;
        }

        RaiseSelectedAreasChanged();
    }

    private void RaiseSelectedAreasChanged()
    {
        OnPropertyChanged(nameof(SelectedAreaKeys));
        OnPropertyChanged(nameof(SelectedAreasDisplay));
        SelectedAreasChanged?.Invoke(this, EventArgs.Empty);
    }
}
