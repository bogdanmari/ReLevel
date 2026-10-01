using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace ReLevel.Revit.Transfer;

internal static class PipeCase
{
    public static Parameter? LevelParameter(Element element) =>
        element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);

    public static Level? SourceLevel(Element element) =>
        LevelParameter(element) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? UnsupportedReason(Element element)
    {
        if (element is not Pipe)
            return L.Get("Кейс 11: требуется труба класса Pipe.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 11: группы и сборки пока не поддерживаются.");
        return SourceLevel(element) is null
            ? L.Get("Кейс 11: Reference Level не указывает на существующий уровень.") : null;
    }

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 11: закреплённая труба не переносится.");
        return LevelParameter(element) is { IsReadOnly: false } ? null
            : L.Get("Кейс 11: Reference Level недоступен для записи.");
    }
}
