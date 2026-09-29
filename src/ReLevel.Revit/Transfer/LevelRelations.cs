using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal enum LevelRelationKind { Level, Base, Top, Reference }
internal enum LevelRelationRoute { Host, WorkPlane }

// Parameter belongs to OwnerId, not necessarily to the element shown in the table.
// A nonempty path is discovery evidence only, never a writable level parameter.
internal sealed record LevelRelationStep(LevelRelationRoute Route, ElementId ElementId);
internal sealed record LevelRelation(ElementId LevelId, ElementId OwnerId, LevelRelationKind Kind,
    BuiltInParameter? Parameter, IReadOnlyList<LevelRelationStep> Path);
internal sealed record LevelRelationResult(IReadOnlyList<LevelRelation> Relations, IReadOnlyList<string> Notices, bool ReadFailed = false)
{
    public bool IsOnLevel(ElementId levelId) => Relations.Any(r => r.LevelId == levelId);
}

// Use one instance per table refresh. Cache API reads of shared hosts, but never retain
// them across model changes or cache partially resolved recursive paths.
internal sealed class LevelRelationFinder(Document document)
{
    private static readonly (BuiltInParameter Parameter, LevelRelationKind Kind)[] LevelParameters =
    [
        (BuiltInParameter.FAMILY_LEVEL_PARAM, LevelRelationKind.Level),
        (BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, LevelRelationKind.Reference),
        (BuiltInParameter.SCHEDULE_LEVEL_PARAM, LevelRelationKind.Level),
        (BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM, LevelRelationKind.Base),
        (BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM, LevelRelationKind.Top),
        (BuiltInParameter.LEVEL_PARAM, LevelRelationKind.Level),
        (BuiltInParameter.WALL_BASE_CONSTRAINT, LevelRelationKind.Base),
        (BuiltInParameter.WALL_HEIGHT_TYPE, LevelRelationKind.Top),
        (BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, LevelRelationKind.Base),
        (BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, LevelRelationKind.Top),
        (BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, LevelRelationKind.Reference),
        (BuiltInParameter.RBS_START_LEVEL_PARAM, LevelRelationKind.Reference),
        (BuiltInParameter.RBS_END_LEVEL_PARAM, LevelRelationKind.Reference),
        (BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, LevelRelationKind.Base),
        (BuiltInParameter.STAIRS_TOP_LEVEL_PARAM, LevelRelationKind.Top),
        (BuiltInParameter.ROOF_BASE_LEVEL_PARAM, LevelRelationKind.Base),
        (BuiltInParameter.ROOF_UPTO_LEVEL_PARAM, LevelRelationKind.Top)
    ];

    private sealed class Node
    {
        public List<LevelRelation> Direct { get; } = [];
        public List<LevelRelationStep> Parents { get; } = [];
        public List<string> Notices { get; } = [];
        public bool ReadFailed { get; set; }
    }

    private readonly Dictionary<long, Node> nodes = [];

    public LevelRelationResult Find(Element element)
    {
        var relations = new List<LevelRelation>();
        var notices = new List<string>();
        var visited = new HashSet<long>();
        var readFailed = false;
        // Iterative traversal also bounds malformed or cyclic host chains.
        var pending = new Stack<(Element Element, LevelRelationStep[] Path)>();
        pending.Push((element, []));
        while (pending.TryPop(out var next))
        {
            if (!visited.Add(next.Element.Id.Value)) continue;
            var node = Read(next.Element);
            readFailed |= node.ReadFailed;
            relations.AddRange(node.Direct.Select(r => r with { Path = next.Path }));
            notices.AddRange(node.Notices);
            if (next.Path.Length > 0 && node.Direct.Count == 0 && node.Parents.Count == 0)
                notices.Add(L.Format($"Связь через ID {next.Element.Id.Value}: уровень не определён."));
            foreach (var parent in node.Parents)
            {
                var host = document.GetElement(parent.ElementId);
                if (host is not null) pending.Push((host, [.. next.Path, parent]));
                else notices.Add(L.Format($"Связанный объект ID {parent.ElementId.Value} недоступен; уровень не определён."));
            }
        }
        return new(relations, notices.Distinct().ToArray(), readFailed);
    }

