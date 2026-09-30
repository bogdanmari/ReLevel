using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace ReLevel.Revit.UI;

internal sealed class ReLevelDockPane : Page, IDockablePaneProvider
{
    public ReLevelDockPane() => ShowMessage(L.Get("Откройте доступный для изменения документ проекта Revit."));

    public void ShowMessage(string text) => Content = new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16)
    };

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right, MinimumWidth = 340, MinimumHeight = 360 };
        data.VisibleByDefault = false;
    }
}
