using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ReLevel.Revit.Inspection;

namespace ReLevel.Revit.UI;

internal static class ElementInspectorAction
{
    // Called after the modal WPF window has closed, in the external command context.
    public static void Run(UIApplication application)
    {
        try
        {
            var path = ElementInspectionFile.ResolvePath();
            var uiDocument = application.ActiveUIDocument;
            var reference = uiDocument.Selection.PickObject(ObjectType.Element,
                L.Get("ReLevel инспектор: выберите один элемент. Escape — отмена."));
            var element = uiDocument.Document.GetElement(reference.ElementId)
                ?? throw new InvalidOperationException(L.Get("Объект больше не существует."));
            var id = element.Id.Value;
            var report = new ElementInspectionReport().Capture(element);
            ElementInspectionFile.Append(path, report.Markdown);
            TaskDialog.Show(L.Get("ReLevel инспектор"),
                L.Format($"Элемент ID {id} записан в ELEMENTS.md. Ошибок чтения: {report.ReadErrors}.\n{path}"));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { } // Escape: no write.
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
        {
            TaskDialog.Show(L.Get("ReLevel инспектор"),
                L.Get("Критическая ошибка Revit. Отчёт не записан; проверьте состояние документа."));
        }
        catch (Exception ex)
        {
            // Keep earlier operations from this command; report export must not cancel them.
            TaskDialog.Show(L.Get("ReLevel инспектор"), L.Format($"Не удалось записать отчёт инспектора: {ex.Message}"));
        }
    }
}
