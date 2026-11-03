using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI;

internal static class ProjectLengthUnits
{
    public static LengthUnitSettings Create(Document document)
    {
        var units = document.GetUnits();
        var type = units.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
        var label = LabelUtils.GetLabelForUnit(type);
        var aliases = new List<string> { label };
        if (type == UnitTypeId.Millimeters) aliases.AddRange(["mm", "мм", "millimeters", "миллиметры"]);
        else if (type == UnitTypeId.Meters) aliases.AddRange(["m", "м", "meters", "метры"]);
        else if (type == UnitTypeId.Centimeters) aliases.AddRange(["cm", "см", "centimeters", "сантиметры"]);
        else if (type == UnitTypeId.Feet) aliases.AddRange(LengthUnitSettings.FeetAliases);
        else if (type == UnitTypeId.Inches) aliases.AddRange(["in", "inches", "дюймы"]);
        double? Parse(string input)
        {
            // Bare numeric values use the selected project unit, accepting either decimal separator.
            if (LengthUnitSettings.ParseDecimal(input) is { } numeric)
            {
                var converted = UnitUtils.ConvertToInternalUnits(numeric, type);
                return double.IsFinite(converted) ? converted : null;
            }
            return UnitFormatUtils.TryParse(units, SpecTypeId.Length, input, out var value) && double.IsFinite(value) ? value : null;
        }
        return new(label, value => UnitFormatUtils.Format(units, SpecTypeId.Length, value, false), Parse, aliases);
    }
}
