// Diagnostic MCP body. Replace DLL_PATH. Uses only an isolated unsaved project.
// The event handler below accepts two known warnings for unattended testing only.
// Production code leaves those warnings to Revit's normal UI and records them in the report.
var assembly = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(@"DLL_PATH"));
var failureType = assembly.GetType("ReLevel.Revit.Transfer.GroupTransferFailures", true);
var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
var observed = new List<string>();
EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs> acceptTestWarnings = (_, args) =>
{
    var accessor = args.GetFailuresAccessor();
    if (!accessor.GetDocument().Equals(test)) return;
    foreach (var f in accessor.GetFailureMessages())
    {
        var id = f.GetFailureDefinitionId();
        if (f.GetSeverity() != FailureSeverity.Warning ||
            (!id.Equals(BuiltInFailures.GroupFailures.AtomViolationWhenOnePlaceInstance) &&
             !id.Equals(BuiltInFailures.OverlapFailures.WallsOverlap))) continue;
        observed.Add(id.Guid.ToString());
        accessor.DeleteWarning(f);
    }
};
app.Application.FailuresProcessing += acceptTestWarnings;
try
{
    ElementId source, target, wallId, otherMember, groupId, overlappingId;
    using (var tx = new Transaction(test, "Overlapping group fixture"))
    {
        tx.Start();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
            .SetFailuresPreprocessor((IFailuresPreprocessor)Activator.CreateInstance(failureType))
            .SetClearAfterRollback(true));
        source = Level.Create(test, 0).Id; target = Level.Create(test, 5).Id;
        var type = new FilteredElementCollector(test).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
        var a = Wall.Create(test, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), type.Id, source, 10, 2, false, false);
        var b = Wall.Create(test, Line.CreateBound(new XYZ(0, 12, 0), new XYZ(10, 12, 0)), type.Id, source, 10, 2, false, false);
        wallId = a.Id; otherMember = b.Id;
        test.Regenerate();
        groupId = test.Create.NewGroup(new List<ElementId> { a.Id, b.Id }).Id;
        overlappingId = Wall.Create(test, Line.CreateBound(new XYZ(1, 0, 0), new XYZ(9, 0, 0)), type.Id, source, 10, 2, false, false).Id;
        if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Fixture rollback");
    }
    var notes = new List<object>();
    var allowed = failureType.GetMethod("AllowsWarning", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    var atom = BuiltInFailures.GroupFailures.AtomViolationWhenOnePlaceInstance;
    var overlap = BuiltInFailures.OverlapFailures.WallsOverlap;
    var unknown = BuiltInFailures.OverlapFailures.DuplicateInstances;
    foreach (var id in new[] { atom, overlap, unknown })
    {
        var processor = (IFailuresPreprocessor)Activator.CreateInstance(failureType);
        var before = test.GetElement(wallId).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString();
        TransactionStatus status;
        using (var tx = new Transaction(test, "Post diagnostic warning"))
        {
            tx.Start();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(processor).SetClearAfterRollback(true));
            test.GetElement(wallId).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(id.Guid.ToString());
            using var message = new FailureMessage(id);
            message.SetFailingElements(new List<ElementId> { wallId, otherMember });
            test.PostFailure(message);
            status = tx.Commit();
        }
        var shouldCommit = id != unknown;
        var reason = (string)failureType.GetProperty("Reason").GetValue(processor);
        notes.Add(new { warning = id.Guid, status = status.ToString(), reason,
            passed = (shouldCommit ? status == TransactionStatus.Committed : status == TransactionStatus.RolledBack)
                && reason.Contains(id.Guid.ToString())
                && (shouldCommit || test.GetElement(wallId).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() == before) });
    }
    notes.Add(new { scenario = "allowed IDs never accepted with Error or DocumentCorruption severity", passed =
        !((bool)allowed.Invoke(null, new object[] { FailureSeverity.Error, overlap })) &&
        !((bool)allowed.Invoke(null, new object[] { FailureSeverity.DocumentCorruption, atom })) });
    string Geometry() => string.Join("|", new FilteredElementCollector(test).OfClass(typeof(Wall)).Cast<Wall>()
        .OrderBy(w => w.Id.Value).Select(w => { var b = w.get_BoundingBox(null); return w.Id.Value + ":" + b.Min + ":" + b.Max; }));
    var beforeGeometry = Geometry();
    var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", true);
    var service = Activator.CreateInstance(serviceType, new object[] { test });
    var execute = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 5);
    using (var undo = new TransactionGroup(test, "Verify transfer rollback"))
    {
        undo.Start();
        var report = execute.Invoke(service, new object[] { new List<ElementId> { wallId }, target, source, null, null });
        var items = (System.Collections.IEnumerable)report.GetType().GetProperty("Items").GetValue(report);
        var reason = (string)items.Cast<object>().Single().GetType().GetProperty("Reason").GetValue(items.Cast<object>().Single());
        notes.Add(new { scenario = "grouped wall overlaps external wall", report, passed =
            (int)report.GetType().GetProperty("Transferred").GetValue(report) == 1 &&
            reason.Contains(overlap.Guid.ToString()) && Geometry() == beforeGeometry &&
            test.GetElement(wallId).LevelId == target && test.GetElement(otherMember).LevelId == source &&
            test.GetElement(overlappingId).LevelId == source &&
            test.GetElement(wallId).GroupId == test.GetElement(otherMember).GroupId &&
            test.GetElement(wallId).GroupId != ElementId.InvalidElementId });
        if (undo.RollBack() != TransactionStatus.RolledBack) throw new Exception("Rollback failed");
        notes.Add(new { scenario = "rollback restores original group and levels", passed =
            test.GetElement(groupId) != null && test.GetElement(wallId).GroupId == groupId &&
            test.GetElement(wallId).LevelId == source && Geometry() == beforeGeometry });
    }
    return new { version = app.Application.VersionBuild, notes, observed };
}
finally
{
    app.Application.FailuresProcessing -= acceptTestWarnings;
    test.Close(false);
}
