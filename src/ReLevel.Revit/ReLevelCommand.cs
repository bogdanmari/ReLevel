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
            var controller = ReLevelApplication.Controller
                ?? throw new InvalidOperationException(L.Get("Панель не зарегистрирована. Обновите ReLevel.addin и перезапустите Revit."));
            controller.Show(ui);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"ReLevel: {ex}";
            return Result.Failed;
        }
    }
}
