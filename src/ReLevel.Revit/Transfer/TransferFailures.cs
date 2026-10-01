using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Scoped to one transfer transaction. Only Revit's explicit join-error resolution is allowed.
internal sealed class TransferFailures : IFailuresPreprocessor
{
    private readonly HashSet<string> attempted = [];
    private readonly SortedSet<long> participants = [];
    private int resolutionPasses;
    public string? Reason { get; private set; }
    public bool Corrupted { get; private set; }
    // Used only after a successful Commit; rolled-back resolutions must never be reported as success.
    public string UnjoinMessage => resolutionPasses == 0 ? "" : " " +
        L.Format($"Unjoined: соединения разорваны. ID участников: {string.Join(", ", participants)}.");

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var errors = accessor.GetFailureMessages().Where(f => f.GetSeverity() != FailureSeverity.Warning).ToList();
        if (errors.Count == 0) return FailureProcessingResult.Continue;
        Reason = string.Join(Environment.NewLine, errors.Select(f => f.GetDescriptionText()));
        Corrupted |= errors.Any(f => f.GetSeverity() == FailureSeverity.DocumentCorruption);
        // Check the whole batch before resolving anything. Other errors retain rollback semantics.
        if (Corrupted || resolutionPasses >= 8 || errors.Any(f => !CanUnjoin(accessor, f)))
            return FailureProcessingResult.ProceedWithRollBack;

        var keys = errors.Select(Key).ToList();
        if (keys.Any(attempted.Contains)) return FailureProcessingResult.ProceedWithRollBack;
        foreach (var key in keys) attempted.Add(key);
        foreach (var failure in errors)
        {
            // These are participants supplied by Revit, not a fabricated list of unjoined pairs.
            foreach (var id in failure.GetFailingElementIds().Concat(failure.GetAdditionalElementIds()))
                participants.Add(id.Value);
            failure.SetCurrentResolutionType(FailureResolutionType.DetachElements);
            accessor.ResolveFailure(failure);
        }
        resolutionPasses++;
        // Revit regenerates and calls us again. A repeated failure causes rollback, not an endless loop.
        return FailureProcessingResult.ProceedWithCommit;
    }

    private static string Key(FailureMessageAccessor failure) =>
        failure.GetFailureDefinitionId().Guid + ":" +
        string.Join(",", failure.GetFailingElementIds().Select(i => i.Value).Order()) + ":" +
        string.Join(",", failure.GetAdditionalElementIds().Select(i => i.Value).Order());

    private static bool CanUnjoin(FailuresAccessor accessor, FailureMessageAccessor failure)
    {
        var id = failure.GetFailureDefinitionId();
        var knownJoinError = id.Equals(BuiltInFailures.JoinElementsFailures.CannotKeepJoined)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElements)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsError)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsMultiPlaneError)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsStructural)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsStructuralError);
        return knownJoinError && failure.GetSeverity() == FailureSeverity.Error
            && failure.HasResolutionOfType(FailureResolutionType.DetachElements)
            && accessor.IsFailureResolutionPermitted(failure, FailureResolutionType.DetachElements);
    }
}
