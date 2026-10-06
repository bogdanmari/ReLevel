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
        if (railing.HasHost != (railing.HostId != ElementId.InvalidElementId))
            return L.Get("Кейс 10: состояние хоста ограждения противоречиво.");
        if (railing.HasHost && element.Document.GetElement(railing.HostId) is not Floor)
            return L.Get("Кейс 10: поддержан только хост класса Floor.");
        if (SourceLevel(element) is null)
            return L.Get("Кейс 10: привязка ограждения не указывает на существующий уровень.");
        if (element.get_Parameter(Binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 10: недоступен Base Offset ограждения.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element is Railing { HasHost: true }
            ? element.Document.GetElement(element.LevelId) as Level
            : element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } p
            ? element.Document.GetElement(p.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 10: закреплённое ограждение не переносится.");
        // A hosted railing's Base Level is expected to be locked until RemoveHost.
        if (element is Railing { HasHost: true })
            return LevelOffsetBinding.Require(element, Binding.OffsetParameter, StorageType.Double).IsReadOnly
                ? L.Get("Кейс 10: Base Offset недоступен для записи.") : null;
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 10: Base Level или Base Offset недоступен для записи.");
    }
}
