using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class ViewAnnotations
{
    private readonly ElementId sourceViewId;
    private readonly List<ElementId> ids;
    private readonly ViewOperationLog log;
    private readonly Dictionary<long, List<ElementId>> copies = [];
    private readonly HashSet<long> failed = [];
    private readonly Dictionary<long, AnnotationSnapshot> snapshots = [];
    private readonly HashSet<long> needsReview = [];
    private readonly HashSet<string> recordedChecks = [];

    public ViewAnnotations(ViewPlan source, ViewOperationLog log)
    {
        sourceViewId = source.Id;
        this.log = log;
        ids = Collect(source).Select(e => e.Id).ToList();
        foreach (var id in ids) snapshots[id.Value] = new AnnotationSnapshot(source.Document.GetElement(id), source);
    }

    public void CopyTo(ViewPlan target)
    {
        var document = target.Document;
        var targetId = target.Id;
        using var options = new CopyPasteOptions();
        foreach (var sourceId in ids)
        {
            List<ElementId> roots = [];
            List<long> returnedIds = [];
            var ok = log.Run(L.Get("Копирование аннотации"), () =>
            {
                var original = document.GetElement(sourceId) ?? throw new InvalidOperationException(L.Get("Исходный элемент отсутствует."));
                if (original is Group || original.GroupId != ElementId.InvalidElementId)
                    throw new NotSupportedException(L.Get("Копирование групп аннотаций пока не поддерживается."));
                if (original.Category?.CategoryType != CategoryType.Annotation
                    && original is not (DetailCurve or FilledRegion or FamilyInstance or ImageInstance or ImportInstance))
                    throw new NotSupportedException(L.Format($"Не поддерживается {original.GetType().Name}, категория «{original.Category?.Name}»."));
                var sourceView = (ViewPlan)document.GetElement(sourceViewId);
                var destination = (ViewPlan)document.GetElement(targetId);
                var copied = ElementTransformUtils.CopyElements(sourceView, [sourceId], destination, Transform.Identity, options);
                returnedIds = copied.Select(id => id.Value).ToList();
                document.Regenerate();
                roots = copied.Select(document.GetElement)
                    .Where(e => e is not null && e.OwnerViewId == targetId
                        && e.GetType() == original.GetType() && e.Category?.Id == original.Category?.Id)
                    .Select(e => e.Id).ToList();
                if (roots.Count == 0)
                    throw new InvalidOperationException(L.Get("Revit не вернул созданную аннотацию в новом виде."));
            }, sourceId.Value);
            if (!ok) { failed.Add(sourceId.Value); continue; }
            copies[sourceId.Value] = roots;
            if (roots.Any(id => document.GetElement(id)?.OwnerViewId == targetId))
                log.Add(LogSeverity.Info, L.Get("Копирование аннотации"), L.Get("Аннотация создана."),
                    L.Get("ID аннотаций: ") + string.Join(", ", roots.Select(id => id.Value))
                    + L.Get("\nВсе ID из результата Revit (включая зависимости): ") + string.Join(", ", returnedIds), sourceId.Value);
            else MarkMissing(sourceId.Value);
        }
        Verify((ViewPlan)document.GetElement(targetId));
    }

    public void CopyGraphicsTo(ViewPlan target)
    {
        var document = target.Document;
        var targetId = target.Id;
        ViewPlan Target() => document.GetElement(targetId) as ViewPlan
            ?? throw new InvalidOperationException(L.Get("Созданный вид отсутствует."));
        foreach (var (sourceId, createdIds) in copies)
        {
            if (failed.Contains(sourceId)) continue;
            if (createdIds.Count != 1)
            {
                log.Add(LogSeverity.Warning, L.Get("Графика аннотации"),
                    L.Get("Индивидуальная графика не перенесена: результат вставки нельзя сопоставить однозначно. Копии сохранены."),
                    L.Get("Подходящие ID из результата Revit: ") + string.Join(", ", createdIds.Select(id => id.Value)), sourceId,
                    L.Get("Сравните копии с исходным элементом и при необходимости настройте их графику вручную."));
                continue;
            }

            var copyId = createdIds[0];
            ViewGraphicsSnapshot? graphics = null;
            try
            {
                if (!log.Run(L.Get("Графика аннотации"), () =>
                {
                    var source = document.GetElement(sourceViewId) as ViewPlan
                        ?? throw new InvalidOperationException(L.Get("Исходный вид отсутствует."));
                    var original = document.GetElement(new ElementId(sourceId))
                        ?? throw new InvalidOperationException(L.Get("Исходная аннотация отсутствует."));
                    var copy = document.GetElement(copyId);
                    if (original.OwnerViewId != sourceViewId || copy is null || copy.OwnerViewId != targetId
                        || copy.GetType() != original.GetType() || copy.Category?.Id != original.Category?.Id)
                        throw new InvalidOperationException(L.Format($"Не удалось подтвердить соответствие копии {copyId.Value} исходной аннотации."));
                    graphics = new ViewGraphicsSnapshot(source, original.Id, copyId);
                    graphics.Apply(Target());
                }, sourceId)) continue;

                log.Check(L.Get("Проверка графики аннотации"), () =>
                {
                    graphics!.Verify(Target());
                    log.Add(LogSeverity.Info, L.Get("Графика аннотации"), L.Get("Индивидуальная графика и скрытие скопированы и проверены."),
                        L.Format($"ID копии: {copyId.Value}."), sourceId);
                }, sourceId);
            }
            finally { graphics?.Dispose(); }
        }
    }

    public void Verify(ViewPlan target)
    {
        foreach (var (sourceId, createdIds) in copies)
        {
            var live = createdIds.Select(target.Document.GetElement).Where(e => e?.OwnerViewId == target.Id).ToArray();
            if (live.Length == 0) { MarkMissing(sourceId); continue; }
            if (live.Length != 1)
            {
                needsReview.Add(sourceId);
                Record(sourceId, new(false, L.Get("Несколько копий аннотации: соответствие исходнику требует ручной проверки.")), createdIds);
                continue;
            }
            var checks = snapshots[sourceId].Verify(live[0], target);
            if (checks.Count > 0) needsReview.Add(sourceId); else needsReview.Remove(sourceId);
            foreach (var check in checks) Record(sourceId, check, createdIds);
        }
    }

    private void Record(long sourceId, AnnotationCheck check, IEnumerable<ElementId> createdIds)
    {
        if (!recordedChecks.Add($"{sourceId}:{check.Error}:{check.Message}")) return;
        log.Add(check.Error ? LogSeverity.Error : LogSeverity.Warning, L.Get("Проверка содержимого аннотации"), check.Message,
            L.Get("ID аннотаций: ") + string.Join(", ", createdIds.Select(id => id.Value)), sourceId,
            L.Get("Сравните исходную аннотацию и копию на новом виде; копия сохранена для проверки."));
    }

    private void MarkMissing(long sourceId)
    {
        needsReview.Remove(sourceId);
        if (!failed.Add(sourceId)) return;
        log.Add(LogSeverity.Error, L.Get("Проверка вставки"), L.Get("Аннотация не сохранилась после обработки Revit. Новый вид сохранён."),
            elementId: sourceId, recommendation: L.Get("Проверьте исходный элемент и его ссылки; при необходимости вставьте его вручную."));
    }

    public string Report => L.Format($"Скопировано аннотаций: {copies.Keys.Count(id => !failed.Contains(id))} из {ids.Count}. Не скопировано: {failed.Count}.")
        + L.Format($" Требуют проверки содержимого: {needsReview.Count}. Без замечаний автоматической проверки: {copies.Keys.Count(id => !failed.Contains(id) && !needsReview.Contains(id))}.")
        + (failed.Count == 0 ? "" : L.Get(" Исходные ID: ") + string.Join(", ", failed.Order()));

    private static List<Element> Collect(View view)
    {
        var owned = new FilteredElementCollector(view.Document).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
        var sketchMembers = new FilteredElementCollector(view.Document).OfClass(typeof(Sketch)).Cast<Sketch>()
            .Where(s => view.Document.GetElement(s.OwnerId)?.OwnerViewId == view.Id)
            .SelectMany(s => s.GetAllElements()).Select(id => id.Value).ToHashSet();
        return owned.Where(e => e.ViewSpecific && e.Category is not null
            && e is not (View or Sketch or SketchPlane or SunAndShadowSettings)
            && !IsSunPath(e.Category) && !sketchMembers.Contains(e.Id.Value)).ToList();
    }

    private static bool IsSunPath(Category category)
    {
        for (Category? current = category; current is not null; current = current.Parent)
            if (current.Id.Value == (long)BuiltInCategory.OST_SunStudy) return true;
        return false;
    }
}
