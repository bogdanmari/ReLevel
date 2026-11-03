namespace ReLevel.Revit.UI.Prototype;

internal enum WorkspaceDebugTool { SelectByCase, AddById, Inspector }
internal sealed record ColorPageState(bool Available, string Message, IReadOnlyDictionary<long, int> Counts);

// API-free contract between the accepted shell and the Revit operations.
internal interface IPrototypeWorkspace
{
    ReLevel.Revit.Logic.LengthUnitSettings Units { get; }
    ColorPageState PrepareColors();
    string ApplyColors(IReadOnlyList<PrototypeLevel> levels, bool reset);
    void RunDebug(WorkspaceDebugTool tool);
    void ProcessInformation(IReadOnlyList<long> levelIds, bool delete, bool views = false);
    void Distribute(IReadOnlyList<PrototypeLevel> levels, Action<int, int, long?> progress, Action<bool> finished, Func<bool> cancelled);
}
