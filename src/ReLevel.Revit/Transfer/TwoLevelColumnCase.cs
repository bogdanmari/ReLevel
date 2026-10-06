using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class TwoLevelColumnCase
{
    public static readonly LevelOffsetBinding Bottom = new(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
    public static readonly LevelOffsetBinding Top = new(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);

    public static LevelOffsetState[] Capture(FamilyInstance instance)
    {
        var top = Top.Capture(instance);
        if (instance.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns
            && ColumnAttachment.IsValidColumn(instance))
        {
            using var attachment = ColumnAttachment.GetColumnAttachment(instance, 1);
            // Revit calculates the attached top offset when its reference level changes.
            if (attachment is not null) top = top with { WriteOffset = false };
        }
        return [Bottom.Capture(instance), top];
    }

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FamilyInstance instance || element.Category?.BuiltInCategory is not
            (BuiltInCategory.OST_StructuralColumns or BuiltInCategory.OST_Columns))
            return L.Get("Кейс 2: требуется несущая или архитектурная колонна FamilyInstance.");
        var structural = element.Category.BuiltInCategory == BuiltInCategory.OST_StructuralColumns;
        if (instance.Symbol.Family.IsInPlace || instance.Symbol.Family.FamilyPlacementType != FamilyPlacementType.TwoLevelsBased)
            return L.Get("Кейс 2: требуется загружаемое семейство TwoLevelsBased.");
        if (!HasSupportedPlacement(instance, structural))
            return L.Get("Кейс 2: требуется Vertical с LocationPoint или вертикальная несущая колонна End Point Driven с прямой LocationCurve.");
        if (instance.Host is not null || instance.HostFace is not null || instance.SuperComponent is not null)
            return L.Get("Кейс 2: хостовые и вложенные экземпляры не поддерживаются.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 2: элементы групп и сборок пока не поддерживаются.");
        if (instance.GetSubComponentIds().Count > 0)
            return L.Get("Кейс 2: колонны с вложенными компонентами пока не поддерживаются.");
        if (ColumnAttachment.IsValidColumn(instance))
        {
            using var baseAttachment = ColumnAttachment.GetColumnAttachment(instance, 0);
            if (baseAttachment is not null)
                return L.Get("Кейс 2: присоединённый низ колонны пока не поддерживается.");
        }
        // Joins, solid cuts and dependent elements do not change the level/offset algorithm.
        if (instance.GetCopingIds().Count > 0)
            return L.Get("Кейс 2: подрезки coping пока не поддерживаются.");
        if (InstanceVoidCutUtils.CanBeCutWithVoid(element) && InstanceVoidCutUtils.GetCuttingVoidInstances(element).Count > 0)
            return L.Get("Кейс 2: внешние вырезы пока не поддерживаются.");
        // Analytical association does not prevent changing the physical column's level/offset pairs.
        // Keep the association; analytical elements are not edited by this operation.
        if (new[] { BuiltInParameter.STRUCT_CONNECTION_COLUMN_BASE, BuiltInParameter.STRUCT_CONNECTION_COLUMN_TOP }
            .Any(name => instance.get_Parameter(name) is { HasValue: true } parameter && parameter.AsElementId() != ElementId.InvalidElementId))
            return L.Get("Кейс 2: соединения концов колонны пока не поддерживаются.");
        var bottom = Bottom.Capture(element);
        var top = Top.Capture(element);
        if (top.Position.Absolute <= bottom.Position.Absolute)
            return L.Get("Кейс 2: верх должен находиться выше низа.");
        return null;
    }

    private static bool HasSupportedPlacement(FamilyInstance instance, bool structural)
    {
        var style = instance.get_Parameter(BuiltInParameter.SLANTED_COLUMN_TYPE_PARAM);
        if (!instance.IsSlantedColumn && instance.Location is LocationPoint)
            return !structural || style?.AsInteger() == 0;

        // End Point Driven can have a vertical axis while still reporting IsSlantedColumn.
        // This is case selection, not a before/after geometry check during transfer.
        if (!structural || !instance.IsSlantedColumn || style?.AsInteger() != 2
            || instance.Location is not LocationCurve { Curve: Line { IsBound: true } axis })
            return false;
        var delta = axis.GetEndPoint(1) - axis.GetEndPoint(0);
        const double tolerance = 1e-9; // Revit internal length units (feet).
        return delta.X * delta.X + delta.Y * delta.Y <= tolerance * tolerance
            && Math.Abs(delta.Z) > tolerance;
    }

    public static string? WriteRestriction(FamilyInstance instance, ElementId source)
    {
        if (instance.Category?.BuiltInCategory != BuiltInCategory.OST_Columns
            || !Top.Capture(instance).Changes(source)
            || !ColumnAttachment.IsValidColumn(instance)) return null;
        using var attachment = ColumnAttachment.GetColumnAttachment(instance, 1);
        return attachment is not null
            ? L.Get("Кейс 2: перенос присоединённого верха архитектурной колонны пока не поддерживается.") : null;
    }
}
