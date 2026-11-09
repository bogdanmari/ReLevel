using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI;

internal sealed class FillLevelWindow : Window
{
    private readonly SearchableComboBox parameters = new() { MinWidth = 330, Margin = new Thickness(0, 14, 0, 14) };
    public FillLevelParameter Parameter => (FillLevelParameter)parameters.SelectedItem;

    public FillLevelWindow(Window owner, IReadOnlyList<FillLevelParameter> choices, int count)
    {
        Owner = owner; Title = L.Get("Заполнить уровень"); Width = 560; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = L.Format($"Элементов: {count}. Выберите текстовый параметр экземпляра. Существующие значения будут заменены названием нижнего уровня привязки."), TextWrapping = TextWrapping.Wrap });
        parameters.SetItems(choices, p => p.Display);
        parameters.SetDisplayName(p => ((FillLevelParameter)p).Display);
        panel.Children.Add(parameters);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = L.Get("Заполнить уровень"), IsDefault = true, IsEnabled = false, Padding = new Thickness(12, 7, 12, 7) };
        parameters.SelectionChanged += (_, _) => apply.IsEnabled = parameters.SelectedItem is FillLevelParameter;
        apply.Click += (_, _) => { if (parameters.SelectedItem is FillLevelParameter) DialogResult = true; };
        actions.Children.Add(apply);
        actions.Children.Add(new Button { Content = L.Get("Отмена"), IsCancel = true, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(actions); Content = panel;
    }

    public static void ShowResults(Window owner, IReadOnlyList<FillLevelResult> rows)
    {
        var window = new Window { Owner = owner, Title = L.Get("Заполнить уровень"), Width = 1000, Height = 540,
            MinWidth = 650, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var summary = new TextBlock { Text = string.Join(". ", rows.GroupBy(r => r.Status).Select(g => $"{g.Key}: {g.Count()}")),
            Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
        var close = new Button { Content = L.Get("Закрыть"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var table = new CopyableDataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, ItemsSource = rows, SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };
        TransferLayout.StyleTableAppearance(table);
        foreach (var (header, property, width) in new[] { ("ID", nameof(FillLevelResult.Id), 100d),
            ("Уровень", nameof(FillLevelResult.Level), 160d), ("Параметр", nameof(FillLevelResult.Parameter), 220d),
            ("Статус", nameof(FillLevelResult.Status), 120d), ("Причина", nameof(FillLevelResult.Reason), 260d) })
            table.Columns.Add(new DataGridTextColumn { Header = L.Get(header), Binding = new Binding(property), Width = width,
                ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } } });
        panel.Children.Add(table); window.Content = panel; window.ShowDialog();
    }
}
