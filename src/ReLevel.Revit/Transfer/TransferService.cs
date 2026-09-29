using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class TransferService(UIApplication application)
{
    public TransferReport Execute(IReadOnlyList<TransferPlan> plans, Document document, TransferContext context)
    {
        var report = new TransferReport();
        if (plans.Any(p => p.Context != context))
            throw new InvalidOperationException(L.Get("Контекст планов не совпадает с контекстом операции."));
        using var batch = new TransactionGroup(document, "ReLevel");
        Require(batch.Start(), TransactionStatus.Started);
        try
        {
            foreach (var plan in plans)
            {
                if (!plan.Ready)
                {
                    report.Items.Add(new(plan.Id.Value, plan.Name, TransferStatus.Skipped, plan.Reason ?? L.Get("Не поддерживается")));
                    continue;
                }
                report.Items.Add(TransferOne(document, plan));
            }
            Require(batch.Assimilate(), TransactionStatus.Committed);
        }
        catch (Exception ex)
        {
            report.CriticalFailure = true;
            if (batch.GetStatus() == TransactionStatus.Started) Require(batch.RollBack(), TransactionStatus.RolledBack);
            // A critical error invalidates all previous successes, including committed inner transactions.
            report.Items.Clear();
            foreach (var plan in plans)
                report.Items.Add(new(plan.Id.Value, plan.Name, plan.Ready ? TransferStatus.Failed : TransferStatus.Skipped,
                    plan.Ready ? L.Format($"Вся операция отменена: {ex.Message}") : plan.Reason ?? L.Get("Не поддерживается")));
        }
        return report;
    }

    private TransferResult TransferOne(Document document, TransferPlan plan)
    {
        using var attempt = new TransactionGroup(document, $"ReLevel {plan.Id.Value}");
        Require(attempt.Start(), TransactionStatus.Started);
        try
        {
            var element = document.GetElement(plan.Id) ?? throw new InvalidOperationException(L.Get("Элемент больше не существует."));
            var current = new TransferAnalyzer().Analyze(element, plan.Context);
            if (!current.Ready)
            {
                Require(attempt.RollBack(), TransactionStatus.RolledBack);
                return new(plan.Id.Value, plan.Name, TransferStatus.Skipped, current.Reason!);
            }
            if (!current.Dependencies.OrderBy(d => d.Id.Value).SequenceEqual(plan.Dependencies.OrderBy(d => d.Id.Value))
                || !current.Bindings.Select(b => b.Parameters).SequenceEqual(plan.Bindings.Select(b => b.Parameters)))
                throw new InvalidOperationException(L.Get("Состав привязок или зависимостей изменился после подготовки; повторите операцию."));
            var snapshot = new TransferSnapshot(element, current);
            var failures = new RollBackFailures();
            var unexpectedChanges = new HashSet<long>();
            void Changed(object? sender, DocumentChangedEventArgs args)
            {
                if (!args.GetDocument().Equals(document)) return;
                foreach (var id in args.GetAddedElementIds().Concat(args.GetDeletedElementIds())) unexpectedChanges.Add(id.Value);
                foreach (var id in args.GetModifiedElementIds())
                    if (!snapshot.AllowsModification(id)) unexpectedChanges.Add(id.Value);
            }

            using (var transaction = new Transaction(document, L.Get("ReLevel: уровень и смещение")))
            {
                Require(transaction.Start(), TransactionStatus.Started);
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
                // All changed constraints and compensations precede the first regeneration.
                foreach (var binding in current.Bindings.Where(b => b.Changes))
                    if (!element.get_Parameter(binding.Parameters.Level).Set(binding.ResultLevelId))
                        throw new InvalidOperationException(L.Get("Revit отклонил изменение параметра."));
                foreach (var binding in current.Bindings.Where(b => b.Changes))
                {
                    var offset = element.get_Parameter(binding.Parameters.Offset);
                    if (!offset.IsReadOnly && offset.AsDouble() != binding.ResultOffset && !offset.Set(binding.ResultOffset))
                        throw new InvalidOperationException(L.Get("Revit отклонил изменение параметра."));
                    if (binding.Parameters.SecondOffset is { } secondId)
                    {
                        var second = element.get_Parameter(secondId);
                        if (!second.IsReadOnly && second.AsDouble() != binding.ResultSecondOffset!.Value && !second.Set(binding.ResultSecondOffset.Value))
                            throw new InvalidOperationException(L.Get("Revit отклонил изменение параметра."));
                    }
                }
                document.Regenerate();
                snapshot.Verify(document);
                application.Application.DocumentChanged += Changed;
                TransactionStatus status;
                try { status = transaction.Commit(); }
                finally { application.Application.DocumentChanged -= Changed; }
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException(failures.Reason ?? L.Format($"Транзакция завершена со статусом {status}."));
            }

            // Commit can trigger joins, constraints, updaters and failure processing. Validate again.
            if (unexpectedChanges.Count > 0)
                throw new InvalidOperationException(L.Get("Revit затронул другие элементы или добавил/удалил элементы; откат. Id: ")
                    + string.Join(", ", unexpectedChanges.Order().Take(20)));
            snapshot.Verify(document);
            Require(attempt.Assimilate(), TransactionStatus.Committed);
            return new(plan.Id.Value, plan.Name, TransferStatus.Transferred, L.Get("Уровень изменён, положение проверено.")
                + L.Format($" Проверено зависимостей: {current.Dependencies.Count}."));
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
        {
            // Do not read the document after a regeneration failure. Abort the entire batch.
            if (attempt.GetStatus() == TransactionStatus.Started) Require(attempt.RollBack(), TransactionStatus.RolledBack);
            throw;
        }
        catch (Exception ex)
        {
            if (attempt.GetStatus() == TransactionStatus.Started) Require(attempt.RollBack(), TransactionStatus.RolledBack);
            return new(plan.Id.Value, plan.Name, TransferStatus.Failed, ex.Message);
        }
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException(L.Format($"Ошибка транзакции: {actual}, ожидалось {expected}."));
    }
}

internal sealed class RollBackFailures : IFailuresPreprocessor
{
    public string? Reason { get; private set; }
    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var failures = accessor.GetFailureMessages();
        if (failures.Count == 0) return FailureProcessingResult.Continue;
        Reason = string.Join(Environment.NewLine, failures.Select(f => f.GetDescriptionText()));
        // Warnings may indicate lost constraints too. Never auto-resolve or delete them.
        return FailureProcessingResult.ProceedWithRollBack;
    }
}
