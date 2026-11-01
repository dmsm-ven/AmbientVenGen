using System.Globalization;
using System.Windows.Data;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>True when an integer is greater than one (used to show the "repeats" summary only for repeated layers).</summary>
public sealed class GreaterThanOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int number && number > 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
