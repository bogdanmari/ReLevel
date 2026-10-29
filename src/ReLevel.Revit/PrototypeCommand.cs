using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReLevel.Revit.UI.Prototype;
using ReLevel.Revit.UI;
using ReLevel.Revit.Transfer;

namespace ReLevel.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class PrototypeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var ui = commandData.Application;
        L.Initialize(ui.Application.Language);
        var document = ui.ActiveUIDocument?.Document;
        if (document is null || document.IsFamilyDocument || document.IsReadOnly)
        {
            TaskDialog.Show("ReLevel", L.Get("Откройте доступный для изменения документ проекта Revit."));
            return Result.Cancelled;
        }
        try
        {
            var levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(level => level.ProjectElevation).ThenBy(level => level.Name).ThenBy(level => level.Id.Value).ToList();
            var snapshot = levels.Select(level => new PrototypeLevel(level.Id.Value, level.Name,
                UnitUtils.ConvertFromInternalUnits(level.ProjectElevation, UnitTypeId.Meters))
                { Color = System.Windows.Media.Color.FromRgb((byte)Random.Shared.Next(40, 231),
                    (byte)Random.Shared.Next(40, 231), (byte)Random.Shared.Next(40, 231)) }).ToList();
            var index = new ModelElementIndex(document, levels);
            using (var loader = new ModelAnalysisLoader()) index.Refresh(progress: loader.Report);
            using var workspace = new WorkspaceController(ui, levels, snapshot, index);
            var window = new PrototypeWindow(snapshot, L.UseEnglish, workspace.CreateTransfer, workspace);
            new System.Windows.Interop.WindowInteropHelper(window) { Owner = ui.MainWindowHandle };
            window.ShowDialog();
            L.UseEnglish = window.English;
            if (workspace.InspectElementRequested) ElementInspectorAction.Run(ui);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"ReLevel: {ex}";
            return Result.Failed;
        }
    }
}
