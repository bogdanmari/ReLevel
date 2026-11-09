namespace ReLevel.Revit.UI.Workspace;

internal enum WorkspaceDebugTool { SelectByCase, AddById, Inspector }
internal sealed record ColorPageState(bool Available, string Message, IReadOnlyDictionary<long, int> Counts);

// API-free contract between the accepted shell and the Revit operations.
internal interface IWorkspace
{
    ReLevel.Revit.Logic.LengthUnitSettings Units { get; }
    ColorPageState PrepareColors();
    string ApplyColors(IReadOnlyList<WorkspaceLevel> levels, bool reset);
    void RunDebug(WorkspaceDebugTool tool);
    void ProcessInformation(IReadOnlyList<long> levelIds, bool delete, bool views = false);
    void DeleteLevel(long levelId);
    void FillLevels(IReadOnlyList<long> levelIds, bool selectedOnly);
    void TransferLevels(IReadOnlyList<long> levelIds, Action<int> started, Action<int, int, long?> progress,
        Action<bool> finished, Func<bool> cancelled);
    void Distribute(IReadOnlyList<WorkspaceLevel> levels, Action<int, int, long?> progress, Action<bool> finished, Func<bool> cancelled);
}
