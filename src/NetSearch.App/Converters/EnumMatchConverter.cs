using System.Globalization;
using System.Windows.Data;

namespace NetSearch.App.Converters;

/// <summary>True when the bound enum value's name equals the ConverterParameter string.
/// On ConvertBack (a radio becoming checked) returns the parsed enum, else Binding.DoNothing.</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name
            ? Enum.Parse(targetType, name)
            : Binding.DoNothing;
}
