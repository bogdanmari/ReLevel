using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class AreaBoundaryOperation(Element source) : IElementTransferOperation
{
    private readonly ElementId id = source.Id;
    private readonly ElementId schemeId = AreaBoundaryCase.FindScheme(source);
    private long replacementId;
    private long? createdViewId;
    public string SuccessMessage => L.Format($"Линия пересоздана. Новый ID: {replacementId}. Доступные параметры перенесены.")
        + (createdViewId is { } viewId ? " " + L.Format($"Создан план площадей. ID: {viewId}.") : "");

    public void Apply(Document document, ElementId target)
    {
        var original = (ModelLine)document.GetElement(id);
        var level = (Level)document.GetElement(target);
        var sourceLevel = (Level)document.GetElement(original.LevelId);
        if (Math.Abs(level.ProjectElevation - sourceLevel.ProjectElevation) > RoomSeparatorCase.ElevationTolerance)
            throw new InvalidOperationException(L.Get("Кейс 18: пока поддерживаются только уровни на одинаковой отметке."));
        using var views = new FilteredElementCollector(document).OfClass(typeof(ViewPlan));
        var view = views.Cast<ViewPlan>().Where(v => !v.IsTemplate && v.ViewType == ViewType.AreaPlan
            && v.GenLevel?.Id == target && v.AreaScheme?.Id == schemeId).OrderBy(v => v.Id.Value).FirstOrDefault();
        if (view is null)
        {
            view = ViewPlan.CreateAreaPlan(document, schemeId, target);
            createdViewId = view.Id.Value;
        }
        var plane = SketchPlane.Create(document, target);
        using var curve = original.GeometryCurve.Clone();
        var replacement = document.Create.NewAreaBoundaryLine(plane, curve, view)
            ?? throw new InvalidOperationException(L.Get("Кейс 18: Revit не создал линию замены."));
        RecreatedElementParameters.Copy(original, replacement, L.Get("Кейс 18"));
        // The replacement, optional plan and source deletion share the caller's transaction and Undo.
        document.Delete(id);
        replacementId = replacement.Id.Value;
    }
}
