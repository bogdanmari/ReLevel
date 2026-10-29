using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class ShaftOpeningOperation(Element element, ElementId source,
    IReadOnlyDictionary<BuiltInParameter, ElementId>? destinations = null) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    private readonly LevelOffsetState[] ends = ShaftOpeningCase.Capture(element);
    public string SuccessMessage => L.Get("Привязки изменены.");

    public void Apply(Document document, ElementId target)
    {
        var opening = document.GetElement(id);
        var level = (Level)document.GetElement(target);
        LevelOffsetState.ApplyAll(opening, ends, source, level, destinations);
    }
}
