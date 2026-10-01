using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class FootPrintRoofCase
{
    public static readonly LevelOffsetBinding Binding = new(
        BuiltInParameter.ROOF_BASE_LEVEL_PARAM, BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FootPrintRoof || element.Category?.BuiltInCategory != BuiltInCategory.OST_Roofs)
            return L.Get("Кейс 6: требуется крыша по контуру класса FootPrintRoof.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 6: элементы групп и сборок пока не поддерживаются.");
        if (element.get_Parameter(BuiltInParameter.ROOF_UPTO_LEVEL_PARAM) is not
            { StorageType: StorageType.ElementId, HasValue: true } cutoff
            || cutoff.AsElementId() != ElementId.InvalidElementId)
            return L.Get("Кейс 6: требуется крыша без уровня среза (Cutoff Level = None).");
        if (SourceLevel(element) is null)
            return L.Get("Кейс 6: Base Level не указывает на существующий уровень.");
        if (element.get_Parameter(Binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 6: недоступно смещение крыши от уровня.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 6: закреплённая крыша не переносится.");
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 6: уровень или смещение крыши недоступны для записи.");
    }
}
