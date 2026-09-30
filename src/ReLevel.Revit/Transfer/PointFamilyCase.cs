using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class PointFamilyCase
{
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

    public static string? WriteRestriction(Element element) => LevelParameter(element)?.IsReadOnly == true
        ? L.Get("Параметр FAMILY_LEVEL_PARAM недоступен для записи.") : null;
}
