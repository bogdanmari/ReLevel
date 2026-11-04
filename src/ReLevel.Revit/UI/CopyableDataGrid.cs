using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReLevel.Revit.UI;

internal sealed class CopyableDataGrid : DataGrid
{
    private readonly CopyNotification notification = new();

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.ClickCount != 2) return;
        // Hit testing also handles clicks on padding and text in template columns.
        var source = InputHitTest(e.GetPosition(this)) as DependencyObject;
        while (source is not null && source is not DataGridCell && source != this)
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        if (source is not DataGridCell cell) return;
        var text = string.Join(" ", ReadText(cell));
        if (text.Length == 0) return;
        e.Handled = true;
        var owner = Window.GetWindow(this);
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(owner, ex.Message, L.Get("Действие не выполнено"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        notification.Show(this, L.Get("Скопировано в буфер обмена."));
    }

    private static IEnumerable<string> ReadText(DependencyObject element)
    {
        if (element is UIElement { Visibility: not Visibility.Visible } || element is CheckBox) yield break;
        if (element is TextBlock block)
        {
            yield return block.Text;
            yield break;
        }
        if (element is TextBox box)
        {
            yield return box.Text;
            yield break;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var text in ReadText(VisualTreeHelper.GetChild(element, i)))
                yield return text;
    }
}
