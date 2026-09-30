using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class TwoLevelOperation(Element element, ElementId source) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    private readonly LevelOffsetState[] ends = TwoLevelCase.Capture(element);
    private readonly IReadOnlyList<WallHostedPlacement> hosted = element is Wall wall ? WallHostedPlacement.Capture(wall) : [];
    public string SuccessMessage => L.Get("Привязки изменены.");

    public void Apply(Document document, ElementId target)
    {
        var instance = document.GetElement(id);
        var level = (Level)document.GetElement(target);
        foreach (var end in ends) end.Apply(instance, source, level);
        if (hosted.Count > 0)
        {
            // Finish recalculating the wall before restoring its hosted instances.
            document.Regenerate();
            foreach (var placement in hosted) placement.Restore(document);
        }
    }
}
