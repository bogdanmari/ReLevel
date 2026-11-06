using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ReLevel.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class PrototypeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            ReLevelApplication.Pane.Show(commandData.Application);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"ReLevel: {ex}";
            return Result.Failed;
        }
    }
}
