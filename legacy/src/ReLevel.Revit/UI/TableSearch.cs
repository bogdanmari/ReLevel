using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace ReLevel.Revit.UI;

internal sealed class TableSearch
{
    private readonly DataGrid table;
    private readonly Action changed;
    private readonly TextBox input = new() { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
    private readonly ToggleButton filter = new() { Content = "Фильтр", Padding = new Thickness(10, 0, 10, 0),
        Margin = new Thickness(0, 0, 8, 0), ToolTip = "Показывать только совпавшие строки. Операции действуют только на видимые строки." };
    private readonly TextBlock counter = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button previous = new() { Content = "↑", Width = 28, ToolTip = "Предыдущая строка (Shift+Enter)" };
    private readonly Button next = new() { Content = "↓", Width = 28, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Следующая строка (Enter)" };
    private ListCollectionView? view;
    private string Query => input.Text.Trim();
    public DockPanel Bar { get; } = new() { Margin = new Thickness(0, 0, 0, 8), Height = 28 };

    public TableSearch(DataGrid table, Action changed)
    {
        this.table = table;
        this.changed = changed;
        DockPanel.SetDock(filter, Dock.Left); Bar.Children.Add(filter);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        navigation.Children.Add(counter); navigation.Children.Add(previous); navigation.Children.Add(next);
        DockPanel.SetDock(navigation, Dock.Right); Bar.Children.Add(navigation);
        var inputPanel = new Grid();
        var placeholder = new TextBlock { Text = "Поиск…", Foreground = System.Windows.Media.Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), IsHitTestVisible = false };
        inputPanel.Children.Add(input); inputPanel.Children.Add(placeholder); Bar.Children.Add(inputPanel);
        System.Windows.Automation.AutomationProperties.SetName(input, "Поиск по таблице");
        System.Windows.Automation.AutomationProperties.SetName(previous, "Предыдущее совпадение");
        System.Windows.Automation.AutomationProperties.SetName(next, "Следующее совпадение");
        input.TextChanged += (_, _) =>
        {
            placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        };
        filter.Checked += (_, _) => Refresh();
        filter.Unchecked += (_, _) => Refresh();
        previous.Click += (_, _) => Navigate(-1);
        next.Click += (_, _) => Navigate(1);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Navigate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        };
        table.SelectionChanged += (_, _) => UpdateCounter();
        UpdateCounter();
    }

    public void SetRows(List<TableRow> rows)
    {
        view = new ListCollectionView(rows) { Filter = item => filter.IsChecked != true || Query.Length == 0 || Matches((TableRow)item) };
        table.ItemsSource = view;
        UpdateCounter();
    }

    private bool Matches(TableRow row)
    {
        if (Query.Length == 0) return false;
        // Search exactly the displayed text fields, including ID, but not hidden columns.
        foreach (var column in table.Columns.OfType<DataGridTextColumn>().Where(c => c.Visibility == Visibility.Visible))
        {
            if (column.Binding is not Binding binding) continue;
            var value = binding.Path.Path switch
            {
                nameof(TableRow.Id) => row.Id.ToString(), nameof(TableRow.Name) => row.Name,
                nameof(TableRow.Category) => row.Category, nameof(TableRow.Level) => row.Level,
                nameof(TableRow.Type) => row.Type, nameof(TableRow.Status) => row.Status, _ => ""
            };
            if (value.Contains(Query, StringComparison.CurrentCultureIgnoreCase)) return true;
        }
        return false;
    }

    private List<TableRow> MatchesInOrder() => table.Items.OfType<TableRow>().Where(Matches).ToList();

    private void Refresh()
    {
        view?.Refresh();
        UpdateCounter();
        changed();
    }

    private void UpdateCounter()
    {
        var matches = MatchesInOrder();
        var index = table.SelectedItem is TableRow row ? matches.IndexOf(row) : -1;
        counter.Text = Query.Length == 0 ? "— совпадений" : index < 0
            ? $"Совпадений: {matches.Count}" : $"{index + 1} из {matches.Count}";
        previous.IsEnabled = next.IsEnabled = matches.Count > 0;
    }

    private void Navigate(int direction)
    {
        var matches = MatchesInOrder();
        if (matches.Count == 0) return;
        var current = table.SelectedItem is TableRow row ? matches.IndexOf(row) : -1;
        var index = current < 0 ? (direction > 0 ? 0 : matches.Count - 1)
            : (current + direction + matches.Count) % matches.Count;
        table.SelectedItems.Clear();
        table.SelectedItem = matches[index];
        table.ScrollIntoView(matches[index]);
        UpdateCounter();
    }
}
