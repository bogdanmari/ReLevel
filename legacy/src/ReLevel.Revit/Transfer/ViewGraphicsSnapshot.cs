using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Per-element overrides and permanent hiding are not a view template parameter.
internal sealed class ViewGraphicsSnapshot : IDisposable
{
    private readonly List<(ElementId Id, OverrideGraphicSettings Overrides, bool Hidden)> elements = [];

    public ViewGraphicsSnapshot(View source)
    {
        using var defaults = new OverrideGraphicSettings();
        var defaultKey = Key(defaults);
        foreach (var element in new FilteredElementCollector(source.Document).WhereElementIsNotElementType()
            .Where(e => !e.ViewSpecific && e is not View))
        {
            var graphics = source.GetElementOverrides(element.Id);
            var hidden = element.IsHidden(source);
            if (hidden || Key(graphics) != defaultKey) elements.Add((element.Id, graphics, hidden));
            else graphics.Dispose();
        }
    }

    public ViewGraphicsSnapshot(View source, ElementId sourceId, ElementId targetId)
    {
        var original = source.Document.GetElement(sourceId)
            ?? throw new InvalidOperationException($"Исходный элемент {sourceId.ToLong()} отсутствует.");
        var hidden = original.IsHidden(source);
        // Capture even default settings: the copy must match the original, including no overrides.
        elements.Add((targetId, source.GetElementOverrides(sourceId), hidden));
    }

    public void Apply(View target)
    {
        foreach (var (id, graphics, hidden) in elements)
        {
            var element = target.Document.GetElement(id)
                ?? throw new InvalidOperationException($"Элемент {id.ToLong()} отсутствует в документе.");
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
        foreach (var (id, graphics, hidden) in elements)
        {
            var element = target.Document.GetElement(id)
                ?? throw new InvalidOperationException($"Элемент {id.ToLong()} не сохранился после переноса графики.");
            using var actual = target.GetElementOverrides(id);
            if (Key(actual) != Key(graphics) || element.IsHidden(target) != hidden)
                throw new InvalidOperationException($"Не перенесена видимость или графика элемента {id.ToLong()}.");
        }
    }

    public void Dispose()
    {
        foreach (var entry in elements) entry.Overrides.Dispose();
    }

    internal static string Key(OverrideGraphicSettings g) => string.Join("|",
        ColorKey(g.ProjectionLineColor), g.ProjectionLinePatternId.ToLong(), g.ProjectionLineWeight,
        ColorKey(g.CutLineColor), g.CutLinePatternId.ToLong(), g.CutLineWeight,
        g.SurfaceForegroundPatternId.ToLong(), ColorKey(g.SurfaceForegroundPatternColor), g.IsSurfaceForegroundPatternVisible,
        g.SurfaceBackgroundPatternId.ToLong(), ColorKey(g.SurfaceBackgroundPatternColor), g.IsSurfaceBackgroundPatternVisible,
        g.CutForegroundPatternId.ToLong(), ColorKey(g.CutForegroundPatternColor), g.IsCutForegroundPatternVisible,
        g.CutBackgroundPatternId.ToLong(), ColorKey(g.CutBackgroundPatternColor), g.IsCutBackgroundPatternVisible,
        g.Transparency, g.Halftone, g.DetailLevel);

    private static string ColorKey(Color color) => color.IsValid ? $"{color.Red},{color.Green},{color.Blue}" : "invalid";
}
