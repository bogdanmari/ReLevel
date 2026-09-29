using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class TransferDependencies
{
    // Read-only participants, not silently added transfer selections. Their IDs and
    // parameter policy are fixed before writing; the complete attempt rolls back together.
    public static IReadOnlyList<TransferDependency> Collect(Element root, bool includeHosted, bool includeJoins)
    {
        var result = new Dictionary<long, TransferDependency>();
        void Add(ElementId id, bool followsHost = false)
        {
            if (id == root.Id || id == ElementId.InvalidElementId) return;
            if (result.TryGetValue(id.Value, out var old)) followsHost |= old.FollowsHost;
            result[id.Value] = new(id, AllowVerifiedChanges: true, FollowsHost: followsHost);
        }

        if (includeHosted)
        {
            if (root is HostObject host)
                foreach (var id in host.FindInserts(true, true, true, true)) Add(id, followsHost: true);
            // FindInserts alone does not enumerate every face/work-plane hosted family.
            using var familyFilter = new ElementClassFilter(typeof(FamilyInstance));
            foreach (var family in root.GetDependentElements(familyFilter).Select(root.Document.GetElement).OfType<FamilyInstance>())
                if (family.Host?.Id == root.Id || family.HostFace is { } face
                    && face.LinkedElementId == ElementId.InvalidElementId && face.ElementId == root.Id)
                    Add(family.Id, followsHost: true);
        }
        if (includeJoins)
        {
            foreach (var id in JoinGeometryUtils.GetJoinedElements(root.Document, root)) Add(id);
            if (root is Wall { Location: LocationCurve location })
                for (var end = 0; end < 2; ++end)
                    foreach (Element joined in location.get_ElementsAtJoin(end)) Add(joined.Id);
        }
        if (root is FamilyInstance familyRoot)
        {
            if (familyRoot.Host is { } host && host is not (Level or ReferencePlane or SketchPlane or RevitLinkInstance)) Add(host.Id);
            foreach (var id in familyRoot.GetSubComponentIds()) Add(id, followsHost: true);
        }

        var nested = new Queue<TransferDependency>(result.Values);
        var visited = new HashSet<long>();
        while (nested.TryDequeue(out var parent))
        {
            if (!visited.Add(parent.Id.Value) || root.Document.GetElement(parent.Id) is not FamilyInstance family) continue;
            foreach (var child in family.GetSubComponentIds())
            {
                Add(child, parent.FollowsHost);
                if (child != root.Id) nested.Enqueue(result[child.Value]);
            }
        }

        // Include the immediate neighbours and system objects. Any update beyond this
        // declared boundary remains a rollback; the whole network is not rewritten.
        var participants = new[] { root.Id }.Concat(result.Values.Select(d => d.Id)).Distinct().ToArray();
        foreach (var id in participants)
        {
            var element = root.Document.GetElement(id);
            foreach (var connector in MepSnapshot.Connectors(element))
                foreach (Connector other in connector.AllRefs)
                    if (other.Owner is not ElementType) Add(other.Owner.Id);
            foreach (var system in MepSnapshot.Systems(element)) Add(system.Id);
        }
        return result.Values.OrderBy(d => d.Id.Value).ToArray();
    }
}
