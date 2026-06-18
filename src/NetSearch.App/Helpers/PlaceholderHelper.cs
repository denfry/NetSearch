using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NetSearch.App.Helpers;

/// <summary>Attached property that renders grey placeholder text over an empty TextBox.</summary>
public static class PlaceholderHelper
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder", typeof(string), typeof(PlaceholderHelper),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public static string GetPlaceholder(DependencyObject o) => (string)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string v) => o.SetValue(PlaceholderProperty, v);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        tb.Loaded -= Refresh;
        tb.TextChanged -= Refresh;
        tb.Loaded += Refresh;
        tb.TextChanged += Refresh;
    }

    private static void Refresh(object sender, RoutedEventArgs e)
    {
        var tb = (TextBox)sender;
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer is null) return;

        var existing = layer.GetAdorners(tb);
        if (existing is not null)
            foreach (var a in existing)
                if (a is PlaceholderAdorner pa) layer.Remove(pa);

        if (string.IsNullOrEmpty(tb.Text) && !string.IsNullOrEmpty(GetPlaceholder(tb)))
            layer.Add(new PlaceholderAdorner(tb, GetPlaceholder(tb)));
    }

    private sealed class PlaceholderAdorner : Adorner
    {
        private readonly string _text;

        public PlaceholderAdorner(UIElement adorned, string text) : base(adorned)
        {
            _text = text;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext ctx)
        {
            var tb = (TextBox)AdornedElement;
            var ft = new FormattedText(
                _text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                VisualTreeHelper.GetDpi(tb).PixelsPerDip);
            ctx.DrawText(ft, new Point(tb.Padding.Left + 2, (tb.ActualHeight - ft.Height) / 2));
        }
    }
}
