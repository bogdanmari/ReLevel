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

    public ViewAnnotations(ViewPlan source, ViewOperationLog log)
    {
        sourceViewId = source.Id;
        this.log = log;
        ids = Collect(source).Select(e => e.Id).ToList();
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
            var ok = log.Run("Копирование аннотации", () =>
            {
                var original = document.GetElement(sourceId) ?? throw new InvalidOperationException("Исходный элемент отсутствует.");
                if (original is Group || original.GroupId != ElementId.InvalidElementId)
                    throw new NotSupportedException("Копирование групп аннотаций пока не поддерживается.");
                if (original.Category?.CategoryType != CategoryType.Annotation
                    && original is not (DetailCurve or FilledRegion or FamilyInstance or ImageInstance or ImportInstance))
                    throw new NotSupportedException($"Не поддерживается {original.GetType().Name}, категория «{original.Category?.Name}».");
                var sourceView = (ViewPlan)document.GetElement(sourceViewId);
                var destination = (ViewPlan)document.GetElement(targetId);
                var copied = ElementTransformUtils.CopyElements(sourceView, [sourceId], destination, Transform.Identity, options);
                returnedIds = copied.Select(id => id.ToLong()).ToList();
                document.Regenerate();
                roots = copied.Select(document.GetElement)
                    .Where(e => e is not null && e.OwnerViewId == targetId
                        && e.GetType() == original.GetType() && e.Category?.Id == original.Category?.Id)
                    .Select(e => e.Id).ToList();
                if (roots.Count == 0)
                    throw new InvalidOperationException("Revit не вернул созданную аннотацию в новом виде.");
            }, sourceId.ToLong());
            if (!ok) { failed.Add(sourceId.ToLong()); continue; }
            copies[sourceId.ToLong()] = roots;
            if (roots.Any(id => document.GetElement(id)?.OwnerViewId == targetId))
                log.Add(LogSeverity.Info, "Копирование аннотации", "Аннотация создана.",
                    "ID аннотаций: " + string.Join(", ", roots.Select(id => id.ToLong()))
                    + "\nВсе ID из результата Revit (включая зависимости): " + string.Join(", ", returnedIds), sourceId.ToLong());
            else MarkMissing(sourceId.ToLong());
        }
        Verify((ViewPlan)document.GetElement(targetId));
    }

    public void CopyGraphicsTo(ViewPlan target)
    {
        var document = target.Document;
        var targetId = target.Id;
        ViewPlan Target() => document.GetElement(targetId) as ViewPlan
            ?? throw new InvalidOperationException("Созданный вид отсутствует.");
        foreach (var (sourceId, createdIds) in copies)
        {
            if (failed.Contains(sourceId)) continue;
            if (createdIds.Count != 1)
            {
                log.Add(LogSeverity.Warning, "Графика аннотации",
                    "Индивидуальная графика не перенесена: результат вставки нельзя сопоставить однозначно. Копии сохранены.",
                    "Подходящие ID из результата Revit: " + string.Join(", ", createdIds.Select(id => id.ToLong())), sourceId,
                    "Сравните копии с исходным элементом и при необходимости настройте их графику вручную.");
                continue;
            }

            var copyId = createdIds[0];
            ViewGraphicsSnapshot? graphics = null;
            try
            {
                if (!log.Run("Графика аннотации", () =>
                {
                    var source = document.GetElement(sourceViewId) as ViewPlan
                        ?? throw new InvalidOperationException("Исходный вид отсутствует.");
                    var original = document.GetElement(ElementIds.Create(sourceId))
                        ?? throw new InvalidOperationException("Исходная аннотация отсутствует.");
                    var copy = document.GetElement(copyId);
                    if (original.OwnerViewId != sourceViewId || copy is null || copy.OwnerViewId != targetId
                        || copy.GetType() != original.GetType() || copy.Category?.Id != original.Category?.Id)
                        throw new InvalidOperationException($"Не удалось подтвердить соответствие копии {copyId.ToLong()} исходной аннотации.");
                    graphics = new ViewGraphicsSnapshot(source, original.Id, copyId);
                    graphics.Apply(Target());
                }, sourceId)) continue;

                log.Check("Проверка графики аннотации", () =>
                {
                    graphics!.Verify(Target());
                    log.Add(LogSeverity.Info, "Графика аннотации", "Индивидуальная графика и скрытие скопированы и проверены.",
                        $"ID копии: {copyId.ToLong()}.", sourceId);
                }, sourceId);
            }
            finally { graphics?.Dispose(); }
        }
    }

    public void Verify(ViewPlan target)
    {
        foreach (var (sourceId, createdIds) in copies)
            if (!createdIds.Any(id => target.Document.GetElement(id)?.OwnerViewId == target.Id)) MarkMissing(sourceId);
    }

    private void MarkMissing(long sourceId)
    {
        if (!failed.Add(sourceId)) return;
        log.Add(LogSeverity.Error, "Проверка вставки", "Аннотация не сохранилась после обработки Revit. Новый вид сохранён.",
            elementId: sourceId, recommendation: "Проверьте исходный элемент и его ссылки; при необходимости вставьте его вручную.");
    }

    public string Report => $"Скопировано аннотаций: {copies.Keys.Count(id => !failed.Contains(id))} из {ids.Count}. Не скопировано: {failed.Count}."
        + (failed.Count == 0 ? "" : " Исходные ID: " + string.Join(", ", failed.Order()));

    private static List<Element> Collect(View view)
    {
        var owned = new FilteredElementCollector(view.Document).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
        var sketchMembers = new FilteredElementCollector(view.Document).OfClass(typeof(Sketch)).Cast<Sketch>()
            .Where(s => view.Document.GetElement(s.OwnerId)?.OwnerViewId == view.Id)
            .SelectMany(s => s.GetAllElements()).Select(id => id.ToLong()).ToHashSet();
        return owned.Where(e => e.ViewSpecific && e.Category is not null
            && e is not (View or Sketch or SketchPlane or SunAndShadowSettings)
            && !IsSunPath(e.Category) && !sketchMembers.Contains(e.Id.ToLong())).ToList();
    }

    private static bool IsSunPath(Category category)
    {
        for (Category? current = category; current is not null; current = current.Parent)
            if (current.Id.ToLong() == (long)BuiltInCategory.OST_SunStudy) return true;
        return false;
    }
}
