using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class AreaBoundaryCase
{
    public static string? UnsupportedReason(Element element)
    {
        if (element is not ModelLine line || element.Category?.BuiltInCategory != BuiltInCategory.OST_AreaSchemeLines)
            return L.Get("Кейс 18: требуется прямая Area Boundary Line.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId
            || element.DesignOption is not null)
            return L.Get("Кейс 18: группы, сборки и варианты конструкции пока не поддерживаются.");
        if (element.Document.GetElement(element.LevelId) is not Level level)
            return L.Get("Кейс 18: не найден исходный уровень.");
        var curve = line.GeometryCurve;
        if (!curve.IsBound || Enumerable.Range(0, 2).Any(i =>
            Math.Abs(curve.GetEndPoint(i).Z - level.ProjectElevation) > RoomSeparatorCase.ElevationTolerance))
            return L.Get("Кейс 18: линия должна лежать на отметке своего уровня.");
        if (element.GetDependentElements(null).Where(id => id != element.Id)
            .Select(element.Document.GetElement).Any(e => e is not null && (e.GetType() != typeof(Element) || e.Category is not null)))
            return L.Get("Кейс 18: линия имеет неподдержанные зависимые элементы.");
        return null;
    }

    public static string? WriteRestriction(Element element) => element.Pinned
        ? L.Get("Кейс 18: закреплённая линия не пересоздаётся.") : null;

    public static ElementId FindScheme(Element line)
    {
        using var collector = new FilteredElementCollector(line.Document).OfClass(typeof(AreaScheme));
        var schemes = collector.Cast<AreaScheme>()
            .Where(scheme => scheme.GetDependentElements(null).Contains(line.Id)).Select(scheme => scheme.Id).ToList();
        return schemes.Count == 1 ? schemes[0]
            : throw new InvalidOperationException(L.Get("Кейс 18: не удалось однозначно определить схему площади для линии."));
    }
}
