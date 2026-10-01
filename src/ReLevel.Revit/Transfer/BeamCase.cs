using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ReLevel.Revit.Transfer;

internal static class BeamCase
{
    public static readonly LevelOffsetBinding Start = new(
        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
    public static readonly LevelOffsetBinding End = new(
        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION);

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Start.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FamilyInstance instance || element.Category?.BuiltInCategory != BuiltInCategory.OST_StructuralFraming
            || instance.StructuralType != StructuralType.Beam || instance.Symbol.Family.IsInPlace
            || instance.Symbol.Family.FamilyPlacementType != FamilyPlacementType.CurveDrivenStructural)
            return L.Get("Кейс 7: требуется загружаемая балка CurveDrivenStructural.");
        if (instance.Location is not LocationCurve { Curve: Line { IsBound: true } })
            return L.Get("Кейс 7: требуется прямая балка с LocationCurve.");
        if (instance.SuperComponent is not null || element.GroupId != ElementId.InvalidElementId
            || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 7: вложенные экземпляры, группы и сборки пока не поддерживаются.");
        if (SourceLevel(element) is not { } level)
            return L.Get("Кейс 7: Reference Level не указывает на существующий уровень.");
        if (instance.HostFace is not null || (instance.Host is not null
            && (instance.Host is not Level hostLevel || hostLevel.Id != level.Id)))
            return L.Get("Кейс 7: допускается хост-уровень Reference Level или отсутствие хоста.");
        if (new[] { Start.OffsetParameter, End.OffsetParameter }.Any(name =>
            element.get_Parameter(name) is not { StorageType: StorageType.Double, HasValue: true }))
            return L.Get("Кейс 7: недоступны смещения концов балки.");
        return null;
    }

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 7: закреплённая балка не переносится.");
        if (new[] { Start.OffsetParameter, End.OffsetParameter }.Any(name =>
            LevelOffsetBinding.Require(element, name, StorageType.Double).IsReadOnly))
            return L.Get("Кейс 7: смещения концов балки недоступны для записи.");
        // A read-only level is expected for the agreed work-plane detachment case.
        if (LevelOffsetBinding.Require(element, Start.LevelParameter, StorageType.ElementId).IsReadOnly
            && (element is not FamilyInstance { Host: Level }
                || element.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM) is null))
            return L.Get("Кейс 7: заблокированный Reference Level не связан с рабочей плоскостью уровня.");
        return null;
    }
}
