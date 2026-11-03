using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace ReLevel.Revit.UI;

internal sealed class TargetLevelWindow : Window
{
    private readonly SearchableComboBox target = new() { DisplayMemberPath = "Name", MinWidth = 280 };
    public Level Target => (Level)target.SelectedItem;
    private readonly object nearest = new();
    public bool Nearest => ReferenceEquals(target.SelectedItem, nearest);

    public TargetLevelWindow(Window owner, IEnumerable<Level> levels, bool allowNearest = false, Func<Level, string>? displayName = null)
    {
        Owner = owner; Title = L.Get("Перенести элементы"); Width = 420;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = L.Get("Целевой уровень"), Margin = new Thickness(0, 0, 0, 8) });
        string Caption(object item) => item is Level level ? displayName?.Invoke(level) ?? level.Name
            : L.Get("Каждый элемент к ближайшему уровню снизу");
        target.SetDisplayName(Caption);
        target.SetItems(levels.Cast<object>().Concat(allowNearest ? new[] { nearest } : Array.Empty<object>()),
            item => item is Level level ? level.Name : Caption(item));
        panel.Children.Add(target);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = L.Get("Перенести"), IsDefault = true, IsEnabled = false, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
        target.SelectionChanged += (_, _) => ok.IsEnabled = target.SelectedItem is Level || Nearest;
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = L.Get("Отмена"), IsCancel = true, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
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
        buttons.Children.Add(new Button { Content = confirmation is null ? L.Get("Закрыть") : L.Get("Отмена"), IsCancel = true,
            IsDefault = true, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = panel;
        return window.ShowDialog() == true;
    }
}
