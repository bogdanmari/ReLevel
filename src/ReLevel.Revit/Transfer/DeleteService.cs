using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal enum DeleteScope { Elements, Views }
internal sealed record DeletePreview(ElementId Id, IReadOnlySet<long> DeletedIds, string Description);
internal sealed class DeleteStoppedException(string message, Exception? inner = null) : Exception(message, inner);

internal sealed class DeleteService(Document document, DeleteScope scope)
{
    public DeletePreview Preview(ElementId id)
    {
        var element = document.GetElement(id) ?? throw new InvalidOperationException(L.Get("Объект больше не существует."));
        CheckTarget(element);

        // Read metadata before the trial deletion; rollback invalidates some API wrappers.
        // Revit requires a native filter; include both types and instances for dependency checks.
        var metadata = new FilteredElementCollector(document)
            .WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
            .ToDictionary(e => e.Id.Value, e => (
            Allowed: e is not Level && (e.Id == id ||
                e is not View && (scope == DeleteScope.Elements || e.OwnerViewId == id || e is Viewport vp && vp.ViewId == id)),
            Label: $"{e.Id.Value} — {e.Name} ({e.Category?.Name ?? e.GetType().Name})"));
        using var transaction = Start(L.Get("ReLevel: проверка удаления"));
        HashSet<long> deleted;
        try { deleted = document.Delete(id).Select(i => i.Value).ToHashSet(); }
        finally { RollBack(transaction); }
        if (!deleted.Contains(id.Value)) throw new InvalidOperationException(L.Get("Revit не включил объект в состав удаления."));
        var forbidden = deleted.Where(i => !metadata.TryGetValue(i, out var item) || !item.Allowed).ToList();
        if (forbidden.Count > 0)
            throw new InvalidOperationException(L.Get("Удаление затрагивает уровни, другие виды или внешние зависимости. ID: ") + string.Join(", ", forbidden));
        return new(id, deleted, string.Join(Environment.NewLine, deleted.Order().Select(i => metadata[i].Label)));
    }

    public void Delete(DeletePreview preview)
    {
        var element = document.GetElement(preview.Id) ?? throw new InvalidOperationException(L.Get("Объект уже удалён предыдущей операцией."));
        CheckTarget(element);
        var allowedModified = preview.DeletedIds.ToHashSet();
        if (scope == DeleteScope.Views)
            allowedModified.UnionWith(new FilteredElementCollector(document).OfClass(typeof(Viewport)).Cast<Viewport>()
                .Where(v => v.ViewId == preview.Id).Select(v => v.SheetId.Value));
        var levelIds = new FilteredElementCollector(document).OfClass(typeof(Level)).ToElementIds().Select(i => i.Value).ToHashSet();
        using var group = new TransactionGroup(document, L.Get("ReLevel: удаление"));
        Require(group.Start(), TransactionStatus.Started);
        var unexpected = new HashSet<long>();
        void Changed(object? sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
        {
            if (!args.GetDocument().Equals(document)) return;
            unexpected.UnionWith(args.GetAddedElementIds().Select(i => i.Value));
            unexpected.UnionWith(args.GetDeletedElementIds().Select(i => i.Value).Where(i => !preview.DeletedIds.Contains(i)));
            unexpected.UnionWith(args.GetModifiedElementIds().Select(i => i.Value).Where(i => !allowedModified.Contains(i)));
        }
        try
        {
            var failures = new DeleteFailures();
            using (var transaction = Start(L.Get("ReLevel: удалить объект"), failures))
            {
                try
                {
                    var deleted = document.Delete(preview.Id).Select(i => i.Value).ToHashSet();
                    if (deleted.Overlaps(levelIds) || !deleted.SetEquals(preview.DeletedIds))
                        throw new InvalidOperationException(L.Get("Состав удаления изменился или затронут уровень. Операция отменена."));
                    document.Application.DocumentChanged += Changed;
                    try
                    {
                        var status = transaction.Commit();
                        if (status != TransactionStatus.Committed)
                            throw new InvalidOperationException(failures.Reason ?? L.Format($"Удаление отменено: {status}."));
                    }
                    finally { document.Application.DocumentChanged -= Changed; }
                }
                finally
                {
                    RollBack(transaction);
                    if (failures.Corrupted) throw new DeleteStoppedException(failures.Reason!);
                }
            }
            if (unexpected.Count > 0 || levelIds.Any(i => document.GetElement(new ElementId(i)) is not Level))
                throw new InvalidOperationException(L.Get("Удаление отменено: Revit затронул объекты вне подтверждённого состава. ID: ") + string.Join(", ", unexpected));
            Require(group.Assimilate(), TransactionStatus.Committed);
        }
        catch
        {
            try
            {
                if (group.GetStatus() == TransactionStatus.Started) Require(group.RollBack(), TransactionStatus.RolledBack);
            }
            catch (Exception ex) { throw new DeleteStoppedException(L.Format($"Не удалось завершить откат: {ex.Message}"), ex); }
            throw;
        }
    }

    private Transaction Start(string name, DeleteFailures? failures = null)
    {
        var transaction = new Transaction(document, name);
        try
        {
            Require(transaction.Start(), TransactionStatus.Started);
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(failures ?? new DeleteFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));
            return transaction;
        }
        catch
        {
            try { RollBack(transaction); }
            finally { transaction.Dispose(); }
            throw;
        }
    }

    private void CheckTarget(Element element)
    {
        if (element is Level) throw new InvalidOperationException(L.Get("Удаление уровней запрещено."));
        if (scope == DeleteScope.Views && element is not View)
            throw new InvalidOperationException(L.Get("Удаление доступно только для видов."));
        if (scope == DeleteScope.Elements && element is View)
            throw new InvalidOperationException(L.Get("Используйте вкладку «Виды»."));
        if (element is View view && (view.GetDependentViewIds().Count > 0 || view.GetPrimaryViewId() != ElementId.InvalidElementId))
            throw new InvalidOperationException(L.Get("Удаление зависимого вида или вида с зависимыми видами не поддерживается."));
    }

    private static void RollBack(Transaction transaction)
    {
        try
        {
            var status = transaction.GetStatus();
            if (status == TransactionStatus.Started) Require(transaction.RollBack(), TransactionStatus.RolledBack);
            else if (status == TransactionStatus.Pending)
                throw new InvalidOperationException(L.Get("Revit не завершил обработку ошибок транзакции."));
        }
        catch (Exception ex) { throw new DeleteStoppedException(L.Format($"Не удалось завершить откат: {ex.Message}"), ex); }
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException(L.Format($"Статус транзакции: {actual}; ожидался {expected}."));
    }
}

internal sealed class DeleteFailures : IFailuresPreprocessor
{
    public string? Reason { get; private set; }
    public bool Corrupted { get; private set; }
    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var failures = accessor.GetFailureMessages();
        if (failures.Count == 0) return FailureProcessingResult.Continue;
        Reason = string.Join(Environment.NewLine, failures.Select(f => f.GetDescriptionText()));
        Corrupted |= failures.Any(f => f.GetSeverity() == FailureSeverity.DocumentCorruption);
        // A warning rejects this deletion attempt.
        return FailureProcessingResult.ProceedWithRollBack;
    }
}
