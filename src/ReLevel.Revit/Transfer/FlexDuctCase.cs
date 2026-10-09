using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace ReLevel.Revit.Transfer;

internal static class FlexDuctCase
{
    public static Level? SourceLevel(Element element) => DuctCase.SourceLevel(element);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FlexDuct) return L.Get("Кейс 20: требуется воздуховод класса FlexDuct.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 20: группы и сборки пока не поддерживаются.");
        return SourceLevel(element) is null
            ? L.Get("Кейс 20: Reference Level не указывает на существующий уровень.") : null;
    }

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 20: закреплённый воздуховод не переносится.");
        return DuctCase.LevelParameter(element) is { IsReadOnly: false } ? null
            : L.Get("Кейс 20: Reference Level недоступен для записи.");
    }
}
