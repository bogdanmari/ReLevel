using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ReLevel.Revit.UI;

// Shared presentation resources for the dockable workspace.
internal static class ReLevelTheme
{
    public static Brush Color(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    public static readonly Brush Text = Color("#242424"), Muted = Color("#616161"),
        Accent = Color("#157D88"), Border = Color("#D6DADF"), Selected = Color("#DDEFF1");

    public static void Table(DataGrid table)
    {
        table.FontSize = 13;
        table.Foreground = Text;
        table.ColumnHeaderHeight = 40;
        table.RowHeight = table.MinRowHeight = 34;
        table.Background = table.RowBackground = Brushes.White;
        table.AlternatingRowBackground = Color("#FAFBFC");
        table.BorderBrush = Border;
        table.HorizontalGridLinesBrush = table.VerticalGridLinesBrush = Color("#E8EAED");
        table.CellStyle = Cells();
        table.EnableRowVirtualization = table.EnableColumnVirtualization = true;
        ScrollViewer.SetHorizontalScrollBarVisibility(table, ScrollBarVisibility.Auto);
        var row = new Style(typeof(DataGridRow));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Color("#F0F5F6")));
        row.Triggers.Add(hover);
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Selected));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        row.Triggers.Add(selected);
        table.RowStyle = row;
    }

    public static Style Cells()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Selected));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Triggers.Add(selected);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        style.Triggers.Add(focus);
        return style;
    }

    public static Style Toggle()
    {
        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { System.Windows.Controls.Border.BackgroundProperty, System.Windows.Controls.Border.BorderBrushProperty,
                     System.Windows.Controls.Border.BorderThicknessProperty, System.Windows.Controls.Border.PaddingProperty })
            frame.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ToggleButton)) { VisualTree = frame }));
        foreach (var property in new[] { ToggleButton.IsCheckedProperty, UIElement.IsKeyboardFocusedProperty, UIElement.IsMouseOverProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, Color("#E7F3F4")));
            trigger.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            style.Triggers.Add(trigger);
        }
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.BackgroundProperty, Color("#F1F2F3")));
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, Color("#737373")));
        style.Triggers.Add(disabled);
        return style;
    }

    public static Style Link()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Color("#106B75")));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var captionStyle = new Style(typeof(TextBlock));
        var hover = new DataTrigger { Binding = new Binding("IsMouseOver") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) }, Value = true };
        hover.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Underline));
        captionStyle.Triggers.Add(hover); label.SetValue(FrameworkElement.StyleProperty, captionStyle);
        var frame = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        frame.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(6, 3, 6, 3));
        frame.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
        frame.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        frame.AppendChild(label);
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = frame }));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); style.Triggers.Add(focus);
        return style;
    }
}
