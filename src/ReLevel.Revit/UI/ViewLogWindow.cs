using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI;

internal sealed class ViewLogWindow : Window
{
    public ViewLogWindow(Window owner, ViewRecreationReport report, Action<long> openView)
    {
        Owner = owner; Title = "Журнал пересоздания видов"; Width = 1120; Height = 720;
        MinWidth = 760; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(12) };
        var summary = new TextBlock { Text = report.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
        var filterBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var issuesOnly = new CheckBox { Content = "Только замечания и ошибки", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(issuesOnly, Dock.Right); filterBar.Children.Add(issuesOnly);
        var label = new TextBlock { Text = "Поиск / ID: ", VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left); filterBar.Children.Add(label);
        var search = new TextBox { MinWidth = 180 }; filterBar.Children.Add(search);
        DockPanel.SetDock(filterBar, Dock.Top); panel.Children.Add(filterBar);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };
        void Column(string title, string property, double width) => table.Columns.Add(new DataGridTextColumn
            { Header = title, Binding = new Binding(property), Width = width });
        Column("Уровень", nameof(ViewLogEntry.SeverityLabel), 95);
        Column("Этап", nameof(ViewLogEntry.Stage), 155);
        Column("Исходный вид", nameof(ViewLogEntry.SourceViewId), 100);
        Column("Новый вид", nameof(ViewLogEntry.CreatedViewId), 100);
        Column("Исходный элемент", nameof(ViewLogEntry.ElementId), 125);
        table.Columns.Add(new DataGridTextColumn { Header = "Результат", Binding = new Binding(nameof(ViewLogEntry.Message)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } } });
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetRow(splitter, 1); Grid.SetRow(details, 2);
        grid.Children.Add(table); grid.Children.Add(splitter); grid.Children.Add(details); panel.Children.Add(grid);

        void Safely(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не выполнено", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(6, 10, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
            button.Click += (_, _) => Safely(action); buttons.Children.Add(button); return button;
        }
        var open = Button("Открыть новый вид", () =>
        {
            if (table.SelectedItem is ViewLogEntry { CreatedViewId: { } id }) openView(id);
        });
        var copy = Button("Копировать запись", () =>
        {
            if (table.SelectedItem is ViewLogEntry entry) Clipboard.SetText(entry.FullText);
        });
        open.IsEnabled = false; copy.IsEnabled = false;
        Button("Копировать весь журнал", () => Clipboard.SetText(report.FullText));
        Button("Сохранить .txt", () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Текстовый журнал (*.txt)|*.txt", FileName = "ReLevel-log.txt" };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, report.FullText, System.Text.Encoding.UTF8);
        });
        Button("Закрыть", Close).IsCancel = true;
        table.SelectionChanged += (_, _) =>
        {
            var entry = table.SelectedItem as ViewLogEntry;
            details.Text = entry?.FullText ?? "Выберите строку, чтобы увидеть подробности.";
            open.IsEnabled = !report.CriticalFailure && entry?.CreatedViewId is not null;
            copy.IsEnabled = entry is not null;
        };
        void Filter()
        {
            var query = search.Text.Trim();
            table.ItemsSource = report.Entries.Where(e => (issuesOnly.IsChecked != true || e.Severity != LogSeverity.Info)
                && (query.Length == 0 || e.FullText.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            table.SelectedIndex = table.Items.Count > 0 ? 0 : -1;
        }
        search.TextChanged += (_, _) => Filter();
        issuesOnly.Checked += (_, _) => Filter(); issuesOnly.Unchecked += (_, _) => Filter();
        Content = panel; Filter();
    }
}
