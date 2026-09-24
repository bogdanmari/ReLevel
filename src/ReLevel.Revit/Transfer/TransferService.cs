using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class TransferService(UIApplication application)
{
    public TransferReport Execute(IReadOnlyList<TransferPlan> plans, Level target)
    {
        var document = target.Document;
        var report = new TransferReport();
        using var batch = new TransactionGroup(document, "ReLevel");
        Require(batch.Start(), TransactionStatus.Started);
        try
        {
            foreach (var plan in plans)
            {
                if (!plan.Ready)
                {
                    report.Items.Add(new(plan.Id.Value, plan.Name, TransferStatus.Skipped, plan.Reason ?? "Не поддерживается"));
                    continue;
                }
                report.Items.Add(TransferOne(document, plan, target));
            }
            Require(batch.Assimilate(), TransactionStatus.Committed);
        }
        catch (Exception ex)
        {
            if (batch.GetStatus() == TransactionStatus.Started) Require(batch.RollBack(), TransactionStatus.RolledBack);
            // A critical error invalidates all previous successes, including committed inner transactions.
            report.Items.Clear();
            foreach (var plan in plans)
                report.Items.Add(new(plan.Id.Value, plan.Name, plan.Ready ? TransferStatus.Failed : TransferStatus.Skipped,
                    plan.Ready ? $"Вся операция отменена: {ex.Message}" : plan.Reason ?? "Не поддерживается"));
        }
        return report;
    }

    private TransferResult TransferOne(Document document, TransferPlan plan, Level target)
    {
        using var attempt = new TransactionGroup(document, $"ReLevel {plan.Id.Value}");
        Require(attempt.Start(), TransactionStatus.Started);
        try
        {
            var element = document.GetElement(plan.Id) ?? throw new InvalidOperationException("Элемент больше не существует.");
            var current = new TransferAnalyzer().Analyze(element, target);
            if (!current.Ready)
            {
                Require(attempt.RollBack(), TransactionStatus.RolledBack);
                return new(plan.Id.Value, plan.Name, TransferStatus.Skipped, current.Reason!);
            }
            var strategy = current.Strategy!;
            var snapshot = GeometrySnapshot.Capture(element);
            var source = (Level)document.GetElement(element.get_Parameter(strategy.LevelParameter).AsElementId());
            var offset = LevelTransfer.NewOffset(source.ProjectElevation, target.ProjectElevation,
                element.get_Parameter(strategy.OffsetParameter).AsDouble());
            var failures = new RollBackFailures();
            var unexpectedChanges = new HashSet<long>();
            void Changed(object? sender, DocumentChangedEventArgs args)
            {
                if (!args.GetDocument().Equals(document)) return;
                foreach (var id in args.GetAddedElementIds().Concat(args.GetDeletedElementIds())) unexpectedChanges.Add(id.Value);
                foreach (var id in args.GetModifiedElementIds())
                    if (id != plan.Id) unexpectedChanges.Add(id.Value);
            }

            using (var transaction = new Transaction(document, "ReLevel: уровень и смещение"))
            {
                Require(transaction.Start(), TransactionStatus.Started);
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
                // No intermediate regeneration: level and compensating offset form one atomic change.
                if (!element.get_Parameter(strategy.LevelParameter).Set(target.Id)
                    || !element.get_Parameter(strategy.OffsetParameter).Set(offset))
                    throw new InvalidOperationException("Revit отклонил изменение параметра.");
                document.Regenerate();
                Verify(element, strategy, target, offset, snapshot);
                application.Application.DocumentChanged += Changed;
                TransactionStatus status;
                try { status = transaction.Commit(); }
                finally { application.Application.DocumentChanged -= Changed; }
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException(failures.Reason ?? $"Транзакция завершена со статусом {status}.");
            }

            // Commit can trigger joins, constraints, updaters and failure processing. Validate again.
            if (unexpectedChanges.Count > 0)
                throw new InvalidOperationException("Revit затронул другие элементы или добавил/удалил элементы; откат. Id: "
                    + string.Join(", ", unexpectedChanges.Order().Take(20)));
            Verify(document.GetElement(plan.Id), strategy, target, offset, snapshot);
            Require(attempt.Assimilate(), TransactionStatus.Committed);
            return new(plan.Id.Value, plan.Name, TransferStatus.Transferred, "Уровень изменён, положение проверено.");
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

    private static void Verify(Element e, ITransferStrategy strategy, Level target, double offset, GeometrySnapshot snapshot)
    {
        var actualOffset = e.get_Parameter(strategy.OffsetParameter).AsDouble();
        if (e.get_Parameter(strategy.LevelParameter).AsElementId() != target.Id
            || !double.IsFinite(actualOffset) || Math.Abs(actualOffset - offset) > GeometrySnapshot.Tolerance)
            throw new InvalidOperationException("Целевой уровень или смещение не совпадают с расчётом.");
        snapshot.Verify(e);
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException($"Ошибка транзакции: {actual}, ожидалось {expected}.");
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
