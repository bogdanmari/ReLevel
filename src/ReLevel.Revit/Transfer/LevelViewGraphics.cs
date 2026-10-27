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
    private readonly List<Entry> entries = [];
    private readonly HashSet<long> touched = [];
    public List<string> Diagnostics { get; } = [];
    public int Count(long levelId) => entries.Count(e => e.LevelId == levelId);

    public LevelViewGraphics(View view, IReadOnlyList<Level> levels)
    {
        this.view = view;
        if (view.IsTemplate || !view.AreGraphicsOverridesAllowed()
            || !FilteredElementCollector.IsViewValidForElementIteration(view.Document, view.Id))
            throw new InvalidOperationException(L.Get("На этом виде переопределение графики недоступно."));
        try
        {
            using var collector = new FilteredElementCollector(view.Document, view.Id);
            foreach (var element in collector.WhereElementIsNotElementType())
            {
                try
                {
                    var transferCase = TransferCases.Find(element);
                    if (transferCase is null) continue;
                    // Multi-level cases always use the base, regardless of level ordering or language.
                    var levelId = element switch
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
                    if (levelId is null) continue;
                    var hidden = element.IsHidden(view);
                    entries.Add(new(element.Id, levelId.Value, view.GetElementOverrides(element.Id), hidden));
                }
                catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
                catch (Exception ex) { Diagnostics.Add($"ID {element.Id.Value}: {ex.Message}"); }
            }
        }
        catch { Dispose(); throw; }
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
            foreach (var entry in entries)
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

    public void Dispose() { foreach (var entry in entries) entry.Graphics.Dispose(); }

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
