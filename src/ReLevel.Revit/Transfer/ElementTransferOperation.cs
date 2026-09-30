using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal interface IElementTransferOperation
{
    string SuccessMessage { get; }
    void Apply(Document document, ElementId target);
}

internal sealed class PointFamilyOperation(Element element) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    private readonly XYZ point = ((LocationPoint)element.Location).Point;
    public string SuccessMessage => L.Get("Уровень изменён.");
    public void Apply(Document document, ElementId target)
    {
        if (!PointFamilyCase.LevelParameter(document.GetElement(id))!.Set(target))
            throw new InvalidOperationException(L.Get("Revit отклонил запись целевого уровня."));
        // Resolve placement after changing the level, then restore the saved position.
        document.Regenerate();
        ((LocationPoint)document.GetElement(id).Location).Point = point;
    }
}
