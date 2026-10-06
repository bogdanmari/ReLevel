using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal static class BuildingPadCase
{
    public static readonly LevelOffsetBinding Binding = new(
        BuiltInParameter.LEVEL_PARAM, BuiltInParameter.BUILDINGPAD_HEIGHTABOVELEVEL_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not BuildingPad)
            return L.Get("Кейс 12: требуется площадка класса BuildingPad.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 12: группы и сборки пока не поддерживаются.");
        if (SourceLevel(element) is null)
            return L.Get("Кейс 12: LEVEL_PARAM не указывает на существующий уровень.");
        if (element.get_Parameter(Binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 12: недоступно смещение площадки от уровня.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 12: закреплённая площадка не переносится.");
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 12: уровень или смещение площадки недоступны для записи.");
    }
}
