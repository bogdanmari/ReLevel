using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed record CaseElementSearchResult(IReadOnlyList<ElementId> Ids, IReadOnlyDictionary<long, string> Errors);

internal static class CaseElementFinder
{
    public static CaseElementSearchResult Find(Document document, TransferCase selectedCase, IEnumerable<long>? additionalIds = null)
    {
        var ids = new List<ElementId>();
        var errors = new Dictionary<long, string>();
        // Document-wide: no view, level, current selection or table filter.
        using var collector = new FilteredElementCollector(document);
        using var filter = selectedCase.CreateFilter();
        var candidates = collector.WherePasses(filter).WhereElementIsNotElementType()
            .Concat((additionalIds ?? []).Select(id => document.GetElement(new ElementId(id))).OfType<Element>())
            .DistinctBy(element => element.Id.Value);
        foreach (var element in candidates)
        {
            var id = element.Id;
            try
            {
                // Debug selection follows case membership, not permission to write parameters.
                if (selectedCase.UnsupportedReason(element) is null
                    && (element is not Wall || TransferCases.Find(element) == selectedCase)) ids.Add(id);
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { errors.Add(id.Value, ex.Message); }
        }
        return new(ids, errors);
    }
}
