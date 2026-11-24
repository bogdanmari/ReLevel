// MCP body; replace DLL_PATH before execution. All edits are confined to an unsaved test project.
var assembly = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(@"DLL_PATH"));
var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
var checks = new List<object>();
try
{
    var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", true);
    var service = Activator.CreateInstance(serviceType, new object[] { test });
    var execute = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 5);
    var cases = assembly.GetType("ReLevel.Revit.Transfer.TransferCases", true);
    foreach (double elevation in new[] { 10.0, -10.0, 0.0 })
    {
        using var group = new TransactionGroup(test, "Separator test rollback");
        group.Start();
        ElementId sourceId, targetId, lineId, noViewId, secondId;
        using (var tx = new Transaction(test, "Fixture"))
        {
            tx.Start();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
            sourceId = Level.Create(test, 0).Id;
            targetId = Level.Create(test, elevation).Id;
            noViewId = Level.Create(test, 30).Id;
            secondId = Level.Create(test, 20).Id;
            var type = new FilteredElementCollector(test).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.FloorPlan);
            var view = ViewPlan.Create(test, type.Id, sourceId);
            ViewPlan.Create(test, type.Id, targetId);
            ViewPlan.Create(test, type.Id, secondId);
            using var curves = new CurveArray();
            curves.Append(Line.CreateBound(new XYZ(2, 3, 0), new XYZ(12, 3, 0)));
            lineId = test.Create.NewRoomBoundaryLines(SketchPlane.Create(test, sourceId), curves, view).get_Item(0).Id;
            if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Fixture failed");
        }
        using var operationGroup = new TransactionGroup(test, "Transfer rollback");
        operationGroup.Start();
        var original = (ModelLine)test.GetElement(lineId);
        var p0 = original.GeometryCurve.GetEndPoint(0);
        var p1 = original.GeometryCurve.GetEndPoint(1);
        var phase = original.CreatedPhaseId;
        var report = execute.Invoke(service, new object[] { new List<ElementId> { lineId }, targetId, sourceId, null, null });
        var items = ((System.Collections.IEnumerable)report.GetType().GetProperty("Items").GetValue(report)).Cast<object>().ToList();
        var newId = (long?)items.Single().GetType().GetProperty("ReplacementId").GetValue(items.Single());
        if (!newId.HasValue) throw new Exception("Transfer failed: " + items.Single().ToString());
        var replacement = (ModelLine)test.GetElement(new ElementId(newId.Value));
        bool xyz = replacement.GeometryCurve.GetEndPoint(0).DistanceTo(p0) < 1e-8 && replacement.GeometryCurve.GetEndPoint(1).DistanceTo(p1) < 1e-8;
        bool binding = replacement.LevelId == targetId && replacement.CreatedPhaseId == phase && test.GetElement(lineId) == null;
        bool reclassified = cases.GetMethod("Find").Invoke(null, new object[] { replacement }) != null;
        bool survives;
        using (var tx = new Transaction(test, "Delete old level"))
        {
            tx.Start();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
            var deleted = test.Delete(sourceId);
            survives = !deleted.Contains(replacement.Id);
            if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Delete failed");
            survives = survives && test.GetElement(replacement.Id) != null;
        }
        var before = new FilteredElementCollector(test).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).ToElementIds().Select(i => i.Value).ToHashSet();
        var errorReport = execute.Invoke(service, new object[] { new List<ElementId> { replacement.Id }, noViewId, targetId, null, null });
        bool errorSafe = (int)errorReport.GetType().GetProperty("Transferred").GetValue(errorReport) == 0
            && before.SetEquals(new FilteredElementCollector(test).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).ToElementIds().Select(i => i.Value));
        // A second successful transfer must accept a line whose Z differs from its source level.
        var again = execute.Invoke(service, new object[] { new List<ElementId> { replacement.Id }, secondId, targetId, null, null });
        bool repeated = (int)again.GetType().GetProperty("Transferred").GetValue(again) == 1;
        operationGroup.RollBack();
        bool rollback = test.GetElement(lineId) != null && test.GetElement(sourceId) != null && test.GetElement(new ElementId(newId.Value)) == null;
        checks.Add(new { elevation, xyz, binding, reclassified, survives, errorSafe, repeated, rollback });
        if (!(xyz && binding && reclassified && survives && errorSafe && repeated && rollback)) throw new Exception("Check failed");
        group.RollBack();
    }
    return new { version = app.Application.VersionBuild, checks };
}
finally { test.Close(false); }
