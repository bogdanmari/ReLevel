using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal sealed record CaseElementSearchResult(IReadOnlyList<ElementId> Ids, IReadOnlyDictionary<long, string> Errors);

internal static class CaseElementFinder
{
    public static CaseElementSearchResult Find(Document document, TransferCase selectedCase)
    {
        var ids = new List<ElementId>();
        var errors = new Dictionary<long, string>();
        // Document-wide: no view, level, current selection or table filter.
        using var collector = new FilteredElementCollector(document);
        using var filter = new ElementMulticlassFilter(selectedCase.ElementClasses);
        foreach (var element in collector.WherePasses(filter).WhereElementIsNotElementType())
        {
            var id = element.Id;
            try
            {
                // Debug selection follows case membership, not permission to write parameters.
                if (selectedCase.UnsupportedReason(element) is null) ids.Add(id);
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { errors.Add(id.Value, ex.Message); }
        }
        return new(ids, errors);
    }
}
