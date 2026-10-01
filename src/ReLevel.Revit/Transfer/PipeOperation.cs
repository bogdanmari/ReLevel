using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class PipeOperation(Element element) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    public string SuccessMessage => L.Get("Уровень изменён.");

    public void Apply(Document document, ElementId target)
    {
        // Revit recalculates the pipe elevations when its reference level changes.
        if (!PipeCase.LevelParameter(document.GetElement(id))!.Set(target))
            throw new InvalidOperationException(L.Get("Revit отклонил запись целевого уровня."));
    }
}
