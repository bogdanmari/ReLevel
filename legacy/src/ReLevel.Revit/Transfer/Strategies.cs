using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal interface ITransferStrategy
{
    bool Matches(Element element);
    long LevelParameter { get; }
    long OffsetParameter { get; }
    string? UnsupportedReason(Element element);
}

internal sealed class PointFamilyStrategy : ITransferStrategy
{
    private static readonly HashSet<long> Categories = [
        (long)BuiltInCategory.OST_Furniture, (long)BuiltInCategory.OST_FurnitureSystems,
        (long)BuiltInCategory.OST_Casework, (long)BuiltInCategory.OST_GenericModel,
        (long)BuiltInCategory.OST_SpecialityEquipment,
        (long)BuiltInCategory.OST_Planting, (long)BuiltInCategory.OST_Entourage,
        (long)BuiltInCategory.OST_Site];
    public bool Matches(Element e) => e is FamilyInstance && Categories.Contains(e.Category?.Id.ToLong() ?? 0);
    public long LevelParameter => (long)Autodesk.Revit.DB.BuiltInParameter.FAMILY_LEVEL_PARAM;
    public long OffsetParameter => (long)Autodesk.Revit.DB.BuiltInParameter.INSTANCE_ELEVATION_PARAM;
    public string? UnsupportedReason(Element element)
    {
        var f = (FamilyInstance)element;
        if (f.Symbol.Family.IsInPlace || f.Symbol.Family.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
            return "Поддерживаются только загружаемые одноуровневые семейства без хоста.";
        if (f.Host is not null || f.SuperComponent is not null || f.GetSubComponentIds().Count > 0)
            return "Семейство имеет хост или вложенные общие компоненты.";
        if (f.MEPModel is not null) return "MEP-семейства пока не поддерживаются.";
        return f.Location is LocationPoint ? null : "У семейства нет точечного размещения.";
    }
}

internal sealed class HorizontalHostStrategy : ITransferStrategy
{
    private readonly bool floor;
    public HorizontalHostStrategy(bool floor) => this.floor = floor;
    public bool Matches(Element e) => floor ? e is Floor : e is Ceiling;
    public long LevelParameter => (long)Autodesk.Revit.DB.BuiltInParameter.LEVEL_PARAM;
    public long OffsetParameter => floor ? (long)Autodesk.Revit.DB.BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM : (long)Autodesk.Revit.DB.BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM;
    public string? UnsupportedReason(Element element)
    {
        if (element is Floor f && (f.GetSlabShapeEditor() is not { IsEnabled: false }))
            return "Перекрытия с уклоном или редактированной формой пока не поддерживаются.";
        var faces = HostObjectUtils.GetTopFaces((HostObject)element);
        if (faces.Count == 0 || faces.Any(r => element.GetGeometryObjectFromReference(r) is not PlanarFace face
                || Math.Abs(face.FaceNormal.Z) < 1 - 1e-9))
            return "Поддерживаются только горизонтальные плоские перекрытия и потолки.";
        return ((HostObject)element).FindInserts(true, true, true, true).Count > 0
            ? "Есть вставки или размещённые на хосте элементы." : null;
    }
}

internal sealed class UnconnectedWallStrategy : ITransferStrategy
{
    public bool Matches(Element e) => e is Wall;
    public long LevelParameter => (long)Autodesk.Revit.DB.BuiltInParameter.WALL_BASE_CONSTRAINT;
    public long OffsetParameter => (long)Autodesk.Revit.DB.BuiltInParameter.WALL_BASE_OFFSET;
    public string? UnsupportedReason(Element element)
    {
        var w = (Wall)element;
        if (w.WallType.Kind != WallKind.Basic || w.IsStackedWallMember || w.CrossSection != WallCrossSection.Vertical
            || w.Location is not LocationCurve { Curve: Line } || w.SketchId != ElementId.InvalidElementId)
            return "Поддерживаются только прямые вертикальные базовые стены без изменённого профиля.";
        if (w.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() != ElementId.InvalidElementId
            || w.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() != 0
            || w.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() != 0)
            return "Стена имеет верхнюю привязку или присоединение к основанию/верху.";
        if (w.FindInserts(true, true, true, true).Count > 0) return "Стена содержит вставки или проёмы.";
        var location = (LocationCurve)w.Location;
        if (location.get_ElementsAtJoin(0).Cast<Element>().Any(e => e.Id != w.Id)
            || location.get_ElementsAtJoin(1).Cast<Element>().Any(e => e.Id != w.Id))
            return "Стена соединена с другими стенами.";
        return null;
    }
}

internal sealed record TransferPlan(ElementId Id, string Name, ITransferStrategy? Strategy, string? Reason)
{
    public bool Ready => Strategy is not null && Reason is null;
}

internal sealed class TransferAnalyzer
{
    private readonly ITransferStrategy[] strategies = [new PointFamilyStrategy(), new HorizontalHostStrategy(true),
        new HorizontalHostStrategy(false), new UnconnectedWallStrategy()];

