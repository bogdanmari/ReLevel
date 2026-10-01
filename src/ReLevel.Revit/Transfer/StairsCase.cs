using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal static class StairsCase
{
    private static readonly LevelOffsetBinding Bottom = new(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, BuiltInParameter.STAIRS_BASE_OFFSET);
    private static readonly LevelOffsetBinding Top = new(BuiltInParameter.STAIRS_TOP_LEVEL_PARAM, BuiltInParameter.STAIRS_TOP_OFFSET);

    private static bool HasUnconnectedTop(Element element) =>
        LevelOffsetBinding.Require(element, Top.LevelParameter, StorageType.ElementId).AsElementId() == ElementId.InvalidElementId;

    public static LevelOffsetState[] Capture(Element element) => HasUnconnectedTop(element)
        ? [Bottom.Capture(element)] : [Bottom.Capture(element), Top.Capture(element)];

    public static string? UnsupportedReason(Element element)
    {
        if (element is not Stairs stairs)
            return L.Get("Кейс 9: требуется лестница класса Stairs.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 9: лестницы в группах и сборках пока не поддерживаются.");
        if (stairs.MultistoryStairsId != ElementId.InvalidElementId)
            return L.Get("Кейс 9: многоэтажные лестницы пока не поддерживаются.");
        if (element.get_Parameter(Top.LevelParameter) is not { StorageType: StorageType.ElementId, HasValue: true })
            return L.Get("Кейс 9: недоступна верхняя привязка лестницы.");
        var unconnected = HasUnconnectedTop(element);
        if (unconnected && element.get_Parameter(BuiltInParameter.STAIRS_STAIRS_HEIGHT) is not
            { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 9: недоступна заданная высота лестницы.");
        if (unconnected && element.get_Parameter(BuiltInParameter.STAIRS_STAIRS_HEIGHT).AsDouble() <= 0)
            return L.Get("Кейс 9: заданная высота лестницы должна быть положительной.");
        foreach (var binding in unconnected ? new[] { Bottom } : new[] { Bottom, Top })
        {
            if (element.get_Parameter(binding.LevelParameter) is not { StorageType: StorageType.ElementId, HasValue: true } level
                || element.Document.GetElement(level.AsElementId()) is not Level)
                return L.Get("Кейс 9: нужны существующие Base Level и Top Level.");
            if (element.get_Parameter(binding.OffsetParameter) is not { StorageType: StorageType.Double, HasValue: true })
                return L.Get("Кейс 9: недоступно смещение низа или верха лестницы.");
        }
        var ends = Capture(element);
        return unconnected || ends[1].Position.Absolute > ends[0].Position.Absolute ? null
            : L.Get("Кейс 9: верх лестницы должен быть выше низа.");
    }

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
        if (element.Pinned) return L.Get("Кейс 9: закреплённая лестница не переносится.");
        return Capture(element).Any(end => end.Changes(source) && !end.CanWrite(element))
            ? L.Get("Кейс 9: уровень или смещение изменяемого конца недоступны для записи.") : null;
    }
}
