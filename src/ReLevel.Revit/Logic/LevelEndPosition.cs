namespace ReLevel.Revit.Logic;

// Shared arithmetic for any mechanism with an explicit level and vertical offset.
internal readonly record struct LevelEndPosition(double Elevation, double Offset)
{
    public double Absolute => Elevation + Offset;
    public double OffsetAt(double targetElevation) => Absolute - targetElevation;
}
