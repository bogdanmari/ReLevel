using System.Windows;
using System.Windows.Controls;

namespace ReLevel.Revit.UI;

internal sealed class ElementIdsWindow : Window
{
    public IReadOnlyList<long> Ids { get; private set; } = [];

    public ElementIdsWindow(Window owner)
    {
        Owner = owner; Title = L.Get("Добавить по ID"); Width = 470; Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = L.Get("Введите ID через запятую, пробел или перенос строки."), TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { AcceptsReturn = true, Height = 85, Margin = new Thickness(0, 10, 0, 10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(input);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = L.Get("Добавить"), Padding = new Thickness(12, 4, 12, 4) };
        add.Click += (_, _) =>
        {
            var ids = new List<long>();
            foreach (var token in input.Text.Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!long.TryParse(token, out var id) || id <= 0)
                { error.Text = L.Get("Нужны положительные числовые ID."); return; }
                ids.Add(id);
            }
            if (ids.Count == 0) { error.Text = L.Get("Введите хотя бы один ID."); return; }
            Ids = ids.Distinct().ToArray(); DialogResult = true;
        };
        buttons.Children.Add(add);
        buttons.Children.Add(new Button { Content = L.Get("Отмена"), IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) });
        panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => input.Focus();
    }
}
