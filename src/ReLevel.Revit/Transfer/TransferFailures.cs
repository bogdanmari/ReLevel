using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// Scoped to one transfer transaction; dimension deletion is opt-in for hosted railings.
internal sealed class TransferFailures(bool allowInvalidDimensionDeletion = false,
    bool allowFabricationRodDialog = false) : IFailuresPreprocessor
{
    private readonly HashSet<string> attempted = [];
    private readonly SortedSet<long> participants = [];
    private readonly SortedSet<long> deletedDimensions = [];
    private readonly List<string> messages = [];
    private int resolutionPasses;
    public string? Reason => messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
    public bool Corrupted { get; private set; }
    // Used only after a successful Commit; rolled-back resolutions must never be reported as success.
    public string UnjoinMessage => participants.Count == 0 ? "" : " " +
        L.Format($"Unjoined: соединения разорваны. ID участников: {string.Join(", ", participants)}.");
    public string DeletedDimensionsMessage => deletedDimensions.Count == 0 ? "" : " " +
        L.Format($"Удалены размеры с недействительными ссылками. ID: {string.Join(", ", deletedDimensions)}.");

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var failures = accessor.GetFailureMessages().ToList();
        foreach (var failure in failures)
        {
            var message = Describe(accessor, failure);
            if (!messages.Contains(message)) messages.Add(message);
        }
        var errors = failures.Where(f => f.GetSeverity() != FailureSeverity.Warning).ToList();
        var joins = failures.Where(IsJoinFailure).ToList();
        Corrupted |= errors.Any(f => f.GetSeverity() == FailureSeverity.DocumentCorruption);
        if (Corrupted) return FailureProcessingResult.ProceedWithRollBack;
        var dimensions = allowInvalidDimensionDeletion
            ? errors.Where(f => f.GetFailureDefinitionId().Equals(
                BuiltInFailures.DimensionFailures.DimensionReferencesInvalid)).ToList()
            : [];
        // Check the whole batch before resolving anything. Other errors retain rollback semantics.
        if (errors.Any(f => !dimensions.Contains(f) && !CanUnjoin(accessor, f) && !AllowRodDialog(f)))
            return FailureProcessingResult.ProceedWithRollBack;
        // Continue leaves the unresolved rod error to Revit's forced-modal failure dialog.
        // The user chooses Detach or Cancel; this preprocessor never detaches rods itself.
        if (joins.Count == 0 && dimensions.Count == 0) return FailureProcessingResult.Continue;
        if (resolutionPasses >= 8 || joins.Any(f => !CanUnjoin(accessor, f)))
            return FailureProcessingResult.ProceedWithRollBack;

        // Only actual Dimension IDs explicitly named by this error may be removed.
        var dimensionIds = dimensions.SelectMany(f => f.GetFailingElementIds()).Distinct().ToList();
        if (dimensions.Count > 0 && (dimensions.Any(f => f.GetFailingElementIds().Count == 0)
            || dimensionIds.Any(id => accessor.GetDocument().GetElement(id) is not Dimension)
            || !accessor.IsElementsDeletionPermitted(dimensionIds)))
            return FailureProcessingResult.ProceedWithRollBack;

        var keys = joins.Concat(dimensions).Select(Key).ToList();
        if (keys.Any(attempted.Contains)) return FailureProcessingResult.ProceedWithRollBack;
        foreach (var key in keys) attempted.Add(key);
        foreach (var failure in joins)
        {
            // These are participants supplied by Revit, not a fabricated list of unjoined pairs.
            foreach (var id in failure.GetFailingElementIds().Concat(failure.GetAdditionalElementIds()))
                participants.Add(id.Value);
            failure.SetCurrentResolutionType(FailureResolutionType.DetachElements);
            accessor.ResolveFailure(failure);
        }
        if (dimensionIds.Count > 0)
        {
            accessor.DeleteElements(dimensionIds);
            foreach (var id in dimensionIds) deletedDimensions.Add(id.Value);
        }
        resolutionPasses++;
        // Revit regenerates and calls us again. A repeated failure causes rollback, not an endless loop.
        return FailureProcessingResult.ProceedWithCommit;
    }

    private static string Key(FailureMessageAccessor failure) =>
        failure.GetFailureDefinitionId().Guid + ":" +
        string.Join(",", failure.GetFailingElementIds().Select(i => i.Value).Order()) + ":" +
        string.Join(",", failure.GetAdditionalElementIds().Select(i => i.Value).Order());

    private bool AllowRodDialog(FailureMessageAccessor failure) => allowFabricationRodDialog
        && failure.GetSeverity() == FailureSeverity.Error
        && failure.GetFailureDefinitionId().Equals(
            BuiltInFailures.MEPFabricationFailures.FabricationRodsDisconnectedError);

    private static string Describe(FailuresAccessor accessor, FailureMessageAccessor failure) =>
        $"{failure.GetDescriptionText()} [{failure.GetSeverity()}; " +
        $"FailureId={failure.GetFailureDefinitionId().Guid}; " +
        $"IDs={string.Join(", ", failure.GetFailingElementIds().Select(i => i.Value))}; " +
        $"AdditionalIDs={string.Join(", ", failure.GetAdditionalElementIds().Select(i => i.Value))}; " +
        $"DetachElements={CanUnjoin(accessor, failure)}]";

    private static bool CanUnjoin(FailuresAccessor accessor, FailureMessageAccessor failure) =>
        IsJoinFailure(failure)
        && (failure.GetSeverity() == FailureSeverity.Error || failure.GetSeverity() == FailureSeverity.Warning)
        && failure.HasResolutionOfType(FailureResolutionType.DetachElements)
        && accessor.IsFailureResolutionPermitted(failure, FailureResolutionType.DetachElements);

    private static bool IsJoinFailure(FailureMessageAccessor failure)
    {
        var id = failure.GetFailureDefinitionId();
        return id.Equals(BuiltInFailures.JoinElementsFailures.CannotKeepJoined)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsWarn)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElements)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsError)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsMultiPlaneError)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsStructural)
            || id.Equals(BuiltInFailures.JoinElementsFailures.CannotJoinElementsStructuralError);
    }
}
