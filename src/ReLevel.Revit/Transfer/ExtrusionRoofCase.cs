using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class ExtrusionRoofCase
{
    public static readonly LevelOffsetBinding Binding = new(
        BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM, BuiltInParameter.ROOF_CONSTRAINT_OFFSET_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not ExtrusionRoof || element.Category?.BuiltInCategory != BuiltInCategory.OST_Roofs)
            return L.Get("Кейс 5: требуется крыша выдавливанием класса ExtrusionRoof.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 5: элементы групп и сборок пока не поддерживаются.");
        if (SourceLevel(element) is null)
            return L.Get("Кейс 5: Reference Level не указывает на существующий уровень.");
        if (element.get_Parameter(Binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 5: недоступно смещение крыши от уровня.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 5: закреплённая крыша не переносится.");
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 5: уровень или смещение крыши недоступны для записи.");
    }
}
