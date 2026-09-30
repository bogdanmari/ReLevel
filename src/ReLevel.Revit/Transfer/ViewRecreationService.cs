using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class ViewRecreationService(Document document)
{
    public ViewRecreationReport Execute(IReadOnlyList<ElementId> sourceIds, ElementId targetLevelId, string prefix)
    {
        var report = new ViewRecreationReport();
        using var allElements = new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true));
        var originalIds = new FilteredElementCollector(document).WherePasses(allElements)
            .ToElementIds().Select(id => id.Value).ToHashSet();
        using var batch = new TransactionGroup(document, L.Get("ReLevel: пересоздать виды"));
        Require(batch.Start(), TransactionStatus.Started);
        try
        {
            foreach (var id in sourceIds)
                report.Results.Items.Add(RecreateOne(id, targetLevelId, prefix, report, originalIds));
            Require(batch.Assimilate(), TransactionStatus.Committed);
        }
        catch (Exception ex)
        {
            if (batch.GetStatus() == TransactionStatus.Started) Require(batch.RollBack(), TransactionStatus.RolledBack);
            report.Results.Items.Clear();
            foreach (var id in sourceIds)
                report.Results.Items.Add(new(id.Value, id.Value.ToString(), ViewResultStatus.Failed, L.Get("Пакет отменён из-за критической ошибки Revit.")));
            report.Entries.Add(new(LogSeverity.Error, L.Get("Операция отменена"), 0, null, null,
                L.Get("Revit не позволил безопасно продолжить. Все изменения этого запуска отменены; ранее записанные ID новых видов недействительны."),
                ex.ToString(), L.Get("Сохраните журнал. При сбое регенерации закройте команду и проверьте состояние документа.")));
            report.CriticalFailure = true;
        }
        return report;
    }

    private ViewResult RecreateOne(ElementId sourceId, ElementId targetLevelId, string prefix,
        ViewRecreationReport report, HashSet<long> originalIds)
    {
        var log = new ViewOperationLog(document, report, sourceId.Value, originalIds);
        var name = sourceId.Value.ToString();
        ViewPlan source;
        try
        {
            source = document.GetElement(sourceId) as ViewPlan ?? throw new NotSupportedException(L.Get("Выбранный объект не является планом."));
            name = source.Name;
            if (ViewRecreationSupport.UnsupportedReason(source) is { } unsupported)
                throw new NotSupportedException(unsupported);
            if (source.GenLevel is null || document.GetElement(targetLevelId) is not Level || source.GenLevel.Id == targetLevelId)
                throw new NotSupportedException(L.Get("Нужен другой существующий целевой уровень."));
            if (new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                .Any(v => string.Equals(v.Name, prefix + name, StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException(L.Format($"Имя «{prefix + name}» уже занято. Укажите другой префикс."));
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            log.Add(LogSeverity.Error, L.Get("Подготовка"), ex.Message, ex.ToString(), recommendation: L.Get("Исправьте указанную причину и повторите создание."));
            return new(sourceId.Value, name, ViewResultStatus.Skipped, ex.Message);
        }

        var newId = ElementId.InvalidElementId;
        var newName = prefix + name;
        if (!log.Run(L.Get("Создание вида"), () =>
        {
            var target = ViewPlan.Create(document, source.GetTypeId(), targetLevelId);
            target.Name = newName;
            newId = target.Id;
        })) return new(sourceId.Value, name, ViewResultStatus.Failed, L.Get("Вид не создан. Подробности в журнале."));

        log.CreatedId = newId.Value;
        log.Add(LogSeverity.Info, L.Get("Создание вида"), L.Format($"Создан «{newName}», ID {newId.Value}."),
            L.Format($"Исходный вид: {name}; целевой уровень: {targetLevelId.Value}."));
        ViewPlan Source() => (ViewPlan)document.GetElement(sourceId);
        ViewPlan Target() => document.GetElement(newId) as ViewPlan ?? throw new InvalidOperationException(L.Get("Созданный вид отсутствует."));

        ViewSettingsSnapshot? settings = null;
        if (log.Run(L.Get("Настройки вида"), () =>
        {
            settings = new ViewSettingsSnapshot(Source());
            settings.Apply(Source(), Target());
        })) log.Check(L.Get("Проверка настроек"), () => settings!.Verify(Target()));

        ViewAnnotations? annotations = null;
        try { annotations = new ViewAnnotations(Source(), log); }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            log.Add(LogSeverity.Error, L.Get("Подготовка аннотаций"), L.Get("Не удалось собрать аннотации. Вид сохранён без их копирования."), ex.ToString());
        }
        if (annotations is not null)
        {
            annotations.CopyTo(Target());
            log.Check(L.Get("Наличие аннотаций"), () => annotations.Verify(Target()));
        }
        ViewGraphicsSnapshot? graphics = null;
        try
        {
            if (log.Run(L.Get("Графика элементов"), () =>
            {
                graphics = new ViewGraphicsSnapshot(Source());
                graphics.Apply(Target());
            })) log.Check(L.Get("Проверка графики элементов"), () => graphics!.Verify(Target()));
        }
        finally { graphics?.Dispose(); }
        if (annotations is not null)
        {
            annotations.CopyGraphicsTo(Target());
            log.Check(L.Get("Наличие аннотаций после переноса графики"), () => annotations.Verify(Target()));
            log.Add(LogSeverity.Info, L.Get("Итог копирования"), annotations.Report);
        }
        var issues = report.Entries.Count(e => e.SourceViewId == sourceId.Value && e.Severity != LogSeverity.Info);
        return new(sourceId.Value, name, ViewResultStatus.Created,
            L.Format($"Создан «{newName}», ID {newId.Value}. Замечаний и ошибок этапов: {issues}. См. журнал."));
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException(L.Format($"Статус транзакции: {actual}; ожидался {expected}."));
    }
}
