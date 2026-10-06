using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class ShaftOpeningCase
{
    private static readonly LevelOffsetBinding Bottom = new(BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.WALL_BASE_OFFSET);
    private static readonly LevelOffsetBinding Top = new(BuiltInParameter.WALL_HEIGHT_TYPE, BuiltInParameter.WALL_TOP_OFFSET);

    public static LevelOffsetState[] Capture(Element element) => [Bottom.Capture(element), Top.Capture(element)];

    public static string? UnsupportedReason(Element element)
    {
        if (element is not Opening || element.Category?.BuiltInCategory != BuiltInCategory.OST_ShaftOpening)
            return L.Get("Кейс 14: требуется шахтный проём класса Opening.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 14: группы и сборки пока не поддерживаются.");
        foreach (var binding in new[] { Bottom, Top })
        {
            if (element.get_Parameter(binding.LevelParameter) is not { StorageType: StorageType.ElementId, HasValue: true } level
                || element.Document.GetElement(level.AsElementId()) is not Level)
                return L.Get("Кейс 14: нужны существующие уровни низа и верха.");
            if (element.get_Parameter(binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
                return L.Get("Кейс 14: недоступно смещение низа или верха.");
        }
        var ends = Capture(element);
        return ends[1].Position.Absolute > ends[0].Position.Absolute ? null
            : L.Get("Кейс 14: верх проёма должен быть выше низа.");
    }

    public static bool IsOnLevel(Element element, ElementId source) => Capture(element).Any(end => end.Changes(source));

    public static string Relation(Element element, ElementId source)
    {
        var ends = Capture(element);
        var bottom = ends[0].Changes(source);
        var top = ends[1].Changes(source);
        return bottom && top ? L.Get("Низ и верх") : bottom ? L.Get("Низ") : top ? L.Get("Верх") : "—";
    }

    public static string? WriteRestriction(Element element, ElementId source)
    {
        if (element.Pinned) return L.Get("Кейс 14: закреплённый проём не переносится.");
        return Capture(element).Any(end => end.Changes(source) && !end.CanWrite(element))
            ? L.Get("Кейс 14: уровень или смещение изменяемого конца недоступны для записи.") : null;
    }
}
