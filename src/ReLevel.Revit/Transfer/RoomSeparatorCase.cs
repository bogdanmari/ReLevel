using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class RoomSeparatorCase
{
    public const double ElevationTolerance = 1e-8; // Feet; accommodates coincident imported levels.

    public static string? UnsupportedReason(Element element)
    {
        if (element is not ModelLine line || element.Category?.BuiltInCategory != BuiltInCategory.OST_RoomSeparationLines)
            return L.Get("Кейс 8: требуется прямая Room Separation Line.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId
            || element.DesignOption is not null)
            return L.Get("Кейс 8: группы, сборки и варианты конструкции пока не поддерживаются.");
        if (element.Document.GetElement(element.LevelId) is not Level level)
            return L.Get("Кейс 8: линия не связана с существующим уровнем.");
        var curve = line.GeometryCurve;
        if (!curve.IsBound || Enumerable.Range(0, 2).Any(i =>
            Math.Abs(curve.GetEndPoint(i).Z - level.ProjectElevation) > ElevationTolerance))
            return L.Get("Кейс 8: линия должна лежать на отметке своего уровня.");
        // Opaque internal dependents accompany the tested separator; user-visible dependencies need a separate case.
        if (element.GetDependentElements(null).Where(id => id != element.Id)
            .Select(element.Document.GetElement).Any(e => e is not null && (e.GetType() != typeof(Element) || e.Category is not null)))
            return L.Get("Кейс 8: линия имеет неподдержанные зависимые элементы.");
        return null;
    }

    public static string? WriteRestriction(Element element) => element.Pinned
        ? L.Get("Кейс 8: закреплённая линия не переносится.") : null;
}
