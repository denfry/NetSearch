using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetSearch.App;

public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible ? false : true;
}
