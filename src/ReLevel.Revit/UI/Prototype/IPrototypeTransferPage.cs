using System.Windows;

namespace ReLevel.Revit.UI.Prototype;

// The shell can host the working page without depending on Revit objects.
internal interface IPrototypeTransferPage
{
    FrameworkElement View { get; }
    void SetLanguage(bool english);
}
