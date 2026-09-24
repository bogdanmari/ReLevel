using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace ReLevel.Revit.UI;

internal sealed class TargetLevelWindow : Window
{
    private readonly ComboBox target = new() { DisplayMemberPath = "Name", MinWidth = 280 };
    public Level Target => (Level)target.SelectedItem;

    public TargetLevelWindow(Window owner, IEnumerable<Level> levels)
    {
        Owner = owner; Title = "Перенести элементы"; Width = 420;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Целевой уровень", Margin = new Thickness(0, 0, 0, 8) });
        target.ItemsSource = levels.ToList();
        panel.Children.Add(target);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Перенести", IsDefault = true, IsEnabled = false, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
        target.SelectionChanged += (_, _) => ok.IsEnabled = target.SelectedItem is Level;
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
        panel.Children.Add(buttons); Content = panel;
    }
}

internal static class OperationDialogs
{
    public static bool Show(Window owner, string title, string text, string? confirmation = null)
    {
        var window = new Window { Owner = owner, Title = title, Width = 780, Height = 470,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (confirmation is not null)
        {
            var confirm = new Button { Content = confirmation, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
            confirm.Click += (_, _) => window.DialogResult = true;
            buttons.Children.Add(confirm);
        }
        buttons.Children.Add(new Button { Content = confirmation is null ? "Закрыть" : "Отмена", IsCancel = true,
            IsDefault = true, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = panel;
        return window.ShowDialog() == true;
    }
}
