using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using System.IO;
using System.Text;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI;

internal sealed record ElementTransferResultRow(long Id, string Status, string Information)
{
    public static ElementTransferResultRow From(ElementTransferResult result) => new(result.Id,
        result.Status switch
        {
            ElementTransferStatus.Transferred => L.Get("Перенесён"),
            ElementTransferStatus.Skipped => L.Get("Пропущен"),
            _ => L.Get("Ошибка")
        }, result.Reason);
}

internal sealed class ElementTransferResultsWindow : Window
{
    private readonly DataGrid table;
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 20, Margin = new Thickness(0, 8, 0, 0) };

    public ElementTransferResultsWindow(Window owner, string summary,
        IReadOnlyList<ElementTransferResultRow> rows, bool stopped)
    {
        Owner = owner; Title = summary; Width = 900; Height = 520;
        MinWidth = 560; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(244, 247, 250));
        Foreground = new SolidColorBrush(Color.FromRgb(24, 50, 71));
        var panel = new DockPanel { Margin = new Thickness(20), LayoutTransform = new ScaleTransform(0.9, 0.9) };
        var heading = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap,
            FontSize = 16, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        if (stopped)
        {
            var warning = new TextBlock
            {
                Text = L.Get("Обработка остановлена. Ранее завершённые переносы сохранены; окно будет закрыто. Проверьте документ и при необходимости используйте Undo."),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(warning, Dock.Top); panel.Children.Add(warning);
        }
        var footer = new StackPanel();
        var actions = TransferLayout.Actions();
        Button ActionButton(string caption, Action? action)
        {
            var button = new Button { Content = L.Get(caption), Margin = new Thickness(8, 0, 0, 6),
                Padding = new Thickness(14, 8, 14, 8), IsEnabled = action is null || rows.Count > 0 };
            if (action is not null) button.Click += (_, _) => Run(action);
            actions.Children.Add(button); return button;
        }
        ActionButton("Копировать всё", () => Copy(false));
        ActionButton("Экспорт в TSV", Export);
        var close = ActionButton("Закрыть", null); close.IsCancel = true; close.IsDefault = true;
        footer.Children.Add(actions); footer.Children.Add(feedback);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        table = new DataGrid
        {
            IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            CanUserSortColumns = true,
            ItemsSource = new ListCollectionView(rows.ToList())
        };
        TransferLayout.StyleTableAppearance(table);
        table.Columns.Add(new DataGridTextColumn { Header = "ID",
            Binding = new Binding(nameof(ElementTransferResultRow.Id)), Width = 115 });
        table.Columns.Add(new DataGridTextColumn { Header = "Status",
            Binding = new Binding(nameof(ElementTransferResultRow.Status)), Width = 140 });
        table.Columns.Add(new DataGridTextColumn { Header = "Information",
            Binding = new Binding(nameof(ElementTransferResultRow.Information)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 220 });
        foreach (var column in table.Columns.OfType<DataGridTextColumn>())
        {
            column.SortMemberPath = ((Binding)column.Binding).Path.Path;
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 7, 8, 7)));
            column.ElementStyle = style;
        }
        table.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            (_, e) => { Run(() => Copy(true)); e.Handled = true; },
            (_, e) => { e.CanExecute = table.SelectedItems.Count > 0; e.Handled = true; }));
        panel.Children.Add(table);
        Content = panel;
    }

    private string ReportText(bool selectedOnly = false)
    {
        var rows = table.Items.Cast<ElementTransferResultRow>()
            .Where(row => !selectedOnly || table.SelectedItems.Contains(row));
        return TabSeparatedValues.Format(["ID", "Status", "Information"], rows.Select(row => new[]
            { row.Id.ToString(CultureInfo.InvariantCulture), row.Status, row.Information }));
    }

    private void Copy(bool selectedOnly)
    {
        Clipboard.SetText(ReportText(selectedOnly));
        feedback.Text = L.Get("Скопировано в буфер обмена.");
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.Get("Экспорт в TSV"), Filter = "TSV (*.tsv)|*.tsv", DefaultExt = ".tsv",
            AddExtension = true, OverwritePrompt = true, FileName = "ReLevel-transfer.tsv"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, ReportText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        feedback.Text = L.Get("Отчёт сохранён.");
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            feedback.Text = "";
            MessageBox.Show(this, ex.Message, L.Get("Действие не выполнено"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
