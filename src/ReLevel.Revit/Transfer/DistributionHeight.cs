using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

// Only reads the reference height needed to choose a destination; no result snapshots or comparisons.
internal static class DistributionHeight
{
    public static double Read(Element element, Level source)
    {
        var binding = element switch
        {
            Floor => FloorCase.Binding,
            ExtrusionRoof => ExtrusionRoofCase.Binding,
            FootPrintRoof => FootPrintRoofCase.Binding,
            BuildingPad => BuildingPadCase.Binding,
            Wall => TwoLevelWallCase.Bottom, // Only an already-classified internal In-Place wall reaches here.
            Railing { HasHost: false } => RailingCase.Binding,
            FamilyInstance family when family.Symbol.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased
                => FaceHostedFamilyCase.Binding,
            _ => null
        };
        if (binding is not null) return binding.Capture(element).Position.Absolute;
        if (element is Room room) return source.ProjectElevation + room.BaseOffset;
        if (element is FamilyInstance { Symbol.Family.FamilyPlacementType: FamilyPlacementType.CurveDrivenStructural })
            return Math.Min(BeamCase.Start.Capture(element).Position.Absolute, BeamCase.End.Capture(element).Position.Absolute);
        if (element.Location is LocationPoint point) return point.Point.Z;
        var curve = element is CurveElement curveElement ? curveElement.GeometryCurve
            : (element.Location as LocationCurve)?.Curve;
        if (curve is { IsBound: true }) return curve.Tessellate().Min(point => point.Z);
        // Hosted railings and fabrication/flexible parts can lack a LocationPoint/LocationCurve.
        // Their model bounding box supplies the lower physical elevation, independently of the view.
        var bounds = element.get_BoundingBox(null);
        if (bounds is not null)
            return Enumerable.Range(0, 8).Min(i => bounds.Transform.OfPoint(new XYZ(
                (i & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z)).Z);
        throw new InvalidOperationException(L.Get("Не удалось определить высоту элемента для распределения."));
    }
}
