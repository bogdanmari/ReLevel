using Autodesk.Revit.DB;
using ReLevel.Revit.Transfer;

namespace ReLevel.Revit;

internal sealed record DeletePreview(ElementId Id, ISet<long> DeletedIds, string Description);

internal sealed class DeleteService(Document document)
{
    public DeletePreview Preview(ElementId id)
    {
        var element = document.GetElement(id) ?? throw new InvalidOperationException("Объект больше не существует.");
        if (element is Level) throw new InvalidOperationException("Удаление уровней запрещено.");
        if (element is View view && (view.GetDependentViewIds().Count > 0 || view.GetPrimaryViewId() != ElementId.InvalidElementId))
            throw new InvalidOperationException("Удаление зависимого вида или вида с зависимыми видами не поддерживается.");

        // Read metadata before the trial deletion; rollback invalidates some API wrappers.
        // Revit requires a native filter; include both types and instances for dependency checks.
        var metadata = new FilteredElementCollector(document)
            .WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
            .ToDictionary(e => e.Id.ToLong(), e => (
            Allowed: e is not Level && (e.Id == id || (element is View
                ? e is not View && (e.OwnerViewId == id || e is Viewport vp && vp.ViewId == id)
                : e is not View)),
            Label: $"{e.Id.ToLong()} — {e.Name} ({e.Category?.Name ?? e.GetType().Name})"));
        using var transaction = Start("ReLevel: проверка удаления");
        var deleted = document.Delete(id).Select(i => i.ToLong()).ToHashSet();
        Require(transaction.RollBack(), TransactionStatus.RolledBack);
        if (!deleted.Contains(id.ToLong())) throw new InvalidOperationException("Revit не включил объект в состав удаления.");
        var forbidden = deleted.Where(i => !metadata.TryGetValue(i, out var item) || !item.Allowed).ToList();
        if (forbidden.Count > 0)
            throw new InvalidOperationException("Удаление затрагивает уровни, другие виды или внешние зависимости. ID: " + string.Join(", ", forbidden));
        return new(id, deleted, string.Join(Environment.NewLine, deleted.Order().Select(i => metadata[i].Label)));
    }

    public void Delete(DeletePreview preview)
    {
        var element = document.GetElement(preview.Id) ?? throw new InvalidOperationException("Объект уже удалён предыдущей операцией.");
        if (element is Level) throw new InvalidOperationException("Удаление уровней запрещено.");
        var allowedModified = preview.DeletedIds.ToHashSet();
        if (element is View)
            allowedModified.UnionWith(new FilteredElementCollector(document).OfClass(typeof(Viewport)).Cast<Viewport>()
                .Where(v => v.ViewId == preview.Id).Select(v => v.SheetId.ToLong()));
        var levelIds = new FilteredElementCollector(document).OfClass(typeof(Level)).ToElementIds().Select(i => i.ToLong()).ToHashSet();
        using var group = new TransactionGroup(document, "ReLevel: удаление");
        Require(group.Start(), TransactionStatus.Started);
        var unexpected = new HashSet<long>();
        void Changed(object? sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
        {
            if (!args.GetDocument().Equals(document)) return;
            unexpected.UnionWith(args.GetAddedElementIds().Select(i => i.ToLong()));
            unexpected.UnionWith(args.GetDeletedElementIds().Select(i => i.ToLong()).Where(i => !preview.DeletedIds.Contains(i)));
            unexpected.UnionWith(args.GetModifiedElementIds().Select(i => i.ToLong()).Where(i => !allowedModified.Contains(i)));
        }
        try
        {
            using (var transaction = Start("ReLevel: удалить объект"))
            {
                var deleted = document.Delete(preview.Id).Select(i => i.ToLong()).ToHashSet();
                if (deleted.Overlaps(levelIds) || !deleted.SetEquals(preview.DeletedIds))
                    throw new InvalidOperationException("Состав удаления изменился или затронут уровень. Операция отменена.");
                document.Application.DocumentChanged += Changed;
                try
                {
                    var status = transaction.Commit();
                    if (status != TransactionStatus.Committed)
                        throw new InvalidOperationException((transaction.GetFailureHandlingOptions().GetFailuresPreprocessor() as RollBackFailures)?.Reason
                            ?? $"Удаление отменено: {status}.");
                }
                finally { document.Application.DocumentChanged -= Changed; }
            }
            if (unexpected.Count > 0 || levelIds.Any(i => document.GetElement(ElementIds.Create(i)) is not Level))
                throw new InvalidOperationException("Удаление отменено: Revit затронул объекты вне подтверждённого состава. ID: " + string.Join(", ", unexpected));
            Require(group.Assimilate(), TransactionStatus.Committed);
        }
        catch
        {
            if (group.GetStatus() == TransactionStatus.Started) Require(group.RollBack(), TransactionStatus.RolledBack);
            throw;
        }
    }

    private Transaction Start(string name)
    {
        var transaction = new Transaction(document, name);
        try
        {
            Require(transaction.Start(), TransactionStatus.Started);
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new RollBackFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));
            return transaction;
        }
        catch { transaction.Dispose(); throw; }
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException($"Статус транзакции: {actual}; ожидался {expected}.");
    }
}
