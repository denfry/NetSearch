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
        tb.Loaded -= OnLoaded;
        tb.TextChanged -= OnTextChanged;
        tb.IsVisibleChanged -= OnVisibleChanged;
        tb.Loaded += OnLoaded;
        tb.TextChanged += OnTextChanged;
        tb.IsVisibleChanged += OnVisibleChanged;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Refresh((TextBox)sender);
    private static void OnTextChanged(object sender, TextChangedEventArgs e) => Refresh((TextBox)sender);
    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh((TextBox)sender);

    private static void Refresh(TextBox tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer is null) return;

        var existing = layer.GetAdorners(tb);
        if (existing is not null)
            foreach (var a in existing)
                if (a is PlaceholderAdorner pa) layer.Remove(pa);

        // Only paint the placeholder when the TextBox is actually visible — otherwise a
        // collapsed panel (e.g. the Filters row) leaves ghost text over the results.
        if (tb.IsVisible && string.IsNullOrEmpty(tb.Text) && !string.IsNullOrEmpty(GetPlaceholder(tb)))
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

            // The caret/typed text starts at the inner content origin: border + padding.
            // Match it exactly so the hint and the text the user types line up, and centre
            // vertically within the same inner box the control template lays the text out in.
            var left = tb.BorderThickness.Left + tb.Padding.Left;
            var innerHeight = tb.ActualHeight
                - tb.BorderThickness.Top - tb.BorderThickness.Bottom
                - tb.Padding.Top - tb.Padding.Bottom;
            var top = tb.BorderThickness.Top + tb.Padding.Top + (innerHeight - ft.Height) / 2;
            ctx.DrawText(ft, new Point(left, top));
        }
    }
}
