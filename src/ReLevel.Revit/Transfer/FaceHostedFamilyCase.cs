using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class FaceHostedFamilyCase
{
    public static readonly LevelOffsetBinding Binding = new(
        BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FamilyInstance instance || instance.Symbol.Family.IsInPlace
            || instance.Symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
            return L.Get("Кейс 3: требуется загружаемое семейство WorkPlaneBased.");
        if (instance.Host is null or RevitLinkInstance || instance.HostFace is not { } face
            || face.LinkedElementId != ElementId.InvalidElementId)
            return L.Get("Кейс 3: требуется размещение на грани хоста текущего документа.");
        if (instance.SuperComponent is not null)
            return L.Get("Кейс 3: вложенный экземпляр не переносится самостоятельно.");
        if (instance.Location is not LocationPoint)
            return L.Get("Кейс 3: требуется точечное размещение LocationPoint.");
        if (element.GroupId != ElementId.InvalidElementId || element.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 3: элементы групп и сборок пока не поддерживаются.");
        if (SourceLevel(instance) is null)
            return L.Get("Кейс 3: Schedule Level не указывает на существующий уровень.");
        if (instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM) is not { StorageType: StorageType.Double, HasValue: true })
            return L.Get("Кейс 3: недоступен параметр Elevation from Level.");
        return null;
    }

    public static Level? SourceLevel(Element element) =>
        element.get_Parameter(Binding.LevelParameter) is { StorageType: StorageType.ElementId, HasValue: true } parameter
            ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 3: закреплённый элемент не переносится.");
        return Binding.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 3: Schedule Level или Elevation from Level недоступны для записи.");
    }
}
