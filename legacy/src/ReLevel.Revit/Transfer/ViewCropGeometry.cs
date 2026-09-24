using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class ViewCropGeometry
{
    public static void Copy(ViewPlan source, ViewPlan target, ViewCropRegionShapeManager targetCrop)
    {
        using var sourceCrop = source.GetCropRegionShapeManager();
        if (sourceCrop.Split)
            throw new NotSupportedException("Разделённая область обрезки пока не поддерживается.");
        if (sourceCrop.ShapeSet)
        {
            if (!targetCrop.CanHaveShape)
                throw new NotSupportedException("Новый вид не поддерживает непрямоугольный контур исходной обрезки.");
            var shapes = sourceCrop.GetCropShape();
            try
            {
                if (shapes.Count != 1)
                    throw new NotSupportedException($"Ожидался один контур непрямоугольной обрезки; Revit вернул: {shapes.Count}.");
                using var transform = ElementTransformUtils.GetTransformFromViewToView(source, target);
                using var shape = CurveLoop.CreateViaTransform(shapes[0], transform);
                targetCrop.SetCropShape(shape);
            }
            finally { foreach (var shape in shapes) shape.Dispose(); }
            return;
        }

        // CanHaveShape only controls non-rectangular crops. Rectangles use CropBox,
        // whose setter ignores Transform: convert the corners into the target frame.
        if (targetCrop.Split) targetCrop.RemoveSplit();
        if (targetCrop.ShapeSet) targetCrop.RemoveCropRegionShape();
        target.Document.Regenerate();
        using var sourceBox = source.CropBox
            ?? throw new NotSupportedException("Revit не предоставил прямоугольную обрезку исходного вида.");
        using var targetBox = target.CropBox
            ?? throw new NotSupportedException("Новый вид не поддерживает прямоугольную обрезку.");
        using var inverse = targetBox.Transform.Inverse;
        using var sourceFrame = sourceBox.Transform;
        if (!IsAxisAligned(inverse.OfVector(sourceFrame.BasisX)) || !IsAxisAligned(inverse.OfVector(sourceFrame.BasisY)))
            throw new NotSupportedException("Прямоугольная обрезка повёрнута относительно нового вида; перенос без изменения границ невозможен.");
        var corners = RectanglePoints(sourceBox).Select(inverse.OfPoint).ToList();
        // Z does not define a plan crop; preserve the target's depth and view range.
        targetBox.Min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), targetBox.Min.Z);
        targetBox.Max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), targetBox.Max.Z);
        target.CropBox = targetBox;
    }

    public static List<XYZ> Points(ViewPlan view, ViewCropRegionShapeManager manager)
    {
        if (!manager.ShapeSet && !manager.Split)
        {
            using var box = view.CropBox
                ?? throw new NotSupportedException($"Revit не предоставил границы обрезки вида {view.Id.ToLong()}.");
            return RectanglePoints(box).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        }
        var shapes = manager.GetCropShape();
        try
        {
            return shapes.SelectMany(loop => loop.SelectMany(curve => curve.Tessellate()))
                .OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        }
        finally { foreach (var shape in shapes) shape.Dispose(); }
    }

    private static IEnumerable<XYZ> RectanglePoints(BoundingBoxXYZ box)
    {
        using var frame = box.Transform;
        foreach (var x in new[] { box.Min.X, box.Max.X })
            foreach (var y in new[] { box.Min.Y, box.Max.Y })
                yield return frame.OfPoint(new XYZ(x, y, box.Min.Z));
    }

    private static bool IsAxisAligned(XYZ direction) => Math.Abs(direction.Z) < 1e-9
        && (Math.Abs(direction.X) < 1e-9 || Math.Abs(direction.Y) < 1e-9);
}
