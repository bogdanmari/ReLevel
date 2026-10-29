using System.Windows;
using System.Windows.Media;

namespace ReLevel.Revit.UI;

// Disable siblings along the progress control's visual ancestry, preserving bindings.
internal sealed class OperationControls : IDisposable
{
    private readonly List<UIElement> disabled = [];

    public OperationControls(Window owner, UIElement progress)
    {
        DependencyObject child = progress;
        while (VisualTreeHelper.GetParent(child) is { } parent && child != owner)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (VisualTreeHelper.GetChild(parent, i) is UIElement sibling && sibling != child && sibling.IsEnabled)
                {
                    sibling.SetCurrentValue(UIElement.IsEnabledProperty, false);
                    disabled.Add(sibling);
                }
            child = parent;
        }
    }

    public void Dispose()
    {
        foreach (var control in disabled) control.SetCurrentValue(UIElement.IsEnabledProperty, true);
        disabled.Clear();
    }
}
