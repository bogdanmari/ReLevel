using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Prototype;

internal sealed class PrototypeColorWindow : Window
{
    public Color SelectedColor { get; private set; }
    public PrototypeColorWindow(PrototypeText text, Color current)
    {
        SelectedColor = current;
        Title = text["PickColor"]; Width = 351; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(20), LayoutTransform = new ScaleTransform(0.9, 0.9) };
        panel.Children.Add(new TextBlock { Text = text["ColorHelp"], TextWrapping = TextWrapping.Wrap });
        var palette = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        var input = new TextBox { Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}", MaxLength = 7, Padding = new Thickness(8) };
        var preview = new Border { Height = 32, Margin = new Thickness(0, 10, 0, 8), Background = new SolidColorBrush(current) };
        foreach (var hex in new[] { "#3182CE", "#157D88", "#38A169", "#D69E2E", "#DD6B20", "#E53E3E", "#D53F8C", "#805AD5", "#1A202C", "#718096", "#CBD5E0", "#FFFFFF" })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button { Width = 45, Height = 32, Margin = new Thickness(3), Background = new SolidColorBrush(color), ToolTip = hex };
            System.Windows.Automation.AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) => input.Text = hex; palette.Children.Add(swatch);
        }
        panel.Children.Add(new PrototypeColorPalette(current, color => input.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}"));
        panel.Children.Add(palette); panel.Children.Add(input); panel.Children.Add(preview);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = text["Cancel"], IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        var apply = new Button { Content = text["Apply"], IsDefault = true, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(10, 0, 0, 0) };
        bool ReadColor(out Color color)
        {
            string value = input.Text.Trim(); color = current;
            if (value.Length != 7 || value[0] != '#' || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) return false;
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
        }
        input.TextChanged += (_, _) =>
        {
            bool valid = ReadColor(out var color); apply.IsEnabled = valid;
            error.Text = valid ? "" : text["InvalidColor"];
            if (valid) preview.Background = new SolidColorBrush(color);
        };
        apply.Click += (_, _) => { if (ReadColor(out var color)) { SelectedColor = color; DialogResult = true; } };
        actions.Children.Add(cancel); actions.Children.Add(apply); panel.Children.Add(actions); Content = panel;
    }
}
