using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed partial class ElementTransferService
{
    private sealed record TransferUnit(ElementId Root, List<ElementId> Ids, string? Error = null);

    private List<TransferUnit> PlanUnits(IReadOnlyList<ElementId> ids)
    {
        var result = new List<TransferUnit>();
        var groups = new Dictionary<long, TransferUnit>();
        foreach (var id in ids.DistinctBy(id => id.Value))
        {
            try
            {
                var element = document.GetElement(id);
                var root = element is null ? ElementId.InvalidElementId : GroupTransferStructure.Root(element);
                if (root == ElementId.InvalidElementId) result.Add(new(root, [id]));
                else if (groups.TryGetValue(root.Value, out var unit)) unit.Ids.Add(id);
                else
                {
                    unit = new(root, [id]);
                    groups.Add(root.Value, unit);
                    result.Add(unit);
                }
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { result.Add(new(ElementId.InvalidElementId, [id], ex.Message)); }
        }
        return result;
    }

    private IReadOnlyList<ElementTransferResult> TransferGroup(TransferUnit unit, Func<ElementId, PreparedElementTransfer> prepare,
        Func<bool>? cancelled, Action<int> progress)
    {
        using var atomic = new TransactionGroup(document, L.Get("ReLevel: перенос участников группы"));
        var items = new List<ElementTransferResult>();
        try
        {
            foreach (var id in unit.Ids)
                if (document.GetElement(id) is { } element && GroupTransferStructure.Restriction(element) is { } restriction)
                    return unit.Ids.Select(member => new ElementTransferResult(member.Value, ElementTransferStatus.Skipped, restriction)).ToList();
            var structure = GroupTransferStructure.Capture(document, unit.Root, unit.Ids);
            Require(atomic.Start(), TransactionStatus.Started);
            var ungroupWarnings = GroupStage(() => structure.Ungroup(document));
            var replacements = new Dictionary<long, long>();
            foreach (var id in unit.Ids)
            {
                if (cancelled?.Invoke() == true)
                {
                    RollBackGroup(atomic);
                    return unit.Ids.Select(member => new ElementTransferResult(member.Value, ElementTransferStatus.Skipped,
                        L.Get("Запуск отменён. Все изменения запуска отменены."))).ToList();
                }
                // Reclassify and check writability after UngroupMembers removed the group restrictions.
                var item = TransferOne(id, prepare);
                items.Add(item);
                if (item.Status == ElementTransferStatus.Failed)
                    throw new InvalidOperationException("ID " + id.Value + ": " + item.Reason);
                if (item.ReplacementId is { } replacement) replacements.Add(id.Value, replacement);
                progress(items.Count);
            }
            if (items.All(item => item.Status == ElementTransferStatus.Skipped))
            {
                RollBackGroup(atomic);
                return items;
            }
            var regroupWarnings = GroupStage(() => structure.Regroup(document, replacements));
            Require(atomic.Assimilate(), TransactionStatus.Committed);
            var suffix = "\n" + L.Format($"Группы пересозданы: {structure.Description}.");
            var warnings = string.Join(Environment.NewLine, new[] { ungroupWarnings, regroupWarnings }.Where(text => text.Length > 0));
            if (warnings.Length > 0) suffix += "\n" + L.Get("Предупреждения Revit при перегруппировке:") + "\n" + warnings;
            return items.Select(item => item with { Reason = item.Reason + suffix }).ToList();
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { RollBackGroup(atomic); throw; }
        catch (TransferStoppedException) { RollBackGroup(atomic); throw; }
        catch (Exception ex)
        {
            RollBackGroup(atomic);
            return unit.Ids.Select(id => new ElementTransferResult(id.Value, ElementTransferStatus.Failed,
                L.Format($"Перенос участников группы отменён; исходная группа восстановлена. {ex.Message}"))).ToList();
        }
    }

    private string GroupStage(Action action)
    {
        using var transaction = new Transaction(document, L.Get("ReLevel: разгруппировка / группировка"));
        var failures = new GroupTransferFailures();
        try
        {
            Require(transaction.Start(), TransactionStatus.Started);
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
            action();
            var status = transaction.Commit();
            if (failures.Corrupted) throw new TransferStoppedException(failures.Reason);
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException(L.Format($"Группировка не зафиксирована: {status}. {failures.Reason}"));
            return failures.Reason;
        }
        catch
        {
            RollBack(transaction);
            if (failures.Corrupted) throw new TransferStoppedException(failures.Reason);
            throw;
        }
    }

    private static void RollBackGroup(TransactionGroup group)
    {
        try
        {
            if (group.GetStatus() == TransactionStatus.Started) Require(group.RollBack(), TransactionStatus.RolledBack);
        }
        catch (Exception ex) { throw new TransferStoppedException(L.Format($"Не удалось завершить откат: {ex.Message}"), ex); }
    }
}
