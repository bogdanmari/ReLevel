using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ReLevel.Revit.UI;

internal static class LanguageLabels
{
    public static void Refresh(DependencyObject root)
    {
        void Translate(DependencyProperty property)
        {
            if (root.GetValue(property) is string text)
                root.SetCurrentValue(property, L.TranslateLabel(text));
        }
        if (root is FrameworkElement)
        {
            Translate(FrameworkElement.ToolTipProperty);
            Translate(AutomationProperties.NameProperty);
        }
        // These controls contain model names, entered text or report data.
        if (root is ComboBox or TextBox) return;
        if (root is DataGrid table)
        {
            foreach (var column in table.Columns)
            {
                if (column.Header is string title) column.Header = L.TranslateLabel(title);
                else if (column.Header is DependencyObject header) Refresh(header);
            }
            return;
        }
        if (root is TextBlock) Translate(TextBlock.TextProperty);
        if (root is ContentControl) Translate(ContentControl.ContentProperty);
        if (root is HeaderedContentControl) Translate(HeaderedContentControl.HeaderProperty);
        if (root is Window) Translate(Window.TitleProperty);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) Refresh(child);
    }
}
