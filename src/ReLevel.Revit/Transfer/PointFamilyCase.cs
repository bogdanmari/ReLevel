using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class PointFamilyCase
{
    private static readonly LevelOffsetBinding AirTerminalBinding = new(
        BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM);

    private static bool IsAirTerminal(Element element) => element.Category?.BuiltInCategory == BuiltInCategory.OST_DuctTerminal;

    public static IElementTransferOperation Prepare(Element element, ElementId source) => IsAirTerminal(element)
        ? new LevelOffsetOperation(element, source, AirTerminalBinding) : new PointFamilyOperation(element);

    public static Parameter? LevelParameter(Element element) =>
        element is FamilyInstance && element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) is { StorageType: StorageType.ElementId } parameter
            ? parameter : null;

    public static Level? SourceLevel(Element element) =>
        LevelParameter(element) is { } parameter ? element.Document.GetElement(parameter.AsElementId()) as Level : null;

    public static string? UnsupportedReason(Element element)
    {
        if (element is not FamilyInstance instance) return L.Get("Случай 1: требуется экземпляр FamilyInstance.");
        var family = instance.Symbol.Family;
        if (family.IsInPlace) return L.Get("Случай 1: семейства In-Place не поддерживаются.");
        if (family.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
            return L.Format($"Случай 1: требуется OneLevelBased; способ размещения: {family.FamilyPlacementType}.");
        if (instance.Host is not null) return L.Get("Случай 1: свойство Host должно быть пустым.");
        if (instance.SuperComponent is not null) return L.Get("Случай 1: вложенный экземпляр не переносится самостоятельно.");
        if (instance.Location is not LocationPoint) return L.Get("Случай 1: требуется точечное размещение LocationPoint.");
        if (SourceLevel(instance) is null) return L.Get("Случай 1: FAMILY_LEVEL_PARAM не указывает на существующий уровень.");
        return null;
    }

    public static string? WriteRestriction(Element element)
    {
        if (LevelParameter(element)?.IsReadOnly == true)
            return L.Get("Параметр FAMILY_LEVEL_PARAM недоступен для записи.");
        if (IsAirTerminal(element))
        {
            if (element.Pinned) return L.Get("Случай 1: закреплённый воздухораспределитель не переносится.");
            if (element.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM) is not
                { StorageType: StorageType.Double, HasValue: true, IsReadOnly: false })
                return L.Get("Случай 1: Elevation from Level воздухораспределителя недоступен для записи.");
        }
        return null;
    }
}
