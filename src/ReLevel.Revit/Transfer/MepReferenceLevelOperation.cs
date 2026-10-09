using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class MepReferenceLevelOperation(Element element,
    BuiltInParameter levelParameter = BuiltInParameter.RBS_START_LEVEL_PARAM) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    public bool IsFabricationPart { get; } = element is FabricationPart;
    public string SuccessMessage => L.Get("Уровень изменён.");

    public void Apply(Document document, ElementId target)
    {
        // Revit recalculates MEP elevations when their reference level changes.
        if (!LevelOffsetBinding.Require(document.GetElement(id),
                levelParameter, StorageType.ElementId).Set(target))
            throw new InvalidOperationException(L.Get("Revit отклонил запись целевого уровня."));
    }
}
