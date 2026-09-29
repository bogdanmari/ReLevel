using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal enum TransferMode { SourceLevel, Selection }

internal sealed record TransferContext(TransferMode Mode, ElementId? SourceLevelId, ElementId TargetLevelId);
internal sealed record LevelParameterPair(BuiltInParameter Level, BuiltInParameter Offset, bool Optional = false,
    BuiltInParameter? SecondOffset = null, bool CompensateOffset = true, bool AllowDerivedOffsets = false);

// Includes unchanged bindings, so the other end of a two-level element is verified too.
internal sealed record TransferBinding(LevelParameterPair Parameters, ElementId OriginalLevelId,
    double OriginalOffset, ElementId ResultLevelId, double ResultOffset,
    double? OriginalSecondOffset = null, double? ResultSecondOffset = null)
{
    public bool Changes => OriginalLevelId != ResultLevelId;
}

// Only a mechanism that declares a dependency in advance can permit its regeneration
// updates. Every dependency must pass its snapshot, including only explicitly
// permitted inherited level changes for hosted families.
internal sealed record TransferDependency(ElementId Id, bool AllowVerifiedChanges = false, bool FollowsHost = false);

internal sealed record TransferPlan(ElementId Id, string Name, TransferContext Context,
    ITransferStrategy? Strategy, IReadOnlyList<TransferBinding> Bindings,
    IReadOnlyList<TransferDependency> Dependencies, string? Reason)
{
    public bool Ready => Strategy is not null && Reason is null && Bindings.Any(b => b.Changes);

    public static TransferPlan Skip(ElementId id, string name, TransferContext context, string reason)
        => new(id, name, context, null, [], [], reason);
}
