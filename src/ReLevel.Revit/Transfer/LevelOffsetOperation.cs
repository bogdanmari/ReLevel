using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class LevelOffsetOperation(Element element, ElementId source, LevelOffsetBinding parameters) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    private readonly LevelOffsetState binding = parameters.Capture(element);
    public string SuccessMessage => L.Get("Уровень изменён.");

    public void Apply(Document document, ElementId target) =>
        binding.Apply(document.GetElement(id), source, (Level)document.GetElement(target));
}
