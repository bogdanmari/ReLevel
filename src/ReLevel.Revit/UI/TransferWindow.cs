using System.Windows;
using Autodesk.Revit.DB;
using UIApplication = Autodesk.Revit.UI.UIApplication;

namespace ReLevel.Revit.UI;

// Original window hosting the shared transfer panel (main-line build only).
internal sealed class TransferWindow : Window
{
    private readonly TransferPanel panel;
    public bool InspectElementRequested => panel.InspectElementRequested;
    public bool LevelColorsRequested => panel.LevelColorsRequested;

    public TransferWindow(UIApplication application, IReadOnlyList<Level> levels)
    {
        Title = "ReLevel"; Width = 920; Height = 607.2; MinWidth = 850; MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        panel = new TransferPanel(this, application, levels);
        Content = panel;
    }
}
