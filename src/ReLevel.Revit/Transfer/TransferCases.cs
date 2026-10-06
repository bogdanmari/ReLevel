using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;

namespace ReLevel.Revit.Transfer;

internal sealed record TransferCase(Func<string> Caption, Type[] ElementClasses, Func<Element, string?> UnsupportedReason,
    Func<Element, ElementId, bool> IsOnLevel, Func<Element, ElementId, string?> WriteRestriction,
    Func<Element, ElementId, IElementTransferOperation> Prepare, Func<Element, ElementId, string> Relation,
    BuiltInCategory? Category = null)
{
    public string Name => Caption();
    public ElementFilter CreateFilter() => Category is { } category
        ? new ElementCategoryFilter(category) : new ElementMulticlassFilter(ElementClasses);
}

internal static class TransferCases
{
    private static readonly TransferCase InternalWalls = new(() => L.Get("Кейс 16 — внутренние стены In-Place"),
        [typeof(Wall)], InPlaceWallCase.UnsupportedReason, InPlaceWallCase.IsOnLevel,
        (element, _) => InPlaceWallCase.WriteRestriction(element),
        (element, source) => new LevelOffsetOperation(element, source, TwoLevelWallCase.Bottom),
        (_, _) => L.Get("Низ"));

    public static IReadOnlyList<TransferCase> All { get; } = Array.AsReadOnly(new[]
    {
        new TransferCase(() => L.Get("Кейс 1 — одноуровневое загружаемое семейство без хоста"),
            [typeof(FamilyInstance)], PointFamilyCase.UnsupportedReason,
            (element, source) => PointFamilyCase.SourceLevel(element)?.Id == source,
            (element, _) => PointFamilyCase.WriteRestriction(element), (element, _) => new PointFamilyOperation(element),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 2 — колонны и стены"),
            [typeof(FamilyInstance), typeof(Wall)], TwoLevelCase.UnsupportedReason, TwoLevelCase.IsOnLevel,
            TwoLevelCase.WriteRestriction, (element, source) => new TwoLevelOperation(element, source),
            TwoLevelCase.Relation),
        new TransferCase(() => L.Get("Кейс 3 — семейство на грани хоста"),
            [typeof(FamilyInstance)], FaceHostedFamilyCase.UnsupportedReason,
            (element, source) => FaceHostedFamilyCase.SourceLevel(element)?.Id == source,
            (element, _) => FaceHostedFamilyCase.WriteRestriction(element),
            (element, source) => new LevelOffsetOperation(element, source, FaceHostedFamilyCase.Binding),
            (_, _) => L.Get("Уровень спецификации")),
        new TransferCase(() => L.Get("Кейс 4 — перекрытия и фундаментные плиты Floor"),
            [typeof(Floor)], FloorCase.UnsupportedReason,
            (element, source) => FloorCase.SourceLevel(element)?.Id == source,
            (element, _) => FloorCase.WriteRestriction(element),
            (element, source) => new LevelOffsetOperation(element, source, FloorCase.Binding),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 5 — крыши выдавливанием ExtrusionRoof"),
            [typeof(ExtrusionRoof)], ExtrusionRoofCase.UnsupportedReason,
            (element, source) => ExtrusionRoofCase.SourceLevel(element)?.Id == source,
            (element, _) => ExtrusionRoofCase.WriteRestriction(element),
            (element, source) => new LevelOffsetOperation(element, source, ExtrusionRoofCase.Binding),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 6 — крыши по контуру FootPrintRoof"),
            [typeof(FootPrintRoof)], FootPrintRoofCase.UnsupportedReason,
            (element, source) => FootPrintRoofCase.SourceLevel(element)?.Id == source,
            (element, _) => FootPrintRoofCase.WriteRestriction(element),
            (element, source) => new LevelOffsetOperation(element, source, FootPrintRoofCase.Binding),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 7 — балки с Reference Level"),
            [typeof(FamilyInstance)], BeamCase.UnsupportedReason,
            (element, source) => BeamCase.SourceLevel(element)?.Id == source,
            (element, _) => BeamCase.WriteRestriction(element),
            (element, _) => new BeamOperation(element),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 8 — пересоздание Room Separation Lines"),
            [typeof(CurveElement)], RoomSeparatorCase.UnsupportedReason,
            (element, source) => element.LevelId == source,
            (element, _) => RoomSeparatorCase.WriteRestriction(element),
            (element, _) => new RoomSeparatorOperation(element),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 9 — лестницы Stairs"),
            [typeof(Stairs)], StairsCase.UnsupportedReason, StairsCase.IsOnLevel,
            StairsCase.WriteRestriction, (element, source) => new StairsOperation(element, source),
            StairsCase.Relation),
        new TransferCase(() => L.Get("Кейс 10 — ограждения"),
            [typeof(Railing)], RailingCase.UnsupportedReason,
            (element, source) => RailingCase.SourceLevel(element)?.Id == source,
            (element, _) => RailingCase.WriteRestriction(element),
            (element, source) => ((Railing)element).HasHost
                ? new HostedRailingOperation(element)
                : new LevelOffsetOperation(element, source, RailingCase.Binding),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 11 — трубы Pipe"),
            [typeof(Pipe)], PipeCase.UnsupportedReason,
            (element, source) => PipeCase.SourceLevel(element)?.Id == source,
            (element, _) => PipeCase.WriteRestriction(element),
            (element, _) => new MepReferenceLevelOperation(element),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 12 — площадки Pads"),
            [typeof(BuildingPad)], BuildingPadCase.UnsupportedReason,
            (element, source) => BuildingPadCase.SourceLevel(element)?.Id == source,
            (element, _) => BuildingPadCase.WriteRestriction(element),
            (element, source) => new LevelOffsetOperation(element, source, BuildingPadCase.Binding),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 13 — воздуховоды Ducts"),
            [typeof(Duct)], DuctCase.UnsupportedReason,
            (element, source) => DuctCase.SourceLevel(element)?.Id == source,
            (element, _) => DuctCase.WriteRestriction(element),
            (element, _) => new MepReferenceLevelOperation(element),
            (_, _) => L.Get("Уровень")),
        new TransferCase(() => L.Get("Кейс 14 — шахтные проёмы"),
            [typeof(Opening)], ShaftOpeningCase.UnsupportedReason, ShaftOpeningCase.IsOnLevel,
            ShaftOpeningCase.WriteRestriction, (element, source) => new ShaftOpeningOperation(element, source),
            ShaftOpeningCase.Relation),
        new TransferCase(() => L.Get("Кейс 15 — пандусы"),
            [], RampCase.UnsupportedReason, RampCase.IsOnLevel,
            RampCase.WriteRestriction, (element, source) => new RampOperation(element, source),
            RampCase.Relation, BuiltInCategory.OST_Ramps),
        InternalWalls,
        new TransferCase(() => L.Get("Кейс 17 — пересоздание помещений"),
            [], RoomCase.UnsupportedReason,
            (element, source) => element.LevelId == source,
            (element, _) => RoomCase.WriteRestriction(element),
            (element, _) => new RoomOperation(element),
            (_, _) => L.Get("Уровень"), BuiltInCategory.OST_Rooms),
        new TransferCase(() => L.Get("Кейс 18 — пересоздание границ площадей"),
            [], AreaBoundaryCase.UnsupportedReason,
            (element, source) => element.LevelId == source,
            (element, _) => AreaBoundaryCase.WriteRestriction(element),
            (element, _) => new AreaBoundaryOperation(element),
            (_, _) => L.Get("Уровень"), BuiltInCategory.OST_AreaSchemeLines)
    });

    public static TransferCase? Find(Element element) => InPlaceWallCase.IsInternalWall(element)
        ? (InternalWalls.UnsupportedReason(element) is null ? InternalWalls : null)
        : All.Where(item => item != InternalWalls).FirstOrDefault(item => (item.Category is { } category
        ? element.Category?.BuiltInCategory == category : item.ElementClasses.Any(type => type.IsInstanceOfType(element)))
        && item.UnsupportedReason(element) is null);

    public static IEnumerable<Element> Candidates(Document document)
    {
        var seen = new HashSet<long>();
        foreach (var type in All.SelectMany(item => item.ElementClasses).Distinct())
        {
            using var collector = new FilteredElementCollector(document);
            foreach (var element in collector.OfClass(type).WhereElementIsNotElementType())
                if (seen.Add(element.Id.Value)) yield return element;
        }
        foreach (var category in All.Where(item => item.Category.HasValue).Select(item => item.Category!.Value).Distinct())
        {
            using var collector = new FilteredElementCollector(document);
            foreach (var element in collector.OfCategory(category).WhereElementIsNotElementType())
                if (seen.Add(element.Id.Value)) yield return element;
        }
    }
}
