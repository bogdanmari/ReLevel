using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class RampCase
{
    private static readonly LevelOffsetBinding Bottom = new(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, BuiltInParameter.STAIRS_BASE_OFFSET);
    private static readonly LevelOffsetBinding Top = new(BuiltInParameter.STAIRS_TOP_LEVEL_PARAM, BuiltInParameter.STAIRS_TOP_OFFSET);

    public static LevelOffsetState[] Capture(Element element) => [Bottom.Capture(element), Top.Capture(element)];

    public static string? UnsupportedReason(Element element)
    {
        if (element is ElementType || element.Category?.BuiltInCategory != BuiltInCategory.OST_Ramps)
            return L.Get("Кейс 15: требуется экземпляр категории Ramps.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 15: группы и сборки пока не поддерживаются.");
        if (element.get_Parameter(BuiltInParameter.STAIRS_MULTISTORY_TOP_LEVEL_PARAM) is not
            { StorageType: StorageType.ElementId, HasValue: true } multistory
            || multistory.AsElementId() != ElementId.InvalidElementId)
            return L.Get("Кейс 15: требуется пандус без многоэтажного верхнего уровня.");
        foreach (var binding in new[] { Bottom, Top })
        {
            if (element.get_Parameter(binding.LevelParameter) is not { StorageType: StorageType.ElementId, HasValue: true } level
                || element.Document.GetElement(level.AsElementId()) is not Level)
                return L.Get("Кейс 15: нужны существующие уровни низа и верха.");
            if (element.get_Parameter(binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
                return L.Get("Кейс 15: недоступно смещение низа или верха.");
        }
        var ends = Capture(element);
        return ends[1].Position.Absolute > ends[0].Position.Absolute ? null
            : L.Get("Кейс 15: верх пандуса должен быть выше низа.");
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
        if (element.Pinned) return L.Get("Кейс 15: закреплённый пандус не переносится.");
        return Capture(element).Any(end => end.Changes(source) && !end.CanWrite(element))
            ? L.Get("Кейс 15: уровень или смещение изменяемого конца недоступны для записи.") : null;
    }
}
