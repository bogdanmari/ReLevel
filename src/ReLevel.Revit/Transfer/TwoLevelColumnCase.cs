using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class TwoLevelColumnCase
{
    public static readonly LevelOffsetBinding Bottom = new(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
    public static readonly LevelOffsetBinding Top = new(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FamilyInstance instance || element.Category?.BuiltInCategory != BuiltInCategory.OST_StructuralColumns)
            return L.Get("Кейс 2: требуется несущая колонна FamilyInstance.");
        if (instance.Symbol.Family.IsInPlace || instance.Symbol.Family.FamilyPlacementType != FamilyPlacementType.TwoLevelsBased)
            return L.Get("Кейс 2: требуется загружаемое семейство TwoLevelsBased.");
        if (instance.IsSlantedColumn || instance.Location is not LocationPoint
            || instance.get_Parameter(BuiltInParameter.SLANTED_COLUMN_TYPE_PARAM)?.AsInteger() != 0)
            return L.Get("Кейс 2: требуется вертикальная колонна с LocationPoint.");
        if (instance.Host is not null || instance.HostFace is not null || instance.SuperComponent is not null)
            return L.Get("Кейс 2: хостовые и вложенные экземпляры не поддерживаются.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 2: элементы групп и сборок пока не поддерживаются.");
        if (instance.GetSubComponentIds().Count > 0)
            return L.Get("Кейс 2: колонны с вложенными компонентами пока не поддерживаются.");
        using var baseAttachment = ColumnAttachment.GetColumnAttachment(instance, 0);
        using var topAttachment = ColumnAttachment.GetColumnAttachment(instance, 1);
        if (baseAttachment is not null || topAttachment is not null)
            return L.Get("Кейс 2: присоединённый верх или низ колонны не поддерживается.");
        // Joins, solid cuts and dependent elements do not change the level/offset algorithm.
        if (instance.GetCopingIds().Count > 0)
            return L.Get("Кейс 2: подрезки coping пока не поддерживаются.");
        if (InstanceVoidCutUtils.CanBeCutWithVoid(element) && InstanceVoidCutUtils.GetCuttingVoidInstances(element).Count > 0)
            return L.Get("Кейс 2: внешние вырезы пока не поддерживаются.");
        if (instance.get_Parameter(BuiltInParameter.ANALYTICAL_ELEMENT_HAS_ASSOCIATION)?.AsInteger() == 1)
            return L.Get("Кейс 2: связь с аналитическим элементом пока не поддерживается.");
        if (new[] { BuiltInParameter.STRUCT_CONNECTION_COLUMN_BASE, BuiltInParameter.STRUCT_CONNECTION_COLUMN_TOP }
            .Any(name => instance.get_Parameter(name) is { HasValue: true } parameter && parameter.AsElementId() != ElementId.InvalidElementId))
            return L.Get("Кейс 2: соединения концов колонны пока не поддерживаются.");
        var bottom = Bottom.Capture(element);
        var top = Top.Capture(element);
        if (top.Position.Absolute <= bottom.Position.Absolute)
            return L.Get("Кейс 2: верх должен находиться выше низа.");
        return null;
    }
}
