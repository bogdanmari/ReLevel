using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class FloorCase
{
    public static readonly LevelOffsetBinding Binding = new(
        BuiltInParameter.LEVEL_PARAM, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not Floor || element.Category?.BuiltInCategory is not (BuiltInCategory.OST_Floors or BuiltInCategory.OST_StructuralFoundation))
            return L.Get("Кейс 4: требуется перекрытие или фундаментная плита класса Floor.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 4: элементы групп и сборок пока не поддерживаются.");
        if (SourceLevel(element) is null)
            return L.Get("Кейс 4: LEVEL_PARAM не указывает на существующий уровень.");
        if (element.get_Parameter(Binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 4: недоступно смещение плиты от уровня.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 4: закреплённая плита не переносится.");
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 4: уровень или смещение плиты недоступны для записи.");
    }
}
