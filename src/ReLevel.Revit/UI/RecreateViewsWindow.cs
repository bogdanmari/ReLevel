using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace ReLevel.Revit.UI;

internal sealed class RecreateViewsWindow : Window
{
    private readonly SearchableComboBox target = new() { DisplayMemberPath = "Name", MinWidth = 280 };
    private readonly TextBox prefix = new() { Text = L.Get("Копия_"), Margin = new Thickness(0, 6, 0, 12) };
    public Level Target => (Level)target.SelectedItem;
    public string Prefix => prefix.Text;

    public RecreateViewsWindow(FrameworkElement owner, IEnumerable<Level> levels, int count)
    {
        DialogOwner.Attach(this, owner); Title = L.Get("Пересоздать виды"); Width = 470;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = L.Format($"Выбрано видов: {count}. Исходные виды и их размещения на листах сохранятся. Новые виды на листы не размещаются."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = L.Get("Целевой уровень"), Margin = new Thickness(0, 0, 0, 6) });
        target.SetItems(levels, level => level.Name); panel.Children.Add(target);
        panel.Children.Add(new TextBlock { Text = L.Get("Префикс имени (префикс + исходное имя)"), Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(prefix);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = L.Get("Пересоздать"), IsDefault = true, IsEnabled = false,
            Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
        target.SelectionChanged += (_, _) => ok.IsEnabled = target.SelectedItem is Level;
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = L.Get("Отмена"), IsCancel = true, Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
        panel.Children.Add(buttons); Content = panel;
    }
}
