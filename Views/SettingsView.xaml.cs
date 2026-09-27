using System.Windows;
using System.Windows.Controls;

namespace JtdxAutoResume.V3.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        foreach (var section in SettingsSections.Children.OfType<Expander>())
            _searchLabels[section] = string.Join(" ", Labels(section)).ToLowerInvariant();
        ApplySearch();
    }

    private readonly Dictionary<Expander, string> _searchLabels = new();
    private Dictionary<Expander, bool>? _expandedBeforeSearch;

    // Search presentation labels only, never user-entered values or credentials.
    private static IEnumerable<string> Labels(DependencyObject parent)
    {
        if (parent is TextBlock text && System.Windows.Data.BindingOperations.GetBinding(text, TextBlock.TextProperty) == null)
            yield return text.Text;
        if (parent is HeaderedContentControl header && header.Header is string title) yield return title;
        if (parent is System.Windows.Controls.Button button && button.Content is string caption) yield return caption;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var label in Labels(child)) yield return label;
    }

    private void SettingsSearch_Changed(object sender, TextChangedEventArgs e) => ApplySearch();
    private void AdvancedSettings_Click(object sender, RoutedEventArgs e) => ApplySearch();
    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SettingsSearchBox.Clear();

    private void ApplySearch()
    {
        if (SettingsSections == null || SettingsSearchBox == null || ShowAdvancedSettings == null) return;
        var words = SettingsSearchBox.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && _expandedBeforeSearch == null)
            _expandedBeforeSearch = _searchLabels.Keys.ToDictionary(section => section, section => section.IsExpanded);
        var visible = 0;
        foreach (var (section, labels) in _searchLabels)
        {
            var show = words.Length > 0 ? words.All(labels.Contains)
                : !Equals(section.Tag, "Advanced") || ShowAdvancedSettings.IsChecked == true;
            section.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) visible++;
            if (words.Length > 0 && show) section.IsExpanded = true;
            else if (words.Length == 0 && _expandedBeforeSearch != null)
                section.IsExpanded = _expandedBeforeSearch[section];
        }
        if (words.Length == 0) _expandedBeforeSearch = null;
        if (SettingsSearchSummary != null) SettingsSearchSummary.Text = $"{visible} sections";
        if (SettingsNoMatches != null) SettingsNoMatches.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
