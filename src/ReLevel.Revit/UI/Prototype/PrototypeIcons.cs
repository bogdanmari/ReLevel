using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ReLevel.Revit.UI.Prototype;

internal static class PrototypeIcons
{
    public static void SetSearchButtonIcon(ButtonBase button, string key)
    {
        var icon = Create(key, new SolidColorBrush(Color.FromRgb(21, 125, 136)), 16);
        icon.IsHitTestVisible = false;
        var style = new Style(typeof(FrameworkElement));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35)); style.Triggers.Add(disabled);
        icon.Style = style; button.Content = icon;
        ToolTipService.SetShowOnDisabled(button, true);
    }

    public static FrameworkElement Create(string key, Brush stroke, double size = 22)
    {
        string data = key switch
        {
            "Filter" => "M3,4 H21 L14,12 V19 L10,21 V12 Z",
            "Previous" => "M12,21 V3 M5,10 L12,3 L19,10",
            "Next" => "M12,3 V21 M5,14 L12,21 L19,14",
            "Distribute" => "M2,4 H22 M2,12 H22 M2,20 H22 M7,2 V6 M16,10 V14 M10,18 V22",
            "Colors" => "M12,2 C5,2 2,6 2,12 C2,20 10,23 14,20 C17,17 11,16 16,13 C22,14 24,8 19,4 Z M7,7 H9 M13,5 H15 M5,12 H7",
            "Transfer" => "M2,7 H21 L16,2 M21,7 L16,12 M22,17 H3 L8,12 M3,17 L8,22",
            "Debug" => "M7,8 H17 V17 C17,23 7,23 7,17 Z M9,8 V4 H15 V8 M2,10 H7 M17,10 H22 M2,16 H7 M17,16 H22 M5,23 L8,19 M19,23 L16,19",
            _ => "M2,6 H22 M2,12 H22 M2,18 H22 M7,3 V9 M17,9 V15 M10,15 V21"
        };
        return new Path { Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = 1.7,
            Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    }
}
