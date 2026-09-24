using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Per-element overrides and permanent hiding are not a view template parameter.
internal sealed class ViewGraphicsSnapshot : IDisposable
{
    private readonly List<(ElementId Id, OverrideGraphicSettings Overrides, bool Hidden)> elements = [];
    private readonly List<(ElementId Id, OverrideGraphicSettings Overrides, bool Hidden)> categories = [];
    private readonly List<(ElementId Id, OverrideGraphicSettings Overrides, bool Visible, bool Enabled)> filters = [];

    public ViewGraphicsSnapshot(View source)
    {
        using var defaults = new OverrideGraphicSettings();
        var defaultKey = Key(defaults);
        foreach (var category in AllCategories(source.Document.Settings.Categories).Where(c => source.CanCategoryBeHidden(c.Id)))
            categories.Add((category.Id, source.GetCategoryOverrides(category.Id), source.GetCategoryHidden(category.Id)));
        foreach (var id in source.GetOrderedFilters())
            filters.Add((id, source.GetFilterOverrides(id), source.GetFilterVisibility(id), source.GetIsFilterEnabled(id)));
        foreach (var element in new FilteredElementCollector(source.Document).WhereElementIsNotElementType()
            .Where(e => !e.ViewSpecific && e is not View))
        {
            var graphics = source.GetElementOverrides(element.Id);
            var hidden = element.IsHidden(source);
            if (hidden || Key(graphics) != defaultKey) elements.Add((element.Id, graphics, hidden));
            else graphics.Dispose();
        }
    }

    public void Apply(View target)
    {
        foreach (var (id, graphics, hidden) in categories)
        {
            using var actual = target.GetCategoryOverrides(id);
            if (Key(actual) != Key(graphics)) target.SetCategoryOverrides(id, graphics);
            if (target.GetCategoryHidden(id) != hidden) target.SetCategoryHidden(id, hidden);
        }
        if (!target.GetOrderedFilters().SequenceEqual(filters.Select(f => f.Id)))
        {
            foreach (var id in target.GetOrderedFilters()) target.RemoveFilter(id);
            foreach (var entry in filters) target.AddFilter(entry.Id);
        }
        foreach (var (id, graphics, visible, enabled) in filters)
        {
            using var actual = target.GetFilterOverrides(id);
            if (Key(actual) != Key(graphics)) target.SetFilterOverrides(id, graphics);
            if (target.GetFilterVisibility(id) != visible) target.SetFilterVisibility(id, visible);
            if (target.GetIsFilterEnabled(id) != enabled) target.SetIsFilterEnabled(id, enabled);
        }
        foreach (var (id, graphics, hidden) in elements)
        {
            target.SetElementOverrides(id, graphics);
            if (hidden && !target.Document.GetElement(id).IsHidden(target)) target.HideElements([id]);
        }
    }

    public void Verify(View target)
    {
        foreach (var (id, graphics, hidden) in categories)
        {
            using var actual = target.GetCategoryOverrides(id);
            if (Key(actual) != Key(graphics) || target.GetCategoryHidden(id) != hidden)
                throw new InvalidOperationException($"Не перенесена графика категории {id.Value}.");
        }
        if (!target.GetOrderedFilters().SequenceEqual(filters.Select(f => f.Id)))
            throw new InvalidOperationException("Не совпадает порядок фильтров вида.");
        foreach (var (id, graphics, visible, enabled) in filters)
        {
            using var actual = target.GetFilterOverrides(id);
            if (Key(actual) != Key(graphics) || target.GetFilterVisibility(id) != visible || target.GetIsFilterEnabled(id) != enabled)
                throw new InvalidOperationException($"Не перенесены настройки фильтра {id.Value}.");
        }
        foreach (var (id, graphics, hidden) in elements)
        {
            using var actual = target.GetElementOverrides(id);
            if (Key(actual) != Key(graphics) || target.Document.GetElement(id).IsHidden(target) != hidden)
                throw new InvalidOperationException($"Не перенесена видимость или графика элемента {id.Value}.");
        }
    }

    public void Dispose()
    {
        foreach (var entry in elements) entry.Overrides.Dispose();
        foreach (var entry in categories) entry.Overrides.Dispose();
        foreach (var entry in filters) entry.Overrides.Dispose();
    }

    private static IEnumerable<Category> AllCategories(Categories categories)
    {
        foreach (Category category in categories)
        {
            yield return category;
            foreach (Category child in category.SubCategories) yield return child;
        }
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
