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
    public TransferReport Results { get; } = new();
    public List<ViewLogEntry> Entries { get; } = [];
    public string Summary => L.Format($"Создано видов: {Results.Transferred}. Не создано: {Results.Skipped + Results.Failed}. ")
        + L.Format($"Замечаний: {Entries.Count(e => e.Severity == LogSeverity.Warning)}. Записей об ошибках: {Entries.Count(e => e.Severity == LogSeverity.Error)}.");
    public string FullText => Summary + "\n\n" + string.Join("\n\n", Entries.Select(e => e.FullText));
}
