using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReLevel.Revit.UI;

namespace ReLevel.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class ReLevelCommand : IExternalCommand
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
                .OrderBy(l => l.ProjectElevation).ThenBy(l => l.Name).ToList();
            var window = new TransferWindow(ui, levels, ui.ActiveUIDocument!.Selection.GetElementIds().ToList());
            new System.Windows.Interop.WindowInteropHelper(window) { Owner = ui.MainWindowHandle };
            window.ShowDialog();
            // Operations are committed while the window is open. Closing it must not undo them.
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"ReLevel: {ex}";
            return Result.Failed;
        }
    }
}
