using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Workspace;

internal static class WorkspaceButtonStyle
{
    public static Style Create(bool primary = false)
    {
        static Brush Color(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Color(primary ? "#157D88" : "#FFFFFF")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Color(primary ? "#FFFFFF" : "#242424")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Color(primary ? "#157D88" : "#D6DADF")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
            border.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty,
            new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        presenter.SetBinding(FrameworkElement.VerticalAlignmentProperty,
            new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Color(primary ? "#106B75" : "#E7F3F4")));
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, Color("#157D88"))); style.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Color("#157D88"))); style.Triggers.Add(focus);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, Color(primary ? "#0C5962" : "#DDEFF1"))); style.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.BackgroundProperty, Color("#F1F2F3"))); disabled.Setters.Add(new Setter(Control.ForegroundProperty, Color("#737373"))); style.Triggers.Add(disabled);
        return style;
    }
}
