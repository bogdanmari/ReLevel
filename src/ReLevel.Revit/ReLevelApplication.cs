using Autodesk.Revit.UI;
using ReLevel.Revit.UI;

namespace ReLevel.Revit;

public sealed class ReLevelApplication : IExternalApplication
{
    internal static DockableWorkspace Pane { get; private set; } = null!;

    public Result OnStartup(UIControlledApplication application)
    {
        Pane = new DockableWorkspace();
        application.RegisterDockablePane(DockableWorkspace.Id, "ReLevel", Pane);
        application.Idling += Pane.OnIdling;
        application.ViewActivated += Pane.OnViewActivated;
        application.ControlledApplication.DocumentChanged += Pane.OnDocumentChanged;
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= Pane.OnIdling;
        application.ViewActivated -= Pane.OnViewActivated;
        application.ControlledApplication.DocumentChanged -= Pane.OnDocumentChanged;
        Pane.Dispose();
        return Result.Succeeded;
    }
}
