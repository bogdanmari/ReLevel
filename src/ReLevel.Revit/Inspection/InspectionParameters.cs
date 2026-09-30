using Autodesk.Revit.DB;
using static ReLevel.Revit.Inspection.InspectionMarkdown;

namespace ReLevel.Revit.Inspection;

internal sealed class InspectionParameters(InspectionMarkdown output, Func<ElementId?, string> describe)
{
    public void Write(Element element)
    {
        output.Table("ID / BuiltInParameter", "Имя", "Shared GUID", "DataType", "StorageType", "IsReadOnly", "HasValue",
            "Значение API", "AsValueString", "UnitTypeId");
        foreach (Parameter parameter in element.Parameters)
        {
            output.Row(
                output.Read(() => ParameterId(parameter)),
                output.Read(() => parameter.Definition?.Name),
                output.Read(() => parameter.IsShared ? parameter.GUID.ToString() : "—"),
                output.Read(() => parameter.Definition?.GetDataType().TypeId),
                output.Read(() => parameter.StorageType.ToString()),
                output.Read(() => parameter.IsReadOnly.ToString()),
                output.Read(() => parameter.HasValue.ToString()),
                output.Read(() => Value(parameter)),
                output.Read(() => parameter.HasValue ? parameter.AsValueString() : "—"),
                output.Read(() => parameter.StorageType == StorageType.Double ? parameter.GetUnitTypeId().TypeId : "—"));
        }
    }

    private static string ParameterId(Parameter parameter)
    {
        var id = parameter.Id.Value;
        var name = id is >= int.MinValue and < 0 ? Enum.GetName(typeof(BuiltInParameter), (int)id) : null;
        return name is null ? id.ToString() : $"{id} / {name}";
    }

    private string? Value(Parameter parameter) => !parameter.HasValue ? "(нет значения)" : parameter.StorageType switch
    {
        StorageType.Double => Number(parameter.AsDouble()),
        StorageType.Integer => parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
        StorageType.String => parameter.AsString(),
        StorageType.ElementId => describe(parameter.AsElementId()),
        _ => "(StorageType.None)"
    };
}
