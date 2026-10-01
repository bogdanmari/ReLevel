using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed class BeamOperation(Element element) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    private readonly LevelOffsetState start = BeamCase.Start.Capture(element);
    private readonly LevelOffsetState end = BeamCase.End.Capture(element);
    private bool detached;

    public string SuccessMessage => detached
        ? L.Get("Уровень изменён с отсоединением балки от рабочей плоскости.")
        : L.Get("Уровень изменён.");

    public void Apply(Document document, ElementId target)
    {
        var beam = document.GetElement(id);
        var level = (Level)document.GetElement(target);
        if (LevelOffsetBinding.Require(beam, BeamCase.Start.LevelParameter, StorageType.ElementId).IsReadOnly)
        {
            // Autodesk's Detach Beam from Plane workaround: +1 internal foot, then restore.
            // Keep all four assignments and the final transfer in the caller's transaction.
            SetOffset(beam, BeamCase.Start, start.Position.Offset + 1.0);
            SetOffset(beam, BeamCase.End, end.Position.Offset + 1.0);
            SetOffset(beam, BeamCase.Start, start.Position.Offset);
            SetOffset(beam, BeamCase.End, end.Position.Offset);
            document.Regenerate();
            beam = document.GetElement(id);
            detached = true;
        }

        var referenceLevel = LevelOffsetBinding.Require(beam, BeamCase.Start.LevelParameter, StorageType.ElementId);
        if (referenceLevel.IsReadOnly)
            throw new InvalidOperationException(L.Get("Кейс 7: Reference Level остался заблокированным после попытки отсоединения."));
        if (!referenceLevel.Set(target))
            throw new InvalidOperationException(L.Get("Revit отклонил запись целевого уровня."));
        SetOffset(beam, BeamCase.Start, start.Position.OffsetAt(level.ProjectElevation));
        SetOffset(beam, BeamCase.End, end.Position.OffsetAt(level.ProjectElevation));
    }

    private static void SetOffset(Element beam, LevelOffsetBinding binding, double value)
    {
        if (!LevelOffsetBinding.Require(beam, binding.OffsetParameter, StorageType.Double).Set(value))
            throw new InvalidOperationException(L.Get("Кейс 7: Revit отклонил запись смещения конца балки."));
    }
}