    private Node Read(Element element)
    {
        if (nodes.TryGetValue(element.Id.Value, out var cached)) return cached;
        var node = new Node();
        nodes.Add(element.Id.Value, node);

        void TryRead(string basis, Action read)
        {
            try { read(); }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex)
            {
                node.ReadFailed = true;
                node.Notices.Add(L.Format($"ID {element.Id.Value}, {basis}: ошибка чтения связи: {ex.Message}"));
            }
        }

        void AddLevel(ElementId id, LevelRelationKind kind, BuiltInParameter? parameter)
        {
            // Negative values are sentinels (including an unconnected wall top).
            if (id.Value > 0 && document.GetElement(id) is Level)
                node.Direct.Add(new(id, element.Id, kind, parameter, []));
        }

        void AddParent(ElementId id, LevelRelationRoute route)
        {
            if (id.Value <= 0 || id == element.Id) return;
            // HostFace is read first so a work-plane/face route takes precedence over
            // the same object returned by Host. Preserve all level parameters of it.
            if (node.Parents.All(p => p.ElementId != id)) node.Parents.Add(new(route, id));
        }

        if (element is Level)
            node.Direct.Add(new(element.Id, element.Id, LevelRelationKind.Level, null, []));
        else
        {
            foreach (var (parameterId, kind) in LevelParameters)
                TryRead(parameterId.ToString(), () =>
                {
                    if (element.get_Parameter(parameterId) is { HasValue: true, StorageType: StorageType.ElementId } parameter)
                        AddLevel(parameter.AsElementId(), kind, parameterId);
                });
            TryRead("Element.LevelId", () =>
            {
                // Do not repeat the generic API level when a named parameter explains it.
                var id = element.LevelId;
                if (node.Direct.All(r => r.LevelId != id)) AddLevel(id, LevelRelationKind.Level, null);
            });
        }

        if (element is FamilyInstance family)
        {
            TryRead("FamilyInstance.HostFace", () =>
            {
                if (family.HostFace is not { } face) return;
                if (face.LinkedElementId != ElementId.InvalidElementId)
                {
                    node.Notices.Add(L.Format($"Хост в связанной модели: связь ID {face.ElementId.Value}, элемент ID {face.LinkedElementId.Value}; уровень текущего документа по нему не определяется."));
                    return;
                }
                AddParent(face.ElementId, LevelRelationRoute.WorkPlane);
            });
            TryRead("FamilyInstance.Host", () =>
            {
                if (family.Host is not { } host) return;
                if (host is RevitLinkInstance || !host.Document.Equals(document))
                {
                    node.Notices.Add(L.Format($"Хост ID {host.Id.Value} находится в связанной модели; его изменение не поддерживается."));
                    return;
                }
                AddParent(host.Id, host is Level or ReferencePlane or SketchPlane
                    ? LevelRelationRoute.WorkPlane : LevelRelationRoute.Host);
            });
        }

        if (element is CurveElement curve)
            TryRead("CurveElement.SketchPlane", () =>
            {
                if (curve.SketchPlane is { } plane) AddParent(plane.Id, LevelRelationRoute.WorkPlane);
            });
        TryRead("SKETCH_PLANE_PARAM", () =>
        {
            if (element.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM) is not { HasValue: true } plane) return;
            if (plane.StorageType == StorageType.ElementId)
                AddParent(plane.AsElementId(), LevelRelationRoute.WorkPlane);
            else if (node.Parents.Count == 0)
                node.Notices.Add(L.Format($"Рабочая плоскость элемента ID {element.Id.Value}: ссылка на объект недоступна; уровень по имени или высоте не определяется."));
        });
        return node;
    }
}
