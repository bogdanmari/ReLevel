using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI.Workspace;

internal sealed partial class WorkspaceView
{
    private void BindUnitLabel(TextBlock label, string key)
    {
        void Update()
        {
            // Full project unit names can be arbitrarily long; keep the header compact.
            var caption = units.Caption(text.English);
            var shortUnit = units.InternalFeet ? "ft" : caption.ToLowerInvariant() switch
            {
                "millimeters" or "миллиметры" => "mm",
                "centimeters" or "сантиметры" => "cm",
                "meters" or "метры" => "m",
                "feet" or "футы" or "decimal feet" or "десятичные футы" => "ft",
                "inches" or "дюймы" => "in",
                _ => caption.Length <= 8 ? caption : ""
            };
            label.Text = text[key] + (shortUnit.Length > 0 ? "\n" + shortUnit : "");
            label.ToolTip = $"{text[key]}, {caption}";
        }
        units.Changed += Update; text.PropertyChanged += (_, _) => Update(); Update();
    }

    private sealed class LengthDisplayConverter(LengthUnitSettings units) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double feet ? units.Format(feet) : "—";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
