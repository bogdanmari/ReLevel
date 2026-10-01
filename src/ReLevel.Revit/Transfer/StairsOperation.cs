using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class StairsOperation(Element stairs, ElementId source) : IElementTransferOperation
{
    private readonly ElementId id = stairs.Id;
    private readonly LevelOffsetState[] ends = StairsCase.Capture(stairs);
    public string SuccessMessage => L.Get("Привязки лестницы изменены.");

    public void Apply(Document document, ElementId target)
    {
        var stairs = document.GetElement(id);
        var level = (Level)document.GetElement(target);
        foreach (var end in ends) end.Apply(stairs, source, level);
    }
}
