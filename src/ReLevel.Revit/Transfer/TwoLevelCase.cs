using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class TwoLevelCase
{
    public static string? UnsupportedReason(Element element) => element is Wall
        ? TwoLevelWallCase.UnsupportedReason(element) : TwoLevelColumnCase.UnsupportedReason(element);

    public static LevelOffsetState[] Capture(Element element) => element is Wall wall
        ? TwoLevelWallCase.Capture(wall)
        : TwoLevelColumnCase.Capture((FamilyInstance)element);

    public static bool IsOnLevel(Element element, ElementId source) => Capture(element).Any(end => end.Changes(source));

    public static string Relation(Element element, ElementId source)
    {
        var ends = Capture(element);
        var bottom = ends[0].Changes(source);
        var top = ends.Length > 1 && ends[1].Changes(source);
        return bottom && top ? L.Get("Низ и верх") : bottom ? L.Get("Низ") : top ? L.Get("Верх") : "—";
    }

    public static string? WriteRestriction(Element element, ElementId source)
    {
        if (element.Pinned) return L.Get("Кейс 2: закреплённый элемент не переносится.");
        if (element is FamilyInstance instance
            && TwoLevelColumnCase.WriteRestriction(instance, source) is { } restriction) return restriction;
        return Capture(element).Any(end => end.Changes(source) && !end.CanWrite(element))
            ? L.Get("Кейс 2: уровень или смещение изменяемого конца недоступны для записи.") : null;
    }
}