    public TransferPlan Analyze(Element e, Level target)
    {
        var strategy = strategies.FirstOrDefault(s => s.Matches(e));
        string? reason = null;
        if (e.Pinned) reason = "Элемент закреплён.";
        else if (e.GroupId != ElementId.InvalidElementId || e.AssemblyInstanceId != ElementId.InvalidElementId)
            reason = "Элемент входит в группу или сборку.";
        else if (e.DesignOption is not null) reason = "Элементы вариантов конструкции пока не поддерживаются.";
        else if (strategy is null) reason = "Механизм привязки этой категории пока не поддерживается.";
        else if (JoinGeometryUtils.GetJoinedElements(e.Document, e).Count > 0) reason = "Геометрия соединена с другими элементами.";
        else reason = strategy.UnsupportedReason(e);

        if (reason is null && strategy is not null)
        {
            var level = e.ParameterById(strategy.LevelParameter);
            var offset = e.ParameterById(strategy.OffsetParameter);
            if (level is null || level.StorageType != StorageType.ElementId || level.IsReadOnly
                || offset is null || offset.StorageType != StorageType.Double || offset.IsReadOnly || !offset.HasValue)
                reason = "Параметры уровня/смещения отсутствуют или недоступны для записи.";
            else if (e.Document.GetElement(level.AsElementId()) is not Level source)
                reason = "Не удалось определить исходный уровень.";
            else if (source.Id == target.Id) reason = "Элемент уже на целевом уровне.";
            else _ = LevelTransfer.NewOffset(source.ProjectElevation, target.ProjectElevation, offset.AsDouble());
        }
        return new(e.Id, $"{e.Category?.Name}: {e.Name}", strategy, reason);
    }

    public bool IsOnLevel(Element e, ElementId levelId)
    {
        if (e.LevelId == levelId) return true;
        // Read-only discovery includes unsupported categories, without attempting to modify them.
        long[] levelParameters = [(long)Autodesk.Revit.DB.BuiltInParameter.FAMILY_LEVEL_PARAM, (long)Autodesk.Revit.DB.BuiltInParameter.LEVEL_PARAM,
            (long)Autodesk.Revit.DB.BuiltInParameter.WALL_BASE_CONSTRAINT, (long)Autodesk.Revit.DB.BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
            (long)Autodesk.Revit.DB.BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, (long)Autodesk.Revit.DB.BuiltInParameter.RBS_START_LEVEL_PARAM,
            (long)Autodesk.Revit.DB.BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, (long)Autodesk.Revit.DB.BuiltInParameter.ROOF_BASE_LEVEL_PARAM];
        return levelParameters.Any(p => e.ParameterById(p) is { StorageType: StorageType.ElementId } parameter
            && parameter.AsElementId() == levelId);
    }
}
