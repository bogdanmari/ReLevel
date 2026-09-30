using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed record TransferCase(Func<string> Caption, Type[] ElementClasses, Func<Element, string?> UnsupportedReason,
    Func<Element, ElementId, bool> IsOnLevel, Func<Element, ElementId, string?> WriteRestriction,
    Func<Element, ElementId, IElementTransferOperation> Prepare, Func<Element, ElementId, string> Relation)
{
    public string Name => Caption();
}

internal static class TransferCases
{
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
            (_, _) => L.Get("Уровень"))
    });

    public static TransferCase? Find(Element element) => All.FirstOrDefault(item => item.ElementClasses.Any(type => type.IsInstanceOfType(element))
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
    }
}
