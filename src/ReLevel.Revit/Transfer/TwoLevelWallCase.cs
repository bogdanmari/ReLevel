using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class TwoLevelWallCase
{
    public static readonly LevelOffsetBinding Bottom = new(BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.WALL_BASE_OFFSET);
    public static readonly LevelOffsetBinding Top = new(BuiltInParameter.WALL_HEIGHT_TYPE, BuiltInParameter.WALL_TOP_OFFSET);

    public static string? UnsupportedReason(Element element)
    {
        if (element is not Wall wall || wall.WallType.Kind is not (WallKind.Basic or WallKind.Curtain) || wall.IsStackedWallMember)
            return L.Get("Кейс 2: требуется самостоятельная стена Basic Wall или Curtain Wall.");
        if (wall.CrossSection != WallCrossSection.Vertical)
            return L.Get("Кейс 2: требуется вертикальная стена.");
        if (wall.GroupId != ElementId.InvalidElementId || wall.AssemblyInstanceId != ElementId.InvalidElementId)
            return L.Get("Кейс 2: элементы групп и сборок пока не поддерживаются.");
        if (wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() == 1)
            return L.Get("Кейс 2: присоединённый низ стены пока не поддерживается.");
        // An attached top keeps its attachment. Compensating both level/offset pairs
        // preserves the nominal top minus base (Unconnected Height), not the attached geometry's height.
        Capture(wall);
        return null;
    }

    public static LevelOffsetState[] Capture(Wall wall)
    {
        var bottom = Bottom.Capture(wall);
        var topId = LevelOffsetBinding.Require(wall, Top.LevelParameter, StorageType.ElementId).AsElementId();
        // Unconnected walls have only one level binding; leave their height and top offset untouched.
        return topId == ElementId.InvalidElementId ? [bottom] : [bottom, Top.Capture(wall)];
    }
}
