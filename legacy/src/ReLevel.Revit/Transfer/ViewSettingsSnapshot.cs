using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class ViewSettingsSnapshot
{
    private const double Tolerance = 1e-5;
    private static readonly PlanViewPlane[] Planes = [PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane,
        PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane];
    private readonly ElementId typeId;
    private readonly ElementId templateId;
    private readonly ElementId scopeBoxId;
    private readonly ElementId underlayBaseId;
    private readonly ElementId underlayTopId;
    private readonly ElementId phaseId;
    private readonly string titleOnSheet;
    private readonly bool cropActive;
    private readonly bool cropVisible;
    private readonly int annotationCrop;
    private readonly XYZ right;
    private readonly XYZ up;
    private readonly (ElementId Level, double Offset)[] range;
    private readonly List<XYZ> cropPoints;
    private readonly bool cropShapeSet;
    private readonly double[] annotationOffsets;
    private readonly Dictionary<long, ParameterValue> parameters;

    public ViewSettingsSnapshot(ViewPlan source)
    {
        typeId = source.GetTypeId(); templateId = source.ViewTemplateId;
        underlayBaseId = source.GetUnderlayBaseLevel();
        underlayTopId = source.GetUnderlayTopLevel();
        phaseId = RequiredParameter(source, (long)Autodesk.Revit.DB.BuiltInParameter.VIEW_PHASE).AsElementId();
        titleOnSheet = RequiredParameter(source, (long)Autodesk.Revit.DB.BuiltInParameter.VIEW_DESCRIPTION).AsString() ?? "";
        cropActive = source.CropBoxActive; cropVisible = source.CropBoxVisible;
        annotationCrop = source.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() ?? 0;
        right = source.RightDirection; up = source.UpDirection;
        using var manager = source.GetCropRegionShapeManager();
        if (manager.Split) throw new NotSupportedException("Разделённая область обрезки пока не поддерживается.");
        scopeBoxId = ScopeBoxId(source);
        cropShapeSet = manager.ShapeSet;
        cropPoints = ViewCropGeometry.Points(source, manager);
        annotationOffsets = AnnotationOffsets(manager);
        using var viewRange = source.GetViewRange();
        range = Planes.Select(p => (viewRange.GetLevelId(p), viewRange.GetOffset(p))).ToArray();
        var ids = source.GetTemplateParameterIds().Select(id => id.ToLong()).ToHashSet();
        parameters = source.Parameters.Cast<Parameter>().Where(p => ids.Contains(p.Id.ToLong()) && p.HasValue && p.StorageType != StorageType.None)
            .ToDictionary(p => p.Id.ToLong(), ParameterValue.Capture);
    }

    public void Apply(ViewPlan source, ViewPlan target)
    {
        // The view type may assign a default template on creation; remove its controls first.
        target.ViewTemplateId = ElementId.InvalidElementId;
        target.ApplyViewTemplateParameters(source);
        if (target.GetUnderlayBaseLevel() != underlayBaseId || target.GetUnderlayTopLevel() != underlayTopId)
            target.SetUnderlayRange(underlayBaseId, underlayTopId);
        var phase = RequiredParameter(target, (long)Autodesk.Revit.DB.BuiltInParameter.VIEW_PHASE);
        if (phase.AsElementId() != phaseId && (phase.IsReadOnly || !phase.Set(phaseId)))
            throw new InvalidOperationException("Не удалось скопировать фазу вида (Phase).");
        var title = RequiredParameter(target, (long)Autodesk.Revit.DB.BuiltInParameter.VIEW_DESCRIPTION);
        if ((title.AsString() ?? "") != titleOnSheet && (title.IsReadOnly || !title.Set(titleOnSheet)))
            throw new InvalidOperationException("Не удалось скопировать заголовок на листе (Title on Sheet).");
        // Preserve the existing scope box before restoring template controls.
        if (ScopeBoxId(target) != scopeBoxId)
        {
            var scopeParameter = target.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (scopeParameter is null || scopeParameter.IsReadOnly || !scopeParameter.Set(scopeBoxId))
                throw new InvalidOperationException("Не удалось назначить исходную область видимости (Scope Box).");
        }
        using var targetRange = target.GetViewRange();
        var rangeChanged = false;
        for (var i = 0; i < Planes.Length; i++)
        {
            rangeChanged |= targetRange.GetLevelId(Planes[i]) != range[i].Level || !Near(targetRange.GetOffset(Planes[i]), range[i].Offset);
            targetRange.SetLevelId(Planes[i], range[i].Level);
            targetRange.SetOffset(Planes[i], range[i].Offset);
        }
        if (rangeChanged) target.SetViewRange(targetRange);
        target.Document.Regenerate();
        using var targetCrop = target.GetCropRegionShapeManager();
        // A scope box controls crop geometry and orientation; do not overwrite its crop shape.
        if (scopeBoxId == ElementId.InvalidElementId)
        {
            ViewCropGeometry.Copy(source, target, targetCrop);
        }
        if (targetCrop.CanHaveAnnotationCrop)
        {
            targetCrop.LeftAnnotationCropOffset = annotationOffsets[0];
            targetCrop.RightAnnotationCropOffset = annotationOffsets[1];
            targetCrop.TopAnnotationCropOffset = annotationOffsets[2];
            targetCrop.BottomAnnotationCropOffset = annotationOffsets[3];
        }
        var annotationParameter = target.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
        if (annotationParameter is not null && annotationParameter.AsInteger() != annotationCrop)
        {
            if (annotationParameter.IsReadOnly || !annotationParameter.Set(annotationCrop))
                throw new InvalidOperationException("Не удалось скопировать обрезку аннотаций.");
        }
        if (target.CropBoxActive != cropActive) target.CropBoxActive = cropActive;
        if (target.CropBoxVisible != cropVisible) target.CropBoxVisible = cropVisible;
        // Restore template controls after setting the values that may otherwise be read-only.
        target.ViewTemplateId = templateId;
    }

    public void Verify(ViewPlan target)
    {
        if (target.GetUnderlayBaseLevel() != underlayBaseId || target.GetUnderlayTopLevel() != underlayTopId)
            throw new InvalidOperationException("Не совпадают уровни подложки (Range: Base Level / Top Level).");
        if (RequiredParameter(target, (long)Autodesk.Revit.DB.BuiltInParameter.VIEW_PHASE).AsElementId() != phaseId)
            throw new InvalidOperationException("Не совпадает фаза вида (Phase).");
        if ((RequiredParameter(target, (long)Autodesk.Revit.DB.BuiltInParameter.VIEW_DESCRIPTION).AsString() ?? "") != titleOnSheet)
            throw new InvalidOperationException("Не совпадает заголовок на листе (Title on Sheet).");
        if (ScopeBoxId(target) != scopeBoxId)
            throw new InvalidOperationException("Область видимости (Scope Box) нового вида отличается от исходной.");
        if (target.GetTypeId() != typeId || target.ViewTemplateId != templateId
            || target.CropBoxActive != cropActive || target.CropBoxVisible != cropVisible
            || (target.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() ?? 0) != annotationCrop
            || !target.RightDirection.IsAlmostEqualTo(right) || !target.UpDirection.IsAlmostEqualTo(up))
            throw new InvalidOperationException("Тип, шаблон, ориентация или обрезка нового вида отличаются от исходного.");
        using var actualRange = target.GetViewRange();
        for (var i = 0; i < Planes.Length; i++)
            if (actualRange.GetLevelId(Planes[i]) != range[i].Level || !Near(actualRange.GetOffset(Planes[i]), range[i].Offset))
                throw new InvalidOperationException($"Не совпадает секущий диапазон: {Planes[i]}.");
        using var crop = target.GetCropRegionShapeManager();
        var actualPoints = ViewCropGeometry.Points(target, crop);
        if (crop.Split || crop.ShapeSet != cropShapeSet || actualPoints.Count != cropPoints.Count
            || cropPoints.Where((p, i) => !Near(p.X, actualPoints[i].X) || !Near(p.Y, actualPoints[i].Y)).Any()
            || AnnotationOffsets(crop).Where((v, i) => !Near(v, annotationOffsets[i])).Any())
            throw new InvalidOperationException("Границы обрезки нового вида отличаются от исходного.");
        var actual = target.Parameters.Cast<Parameter>().ToDictionary(p => p.Id.ToLong());
        foreach (var (id, value) in parameters)
            if (!actual.TryGetValue(id, out var parameter) || !value.Matches(parameter))
                throw new InvalidOperationException($"Параметр вида не скопирован: {parameter?.Definition.Name ?? id.ToString()}.");
    }

    private static ElementId ScopeBoxId(View view) =>
        view.ParameterById((long)Autodesk.Revit.DB.BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId() ?? ElementId.InvalidElementId;

    private static Parameter RequiredParameter(View view, long id) => view.ParameterById(id)
        ?? throw new InvalidOperationException($"У вида {view.Id.ToLong()} отсутствует параметр {id}.");

    private static double[] AnnotationOffsets(ViewCropRegionShapeManager manager) => manager.CanHaveAnnotationCrop
        ? [manager.LeftAnnotationCropOffset, manager.RightAnnotationCropOffset, manager.TopAnnotationCropOffset, manager.BottomAnnotationCropOffset]
        : [0, 0, 0, 0];

    private static bool Near(double a, double b) => Numeric.IsFinite(a) && Numeric.IsFinite(b) && Math.Abs(a - b) <= Tolerance;

    private sealed record ParameterValue(StorageType Type, object? Value)
    {
        public static ParameterValue Capture(Parameter p) => new(p.StorageType, p.StorageType switch
        {
            StorageType.Double => p.AsDouble(), StorageType.Integer => p.AsInteger(),
            StorageType.ElementId => p.AsElementId().ToLong(), StorageType.String => p.AsString(), _ => null
        });

        public bool Matches(Parameter p)
        {
            if (!p.HasValue || p.StorageType != Type) return false;
            var actual = Capture(p).Value;
            return Value is double a && actual is double b ? Near(a, b) : Equals(Value, actual);
        }
    }
}
