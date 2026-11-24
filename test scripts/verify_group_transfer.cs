// MCP body. Creates and closes an isolated, unsaved project; never edits the active project.
// Substitute DLL_PATH and set nestedFixture before sending the body to MCP (no JObject dependency).
const bool nestedFixture = false;
var assembly = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(@"DLL_PATH"));
var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", true);
var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
try
{
    ElementId sourceId, targetId, firstId, secondId, groupId, copyId;
    using (var tx = new Transaction(test, "Group transfer fixture"))
    {
        tx.Start();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
        var source = Level.Create(test, 0); sourceId = source.Id;
        targetId = Level.Create(test, 5).Id;
        var type = new FilteredElementCollector(test).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
        var a = Wall.Create(test, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), type.Id, sourceId, 10, 2, false, false);
        var b = Wall.Create(test, Line.CreateBound(new XYZ(0, 12, 0), new XYZ(10, 12, 0)), type.Id, sourceId, 10, 2, false, false);
        firstId = a.Id; secondId = b.Id;
        test.Regenerate();
        var group = test.Create.NewGroup(new List<ElementId> { a.Id, b.Id });
        if (nestedFixture)
        {
            var c = Wall.Create(test, Line.CreateBound(new XYZ(0, 24, 0), new XYZ(10, 24, 0)), type.Id, sourceId, 10, 2, false, false);
            test.Regenerate();
            group = test.Create.NewGroup(new List<ElementId> { group.Id, c.Id });
        }
        groupId = group.Id;
        copyId = test.Create.PlaceGroup(new XYZ(50, 0, 0), group.GroupType).Id;
        if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Fixture failed");
    }
    string Snapshot()
    {
        return string.Join("|", new FilteredElementCollector(test).OfClass(typeof(Wall)).Cast<Wall>().OrderBy(w => w.Id.Value).Select(w =>
            w.Id.Value + ":" + w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId().Value + ":" +
            w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble() + ":" + w.GroupId.Value));
    }
    var before = Snapshot();
    string Geometry() => string.Join("|", new FilteredElementCollector(test).OfClass(typeof(Wall)).Cast<Wall>()
        .OrderBy(w => w.Id.Value).Select(w => { var b = w.get_BoundingBox(null); return w.Id.Value + ":" + b.Min + ":" + b.Max; }));
    string CopyState() => string.Join("|", ((Group)test.GetElement(copyId)).GetMemberIds().Select(id =>
        { var e = test.GetElement(id); return id.Value + ":" + e.GroupId.Value + ":" + e.LevelId.Value; }));
    var geometry = Geometry();
    var copyState = CopyState();
    var service = Activator.CreateInstance(serviceType, new object[] { test });
    var execute = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 5);
    var notes = new List<object>();
    using (var rollbackPin = new TransactionGroup(test, "Pinned group test"))
    {
        rollbackPin.Start();
        using (var pin = new Transaction(test, "Pin group"))
        {
            pin.Start();
            pin.SetFailureHandlingOptions(pin.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
            test.GetElement(groupId).Pinned = true;
            if (pin.Commit() != TransactionStatus.Committed) throw new Exception("Pin failed");
        }
        var pinned = execute.Invoke(service, new object[] { new List<ElementId> { firstId }, targetId, sourceId, null, null });
        notes.Add(new { scenario = "pinned root group skipped", passed = Snapshot() == before &&
            (int)pinned.GetType().GetProperty("Skipped").GetValue(pinned) == 1 });
        if (rollbackPin.RollBack() != TransactionStatus.RolledBack) throw new Exception("Pin rollback failed");
    }
    using (var undo = new TransactionGroup(test, "Verify rollback"))
    {
        undo.Start();
        var report = execute.Invoke(service, new object[] { new List<ElementId> { firstId }, targetId, sourceId, null, null });
        var first = (Wall)test.GetElement(firstId);
        var second = (Wall)test.GetElement(secondId);
        var newGroup = first.GroupId;
        var passed = (int)report.GetType().GetProperty("Transferred").GetValue(report) == 1
            && first.LevelId == targetId && second.LevelId == sourceId
            && Math.Abs(first.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble() + 3) < 1e-9
            && newGroup != ElementId.InvalidElementId && second.GroupId == newGroup
            && test.GetElement(groupId) == null && CopyState() == copyState && Geometry() == geometry;
        notes.Add(new { scenario = "selected member only", passed, report });
        if (undo.RollBack() != TransactionStatus.RolledBack) throw new Exception("Rollback failed");
    }
    notes.Add(new { scenario = "transaction group rollback restores IDs and bindings", passed = Snapshot() == before });
    var noOp = execute.Invoke(service, new object[] { new List<ElementId> { firstId, secondId }, sourceId, sourceId, null, null });
    notes.Add(new { scenario = "all skipped preserves original group", passed = Snapshot() == before &&
        (int)noOp.GetType().GetProperty("Skipped").GetValue(noOp) == 2 });
    var cancel = false;
    Action<int, int, long?> progress = (done, total, id) => { if (done > 0) cancel = true; };
    var cancelled = execute.Invoke(service, new object[] { new List<ElementId> { firstId, secondId }, targetId, sourceId,
        progress, (Func<bool>)(() => cancel) });
    notes.Add(new { scenario = "cancel after first member restores entire launch", passed = Snapshot() == before &&
        (int)cancelled.GetType().GetProperty("Transferred").GetValue(cancelled) == 0 });
    // Inject an ordinary failure after the first selected member succeeded.
    var preparedType = assembly.GetType("ReLevel.Revit.Transfer.PreparedElementTransfer", true);
    var prepareMethod = serviceType.GetMethod("PrepareSingle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var arg = System.Linq.Expressions.Expression.Parameter(typeof(ElementId), "id");
    var body = System.Linq.Expressions.Expression.Condition(
        System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(arg, "Value"), System.Linq.Expressions.Expression.Constant(secondId.Value)),
        System.Linq.Expressions.Expression.Throw(System.Linq.Expressions.Expression.New(typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }),
            System.Linq.Expressions.Expression.Constant("Injected transfer failure")), preparedType),
        System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Constant(service), prepareMethod, arg,
            System.Linq.Expressions.Expression.Constant(targetId), System.Linq.Expressions.Expression.Constant(sourceId)));
    var callback = System.Linq.Expressions.Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(ElementId), preparedType), body, arg).Compile();
    var executePrepared = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 4);
    var failedReport = executePrepared.Invoke(service, new object[] { new List<ElementId> { firstId, secondId }, callback, null, null });
    notes.Add(new { scenario = "failure after first success restores entire original group", passed = Snapshot() == before &&
        (int)failedReport.GetType().GetProperty("Failed").GetValue(failedReport) == 2, report = failedReport });
    Func<ElementId, object> removeMemberBeforeTransfer = id =>
    {
        using (var tx = new Transaction(test, "Inject missing regroup member"))
        {
            tx.Start();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
            test.Delete(secondId);
            if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Injection failed");
        }
        return prepareMethod.Invoke(service, new object[] { id, targetId, sourceId });
    };
    var missingCallback = System.Linq.Expressions.Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(ElementId), preparedType),
        System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Invoke(
            System.Linq.Expressions.Expression.Constant(removeMemberBeforeTransfer), arg), preparedType), arg).Compile();
    var regroupFailure = executePrepared.Invoke(service, new object[] { new List<ElementId> { firstId }, missingCallback, null, null });
    notes.Add(new { scenario = "regroup failure restores original members", passed = Snapshot() == before &&
        (int)regroupFailure.GetType().GetProperty("Failed").GetValue(regroupFailure) == 1, report = regroupFailure });
    using (var rollbackDistribution = new TransactionGroup(test, "Distribution test"))
    {
        rollbackDistribution.Start();
        var retainedType = assembly.GetType("ReLevel.Revit.Logic.RetainedLevel", true);
        var retained = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(retainedType));
        retained.Add(Activator.CreateInstance(retainedType, new object[] { targetId.Value, 5.0 }));
        var distributionType = assembly.GetType("ReLevel.Revit.Transfer.DistributionService", true);
        var distributor = Activator.CreateInstance(distributionType, new object[] { test,
            new List<Level> { (Level)test.GetElement(sourceId), (Level)test.GetElement(targetId) } });
        var method = distributionType.GetMethod("Execute");
        var parameters = method.GetParameters().Select(p => p.Name switch
        {
            "retained" => (object)retained, "protectedIds" => new HashSet<long>(), "additionalIds" => Array.Empty<long>(),
            "analyzedIds" => new long[] { firstId.Value }, _ => null
        }).ToArray();
        var distributed = method.Invoke(distributor, parameters);
        notes.Add(new { scenario = "distribution of grouped member", passed =
            (int)distributed.GetType().GetProperty("Transferred").GetValue(distributed) == 1
            && test.GetElement(firstId).LevelId == targetId && test.GetElement(secondId).LevelId == sourceId
            && Geometry() == geometry && CopyState() == copyState, report = distributed });
        if (rollbackDistribution.RollBack() != TransactionStatus.RolledBack) throw new Exception("Distribution rollback failed");
    }
    var success = execute.Invoke(service, new object[] { new List<ElementId> { firstId, secondId }, targetId, sourceId, null, null });
    notes.Add(new { scenario = "two selected members", passed = (int)success.GetType().GetProperty("Transferred").GetValue(success) == 2
        && test.GetElement(firstId).LevelId == targetId && test.GetElement(secondId).LevelId == targetId
        && test.GetElement(firstId).GroupId == test.GetElement(secondId).GroupId && Geometry() == geometry
        && CopyState() == copyState, report = success });
    return new { version = app.Application.VersionBuild, nested = nestedFixture, notes };
}
finally { test.Close(false); }
