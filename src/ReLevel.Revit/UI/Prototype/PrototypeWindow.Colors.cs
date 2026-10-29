using System.Windows;
using System.Windows.Controls;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow
{
    private FrameworkElement? colorsPage;
    private readonly TextBlock colorStatus = new() { TextWrapping = TextWrapping.Wrap, MaxHeight = 68 };
    private readonly Button applyColors = new(), resetColors = new();

    private void ColorFooter(DockPanel content)
    {
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (button, key, reset) in new[] { (resetColors, "Reset", true), (applyColors, "Apply", false) })
        {
            button.Padding = new Thickness(18, 9, 18, 9);
            button.Margin = new Thickness(8, 0, 0, 6);
            Bind(button, ContentProperty, key);
            button.Click += (_, _) =>
            {
                var message = workspace!.ApplyColors(levels, reset);
                SetColorStatus(message);
                if (reset)
                    foreach (var level in levels) { level.Selected = false; level.Hidden = false; }
            };
            buttons.Children.Add(button);
        }
        footer.Children.Add(buttons);
        footer.Children.Add(colorStatus);
        Bind(resetColors, ToolTipProperty, "ResetColorsHelp");
        DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
    }

    private void PrepareColors()
    {
        var state = workspace!.PrepareColors();
        applyColors.IsEnabled = resetColors.IsEnabled = state.Available;
        foreach (var level in levels) level.ElementCount = state.Counts.GetValueOrDefault(level.Id);
        SetColorStatus(state.Message);
    }

    private void SetColorStatus(string message)
    {
        colorStatus.Text = message;
        colorStatus.ToolTip = message;
    }
}
