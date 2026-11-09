namespace ReLevel.Revit.Logic;

internal sealed record FillLevelParameter(long Id, string Name)
{
    public string Display => $"{Name} [ID: {Id}]";
}

internal sealed record FillLevelResult(long Id, string Level, string Parameter, string Status, string Reason);
