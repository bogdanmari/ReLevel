using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReLevel.Revit.UI;

internal sealed class CopyNotification
{
    private readonly TextBlock message = new() { FontSize = 16, TextAlignment = TextAlignment.Center };
    private readonly Popup popup;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private FrameworkElement? source;

    public CopyNotification()
    {
        var accent = new SolidColorBrush(Color.FromRgb(21, 125, 136));
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "✓", FontSize = 28, Foreground = accent,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(message);
        popup = new Popup
        {
            Placement = PlacementMode.Center, StaysOpen = true,
            AllowsTransparency = true, Focusable = false, IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(244, 247, 250)),
                BorderBrush = accent, BorderThickness = new Thickness(1), Padding = new Thickness(30, 20, 30, 20),
                Child = content
            }
        };
        content.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
        content.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(24, 50, 71)));
        timer.Tick += (_, _) => Close();
    }

    public void Show(FrameworkElement target, string text)
    {
        timer.Stop();
        if (source != target)
        {
            Close();
            source = target;
            source.Unloaded += OnUnloaded;
        }
        message.Text = text;
        popup.PlacementTarget = (UIElement?)Window.GetWindow(target) ?? target;
        popup.IsOpen = true;
        timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Close();

    private void Close()
    {
        timer.Stop();
        popup.IsOpen = false;
        popup.PlacementTarget = null;
        if (source is not null) source.Unloaded -= OnUnloaded;
        source = null;
    }
}
