using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class FillLevelService(Document document)
{
    public IReadOnlyList<FillLevelParameter> Parameters(IEnumerable<long> ids)
    {
        var choices = new Dictionary<long, FillLevelParameter>();
        foreach (var id in ids)
        {
            var element = document.GetElement(new ElementId(id));
            if (element is null) continue;
            foreach (Parameter parameter in element.Parameters)
                if (parameter.StorageType == StorageType.String)
                    choices.TryAdd(parameter.Id.Value, new(parameter.Id.Value, parameter.Definition.Name));
        }
        return choices.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id).ToList();
    }

    public IReadOnlyList<FillLevelResult> Execute(IReadOnlyDictionary<long, long> sources, FillLevelParameter choice)
    {
        var results = new List<FillLevelResult>();
        using var group = new TransactionGroup(document, L.Get("Заполнить уровень"));
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException(L.Get("Не удалось начать операцию."));
        // Each failed write rolls back independently. An unexpected/critical failure unwinds the entire group.
        foreach (var (id, levelId) in sources)
        {
            var element = document.GetElement(new ElementId(id));
            var level = document.GetElement(new ElementId(levelId)) as Level;
            var levelName = level?.Name ?? "";
            void Add(string status, string reason) => results.Add(new(id, levelName, choice.Display, L.Get(status), reason));
            if (element is null) { Add("Пропущен", L.Get("Элемент не найден.")); continue; }
            if (level is null) { Add("Пропущен", L.Get("Уровень не определён.")); continue; }
            // Parameter identity, not its potentially duplicated/localized name, selects the destination.
            var parameter = element.Parameters.Cast<Parameter>().SingleOrDefault(p => p.Id.Value == choice.Id);
            if (parameter is null) { Add("Пропущен", L.Get("Параметр не найден.")); continue; }
            if (parameter.StorageType != StorageType.String) { Add("Пропущен", L.Get("Параметр не является текстовым.")); continue; }
            if (parameter.IsReadOnly) { Add("Пропущен", L.Get("Параметр доступен только для чтения.")); continue; }
            if (parameter.AsString() == levelName) { Add("Без изменений", L.Get("Значение уже совпадает с названием уровня.")); continue; }
            using var transaction = new Transaction(document, L.Get("Заполнить уровень"));
            if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException(L.Get("Не удалось начать операцию."));
            var failures = new FillFailures();
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
            try
            {
                if (!parameter.Set(levelName)) throw new InvalidOperationException(L.Get("Revit отклонил запись параметра."));
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex)
            {
                if (transaction.RollBack() != TransactionStatus.RolledBack) throw;
                Add("Ошибка", ex.Message);
                continue;
            }
            // Commit exceptions are fatal: do not read the document or continue the batch.
            var status = transaction.Commit();
            if (failures.Corrupted) throw new InvalidOperationException(failures.Reason);
            if (status == TransactionStatus.Committed) Add("Заполнено", "");
            else if (status == TransactionStatus.RolledBack) Add("Ошибка", failures.Reason);
            else throw new InvalidOperationException($"Transaction: {status}. {failures.Reason}");
        }
        if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException(L.Get("Не удалось завершить операцию."));
        return results;
    }

    private sealed class FillFailures : IFailuresPreprocessor
    {
        public string Reason { get; private set; } = "";
        public bool Corrupted { get; private set; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;
            Reason = string.Join(Environment.NewLine, failures.Select(f => f.GetDescriptionText()));
            Corrupted = failures.Any(f => f.GetSeverity() == FailureSeverity.DocumentCorruption);
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
