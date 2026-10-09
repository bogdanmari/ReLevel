using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class FabricationPartCase
{
    public static Parameter? LevelParameter(Element element) =>
        element.get_Parameter(BuiltInParameter.FABRICATION_LEVEL_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FabricationPart)
            return L.Get("Кейс 19: требуется FabricationPart.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId
            || element.DesignOption is not null)
            return L.Get("Кейс 19: группы, сборки и варианты конструкции пока не поддерживаются.");
        if (LevelParameter(element) is not
                { StorageType: StorageType.ElementId, HasValue: true } level
            || element.Document.GetElement(level.AsElementId()) is not Level)
            return L.Get("Кейс 19: Reference Level не указывает на существующий уровень.");
        return null;
    }

    public static bool IsOnLevel(Element element, ElementId source) => LevelParameter(element)?.AsElementId() == source;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 19: закреплённая деталь не переносится.");
        return LevelParameter(element) is { IsReadOnly: false } ? null
            : L.Get("Кейс 19: Reference Level недоступен для записи.");
    }
}
