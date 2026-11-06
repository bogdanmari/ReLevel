using System.Windows;
using System.Windows.Controls;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow
{
    private FrameworkElement? colorsPage;
    private readonly TextBlock colorStatus = new() { Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 22 };
    private readonly Button applyColors = new(), resetColors = new();

    private void ColorFooter(DockPanel content)
    {
        var buttons = new Grid();
        foreach (var (button, key, reset) in new[] { (resetColors, "Reset", true), (applyColors, "Colorize", false) })
        {
            button.Padding = new Thickness(18, 9, 18, 9);
            button.Margin = new Thickness(0, 0, 0, 6);
            button.HorizontalAlignment = reset ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            button.IsEnabled = workspace is not null;
            Bind(button, ContentProperty, key);
            button.Click += (_, _) => runApi(() =>
            {
                var message = workspace!.ApplyColors(levels, reset);
                SetColorStatus(message);
                if (reset)
                    foreach (var level in levels) { level.Selected = false; level.Hidden = false; }
            });
            buttons.Children.Add(button);
        }
        var footer = StablePageLayout.Footer(buttons, colorStatus);
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
        colorStatus.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        colorStatus.Text = $"{text["Elements"]}: {levels.Sum(level => level.ElementCount)}. {message}";
        colorStatus.ToolTip = message;
    }
}
