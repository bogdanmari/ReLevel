using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal interface ITransferStrategy
{
    bool Matches(Element element);
    IReadOnlyList<LevelParameterPair> GetBindings(Element element);
    IReadOnlyList<TransferDependency> GetDependencies(Element element);
    string? UnsupportedReason(Element element);
}

internal sealed class PointFamilyStrategy : ITransferStrategy
{
    public bool Matches(Element e) => e is FamilyInstance;
    public IReadOnlyList<TransferDependency> GetDependencies(Element e) => TransferDependencies.Collect(e, false, true);
    public IReadOnlyList<LevelParameterPair> GetBindings(Element e)
    {
        // Match placement and actual parameter storage, not a category allow-list.
        foreach (var level in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM })
            if (WritableLevel(e, level)
                && e.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM) is { StorageType: StorageType.Double, HasValue: true })
                return [new(level, BuiltInParameter.INSTANCE_ELEVATION_PARAM, AllowDerivedOffsets: e is FamilyInstance { MEPModel: not null })];
        if (e is FamilyInstance { MEPModel: not null }
            && WritableLevel(e, BuiltInParameter.RBS_START_LEVEL_PARAM)
            && e.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM) is { StorageType: StorageType.Double, HasValue: true })
            return [new(BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.RBS_OFFSET_PARAM, AllowDerivedOffsets: true)];
        if (e is FamilyInstance f && (f.Host is not null || f.HostFace is not null)
            && WritableLevel(e, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM))
            foreach (var offset in new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM })
                if (e.get_Parameter(offset) is { StorageType: StorageType.Double, HasValue: true })
                    return [new(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, offset, CompensateOffset: false)];
        return [];
    }
    private static bool WritableLevel(Element element, BuiltInParameter id)
        => element.get_Parameter(id) is { StorageType: StorageType.ElementId, HasValue: true, IsReadOnly: false } p
            && element.Document.GetElement(p.AsElementId()) is Level;

    public string? UnsupportedReason(Element element)
    {
        var f = (FamilyInstance)element;
        if (f.Symbol.Family.IsInPlace || f.SuperComponent is not null)
            return L.Get("In-Place и вложенные компоненты не переносятся самостоятельно.");
        if (f.Symbol.Family.FamilyPlacementType is not (FamilyPlacementType.OneLevelBased
            or FamilyPlacementType.OneLevelBasedHosted or FamilyPlacementType.WorkPlaneBased))
            return L.Get("Способ размещения семейства не поддерживает одноуровневый перенос.");
        if (f.Location is not LocationPoint) return L.Get("У семейства нет точечного размещения.");
        if (GetBindings(f).Count == 0)
            return f.Host is not null || f.HostFace is not null
                ? L.Get("Зависит от хоста: доступной самостоятельной привязки нет.")
                : L.Get("Параметры уровня/смещения отсутствуют или недоступны для записи.");
        return null;
    }
}

internal sealed class HorizontalHostStrategy(bool floor) : ITransferStrategy
{
    public bool Matches(Element e) => floor ? e is Floor : e is Ceiling;
    public IReadOnlyList<LevelParameterPair> GetBindings(Element e) =>
        [new(BuiltInParameter.LEVEL_PARAM, floor ? BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM : BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM)];
    public IReadOnlyList<TransferDependency> GetDependencies(Element e) => TransferDependencies.Collect(e, true, true);
    public string? UnsupportedReason(Element element)
    {
        if (element is Floor f && f.GetSlabShapeEditor() is not { IsEnabled: false })
            return L.Get("Перекрытия с уклоном или редактированной формой пока не поддерживаются.");
        var faces = HostObjectUtils.GetTopFaces((HostObject)element);
        return faces.Count == 0 || faces.Any(r => element.GetGeometryObjectFromReference(r) is not PlanarFace face
                || Math.Abs(face.FaceNormal.Z) < 1 - 1e-9)
            ? L.Get("Поддерживаются только горизонтальные плоские перекрытия и потолки.") : null;
    }
}

internal sealed class WallStrategy : ITransferStrategy
{
    public bool Matches(Element e) => e is Wall;
    public IReadOnlyList<LevelParameterPair> GetBindings(Element e) =>
        [new(BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.WALL_BASE_OFFSET),
            new(BuiltInParameter.WALL_HEIGHT_TYPE, BuiltInParameter.WALL_TOP_OFFSET, Optional: true)];
    public IReadOnlyList<TransferDependency> GetDependencies(Element e) => TransferDependencies.Collect(e, true, true);
    public string? UnsupportedReason(Element element)
    {
        var w = (Wall)element;
        if (w.WallType.Kind != WallKind.Basic || w.IsStackedWallMember || w.CrossSection != WallCrossSection.Vertical
            || w.Location is not LocationCurve { Curve: Line } || w.SketchId != ElementId.InvalidElementId)
            return L.Get("Поддерживаются только прямые вертикальные базовые стены без изменённого профиля.");
        if (w.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() != 0
            || w.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() != 0)
            return L.Get("Присоединённые стены не поддерживаются.");
        return null;
    }
}

internal sealed class ColumnStrategy : ITransferStrategy
{
    public bool Matches(Element e) => e is FamilyInstance && e.Category?.Id.Value is
        (long)BuiltInCategory.OST_Columns or (long)BuiltInCategory.OST_StructuralColumns;
    public IReadOnlyList<LevelParameterPair> GetBindings(Element e) =>
        [new(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM),
            new(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)];
    public IReadOnlyList<TransferDependency> GetDependencies(Element e) => TransferDependencies.Collect(e, true, true);
    public string? UnsupportedReason(Element element)
    {
        var f = (FamilyInstance)element;
        if (f.Symbol.Family.IsInPlace || f.SuperComponent is not null)
            return L.Get("In-Place и вложенные компоненты не переносятся самостоятельно.");
        if (f.Symbol.Family.FamilyPlacementType != FamilyPlacementType.TwoLevelsBased
            || f.IsSlantedColumn || f.GetTransform().BasisZ.CrossProduct(XYZ.BasisZ).GetLength() > 1e-9)
            return L.Get("Поддерживаются только вертикальные двухуровневые колонны.");
        if (f.get_Parameter(BuiltInParameter.COLUMN_BASE_ATTACHED_PARAM)?.AsInteger() == 1
            || f.get_Parameter(BuiltInParameter.COLUMN_TOP_ATTACHED_PARAM)?.AsInteger() == 1)
            return L.Get("Присоединённые колонны не поддерживаются.");
        if (ColumnAttachment.IsValidColumn(f))
            for (var end = 0; end < 2; ++end)
                if (ColumnAttachment.GetColumnAttachment(f, end) is { } attachment)
                { attachment.Dispose(); return L.Get("Присоединённые колонны не поддерживаются."); }
        return null;
    }
}
