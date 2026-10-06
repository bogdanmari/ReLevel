using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class RoomSeparatorOperation(Element source) : IElementTransferOperation
{
    private readonly ElementId id = source.Id;
    private long replacementId;
    public string SuccessMessage => L.Format($"Линия пересоздана. Новый ID: {replacementId}. Доступные параметры перенесены.");

    public void Apply(Document document, ElementId target)
    {
        var original = (ModelLine)document.GetElement(id);
        var level = (Level)document.GetElement(target);
        var sourceLevel = (Level)document.GetElement(original.LevelId);
        if (Math.Abs(level.ProjectElevation - sourceLevel.ProjectElevation) > RoomSeparatorCase.ElevationTolerance)
            throw new InvalidOperationException(L.Get("Кейс 8: пока поддерживаются только уровни на одинаковой отметке."));
        using var views = new FilteredElementCollector(document).OfClass(typeof(ViewPlan));
        var view = views.Cast<ViewPlan>().Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan
            && v.GenLevel?.Id == target && v.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() == original.CreatedPhaseId)
            .OrderBy(v => v.Id.Value).FirstOrDefault()
            ?? throw new InvalidOperationException(L.Get("Кейс 8: нужен план этажа целевого уровня с фазой создания линии."));

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
