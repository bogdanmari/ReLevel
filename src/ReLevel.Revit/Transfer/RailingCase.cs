using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal static class RailingCase
{
    public static readonly LevelOffsetBinding Binding = new(
        BuiltInParameter.STAIRS_RAILING_BASE_LEVEL_PARAM, BuiltInParameter.STAIRS_RAILING_HEIGHT_OFFSET);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not Railing railing)
            return L.Get("Кейс 10: требуется ограждение класса Railing.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 10: группы и сборки пока не поддерживаются.");
        if (railing.HasHost || railing.HostId != ElementId.InvalidElementId)
            return L.Get("Кейс 10: ограждение на хосте пока не переносится отдельно.");
        if (SourceLevel(element) is null)
            return L.Get("Кейс 10: Base Level не указывает на существующий уровень.");
        if (element.get_Parameter(Binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 10: недоступен Base Offset ограждения.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } p
            ? element.Document.GetElement(p.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 10: закреплённое ограждение не переносится.");
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 10: Base Level или Base Offset недоступен для записи.");
    }
}
