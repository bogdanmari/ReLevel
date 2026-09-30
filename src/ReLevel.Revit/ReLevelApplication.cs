using Autodesk.Revit.UI;
using ReLevel.Revit.UI;

namespace ReLevel.Revit;

public sealed class ReLevelApplication : IExternalApplication
{
    internal static ReLevelPaneController? Controller { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        Controller = new ReLevelPaneController();
        application.RegisterDockablePane(ReLevelPaneController.PaneId, "ReLevel", Controller.Pane);
        application.Idling += Controller.OnIdling;
        application.ControlledApplication.DocumentChanged += Controller.OnDocumentChanged;
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        if (Controller is not null)
        {
            application.Idling -= Controller.OnIdling;
            application.ControlledApplication.DocumentChanged -= Controller.OnDocumentChanged;
            Controller.Dispose();
            Controller = null;
        }
        return Result.Succeeded;
    }
}
