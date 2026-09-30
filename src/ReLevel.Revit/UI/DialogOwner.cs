using System.Windows;
using System.Windows.Interop;

namespace ReLevel.Revit.UI;

internal static class DialogOwner
{
    public static void Attach(Window dialog, FrameworkElement owner)
    {
        if (owner is TransferPanel panel)
            new WindowInteropHelper(dialog) { Owner = panel.MainWindowHandle };
        else dialog.Owner = owner as Window ?? Window.GetWindow(owner);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
