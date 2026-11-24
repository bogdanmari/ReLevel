// Copies a selected report fixture and its root groups into an unsaved document.
// Run different design options separately: document-to-document copying merges them.
// All geometry/deletion checks below belong to this test only.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

public static class McpDynamicScript
{
    private sealed class UseDestinationTypes : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes;
    }

    public static object Run(UIApplication app)
    {
        var source = app.ActiveUIDocument.Document;
        var reportIds = new long[] { REPORT_IDS };
        var sourceNames = new[] { "LEVEL 4", "SHROUD", "STAIRS ROOF T.O. ROOF", "T/O WALL, B/O SCREEN" };
        var assembly = Assembly.Load(System.IO.File.ReadAllBytes(@"DLL_PATH"));
        var cases = assembly.GetType("ReLevel.Revit.Transfer.TransferCases", true).GetMethod("Find");
        var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", true);
        var execute = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 5);
        var root = assembly.GetType("ReLevel.Revit.Transfer.GroupTransferStructure", true).GetMethod("Root");
        var originals = reportIds.Select(id => source.GetElement(new ElementId(id)))
            .Where(e => e != null && e is not Dimension).ToList();
        var roots = originals.Select(e => e.GroupId == ElementId.InvalidElementId ? e.Id
            : (ElementId)root.Invoke(null, new object[] { e })).Distinct().ToList();
        roots.AddRange(new FilteredElementCollector(source).OfClass(typeof(Level)).ToElementIds());
        var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
        try
        {
            ElementId target;
            using (var tx = new Transaction(test, "Copy all report fixtures"))
            {
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
                using var options = new CopyPasteOptions();
                options.SetDuplicateTypeNamesHandler(new UseDestinationTypes());
                ElementTransformUtils.CopyElements(source, roots, test, Transform.Identity, options);
                target = Level.Create(test, -10).Id;
                if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Copy fixture rolled back");
            }
            var service = Activator.CreateInstance(serviceType, new object[] { test });
            var initial = new FilteredElementCollector(test).WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance || e is Wall || e is FootPrintRoof || e is CurveElement)
                .Select(e => e.Id).ToList();
            if (initial.Count == 0) throw new Exception("Copy did not retain any model elements; fixture is invalid");
            var before = initial.ToDictionary(id => id.Value, id =>
            {
                var b = test.GetElement(id).get_BoundingBox(null);
                return b == null ? null : new[] { b.Min, b.Max };
            });
            var notes = new List<object>();
            var replacements = new Dictionary<long, long>();
            using (var undo = new TransactionGroup(test, "Verify report transfer and rollback"))
            {
                undo.Start();
                foreach (var name in sourceNames)
                {
                    var level = new FilteredElementCollector(test).OfClass(typeof(Level)).Cast<Level>().Single(l => l.Name == name);
                    var selected = new List<ElementId>();
                    foreach (var e in new FilteredElementCollector(test).WhereElementIsNotElementType())
                    {
                        var c = cases.Invoke(null, new object[] { e });
                        if (c == null) continue;
                        var isOnLevel = (Delegate)c.GetType().GetProperty("IsOnLevel").GetValue(c);
                        if ((bool)isOnLevel.DynamicInvoke(e, level.Id)) selected.Add(e.Id);
                    }
                    var report = execute.Invoke(service, new object[] { selected, target, level.Id, null, null });
                    var items = ((System.Collections.IEnumerable)report.GetType().GetProperty("Items").GetValue(report)).Cast<object>().ToList();
                    foreach (var item in items)
                    {
                        var replacement = item.GetType().GetProperty("ReplacementId").GetValue(item);
                        if (replacement is long id) replacements[(long)item.GetType().GetProperty("Id").GetValue(item)] = id;
                    }
                    notes.Add(new { level = name, selected = selected.Count,
                        transferred = report.GetType().GetProperty("Transferred").GetValue(report),
                        skipped = report.GetType().GetProperty("Skipped").GetValue(report),
                        failed = report.GetType().GetProperty("Failed").GetValue(report),
                        problems = items.Where(i => Convert.ToInt32(i.GetType().GetProperty("Status").GetValue(i)) != 0).ToArray() });
                    if ((bool)report.GetType().GetProperty("Stopped").GetValue(report)) throw new Exception("Transfer stopped critically");
                }
                var geometryFailures = before.Where(pair =>
                {
                    var id = replacements.TryGetValue(pair.Key, out var replacement) ? replacement : pair.Key;
                    var e = test.GetElement(new ElementId(id));
                    if (e == null) return true;
                    if (pair.Value == null) return false;
                    var b = e.get_BoundingBox(null);
                    return b == null || !b.Min.IsAlmostEqualTo(pair.Value[0], 1e-6) || !b.Max.IsAlmostEqualTo(pair.Value[1], 1e-6);
                }).Select(p => p.Key).ToArray();
                var deletion = new List<object>();
                foreach (var name in sourceNames)
                {
                    var level = new FilteredElementCollector(test).OfClass(typeof(Level)).Cast<Level>().Single(l => l.Name == name);
                    using var tx = new Transaction(test, "Probe released level");
                    tx.Start();
                    try
                    {
                        var removed = test.Delete(level.Id).Select(i => i.Value).ToHashSet();
                        deletion.Add(new { level = name, deletedModelIds = before.Keys
                            .Select(id => replacements.TryGetValue(id, out var replacement) ? replacement : id).Where(removed.Contains).ToArray() });
                    }
                    finally { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); }
                }
                if (undo.RollBack() != TransactionStatus.RolledBack) throw new Exception("Full rollback failed");
                return new { originalReportElements = originals.Count, copiedModelElements = initial.Count,
                    notes, geometryFailures, deletion, restoredIds = initial.All(id => test.GetElement(id) != null) };
            }
        }
        finally { test.Close(false); }
    }
}
