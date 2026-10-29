using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Prototype;

internal sealed class PrototypeColorPalette : StackPanel
{
    public PrototypeColorPalette(Color current, Action<Color> changed)
    {
        double r = current.R / 255d, g = current.G / 255d, b = current.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta + 6) % 6)
            : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        double saturation = max == 0 ? 0 : delta / max, value = max;
        var field = new Grid { Height = 150, Margin = new Thickness(0, 8, 0, 8), ClipToBounds = true };
        var white = new LinearGradientBrush(Colors.White, Colors.Transparent, 0);
        var black = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90);
        field.Children.Add(new Border { Background = white }); field.Children.Add(new Border { Background = black });
        var marker = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2), Background = Brushes.Transparent, IsHitTestVisible = false };
        var overlay = new Canvas { IsHitTestVisible = false }; overlay.Children.Add(marker); field.Children.Add(overlay);
        var hueBar = new Border { Height = 12 };
        var spectrum = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) spectrum.GradientStops.Add(new GradientStop(Hsv(i * 60, 1, 1), i / 6d));
        hueBar.Background = spectrum;
        var slider = new Slider { Minimum = 0, Maximum = 360, Value = hue, SmallChange = 1, LargeChange = 15 };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Hue / Цветовой тон");
        void Update(bool notify)
        {
            field.Background = new SolidColorBrush(Hsv(slider.Value, 1, 1));
            Canvas.SetLeft(marker, saturation * field.ActualWidth - 5);
            Canvas.SetTop(marker, (1 - value) * field.ActualHeight - 5);
            if (notify) changed(Hsv(slider.Value, saturation, value));
        }
        void Pick(MouseEventArgs e)
        {
            var point = e.GetPosition(field);
            saturation = Math.Clamp(point.X / Math.Max(1, field.ActualWidth), 0, 1);
            value = 1 - Math.Clamp(point.Y / Math.Max(1, field.ActualHeight), 0, 1); Update(true);
        }
        field.MouseLeftButtonDown += (_, e) => { field.CaptureMouse(); Pick(e); e.Handled = true; };
        field.MouseMove += (_, e) => { if (field.IsMouseCaptured) Pick(e); };
        field.MouseLeftButtonUp += (_, e) => { if (field.IsMouseCaptured) { Pick(e); field.ReleaseMouseCapture(); } };
        field.SizeChanged += (_, _) => Update(false);
        slider.ValueChanged += (_, _) => Update(true);
        Children.Add(field); Children.Add(hueBar); Children.Add(slider); Update(false);
    }

    private static Color Hsv(double hue, double saturation, double value)
    {
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
