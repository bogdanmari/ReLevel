using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Per-element overrides and permanent hiding are not a view template parameter.
internal sealed class ViewGraphicsSnapshot : IDisposable
{
    private readonly List<(ElementId Id, OverrideGraphicSettings Overrides, bool Hidden)> elements = [];
    private readonly List<ElementId> defaults = [];

    public ViewGraphicsSnapshot(View source)
    {
        using var defaultGraphics = new OverrideGraphicSettings();
        var defaultKey = Key(defaultGraphics);
        foreach (var element in new FilteredElementCollector(source.Document).WhereElementIsNotElementType()
            .Where(e => !e.ViewSpecific && e is not View))
        {
            var graphics = source.GetElementOverrides(element.Id);
            var hidden = element.IsHidden(source);
            if (hidden || Key(graphics) != defaultKey) elements.Add((element.Id, graphics, hidden));
            else { defaults.Add(element.Id); graphics.Dispose(); }
        }
    }

    public ViewGraphicsSnapshot(View source, ElementId sourceId, ElementId targetId)
    {
        var original = source.Document.GetElement(sourceId)
            ?? throw new InvalidOperationException(L.Format($"Исходный элемент {sourceId.Value} отсутствует."));
        var hidden = original.IsHidden(source);
        // Capture even default settings: the copy must match the original, including no overrides.
        elements.Add((targetId, source.GetElementOverrides(sourceId), hidden));
    }

    public void Apply(View target)
    {
        using var defaultGraphics = new OverrideGraphicSettings();
        var defaultKey = Key(defaultGraphics);
        foreach (var id in defaults)
        {
            var element = target.Document.GetElement(id)
                ?? throw new InvalidOperationException(L.Format($"Элемент {id.Value} отсутствует в документе."));
            using var actual = target.GetElementOverrides(id);
            if (Key(actual) != defaultKey) target.SetElementOverrides(id, defaultGraphics);
            if (element.IsHidden(target)) target.UnhideElements([id]);
        }
        foreach (var (id, graphics, hidden) in elements)
        {
            var element = target.Document.GetElement(id)
                ?? throw new InvalidOperationException(L.Format($"Элемент {id.Value} отсутствует в документе."));
            using var actual = target.GetElementOverrides(id);
            if (Key(actual) != Key(graphics)) target.SetElementOverrides(id, graphics);
            if (element.IsHidden(target) != hidden)
            {
                if (hidden) target.HideElements([id]);
                else target.UnhideElements([id]);
            }
        }
    }

    public void Verify(View target)
    {
        using var defaultGraphics = new OverrideGraphicSettings();
        var defaultKey = Key(defaultGraphics);
        foreach (var id in defaults)
        {
            var element = target.Document.GetElement(id)
                ?? throw new InvalidOperationException(L.Format($"Элемент {id.Value} не сохранился после переноса графики."));
            using var actual = target.GetElementOverrides(id);
            if (Key(actual) != defaultKey || element.IsHidden(target))
                throw new InvalidOperationException(L.Format($"Не перенесена видимость или графика элемента {id.Value}."));
        }
        foreach (var (id, graphics, hidden) in elements)
        {
            var element = target.Document.GetElement(id)
                ?? throw new InvalidOperationException(L.Format($"Элемент {id.Value} не сохранился после переноса графики."));
            using var actual = target.GetElementOverrides(id);
            if (Key(actual) != Key(graphics) || element.IsHidden(target) != hidden)
                throw new InvalidOperationException(L.Format($"Не перенесена видимость или графика элемента {id.Value}."));
        }
    }

    public void Dispose()
    {
        foreach (var entry in elements) entry.Overrides.Dispose();
    }

    internal static string Key(OverrideGraphicSettings g) => string.Join("|",
        ColorKey(g.ProjectionLineColor), g.ProjectionLinePatternId.Value, g.ProjectionLineWeight,
        ColorKey(g.CutLineColor), g.CutLinePatternId.Value, g.CutLineWeight,
        g.SurfaceForegroundPatternId.Value, ColorKey(g.SurfaceForegroundPatternColor), g.IsSurfaceForegroundPatternVisible,
        g.SurfaceBackgroundPatternId.Value, ColorKey(g.SurfaceBackgroundPatternColor), g.IsSurfaceBackgroundPatternVisible,
        g.CutForegroundPatternId.Value, ColorKey(g.CutForegroundPatternColor), g.IsCutForegroundPatternVisible,
        g.CutBackgroundPatternId.Value, ColorKey(g.CutBackgroundPatternColor), g.IsCutBackgroundPatternVisible,
        g.Transparency, g.Halftone, g.DetailLevel);

    private static string ColorKey(Color color) => color.IsValid ? $"{color.Red},{color.Green},{color.Blue}" : "invalid";
}
