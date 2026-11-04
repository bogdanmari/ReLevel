using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace ReLevel.Revit.Transfer;

internal sealed class DuctLevelOperation : IElementTransferOperation
{
    private readonly MepReferenceLevelOperation referenceLevel;
    private readonly Dictionary<ElementId, double> takeoffLengths = [];

    public DuctLevelOperation(Duct duct)
    {
        referenceLevel = new MepReferenceLevelOperation(duct);
        foreach (Connector connector in duct.ConnectorManager.Connectors)
        foreach (Connector connected in connector.AllRefs)
        {
            var fitting = connected.Owner;
            if (fitting.Category?.BuiltInCategory != BuiltInCategory.OST_DuctFitting
                || takeoffLengths.ContainsKey(fitting.Id)) continue;
            var length = fitting.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_TAKEOFF_LENGTH);
            if (length is not { StorageType: StorageType.Double, HasValue: true }) continue;
            if (length.IsReadOnly)
                throw new InvalidOperationException(L.Format($"Длина врезки {fitting.Id.Value} недоступна для записи."));
            takeoffLengths.Add(fitting.Id, length.AsDouble());
        }
    }

    public string SuccessMessage => referenceLevel.SuccessMessage;

    public void Apply(Document document, ElementId target)
    {
        referenceLevel.Apply(document, target);
        if (takeoffLengths.Count == 0) return;
        // Level changes can reset takeoffs to their family's fixed length and reverse short branches.
        // Restore the saved lengths after that recalculation, within the same transfer transaction.
        document.Regenerate();
        foreach (var (id, length) in takeoffLengths)
        {
            if (!LevelOffsetBinding.Require(document.GetElement(id),
                    BuiltInParameter.RBS_FAMILY_CONTENT_TAKEOFF_LENGTH, StorageType.Double).Set(length))
                throw new InvalidOperationException(L.Format($"Revit отклонил восстановление длины врезки {id.Value}."));
        }
    }
}
