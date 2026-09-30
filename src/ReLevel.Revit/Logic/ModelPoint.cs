namespace ReLevel.Revit.Logic;

public readonly record struct ModelPoint(double X, double Y, double Z)
{
    public bool IsWithin(ModelPoint other, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return double.IsFinite(dx) && double.IsFinite(dy) && double.IsFinite(dz)
            && Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tolerance;
    }
}
