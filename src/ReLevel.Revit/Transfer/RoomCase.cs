using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal static class RoomCase
{
    public static string? UnsupportedReason(Element element)
    {
        if (element is not Room room) return L.Get("Кейс 17: требуется Room.");
        if (room.GroupId != ElementId.InvalidElementId || room.AssemblyInstanceId != ElementId.InvalidElementId
            || room.DesignOption is not null)
            return L.Get("Кейс 17: группы, сборки и варианты конструкции пока не поддерживаются.");
        if (room.Document.GetElement(room.LevelId) is not Level || Phase(room) is null)
            return L.Get("Кейс 17: не найден исходный уровень или фаза помещения.");
        if (room.Location is not null and not LocationPoint)
            return L.Get("Кейс 17: неподдерживаемый способ размещения помещения.");
        return null;
    }

    public static Phase? Phase(Room room) => room.Document.GetElement(
        room.get_Parameter(BuiltInParameter.ROOM_PHASE_ID)?.AsElementId() ?? ElementId.InvalidElementId) as Phase;

    public static string? WriteRestriction(Element element)
    {
        if (element.Pinned) return L.Get("Кейс 17: закреплённое помещение не пересоздаётся.");
        // Until the tag policy is agreed, do not delete rooms carrying annotations.
        // RoomTag is an API-only subclass and cannot be used with OfClass.
        using var tagFilter = new RoomTagFilter();
        using var tags = new FilteredElementCollector(element.Document).WherePasses(tagFilter);
        if (tags.Cast<RoomTag>().Any(tag => tag.TaggedLocalRoomId == element.Id))
            return L.Get("Кейс 17: перенос помещений с марками пока не поддерживается.");
        if (element.GetDependentElements(null).Where(id => id != element.Id)
            .Select(element.Document.GetElement).Any(e => e is not null && (e.GetType() != typeof(Element) || e.Category is not null)))
            return L.Get("Кейс 17: помещение имеет неподдержанные зависимые элементы.");
        return null;
    }
}
