using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace ReLevel.Revit.Transfer;

internal static class FlexPipeCase
{
    public static Level? SourceLevel(Element element) => PipeCase.SourceLevel(element);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FlexPipe) return L.Get("Кейс 22: требуется труба класса FlexPipe.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 22: группы и сборки пока не поддерживаются.");
        return SourceLevel(element) is null
            ? L.Get("Кейс 22: Reference Level не указывает на существующий уровень.") : null;
    }

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 22: закреплённая гибкая труба не переносится.");
        return PipeCase.LevelParameter(element) is { IsReadOnly: false } ? null
            : L.Get("Кейс 22: Reference Level недоступен для записи.");
    }
}
