using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class DistributionService(Document document, IReadOnlyList<Level> levels)
{
    public ElementTransferReport Execute(IReadOnlyList<RetainedLevel> retained, IReadOnlySet<long> protectedIds,
        IReadOnlyCollection<long> additionalIds, Action<int, int, long?>? progress = null,
        IReadOnlyCollection<long>? analyzedIds = null, Func<bool>? cancelled = null)
    {
        if (retained.Count == 0) throw new InvalidOperationException(L.Get("Выберите хотя бы один сохраняемый уровень."));
        var ids = (analyzedIds is null ? TransferCases.Candidates(document).Select(element => element.Id)
                : analyzedIds.Select(id => new ElementId(id)))
            .Concat(additionalIds.Select(id => new ElementId(id))).DistinctBy(id => id.Value).ToList();
        var currentLevels = levels.ToDictionary(level => level.Id.Value);
        var targets = retained.Select(choice => currentLevels.TryGetValue(choice.Id, out var level)
            ? new RetainedLevel(choice.Id, level.ProjectElevation)
            : throw new InvalidOperationException(L.Get("Целевой уровень больше не существует."))).ToList();

        return new ElementTransferService(document).Execute(ids, id => Prepare(id, targets, protectedIds), progress, cancelled);
    }

    private PreparedElementTransfer Prepare(ElementId id, IReadOnlyList<RetainedLevel> retained, IReadOnlySet<long> protectedIds)
    {
        var element = document.GetElement(id);
        if (element is null) return PreparedElementTransfer.Skip(L.Get("Объект больше не существует."));
        var transferCase = TransferCases.Find(element);
        if (transferCase is null) return PreparedElementTransfer.Skip(L.Get("Элемент не соответствует реализованным кейсам."));
        var ends = CaptureEnds(element);
        if (ends is not null) return PrepareEnds(element, transferCase, ends, retained, protectedIds);

        var source = levels.FirstOrDefault(level => transferCase.IsOnLevel(element, level.Id));
        if (source is null) return PreparedElementTransfer.Skip(L.Get("Не удалось определить исходный уровень."));
        if (protectedIds.Contains(source.Id.Value)) return PreparedElementTransfer.Skip(L.Get("Исходный уровень защищён от распределения."));
        if (transferCase.WriteRestriction(element, source.Id) is { } restriction) return PreparedElementTransfer.Skip(restriction);
        var height = DistributionHeight.Read(element, source);
        var target = new ElementId(DistributionTarget.Choose(retained, height, source.Id.Value));
        if (target == source.Id) return PreparedElementTransfer.Skip(L.Get("Элемент уже на подходящем сохраняемом уровне."));
        return new(transferCase.Prepare(element, source.Id), target,
            Description: Describe(source.Id.Value, target.Value));
    }

    private PreparedElementTransfer PrepareEnds(Element element, TransferCase transferCase, LevelOffsetState[] ends,
        IReadOnlyList<RetainedLevel> retained, IReadOnlySet<long> protectedIds)
    {
        var destinations = new Dictionary<BuiltInParameter, ElementId>();
        var description = new List<string>();
        for (var index = 0; index < ends.Length; index++)
        {
            var end = ends[index];
            if (protectedIds.Contains(end.LevelId)) continue;
            var target = DistributionTarget.Choose(retained, end.Position.Absolute, end.LevelId);
            if (target == end.LevelId) continue;
            if (transferCase.WriteRestriction(element, new ElementId(end.LevelId)) is { } restriction)
                return PreparedElementTransfer.Skip(restriction);
            destinations.Add(end.Binding.LevelParameter, new ElementId(target));
            description.Add(L.Get(index == 0 ? "Низ" : "Верх") + ": " + Describe(end.LevelId, target));
        }
        if (destinations.Count == 0)
            return PreparedElementTransfer.Skip(L.Get("Привязки защищены или уже на подходящих сохраняемых уровнях."));
        // All ends are written in the same existing per-element transaction. In particular,
        // two ends originally on one level can receive different targets without moving each other twice.
        var source = ElementId.InvalidElementId;
        IElementTransferOperation operation = element switch
        {
            Stairs => new StairsOperation(element, source, destinations),
            Opening => new ShaftOpeningOperation(element, source, destinations),
            _ when element.Category?.BuiltInCategory == BuiltInCategory.OST_Ramps => new RampOperation(element, source, destinations),
            _ => new TwoLevelOperation(element, source, destinations)
        };
        return new(operation, destinations.Values.First(), Description: string.Join("\n", description));
    }

    private string Describe(long source, long target) =>
        levels.First(level => level.Id.Value == source).Name + " → " + levels.First(level => level.Id.Value == target).Name;

    private static LevelOffsetState[]? CaptureEnds(Element element) => element switch
    {
        Wall wall when !InPlaceWallCase.IsInternalWall(wall) => TwoLevelCase.Capture(wall),
        FamilyInstance family when family.Symbol.Family.FamilyPlacementType == FamilyPlacementType.TwoLevelsBased
            => TwoLevelCase.Capture(family),
        Stairs => StairsCase.Capture(element),
        Opening => ShaftOpeningCase.Capture(element),
        _ when element.Category?.BuiltInCategory == BuiltInCategory.OST_Ramps => RampCase.Capture(element),
        _ => null
    };
}
