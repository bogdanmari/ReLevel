// Copy a PV group into an isolated unsaved document, exercise the production service,
// and close the test without saving. The source project is read only.
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
        var assembly = Assembly.Load(System.IO.File.ReadAllBytes(@"DLL_PATH"));
        var test = app.Application.NewProjectDocument(UnitSystem.Imperial);
        try
        {
            ElementId rootId, targetId;
            using (var tx = new Transaction(test, "Copy report fixture"))
            {
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(new RvtMcp.Plugin.SafeFailuresPreprocessor()).SetClearAfterRollback(true));
                using var options = new CopyPasteOptions();
                options.SetDuplicateTypeNamesHandler(new UseDestinationTypes());
                var copied = ElementTransformUtils.CopyElements(source,
                    new List<ElementId> { new ElementId(ROOT_ID) }, test, Transform.Identity, options);
                rootId = copied.First(id => test.GetElement(id) is Group g && g.GroupId == ElementId.InvalidElementId);
                targetId = Level.Create(test, -10).Id;
                if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Copy fixture rolled back");
            }
            var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", true);
            var execute = serviceType.GetMethod("ExecuteFromLevels");
            var cases = assembly.GetType("ReLevel.Revit.Transfer.TransferCases", true).GetMethod("Find");
            var root = assembly.GetType("ReLevel.Revit.Transfer.GroupTransferStructure", true).GetMethod("Root");
            var selected = new FilteredElementCollector(test).WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance || e is Wall || e is FootPrintRoof)
                .Where(e => ((ElementId)root.Invoke(null, new object[] { e })) == rootId)
                .Where(e => cases.Invoke(null, new object[] { e }) != null && e.LevelId != ElementId.InvalidElementId)
                .ToDictionary(e => e.Id.Value, e => e.LevelId.Value);
            var before = selected.Keys.ToDictionary(id => id, id =>
            {
                var b = test.GetElement(new ElementId(id)).get_BoundingBox(null);
                return new[] { b.Min, b.Max };
            });
            object report;
            bool geometry, levels, rollback;
            using (var undo = new TransactionGroup(test, "Verify full rollback"))
            {
                undo.Start();
                report = execute.Invoke(Activator.CreateInstance(serviceType, new object[] { test }),
                    new object[] { selected, targetId, null, null });
                geometry = before.All(pair =>
                {
                    var b = test.GetElement(new ElementId(pair.Key))?.get_BoundingBox(null);
                    return b != null && b.Min.IsAlmostEqualTo(pair.Value[0], 1e-6) && b.Max.IsAlmostEqualTo(pair.Value[1], 1e-6);
                });
                levels = selected.Keys.All(id => test.GetElement(new ElementId(id)).LevelId == targetId);
                if (undo.RollBack() != TransactionStatus.RolledBack) throw new Exception("Test rollback failed");
                rollback = test.GetElement(rootId) is Group && selected.All(p => test.GetElement(new ElementId(p.Key)).LevelId.Value == p.Value);
            }
            return new { selected = selected.Count, report, geometry, levels, rollback };
        }
        finally { test.Close(false); }
    }
}
