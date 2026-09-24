using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace ReLevel.Revit.UI;

internal sealed class RecreateViewsWindow : Window
{
    private readonly ComboBox target = new() { DisplayMemberPath = "Name", MinWidth = 280 };
    private readonly TextBox prefix = new() { Text = "Копия_", Margin = new Thickness(0, 6, 0, 12) };
    public Level Target => (Level)target.SelectedItem;
    public string Prefix => prefix.Text;

    public RecreateViewsWindow(Window owner, IEnumerable<Level> levels, int count)
    {
        Owner = owner; Title = "Пересоздать виды"; Width = 470;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = $"Выбрано видов: {count}. Исходные виды и их размещения на листах сохранятся. Новые виды на листы не размещаются.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Целевой уровень", Margin = new Thickness(0, 0, 0, 6) });
        target.ItemsSource = levels.ToList(); panel.Children.Add(target);
        panel.Children.Add(new TextBlock { Text = "Префикс имени (префикс + исходное имя)", Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(prefix);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Пересоздать", IsDefault = true, IsEnabled = false,
            Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
        target.SelectionChanged += (_, _) => ok.IsEnabled = target.SelectedItem is Level;
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
        panel.Children.Add(buttons); Content = panel;
    }
}
