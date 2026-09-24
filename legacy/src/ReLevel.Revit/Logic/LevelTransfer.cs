namespace ReLevel.Revit.Logic;

public static class LevelTransfer
{
    // All three inputs must use the same units (Revit callers use internal feet).
    public static double NewOffset(double sourceElevation, double targetElevation, double originalOffset)
    {
        if (!Numeric.IsFinite(sourceElevation) || !Numeric.IsFinite(targetElevation) || !Numeric.IsFinite(originalOffset))
            throw new ArgumentOutOfRangeException(nameof(originalOffset), "Elevations and offset must be finite.");
        var result = originalOffset + (sourceElevation - targetElevation);
        if (!Numeric.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(originalOffset), "Offset calculation overflowed.");
        return result;
    }
}

public readonly record struct ModelPoint(double X, double Y, double Z)
{
    public bool IsWithin(ModelPoint other, double tolerance)
    {
        if (!Numeric.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Numeric.IsFinite(dx) && Numeric.IsFinite(dy) && Numeric.IsFinite(dz)
            && Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tolerance;
    }
}

public enum TransferStatus { Transferred, Skipped, Failed }
public sealed record TransferResult(long ElementId, string Name, TransferStatus Status, string Reason);
public sealed class TransferReport
{
    public List<TransferResult> Items { get; } = [];
    public int Transferred => Items.Count(x => x.Status == TransferStatus.Transferred);
    public int Skipped => Items.Count(x => x.Status == TransferStatus.Skipped);
    public int Failed => Items.Count(x => x.Status == TransferStatus.Failed);
}
