// Read no geometry from, and make no edits to, the user's model.
// Explore whether Room Separation Lines can keep Z independently of their LevelId.
var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
var results = new List<object>();
try
{
    foreach (var mode in new[] { "level-plane", "geometric-plane", "moved-level-plane" })
    {
        using var tx = new Transaction(test, "Probe room separator elevation");
        tx.Start();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
        try
        {
            var level = Level.Create(test, 10);
            var type = new FilteredElementCollector(test).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .First(v => v.ViewFamily == ViewFamily.FloorPlan);
            var view = ViewPlan.Create(test, type.Id, level.Id);
            var plane = mode == "geometric-plane"
                ? SketchPlane.Create(test, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero))
                : SketchPlane.Create(test, level.Id);
            if (mode == "moved-level-plane") ElementTransformUtils.MoveElement(test, plane.Id, new XYZ(0, 0, -10));
            using var curves = new CurveArray();
            curves.Append(Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)));
            var line = test.Create.NewRoomBoundaryLines(plane, curves, view).get_Item(0);
            test.Regenerate();
            results.Add(new { mode, level = line.LevelId.Value, target = level.Id.Value,
                z0 = line.GeometryCurve.GetEndPoint(0).Z, z1 = line.GeometryCurve.GetEndPoint(1).Z,
                planeZ = line.SketchPlane.GetPlane().Origin.Z });
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex) { results.Add(new { mode, error = ex.Message }); }
        finally { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); }
    }
    return new { version = app.Application.VersionBuild, results };
}
finally { test.Close(false); }
