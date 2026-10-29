using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal enum LevelGraphicsAction { Unchanged, Color, Hide }
internal sealed record LevelGraphicsChoice(LevelGraphicsAction Action, Color Color);

// This snapshot belongs only to the graphics tool, never to a transfer operation.
internal sealed class LevelViewGraphics : IDisposable
{
    private sealed record Entry(ElementId Id, long LevelId, OverrideGraphicSettings Graphics, bool Hidden);
    private readonly View view;
    private readonly Func<long, TransferCase?>? classifiedCase;
    private readonly Dictionary<long, Entry> entries = [];
    private readonly HashSet<long> touched = [];
    public List<string> Diagnostics { get; } = [];
    public int Count(long levelId) => entries.Values.Count(e => e.LevelId == levelId);

    public LevelViewGraphics(View view, IReadOnlyList<Level> levels, Func<long, TransferCase?>? classifiedCase = null)
    {
        this.view = view;
        this.classifiedCase = classifiedCase;
        try { Refresh(levels); }
        catch { Dispose(); throw; }
    }

    // The modern workspace can transfer/delete elements or open another view between applications.
    // Re-read membership while retaining the original overrides of previously captured IDs.
    public void Refresh(IReadOnlyList<Level> levels)
    {
        if (view.IsTemplate || !view.AreGraphicsOverridesAllowed()
            || !FilteredElementCollector.IsViewValidForElementIteration(view.Document, view.Id))
            throw new InvalidOperationException(L.Get("На этом виде переопределение графики недоступно."));
        Diagnostics.Clear();
        using var collector = new FilteredElementCollector(view.Document, view.Id);
        var ids = collector.WhereElementIsNotElementType().ToElementIds().Select(id => id.Value)
            .Concat(entries.Keys).Distinct().ToList();
        foreach (var id in ids)
        {
            entries.TryGetValue(id, out var previous);
            try
            {
                var element = view.Document.GetElement(new ElementId(id));
                if (element is null)
                {
                    previous?.Graphics.Dispose(); entries.Remove(id); touched.Remove(id);
                    continue;
                }
                var transferCase = classifiedCase is null ? TransferCases.Find(element) : classifiedCase(id);
                long? levelId = null;
                if (transferCase is not null)
                {
                    // Multi-level cases always use the base, regardless of level ordering or language.
                    levelId = element switch
                    {
                        Wall => TwoLevelWallCase.Bottom.Capture(element).LevelId,
                        Stairs => StairsCase.Capture(element)[0].LevelId,
                        Opening when element.Category?.BuiltInCategory == BuiltInCategory.OST_ShaftOpening
                            => ShaftOpeningCase.Capture(element)[0].LevelId,
                        FamilyInstance family when family.Symbol.Family.FamilyPlacementType == FamilyPlacementType.TwoLevelsBased
                            => TwoLevelColumnCase.Bottom.Capture(element).LevelId,
                        _ when element.Category?.BuiltInCategory == BuiltInCategory.OST_Ramps
                            => RampCase.Capture(element)[0].LevelId,
                        _ => levels.FirstOrDefault(level => transferCase.IsOnLevel(element, level.Id))?.Id.Value
                    };
                }
                if (previous is not null) entries[id] = previous with { LevelId = levelId ?? -1 };
                else if (levelId is not null)
                {
                    var hidden = element.IsHidden(view);
                    entries.Add(id, new(element.Id, levelId.Value, view.GetElementOverrides(element.Id), hidden));
                }
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex)
            {
                Diagnostics.Add($"ID {id}: {ex.Message}");
                if (previous is not null) entries[id] = previous with { LevelId = -1 };
            }
        }
    }

    public string Apply(IReadOnlyDictionary<long, LevelGraphicsChoice> choices)
    {
        var document = view.Document;
        var pending = new HashSet<long>();
        var skipped = new List<long>();
        ElementId? solid = null;
        if (choices.Values.Any(c => c.Action == LevelGraphicsAction.Color))
        {
            using var patterns = new FilteredElementCollector(document).OfClass(typeof(FillPatternElement));
            solid = patterns.Cast<FillPatternElement>().FirstOrDefault(p => p.GetFillPattern().IsSolidFill)?.Id
                ?? throw new InvalidOperationException(L.Get("Не найдена сплошная заливка."));
        }
        using var transaction = new Transaction(document, L.Get("Цвета по уровням"));
        if (transaction.Start() != TransactionStatus.Started)
            throw new InvalidOperationException(L.Get("Не удалось начать изменение графики."));
        var failures = new GraphicsFailures();
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
        try
        {
            foreach (var entry in entries.Values)
            {
                var action = choices.TryGetValue(entry.LevelId, out var choice) ? choice.Action : LevelGraphicsAction.Unchanged;
                if (action == LevelGraphicsAction.Unchanged && !touched.Contains(entry.Id.Value)) continue;
                var element = document.GetElement(entry.Id)
                    ?? throw new InvalidOperationException(L.Format($"Элемент {entry.Id.Value} отсутствует в документе."));
                // Every application starts from the original overrides, not the previous colour.
                view.SetElementOverrides(entry.Id, entry.Graphics);
                if (element.IsHidden(view) != entry.Hidden)
                {
                    if (entry.Hidden) view.HideElements([entry.Id]);
                    else view.UnhideElements([entry.Id]);
                }
                if (action == LevelGraphicsAction.Hide)
                {
                    if (!element.CanBeHidden(view)) { skipped.Add(entry.Id.Value); continue; }
                    if (!element.IsHidden(view)) view.HideElements([entry.Id]);
                    pending.Add(entry.Id.Value);
                }
                else if (action == LevelGraphicsAction.Color)
                {
                    using var graphics = new OverrideGraphicSettings(entry.Graphics);
                    graphics.SetProjectionLineColor(choice!.Color).SetCutLineColor(choice.Color)
                        .SetSurfaceForegroundPatternId(solid!).SetSurfaceForegroundPatternColor(choice.Color)
                        .SetSurfaceForegroundPatternVisible(true)
                        .SetCutForegroundPatternId(solid!).SetCutForegroundPatternColor(choice.Color)
                        .SetCutForegroundPatternVisible(true);
                    view.SetElementOverrides(entry.Id, graphics);
                    pending.Add(entry.Id.Value);
                }
            }
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException(L.Get("Изменение графики отменено.") + " " + failures.Reason);
            touched.Clear(); touched.UnionWith(pending);
            return L.Format($"Обработано элементов: {pending.Count}. Нельзя скрыть: {skipped.Count}.")
                + (skipped.Count == 0 ? "" : "\nID: " + string.Join(", ", skipped));
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started
                && transaction.RollBack() != TransactionStatus.RolledBack)
                throw new InvalidOperationException(L.Get("Не удалось откатить изменение графики. Закройте команду."));
            throw;
        }
    }

    public void Dispose() { foreach (var entry in entries.Values) entry.Graphics.Dispose(); entries.Clear(); }

    private sealed class GraphicsFailures : IFailuresPreprocessor
    {
        public string Reason { get; private set; } = "";
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var errors = accessor.GetFailureMessages().Where(f => f.GetSeverity() != FailureSeverity.Warning).ToList();
            Reason = string.Join("\n", errors.Select(f => f.GetDescriptionText()));
            return errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
