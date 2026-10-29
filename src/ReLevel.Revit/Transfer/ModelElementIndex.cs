using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal sealed record IndexedModelElement(long Id, TransferCase Case, IReadOnlyList<long> LevelIds);

// Window-local classification, not a geometry/result snapshot. Operations still validate
// the current element before writing through the existing transfer cases.
internal sealed class ModelElementIndex(Document document, IReadOnlyList<Level> levels)
{
    private Dictionary<long, IndexedModelElement> elements = [];
    private Dictionary<long, List<long>> byLevel = [];
    private Dictionary<long, int> counts = [];
    public IReadOnlyDictionary<long, string> Errors { get; private set; } = new Dictionary<long, string>();
    public IEnumerable<long> ElementIds => elements.Keys;
    public event Action? Changed;

    public TransferCase? Case(long id) => elements.GetValueOrDefault(id)?.Case;
    public IReadOnlyList<long> AtLevel(long levelId) => byLevel.TryGetValue(levelId, out var ids) ? ids : [];
    public int Count(long levelId) => counts.GetValueOrDefault(levelId);
    public CaseElementSearchResult FindCase(TransferCase selected) => new(
        elements.Values.Where(entry => entry.Case == selected).Select(entry => new ElementId(entry.Id)).ToList(), Errors);

    public void Refresh(IEnumerable<long>? additionalIds = null, Action<int, int>? progress = null)
    {
        var ids = TransferCases.Candidates(document).Select(element => element.Id.Value)
            .Concat(additionalIds ?? []).Distinct().ToList();
        var updated = new Dictionary<long, IndexedModelElement>();
        var levelIndex = levels.ToDictionary(level => level.Id.Value, _ => new List<long>());
        var levelCounts = levels.ToDictionary(level => level.Id.Value, _ => 0);
        var errors = new Dictionary<long, string>();
        progress?.Invoke(0, ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            try
            {
                var element = document.GetElement(new ElementId(id));
                if (element is not null && TransferCases.Find(element) is { } transferCase)
                {
                    var memberships = Membership(element, transferCase).Where(levelIndex.ContainsKey).Distinct().ToList();
                    updated.Add(id, new(id, transferCase, memberships));
                    foreach (var levelId in memberships) levelIndex[levelId].Add(id);
                    // Membership returns the base binding first for every multi-level case.
                    // Counts use only that binding; Transfer retains membership at both ends.
                    if (memberships.Count > 0) levelCounts[memberships[0]]++;
                }
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { errors[id] = ex.Message; }
            progress?.Invoke(i + 1, ids.Count);
        }
        elements = updated; byLevel = levelIndex; counts = levelCounts; Errors = errors;
        Changed?.Invoke();
    }

    private IEnumerable<long> Membership(Element element, TransferCase transferCase)
    {
        // Read the case's bindings once, rather than recapturing both ends for every level.
        switch (element)
        {
            case Wall wall:
                return InPlaceWallCase.IsInternalWall(wall) ? [TwoLevelWallCase.Bottom.Capture(wall).LevelId]
                    : TwoLevelCase.Capture(wall).Select(end => end.LevelId);
            case FamilyInstance family:
                return family.Symbol.Family.FamilyPlacementType switch
                {
                    FamilyPlacementType.TwoLevelsBased => TwoLevelCase.Capture(family).Select(end => end.LevelId),
                    FamilyPlacementType.OneLevelBased => [PointFamilyCase.SourceLevel(family)!.Id.Value],
                    FamilyPlacementType.WorkPlaneBased => [FaceHostedFamilyCase.SourceLevel(family)!.Id.Value],
                    FamilyPlacementType.CurveDrivenStructural => [BeamCase.SourceLevel(family)!.Id.Value],
                    _ => levels.Where(level => transferCase.IsOnLevel(element, level.Id)).Select(level => level.Id.Value)
                };
            case Stairs: return StairsCase.Capture(element).Select(end => end.LevelId);
            case Opening: return ShaftOpeningCase.Capture(element).Select(end => end.LevelId);
            case Floor: return [FloorCase.SourceLevel(element)!.Id.Value];
            case ExtrusionRoof: return [ExtrusionRoofCase.SourceLevel(element)!.Id.Value];
            case FootPrintRoof: return [FootPrintRoofCase.SourceLevel(element)!.Id.Value];
            case BuildingPad: return [BuildingPadCase.SourceLevel(element)!.Id.Value];
            case Railing: return [RailingCase.SourceLevel(element)!.Id.Value];
            case FabricationPart: return [FabricationPartCase.LevelParameter(element)!.AsElementId().Value];
            case MEPCurve: return [LevelOffsetBinding.Require(element, BuiltInParameter.RBS_START_LEVEL_PARAM, StorageType.ElementId).AsElementId().Value];
            case Room or CurveElement: return [element.LevelId.Value];
            default:
                return element.Category?.BuiltInCategory == BuiltInCategory.OST_Ramps
                    ? RampCase.Capture(element).Select(end => end.LevelId)
                    : levels.Where(level => transferCase.IsOnLevel(element, level.Id)).Select(level => level.Id.Value);
        }
    }
}
