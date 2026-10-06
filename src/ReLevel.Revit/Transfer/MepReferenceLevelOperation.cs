using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class MepReferenceLevelOperation(Element element) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    public string SuccessMessage => L.Get("Уровень изменён.");

    public void Apply(Document document, ElementId target)
    {
        // Revit recalculates pipe/duct elevations when their reference level changes.
        if (!LevelOffsetBinding.Require(document.GetElement(id),
                BuiltInParameter.RBS_START_LEVEL_PARAM, StorageType.ElementId).Set(target))
            throw new InvalidOperationException(L.Get("Revit отклонил запись целевого уровня."));
    }
}
