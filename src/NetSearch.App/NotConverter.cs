using System.Globalization;
using System.Windows.Data;

namespace NetSearch.App;

public sealed class NotConverter : IValueConverter
{
    public static readonly NotConverter Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => !(bool)value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => !(bool)value;
}
