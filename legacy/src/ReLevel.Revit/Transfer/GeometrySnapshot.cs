using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class GeometrySnapshot
{
    // Internal feet: approximately 0.003 mm. Never move an element to hide a failed invariant.
    internal const double Tolerance = 1e-5;
    private readonly List<ModelPoint> points = [];
    private readonly List<double> volumes = [];
    private readonly List<double> areas = [];
    private readonly ElementId typeId;
    private readonly double? rotation;

    private GeometrySnapshot(Element e)
    {
        typeId = e.GetTypeId();
        if (e.Location is LocationPoint p) { Add(p.Point); rotation = p.Rotation; }
        if (e.Location is LocationCurve c) foreach (var pnt in c.Curve.Tessellate()) Add(pnt);
        var box = e.get_BoundingBox(null) ?? throw new InvalidOperationException("Нет габаритов для проверки положения.");
        Add(box.Transform.OfPoint(box.Min));
        Add(box.Transform.OfPoint(box.Max));
        using var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
        using var geometry = e.get_Geometry(options) ?? throw new InvalidOperationException("Нет геометрии для проверки.");
        Read(geometry);
        if (volumes.Count == 0) throw new InvalidOperationException("Нет твёрдотельной геометрии для надёжной проверки.");
    }

    public static GeometrySnapshot Capture(Element e) => new(e);
    private void Add(XYZ p) => points.Add(new(p.X, p.Y, p.Z));
    private void Read(GeometryElement geometry)
    {
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case GeometryInstance instance:
                    using (var nested = instance.GetInstanceGeometry()) Read(nested);
                    break;
                case Solid solid when solid.Faces.Size > 0:
                    volumes.Add(solid.Volume);
                    areas.Add(solid.SurfaceArea);
                    foreach (Face face in solid.Faces)
                    {
                        using var mesh = face.Triangulate();
                        foreach (var vertex in mesh.Vertices) Add(vertex);
                    }
                    break;
                case Curve curve:
                    foreach (var point in curve.Tessellate()) Add(point);
                    break;
                case Mesh mesh:
                    foreach (var vertex in mesh.Vertices) Add(vertex);
                    break;
            }
        }
    }

    public void Verify(Element e)
    {
        var after = Capture(e);
        // Keeping enumeration order is deliberately conservative: retessellation also rejects a transfer.
        if (typeId != after.typeId || rotation.HasValue != after.rotation.HasValue
            || (rotation.HasValue && Math.Abs(rotation.Value - after.rotation!.Value) > 1e-9)
            || points.Count != after.points.Count || volumes.Count != after.volumes.Count
            || points.Where((p, i) => !p.IsWithin(after.points[i], Tolerance)).Any()
            || volumes.Where((v, i) => Math.Abs(v - after.volumes[i]) > Math.Max(1, Math.Abs(v)) * 1e-9).Any()
            || areas.Where((a, i) => Math.Abs(a - after.areas[i]) > Math.Max(1, Math.Abs(a)) * 1e-9).Any())
            throw new InvalidOperationException("Положение, размеры или геометрия изменились. Перенос отменён.");
    }
}
