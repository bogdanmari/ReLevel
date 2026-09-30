using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class ElementTransferService(Document document)
{
    public ElementTransferReport Execute(IReadOnlyList<ElementId> ids, ElementId targetLevelId, ElementId sourceLevelId)
    {
        var report = new ElementTransferReport();
        foreach (var id in ids)
        {
            if (report.Stopped)
            {
                report.Items.Add(new(id.Value, ElementTransferStatus.Skipped, L.Get("Не обработан: выполнение остановлено.")));
                continue;
            }
            try { report.Items.Add(TransferOne(id, targetLevelId, sourceLevelId)); }
            catch (Exception ex)
            {
                // An escaped regeneration/rollback error must not be followed by model reads.
                report.Stopped = true;
                report.Items.Add(new(id.Value, ElementTransferStatus.Failed,
                    L.Format($"Обработка остановлена: {ex.Message}")));
            }
        }
        return report;
    }

    private ElementTransferResult TransferOne(ElementId id, ElementId targetLevelId, ElementId sourceLevelId)
    {
        ElementTransferResult Result(ElementTransferStatus status, string reason) => new(id.Value, status, reason);
        IElementTransferOperation operation;
        try
        {
            var element = document.GetElement(id);
            if (element is null) return Result(ElementTransferStatus.Skipped, L.Get("Объект больше не существует."));
            var transferCase = TransferCases.Find(element);
            if (transferCase is null) return Result(ElementTransferStatus.Skipped, L.Get("Элемент не соответствует реализованным кейсам."));
            if (!transferCase.IsOnLevel(element, sourceLevelId))
                return Result(ElementTransferStatus.Skipped, L.Get("Исходный уровень экземпляра изменился. Обновите таблицу."));
            if (document.GetElement(targetLevelId) is not Level)
                return Result(ElementTransferStatus.Skipped, L.Get("Целевой уровень больше не существует."));
            if (sourceLevelId == targetLevelId)
                return Result(ElementTransferStatus.Skipped, L.Get("Экземпляр уже принадлежит целевому уровню; изменений нет."));
            if (transferCase.WriteRestriction(element, sourceLevelId) is { } restriction)
                return Result(ElementTransferStatus.Skipped, restriction);
            operation = transferCase.Prepare(element, sourceLevelId);
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex) { return Result(ElementTransferStatus.Failed, ex.Message); }

        using var transaction = new Transaction(document, L.Get("ReLevel: перенос элемента"));
        var failures = new TransferFailures();
        try
        {
            Require(transaction.Start(), TransactionStatus.Started);
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
            operation.Apply(document, targetLevelId);
            var status = transaction.Commit();
            if (failures.Corrupted) throw new TransferStoppedException(failures.Reason);
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException(failures.Reason ?? L.Format($"Перенос не зафиксирован: {status}."));
            return Result(ElementTransferStatus.Transferred, operation.SuccessMessage);
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
        {
            RollBack(transaction);
            throw;
        }
        catch (TransferStoppedException)
        {
            RollBack(transaction);
            throw;
        }
        catch (Exception ex)
        {
            RollBack(transaction);
            if (failures.Corrupted) throw new TransferStoppedException(failures.Reason);
            return Result(ElementTransferStatus.Failed, ex.Message);
        }
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
        catch (Exception ex) { throw new TransferStoppedException(L.Format($"Не удалось завершить откат: {ex.Message}"), ex); }
    }

    private static void Require(TransactionStatus actual, TransactionStatus expected)
    {
        if (actual != expected) throw new InvalidOperationException(L.Format($"Статус транзакции: {actual}; ожидался {expected}."));
    }

    private sealed class TransferStoppedException(string? message, Exception? inner = null) : Exception(message, inner);

    private sealed class TransferFailures : IFailuresPreprocessor
    {
        public string? Reason { get; private set; }
        public bool Corrupted { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var errors = accessor.GetFailureMessages().Where(f => f.GetSeverity() != FailureSeverity.Warning).ToList();
            if (errors.Count == 0) return FailureProcessingResult.Continue; // Warnings retain Revit's normal handling.
            Reason = string.Join(Environment.NewLine, errors.Select(f => f.GetDescriptionText()));
            Corrupted |= errors.Any(f => f.GetSeverity() == FailureSeverity.DocumentCorruption);
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
