using System.Globalization;

namespace ReLevel.Revit.Logic;

// All values crossing this boundary are internal feet. A display change never changes stored lengths.
internal sealed class LengthUnitSettings(string projectLabel, Func<double, string> projectFormat,
    Func<string, double?> projectParse, IReadOnlyList<string> projectAliases)
{
    private bool internalFeet;
    public event Action? Changed;
    public bool InternalFeet
    {
        get => internalFeet;
        set { if (internalFeet == value) return; internalFeet = value; Changed?.Invoke(); }
    }
    public string Caption(bool english) => InternalFeet ? (english ? "decimal ft" : "десятичные футы") : projectLabel;
    public string Format(double feet) => InternalFeet ? feet.ToString("0.#########", CultureInfo.InvariantCulture) : projectFormat(feet);
    public string Format(double? feet) => feet is { } value ? Format(value) : "—";
    public double? Parse(string input) => InternalFeet ? ParseDecimal(input) : projectParse(input);
    public bool AcceptsHeaderUnit(string unit) => unit.Length == 0 ||
        (InternalFeet ? FeetAliases : projectAliases).Contains(unit, StringComparer.OrdinalIgnoreCase);
    public static double? ParseDecimal(string input) => double.TryParse(input.Trim().Replace(',', '.'), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    public static readonly string[] FeetAliases = ["ft", "feet", "decimal feet", "decimal ft", "фут", "футы", "футов", "десятичные футы"];
    public static LengthUnitSettings FeetOnly() => new("ft", feet => feet.ToString("0.#########", CultureInfo.InvariantCulture),
        ParseDecimal, FeetAliases);
}
