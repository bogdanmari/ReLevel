using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class ParticipantGeometry
{
    private readonly GeometrySnapshot? solid;
    private readonly string identity;
    private readonly ModelPoint[] boundary;

    public ParticipantGeometry(Element e)
    {
        identity = Identity(e);
        boundary = e is Opening opening ? Boundary(opening) : [];
        if (e is not (Opening or MEPSystem)) solid = GeometrySnapshot.Capture(e);
    }

    public void Verify(Element e)
    {
        if (Identity(e) != identity) throw new InvalidOperationException(L.Get("Тип, хост проёма или состав системы изменились."));
        if (e is Opening opening)
        {
            var after = Boundary(opening);
            if (boundary.Length != after.Length || boundary.Where((p, i) => !p.IsWithin(after[i], GeometrySnapshot.Tolerance)).Any())
                throw new InvalidOperationException(L.Get("Контур проёма изменился."));
        }
        solid?.Verify(e);
    }

    private static ModelPoint[] Boundary(Opening opening)
    {
        IEnumerable<XYZ> points = opening.IsRectBoundary ? opening.BoundaryRect
            : opening.BoundaryCurves.Cast<Curve>().SelectMany(c => c.Tessellate());
        var result = points.Select(p => new ModelPoint(p.X, p.Y, p.Z)).ToArray();
        return result.Length > 0 ? result : throw new InvalidOperationException(L.Get("Контур проёма недоступен для проверки."));
    }

    private static string Identity(Element e)
    {
        var identity = $"{e.UniqueId}:{e.GetTypeId().Value}";
        if (e is Opening opening) return identity + $":{opening.Host?.UniqueId}:{opening.IsRectBoundary}";
        if (e is not MEPSystem system) return identity;
        var members = system.Elements.Cast<Element>().Select(x => x.Id.Value);
        if (system is MechanicalSystem mechanical) members = members.Concat(mechanical.DuctNetwork.Cast<Element>().Select(x => x.Id.Value));
        if (system is PipingSystem piping) members = members.Concat(piping.PipingNetwork.Cast<Element>().Select(x => x.Id.Value));
        return identity + $":{system.BaseEquipment?.Id.Value}:" + string.Join(",", members.Distinct().Order());
    }
}
