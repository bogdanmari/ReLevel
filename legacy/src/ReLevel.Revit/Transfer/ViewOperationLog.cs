using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

// Used only by view recreation. Transfer and deletion retain their own failure policies.
internal sealed class ViewOperationLog(Document document, ViewRecreationReport report, long sourceId, HashSet<long> originalIds)
{
    public long? CreatedId { get; set; }

    public void Add(LogSeverity severity, string stage, string message, string details = "", long? elementId = null,
        string recommendation = "") => report.Entries.Add(new(severity, stage, sourceId, CreatedId, elementId, message, details, recommendation));

    public bool Run(string stage, Action action, long? elementId = null)
    {
        var failures = new ViewFailures(this, stage, elementId);
        using var transaction = new Transaction(document, "ReLevel: " + stage);
        if (transaction.Start() != TransactionStatus.Started)
            throw new InvalidOperationException("Revit не смог начать транзакцию: " + stage);
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
        var modified = new HashSet<long>();
        var deleted = new HashSet<long>();
        void Changed(object? sender, DocumentChangedEventArgs args)
        {
            if (!args.GetDocument().Equals(document) || args.Operation != UndoOperation.TransactionCommitted) return;
            modified.UnionWith(args.GetModifiedElementIds().Select(id => id.ToLong()).Where(originalIds.Contains));
            deleted.UnionWith(args.GetDeletedElementIds().Select(id => id.ToLong()).Where(originalIds.Contains));
        }
        try
        {
            action();
            document.Regenerate();
            document.Application.DocumentChanged += Changed;
            TransactionStatus status;
            try { status = transaction.Commit(); }
            finally { document.Application.DocumentChanged -= Changed; }
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException(failures.Reason ?? $"Статус фиксации: {status}.");
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started && transaction.RollBack() != TransactionStatus.RolledBack)
                throw new InvalidOperationException("Revit не смог откатить этап: " + stage, ex);
            if (transaction.GetStatus() != TransactionStatus.RolledBack) throw;
            Add(LogSeverity.Error, stage, CreatedId is null
                    ? "Вид не создан: Revit отклонил операцию."
                        + ReadableReason(ex.Message)
                    : "Этап не выполнен. Его изменения отменены; ранее созданный вид сохранён." + ReadableReason(ex.Message),
                ex.ToString(), elementId, "Проверьте технические подробности. Исправьте причину в модели и повторите операцию с другим префиксом.");
            return false;
        }
        var trackers = modified.Order().Select(id => document.GetElement(ElementIds.Create(id)))
            .Where(IsKnownTracker).ToList();
        modified.ExceptWith(trackers.Select(e => e!.Id.ToLong()));
        if (trackers.Count > 0)
            Add(LogSeverity.Info, stage, "Revit обновил служебные объекты отслеживания.",
                string.Join("\n", trackers.Select(e => $"ID: {e!.Id.ToLong()}; имя: {e.Name}; класс: {e.GetType().Name}; категория: <null>")), elementId);
        if (modified.Count > 0 || deleted.Count > 0)
            Add(LogSeverity.Warning, stage, "Revit изменил существующие объекты. Результат сохранён для проверки.",
                $"Изменены ID: {string.Join(", ", modified.Order())}\nУдалены ID: {string.Join(", ", deleted.Order())}", elementId,
                "Проверьте перечисленные объекты. При нежелательных изменениях используйте Undo.");
        return true;
    }

    // These internal elements have no dedicated public API class. Match only the
    // observed names and shape; unknown elements and all deletions remain warnings.
    private static bool IsKnownTracker(Element? element) => element is not null
        && element.GetType() == typeof(Element) && element.Category is null && !element.ViewSpecific
        && (string.Equals(element.Name, "Autojoin Tracker Element", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name, "GCS Tracker", StringComparison.OrdinalIgnoreCase));

    public void Check(string stage, Action check, long? elementId = null)
    {
        try { check(); }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            Add(LogSeverity.Warning, stage, "Проверка выявила отличие или не смогла завершиться. Вид сохранён." + ReadableReason(ex.Message), ex.ToString(),
                elementId, "Откройте новый вид и проверьте указанные настройки вручную.");
        }
    }

    private static string ReadableReason(string message) => message.Any(c => c >= 'А' && c <= 'я') ? " " + message : " См. сообщение Revit в подробностях.";

    private sealed class ViewFailures(ViewOperationLog log, string stage, long? elementId) : IFailuresPreprocessor
    {
        private readonly HashSet<string> recorded = [];
        public string? Reason { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var hasErrors = false;
            foreach (var failure in accessor.GetFailureMessages())
            {
                var warning = failure.GetSeverity() == FailureSeverity.Warning;
                var description = failure.GetDescriptionText();
                var details = $"{description}\nКод Revit: {failure.GetFailureDefinitionId().Guid}\n"
                    + $"Связанные ID Revit: {string.Join(", ", failure.GetFailingElementIds().Select(id => id.ToLong()))}";
                var message = description.Contains("lost", StringComparison.OrdinalIgnoreCase)
                    && description.Contains("References", StringComparison.OrdinalIgnoreCase)
                    ? "При вставке размер потерял часть ссылок на модель."
                    : warning ? "Revit сообщил о проблеме, допускающей сохранение результата." : "Revit не разрешил завершить этот этап.";
                if (recorded.Add(details))
                    log.Add(warning ? LogSeverity.Warning : LogSeverity.Error, stage, message, details, elementId,
                        warning ? "Проверьте результат на новом виде; проблемный элемент мог быть изменён Revit."
                            : "Эта вставка или настройка будет отменена. Проверьте исходный элемент по ID.");
                if (warning) accessor.DeleteWarning(failure); // Preserve the warning in the journal before dismissing it.
                else { hasErrors = true; Reason = description; }
            }
            return hasErrors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
