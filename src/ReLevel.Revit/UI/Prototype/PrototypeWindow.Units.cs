using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow
{
    private void BindUnitLabel(TextBlock label, string key)
    {
        void Update() => label.Text = $"{text[key]}, {units.Caption(text.English)}";
        units.Changed += Update; text.PropertyChanged += (_, _) => Update(); Update();
    }

    private sealed class LengthDisplayConverter(LengthUnitSettings units) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double feet ? units.Format(feet) : "—";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
