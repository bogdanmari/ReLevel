using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Never accept a failure resolution that leaves a group dissolved or members excluded.
internal sealed class GroupTransferFailures : IFailuresPreprocessor
{
    private readonly List<string> messages = [];
    public bool Corrupted { get; private set; }
    public string Reason => string.Join(Environment.NewLine, messages);
    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var failures = accessor.GetFailureMessages();
        Corrupted |= failures.Any(f => f.GetSeverity() == FailureSeverity.DocumentCorruption);
        foreach (var failure in failures)
        {
            var message = failure.GetDescriptionText() + " [" + failure.GetSeverity() + "; " +
                failure.GetFailureDefinitionId().Guid + "; IDs=" +
                string.Join(",", failure.GetFailingElementIds().Select(id => id.Value)) + "; AdditionalIDs=" +
                string.Join(",", failure.GetAdditionalElementIds().Select(id => id.Value)) + "]";
            if (!messages.Contains(message)) messages.Add(message);
        }
        // Leave only these informational warnings to Revit's normal warning handling.
        // Do not suppress them or allow resolutions that dissolve/exclude group members.
        return failures.All(f => AllowsWarning(f.GetSeverity(), f.GetFailureDefinitionId()))
            ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
    }

    internal static bool AllowsWarning(FailureSeverity severity, FailureDefinitionId id) =>
        severity == FailureSeverity.Warning &&
        (id.Equals(BuiltInFailures.GroupFailures.AtomViolationWhenOnePlaceInstance)
         || id.Equals(BuiltInFailures.OverlapFailures.WallsOverlap));
}
