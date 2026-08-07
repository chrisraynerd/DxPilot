using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JtdxAutoResume.V3.Views;

public sealed class WizardStepVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int current
            && int.TryParse(parameter?.ToString(), out var expected)
            && current == expected
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
