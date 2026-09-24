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
        using var batch = new TransactionGroup(document, "ReLevel: пересоздать виды");
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
                report.Results.Items.Add(new(id.Value, id.Value.ToString(), TransferStatus.Failed, "Пакет отменён из-за критической ошибки Revit."));
            report.Entries.Add(new(LogSeverity.Error, "Операция отменена", 0, null, null,
                "Revit не позволил безопасно продолжить. Все изменения этого запуска отменены; ранее записанные ID новых видов недействительны.",
                ex.ToString(), "Сохраните журнал. При сбое регенерации закройте команду и проверьте состояние документа."));
            report.CriticalFailure = true;
        }
        return report;
    }

    private TransferResult RecreateOne(ElementId sourceId, ElementId targetLevelId, string prefix,
        ViewRecreationReport report, HashSet<long> originalIds)
    {
        var log = new ViewOperationLog(document, report, sourceId.Value, originalIds);
        var name = sourceId.Value.ToString();
        ViewPlan source;
        try
        {
            source = document.GetElement(sourceId) as ViewPlan ?? throw new NotSupportedException("Выбранный объект не является планом.");
            name = source.Name;
            if (source.IsTemplate || source.ViewType is not (ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan))
                throw new NotSupportedException("Поддерживаются только планы этажей, потолков и конструкций.");
            if (source.GetPrimaryViewId() != ElementId.InvalidElementId || source.GetDependentViewIds().Count > 0)
                throw new NotSupportedException("Зависимые виды и виды с зависимыми видами пока не поддерживаются.");
            if (source.GenLevel is null || document.GetElement(targetLevelId) is not Level || source.GenLevel.Id == targetLevelId)
                throw new NotSupportedException("Нужен другой существующий целевой уровень.");
            if (new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                .Any(v => string.Equals(v.Name, prefix + name, StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException($"Имя «{prefix + name}» уже занято. Укажите другой префикс.");
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            log.Add(LogSeverity.Error, "Подготовка", ex.Message, ex.ToString(), recommendation: "Исправьте указанную причину и повторите создание.");
            return new(sourceId.Value, name, TransferStatus.Skipped, ex.Message);
        }

        var newId = ElementId.InvalidElementId;
        var newName = prefix + name;
        if (!log.Run("Создание вида", () =>
        {
            var target = ViewPlan.Create(document, source.GetTypeId(), targetLevelId);
            target.Name = newName;
            newId = target.Id;
        })) return new(sourceId.Value, name, TransferStatus.Failed, "Вид не создан. Подробности в журнале.");

        log.CreatedId = newId.Value;
        log.Add(LogSeverity.Info, "Создание вида", $"Создан «{newName}», ID {newId.Value}.",
            $"Исходный вид: {name}; целевой уровень: {targetLevelId.Value}.");
        ViewPlan Source() => (ViewPlan)document.GetElement(sourceId);
        ViewPlan Target() => document.GetElement(newId) as ViewPlan ?? throw new InvalidOperationException("Созданный вид отсутствует.");

        ViewSettingsSnapshot? settings = null;
        if (log.Run("Настройки вида", () =>
        {
            settings = new ViewSettingsSnapshot(Source());
            settings.Apply(Source(), Target());
        })) log.Check("Проверка настроек", () => settings!.Verify(Target()));

        ViewGraphicsSnapshot? graphics = null;
        try
        {
            if (log.Run("Графика и фильтры", () =>
            {
                graphics = new ViewGraphicsSnapshot(Source());
                graphics.Apply(Target());
            })) log.Check("Проверка графики", () => graphics!.Verify(Target()));
        }
        finally { graphics?.Dispose(); }

        ViewAnnotations? annotations = null;
        try { annotations = new ViewAnnotations(Source(), log); }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            log.Add(LogSeverity.Error, "Подготовка аннотаций", "Не удалось собрать аннотации. Вид сохранён без их копирования.", ex.ToString());
        }
        if (annotations is not null)
        {
            annotations.CopyTo(Target());
            log.Check("Наличие аннотаций", () => annotations.Verify(Target()));
            log.Add(LogSeverity.Info, "Итог копирования", annotations.Report);
        }
        var issues = report.Entries.Count(e => e.SourceViewId == sourceId.Value && e.Severity != LogSeverity.Info);
        return new(sourceId.Value, name, TransferStatus.Transferred,
            $"Создан «{newName}», ID {newId.Value}. Замечаний и ошибок этапов: {issues}. См. журнал.");
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException($"Статус транзакции: {actual}; ожидался {expected}.");
    }
}
