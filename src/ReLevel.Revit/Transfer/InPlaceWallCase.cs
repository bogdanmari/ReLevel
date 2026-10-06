using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class InPlaceWallCase
{
    public static bool IsInternalWall(Element element)
    {
        if (element is not Wall) return false;
        // Internal family walls can be read by ID but are absent from the project wall collector.
        using var collector = new FilteredElementCollector(element.Document);
        using var ids = new ElementIdSetFilter(new[] { element.Id });
        if (collector.OfClass(typeof(Wall)).WherePasses(ids).FirstElementId() != ElementId.InvalidElementId) return false;
        return element.GetDependentElements(null).Any(id => element.Document.GetElement(id) is Family
            { IsInPlace: true, FamilyPlacementType: FamilyPlacementType.OneLevelBasedHosted });
    }

    public static string? UnsupportedReason(Element element)
    {
        if (!IsInternalWall(element))
            return L.Get("Кейс 16: требуется внутренняя стена хостового семейства In-Place.");
        var wall = (Wall)element;
        if (wall.WallType.Kind != WallKind.Basic || wall.CrossSection != WallCrossSection.Vertical
            || wall.IsStackedWallMember || wall.GroupId != ElementId.InvalidElementId
            || wall.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 16: требуется вертикальная Basic Wall без группы и сборки.");
        if (wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() == 1)
            return L.Get("Кейс 16: присоединённый низ не поддерживается.");
        TwoLevelWallCase.Bottom.Capture(wall);
        return null;
    }

    public static bool IsOnLevel(Element element, ElementId source) =>
        TwoLevelWallCase.Bottom.Capture(element).Changes(source);

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 16: закреплённая стена не переносится.");
        return TwoLevelWallCase.Bottom.Capture(element).CanWrite(element) ? null
            : L.Get("Кейс 16: нижний уровень или смещение недоступны для записи.");
    }
}
