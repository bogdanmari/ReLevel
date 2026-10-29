namespace ReLevel.Revit.Logic;

internal readonly record struct RetainedLevel(long Id, double Elevation);

internal static class DistributionTarget
{
    // Revit internal feet; a tiny tolerance avoids moving an exactly-on-level binding
    // because of floating point compensation in an earlier operation.
    private const double Tolerance = 1e-9;

    public static long Choose(IReadOnlyList<RetainedLevel> levels, double height, long currentId)
    {
        if (levels.Count == 0) throw new ArgumentException("At least one retained level is required.", nameof(levels));
        if (!double.IsFinite(height) || levels.Any(level => !double.IsFinite(level.Elevation)))
            throw new ArgumentException("Level and element elevations must be finite.");
        var below = levels.Where(level => level.Elevation <= height + Tolerance).ToList();
        var elevation = below.Count > 0 ? below.Max(level => level.Elevation) : levels.Min(level => level.Elevation);
        var candidates = levels.Where(level => Math.Abs(level.Elevation - elevation) <= Tolerance).ToList();
        return candidates.Any(level => level.Id == currentId) ? currentId : candidates.Min(level => level.Id);
    }
}
