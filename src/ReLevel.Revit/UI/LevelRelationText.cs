using Autodesk.Revit.DB;
using ReLevel.Revit.Transfer;

namespace ReLevel.Revit.UI;

internal static class LevelRelationText
{
    public static string Describe(LevelRelationResult result, Document document, ElementId? sourceId)
    {
        var descriptions = result.Relations.Where(r => sourceId is null || r.LevelId == sourceId)
            .Select(r => Describe(r, document)).Distinct().ToList();
        if (descriptions.Count == 0) descriptions.Add(L.Get("Связь с уровнем не определена."));
        return string.Join(Environment.NewLine, descriptions.Concat(result.Notices));
    }

    private static string Describe(LevelRelation relation, Document document)
    {
        var kind = relation.Kind switch
        {
            LevelRelationKind.Base => L.Get("Нижняя привязка"),
            LevelRelationKind.Top => L.Get("Верхняя привязка"),
            LevelRelationKind.Reference => L.Get("Опорный уровень"),
            _ => L.Get("Уровень")
        };
        var name = document.GetElement(relation.LevelId)?.Name ?? relation.LevelId.Value.ToString();
        var direct = $"{kind}: {name} [ID {relation.LevelId.Value}]";
        if (relation.Path.Count == 0) return direct;
        var path = string.Join(" → ", relation.Path.Select(step => step.Route == LevelRelationRoute.Host
            ? L.Format($"Хост ID {step.ElementId.Value}")
            : L.Format($"Грань / рабочая плоскость ID {step.ElementId.Value}")));
        return $"{path} → {direct}";
    }
}
