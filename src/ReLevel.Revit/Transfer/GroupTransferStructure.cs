using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// The saved membership is the input to regrouping, not a post-operation comparison.
internal sealed class GroupTransferStructure
{
    private sealed record Node(ElementId Id, string Name, List<ElementId> Members,
        List<Node> Children, List<(BuiltInParameter Key, object Value)> Parameters);

    private readonly Node root;
    private readonly List<string> replacements = [];
    private GroupTransferStructure(Node root) => this.root = root;
    public string Description => string.Join("; ", replacements);

    public static ElementId Root(Element element)
    {
        var id = element.GroupId;
        if (id == ElementId.InvalidElementId) return id;
        while (element.Document.GetElement(id) is Group group && group.GroupId != ElementId.InvalidElementId)
            id = group.GroupId;
        return id;
    }

    public static string? Restriction(Element element)
    {
        for (var id = element.GroupId; id != ElementId.InvalidElementId;)
        {
            if (element.Document.GetElement(id) is not Group group)
                return L.Get("Не удалось прочитать группу элемента.");
            if (group.Category?.BuiltInCategory != BuiltInCategory.OST_IOSModelGroups
                || group.AssemblyInstanceId != ElementId.InvalidElementId)
                return L.Get("Разгруппировка поддерживает только модельные группы вне сборок.");
            if (group.GroupId == ElementId.InvalidElementId && group.Pinned)
                return L.Get("Группа закреплена. Перенос её участников недоступен.");
            if (group.GetAvailableAttachedDetailGroupTypeIds().Count > 0)
                return L.Get("Перенос участников группы с присоединёнными группами детализации пока не поддерживается.");
            id = group.GroupId;
        }
        return null;
    }

    public static GroupTransferStructure Capture(Document document, ElementId rootId, IReadOnlyList<ElementId> selected)
    {
        var paths = new HashSet<long>();
        foreach (var id in selected)
        {
            var element = document.GetElement(id);
            if (Restriction(element) is { } restriction) throw new InvalidOperationException(restriction);
            for (var parent = element.GroupId; parent != ElementId.InvalidElementId;)
            {
                paths.Add(parent.Value);
                parent = document.GetElement(parent).GroupId;
            }
        }
        Node Read(ElementId id)
        {
            var group = (Group)document.GetElement(id);
            var members = group.GetMemberIds().ToList();
            var parameters = new List<(BuiltInParameter, object)>();
            foreach (var key in new[] { BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, BuiltInParameter.ALL_MODEL_MARK,
                BuiltInParameter.ELEM_PARTITION_PARAM, BuiltInParameter.PHASE_CREATED, BuiltInParameter.PHASE_DEMOLISHED })
            {
                var p = group.get_Parameter(key);
                if (p is not { HasValue: true, IsReadOnly: false }) continue;
                object? value = p.StorageType switch
                {
                    StorageType.String => p.AsString() ?? "", StorageType.Integer => p.AsInteger(),
                    StorageType.ElementId => p.AsElementId(), _ => null
                };
                if (value is not null) parameters.Add((key, value));
            }
            return new(id, group.Name, members, members.Where(m => paths.Contains(m.Value)).Select(Read).ToList(), parameters);
        }
        return new(Read(rootId));
    }

    public void Ungroup(Document document)
    {
        void Open(Node node)
        {
            ((Group)document.GetElement(node.Id)).UngroupMembers();
            foreach (var child in node.Children) Open(child);
        }
        Open(root);
    }

    public void Regroup(Document document, Dictionary<long, long> changedIds)
    {
        void Close(Node node)
        {
            foreach (var child in node.Children) Close(child);
            var members = node.Members.Select(id => changedIds.TryGetValue(id.Value, out var replacement)
                ? new ElementId(replacement) : id).ToList();
            // Revit needs regeneration before grouping newly created members.
            document.Regenerate();
            var group = document.Create.NewGroup(members);
            var name = node.Name + " - ReLevel " + node.Id.Value;
            using var types = new FilteredElementCollector(document).OfClass(typeof(GroupType));
            var used = types.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unique = name;
            for (var i = 2; used.Contains(unique); i++) unique = name + " (" + i + ")";
            group.GroupType.Name = unique;
            foreach (var (key, value) in node.Parameters)
            {
                var p = group.get_Parameter(key);
                var written = p is { IsReadOnly: false } && (value switch
                {
                    string text => p.Set(text), int number => p.Set(number), ElementId id => p.Set(id), _ => false
                });
                if (!written) throw new InvalidOperationException(L.Format($"Не удалось восстановить параметр группы: {key}."));
            }
            changedIds[node.Id.Value] = group.Id.Value;
            replacements.Add(node.Id.Value + " → " + group.Id.Value + " (" + unique + ")");
        }
        Close(root);
    }
}
