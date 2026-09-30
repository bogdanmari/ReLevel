namespace ReLevel.Revit.Logic;

internal enum LogSeverity { Info, Warning, Error }

internal sealed record ViewLogEntry(LogSeverity Severity, string Stage, long SourceViewId,
    long? CreatedViewId, long? ElementId, string Message, string Details, string Recommendation)
{
    public string SeverityLabel => Severity switch { LogSeverity.Info => L.Get("Готово"), LogSeverity.Warning => L.Get("Внимание"), _ => L.Get("Ошибка") };
    public string FullText => L.Format($"[{SeverityLabel}] {Stage}\nИсходный вид: {SourceViewId}; новый вид: {CreatedViewId?.ToString() ?? "—"}; исходный элемент: {ElementId?.ToString() ?? "—"}\n")
        + L.Format($"{Message}\n{Recommendation}\nТехнические подробности:\n{Details}");
}

internal sealed class ViewRecreationReport
{
    public bool CriticalFailure { get; set; }
    public ViewResults Results { get; } = new();
    public List<ViewLogEntry> Entries { get; } = [];
    public string Summary => L.Format($"Создано видов: {Results.Created}. Не создано: {Results.Skipped + Results.Failed}. ")
        + L.Format($"Замечаний: {Entries.Count(e => e.Severity == LogSeverity.Warning)}. Записей об ошибках: {Entries.Count(e => e.Severity == LogSeverity.Error)}.");
    public string FullText => Summary + "\n\n" + string.Join("\n\n", Entries.Select(e => e.FullText));
}

internal enum ViewResultStatus { Created, Skipped, Failed }
internal sealed record ViewResult(long ElementId, string Name, ViewResultStatus Status, string Reason);
internal sealed class ViewResults
{
    public List<ViewResult> Items { get; } = [];
    public int Created => Items.Count(x => x.Status == ViewResultStatus.Created);
    public int Skipped => Items.Count(x => x.Status == ViewResultStatus.Skipped);
    public int Failed => Items.Count(x => x.Status == ViewResultStatus.Failed);
}
