// MCP body; replace DLL_PATH with the absolute Release DLL path before execution.
var assembly = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(@"DLL_PATH"));
var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
try
{
    ElementId sourceId, targetId, lineId, wallId, groupId;
    using (var tx = new Transaction(test, "Grouped room separator fixture"))
    {
        tx.Start();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
        sourceId = Level.Create(test, 0).Id;
        targetId = Level.Create(test, 0).Id;
        var viewType = new FilteredElementCollector(test).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(v => v.ViewFamily == ViewFamily.FloorPlan);
        var view = ViewPlan.Create(test, viewType.Id, sourceId);
        ViewPlan.Create(test, viewType.Id, targetId);
        var plane = SketchPlane.Create(test, sourceId);
        using var curves = new CurveArray();
        curves.Append(Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)));
        lineId = test.Create.NewRoomBoundaryLines(plane, curves, view).get_Item(0).Id;
        var wallType = new FilteredElementCollector(test).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
        wallId = Wall.Create(test, Line.CreateBound(new XYZ(0, 20, 0), new XYZ(10, 20, 0)), wallType.Id, sourceId, 10, 0, false, false).Id;
        test.Regenerate();
        groupId = test.Create.NewGroup(new List<ElementId> { lineId, wallId }).Id;
        if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Fixture failed");
    }
    var cases = assembly.GetType("ReLevel.Revit.Transfer.TransferCases", true);
    var found = cases.GetMethod("Find").Invoke(null, new object[] { test.GetElement(lineId) });
    var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", true);
    var service = Activator.CreateInstance(serviceType, new object[] { test });
    var execute = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 5);
    var report = execute.Invoke(service, new object[] { new List<ElementId> { lineId }, targetId, sourceId, null, null });
    var items = (System.Collections.IEnumerable)report.GetType().GetProperty("Items").GetValue(report);
    var item = items.Cast<object>().Single();
    var replacement = (long?)item.GetType().GetProperty("ReplacementId").GetValue(item);
    var newLine = replacement.HasValue ? test.GetElement(new ElementId(replacement.Value)) : null;
    return new { version = app.Application.VersionBuild, classified = found != null, report,
        passed = found != null && (int)report.GetType().GetProperty("Transferred").GetValue(report) == 1
            && test.GetElement(lineId) == null && newLine != null && newLine.LevelId == targetId
            && newLine.GroupId == test.GetElement(wallId).GroupId && newLine.GroupId != ElementId.InvalidElementId
            && test.GetElement(wallId).LevelId == sourceId && test.GetElement(groupId) == null };
}
finally { test.Close(false); }
