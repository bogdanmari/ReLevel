using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class RoomSeparatorOperation(Element source) : IElementTransferOperation
{
    private readonly ElementId id = source.Id;
    private long replacementId;
    public long? ReplacementId => replacementId == 0 ? null : replacementId;
    public string SuccessMessage => L.Format($"Линия пересоздана. Новый ID: {replacementId}. Доступные параметры перенесены.");

    public void Apply(Document document, ElementId target)
    {
        var original = (ModelLine)document.GetElement(id);
        using var views = new FilteredElementCollector(document).OfClass(typeof(ViewPlan));
        var view = views.Cast<ViewPlan>().Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan
            && v.GenLevel?.Id == target && v.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() == original.CreatedPhaseId)
            .OrderBy(v => v.Id.Value).FirstOrDefault()
            ?? throw new InvalidOperationException(L.Get("Кейс 8: нужен план этажа целевого уровня с фазой создания линии."));

        // The target plane/view establish the level; pass the original XYZ without projecting to it.
        var plane = SketchPlane.Create(document, target);
        using var curves = new CurveArray();
        using var curve = original.GeometryCurve.Clone();
        curves.Append(curve);
        var created = document.Create.NewRoomBoundaryLines(plane, curves, view);
        if (created.Size != 1)
            throw new InvalidOperationException(L.Get("Кейс 8: Revit не создал единственную линию замены."));
        var replacement = created.get_Item(0);
        RecreatedElementParameters.Copy(original, replacement, L.Get("Кейс 8"));
        // Replacement and deletion, including the original's internal dependents, share the caller's transaction.
        document.Delete(id);
        replacementId = replacement.Id.Value;
    }
}
