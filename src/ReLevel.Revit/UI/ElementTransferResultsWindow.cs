using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    public ElementTransferResultsWindow(Window owner, string summary,
        IReadOnlyList<ElementTransferResultRow> rows, bool stopped)
    {
        Owner = owner; Title = summary; Width = 900; Height = 520;
        MinWidth = 560; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        if (stopped)
        {
            var warning = new TextBlock
            {
                Text = L.Get("Обработка остановлена. Ранее завершённые переносы сохранены; окно будет закрыто. Проверьте документ и при необходимости используйте Undo."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(warning, Dock.Top); panel.Children.Add(warning);
        }
        var close = new Button { Content = L.Get("Закрыть"), IsCancel = true, IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var table = new DataGrid
        {
            IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            ItemsSource = rows
        };
        table.Columns.Add(new DataGridTextColumn { Header = "ID",
            Binding = new Binding(nameof(ElementTransferResultRow.Id)), Width = 115 });
        table.Columns.Add(new DataGridTextColumn { Header = "Status",
            Binding = new Binding(nameof(ElementTransferResultRow.Status)), Width = 140 });
        table.Columns.Add(new DataGridTextColumn { Header = "Information",
            Binding = new Binding(nameof(ElementTransferResultRow.Information)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 220,
            ElementStyle = new Style(typeof(TextBlock))
            {
                Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) }
            } });
        panel.Children.Add(table);
        Content = panel;
    }
}
