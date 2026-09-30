using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Positions needed by the wall transfer algorithm, not a before/after verification snapshot.
internal sealed record WallHostedPlacement(ElementId Id, XYZ Point)
{
    public static IReadOnlyList<WallHostedPlacement> Capture(Wall wall)
    {
        using var grid = wall.CurtainGrid;
        var gridElements = grid is null ? new HashSet<ElementId>()
            : grid.GetPanelIds().Concat(grid.GetMullionIds()).ToHashSet();
        using var collector = new FilteredElementCollector(wall.Document);
        return collector.OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            // Grid panels (including panel doors) and mullions follow the curtain wall's grid.
            .Where(instance => instance.SuperComponent is null && instance.Host?.Id == wall.Id && !gridElements.Contains(instance.Id))
            .Select(instance => instance.Location is LocationPoint location
                ? new WallHostedPlacement(instance.Id, location.Point)
                : throw new InvalidOperationException(L.Format($"У семейства на стене ID {instance.Id.Value} нет точечного размещения.")))
            .ToArray();
    }

    public void Restore(Document document)
    {
        try
        {
            var location = document.GetElement(Id)?.Location as LocationPoint
                ?? throw new InvalidOperationException(L.Get("Точка размещения семейства недоступна."));
            // Do not write level parameters; Revit updates offsets from the restored physical position.
            location.Point = Point;
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(L.Format($"Не удалось восстановить положение семейства на стене ID {Id.Value}: {ex.Message}"), ex);
        }
    }
}
