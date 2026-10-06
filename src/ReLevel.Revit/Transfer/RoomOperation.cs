using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal sealed class RoomOperation(Element source) : IElementTransferOperation
{
    private readonly ElementId id = source.Id;
    private long replacementId;
    private bool unplaced;
    private static readonly HashSet<long> SpatialParameters =
    [
        (long)BuiltInParameter.ROOM_LEVEL_ID, (long)BuiltInParameter.ROOM_PHASE,
        (long)BuiltInParameter.ROOM_PHASE_ID, (long)BuiltInParameter.PHASE_CREATED,
        (long)BuiltInParameter.PHASE_DEMOLISHED, (long)BuiltInParameter.ROOM_UPPER_LEVEL,
        (long)BuiltInParameter.ROOM_LOWER_OFFSET, (long)BuiltInParameter.ROOM_UPPER_OFFSET
    ];

    public string SuccessMessage => unplaced
        ? L.Format($"Помещение пересоздано как Not Placed. Новый ID: {replacementId}. Доступные параметры перенесены.")
        : L.Format($"Помещение пересоздано. Новый ID: {replacementId}. Доступные параметры перенесены.");

    public void Apply(Document document, ElementId target)
    {
        var original = (Room)document.GetElement(id);
        var level = (Level)document.GetElement(target);
        var phase = RoomCase.Phase(original)
            ?? throw new InvalidOperationException(L.Get("Кейс 17: не найден исходный уровень или фаза помещения."));
        var replacement = document.Create.NewRoom(phase)
            ?? throw new InvalidOperationException(L.Get("Кейс 17: Revit не создал помещение."));
        unplaced = original.Location is null;
        if (original.Location is LocationPoint location)
        {
            // A circuit supplies level and phase. The final XY comes from the source, not its centre.
            using var topology = document.get_PlanTopology(level, phase);
            var circuit = topology.Circuits.Cast<PlanCircuit>().OrderBy(c => c.IsRoomLocated).FirstOrDefault()
                ?? throw new InvalidOperationException(L.Get("Кейс 17: на целевом уровне нет контура для размещения в исходной фазе."));
            replacement = document.Create.NewRoom(replacement, circuit)
                ?? throw new InvalidOperationException(L.Get("Кейс 17: Revit не создал помещение."));
            var placed = replacement.Location as LocationPoint
                ?? throw new InvalidOperationException(L.Get("Кейс 17: Revit не разместил помещение."));
            placed.Point = new XYZ(location.Point.X, location.Point.Y, placed.Point.Z);
        }

        RecreatedElementParameters.Copy(original, replacement, L.Get("Кейс 17"), SpatialParameters);
        CopyLimits(original, replacement, level);
        // Creation, all parameter writes and source deletion share one transfer transaction.
        document.Delete(id);
        replacementId = replacement.Id.Value;
    }

    private static void CopyLimits(Room source, Room target, Level level)
    {
        var sourceLevel = (Level)source.Document.GetElement(source.LevelId);
        var delta = source.Location is null ? 0 : sourceLevel.ProjectElevation - level.ProjectElevation;
        var upper = source.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
        var upperOffset = source.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
        var lowerOffset = source.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
        var upperDelta = 0.0;
        if (upper?.HasValue == true && source.Document.GetElement(upper.AsElementId()) is Level oldUpper)
        {
            target.UpperLimit = oldUpper.Id == sourceLevel.Id ? level : oldUpper;
            upperDelta = oldUpper.Id == sourceLevel.Id ? delta : 0;
        }
        if (upperOffset?.HasValue == true) target.LimitOffset = upperOffset.AsDouble() + upperDelta;
        if (lowerOffset?.HasValue == true) target.BaseOffset = lowerOffset.AsDouble() + delta;
    }
}
