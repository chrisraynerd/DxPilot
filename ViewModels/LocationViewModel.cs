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
    private bool _isFocused;
    private bool _isVisible = true;

    public LocationPanelViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }
    public string LocationDetailHeader => Key.Equals("IOTA", StringComparison.OrdinalIgnoreCase) ? "IOTA" : "State";
    public ObservableCollection<DxCandidateRow> Candidates { get; } = new();

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (SetProperty(ref _isFocused, value))
                OnPropertyChanged(nameof(FocusButtonText));
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public string FocusButtonText => IsFocused ? "Show all regions" : "Expand";

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
    private int _panelColumnCount = 4;

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
        TogglePanelFocusCommand = new RelayCommand(TogglePanelFocus);
    }

    public ObservableCollection<LocationHuntAreaViewModel> Areas { get; }
    public ObservableCollection<LocationPanelViewModel> Panels { get; } = new();
    public event EventHandler? SelectedAreasChanged;
    public ICommand SelectAllAreasCommand { get; }
    public ICommand ClearAllAreasCommand { get; }
    public ICommand TogglePanelFocusCommand { get; }
    public int PanelColumnCount
    {
        get => _panelColumnCount;
        private set => SetProperty(ref _panelColumnCount, value);
    }
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

    public void ClearPanelFocus()
    {
        PanelColumnCount = 4;
        foreach (var panel in Panels)
        {
            panel.IsFocused = false;
            panel.IsVisible = true;
        }
    }

    private void TogglePanelFocus(object? parameter)
    {
        if (parameter is not LocationPanelViewModel selected)
            return;

        if (selected.IsFocused)
        {
            ClearPanelFocus();
            return;
        }

        PanelColumnCount = 1;
        foreach (var panel in Panels)
        {
            panel.IsFocused = ReferenceEquals(panel, selected);
            panel.IsVisible = ReferenceEquals(panel, selected);
        }
    }

    private void RaiseSelectedAreasChanged()
    {
        OnPropertyChanged(nameof(SelectedAreaKeys));
        OnPropertyChanged(nameof(SelectedAreasDisplay));
        SelectedAreasChanged?.Invoke(this, EventArgs.Empty);
    }
}
