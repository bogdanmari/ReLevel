using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class MepSnapshot
{
    private sealed record Port(int Id, Domain Domain, ConnectorType Type, long SystemId,
        string References, string Profile, ModelPoint[] Frame, double[] Size);
    private readonly Port[] ports;
    private readonly long[] systems;

    public MepSnapshot(Element element)
    {
        ports = Connectors(element).Select(Read).OrderBy(p => p.Id).ToArray();
        systems = Systems(element).Select(s => s.Id.Value).Distinct().Order().ToArray();
    }

    public static IEnumerable<Connector> Connectors(Element element)
    {
        var manager = element switch
        {
            MEPCurve curve => curve.ConnectorManager,
            FamilyInstance family => family.MEPModel?.ConnectorManager,
            _ => null
        };
        return manager is null ? [] : manager.Connectors.Cast<Connector>().ToArray();
    }

    public static IEnumerable<MEPSystem> Systems(Element element)
    {
        var systems = new Dictionary<long, MEPSystem>();
        foreach (var connector in Connectors(element))
            if (connector.Domain is Domain.DomainHvac or Domain.DomainPiping or Domain.DomainElectrical
                && connector.MEPSystem is { } system) systems[system.Id.Value] = system;
        if (element is FamilyInstance { MEPModel: { } model })
            foreach (var system in model.GetElectricalSystems() ?? new HashSet<Autodesk.Revit.DB.Electrical.ElectricalSystem>())
                systems[system.Id.Value] = system;
        return systems.Values;
    }

    public void Verify(Element element)
    {
        var after = new MepSnapshot(element);
        if (!systems.SequenceEqual(after.systems) || ports.Length != after.ports.Length)
            throw new InvalidOperationException(L.Get("Состав коннекторов или систем MEP изменился."));
        for (var i = 0; i < ports.Length; ++i)
        {
            var a = ports[i]; var b = after.ports[i];
            if (a.Id != b.Id || a.Domain != b.Domain || a.Type != b.Type || a.SystemId != b.SystemId
                || a.References != b.References || a.Profile != b.Profile || a.Frame.Length != b.Frame.Length
                || a.Frame.Where((p, n) => !p.IsWithin(b.Frame[n], n == 0 ? GeometrySnapshot.Tolerance : 1e-9)).Any()
                || a.Size.Length != b.Size.Length || a.Size.Where((v, n) => Math.Abs(v - b.Size[n]) > 1e-9).Any())
                throw new InvalidOperationException(L.Format($"ID {element.Id.Value}: коннектор {a.Id}, соединение или система MEP изменились."));
        }
    }

    private static Port Read(Connector connector)
    {
        var refs = string.Join(";", connector.AllRefs.Cast<Connector>()
            .Select(c => $"{c.Owner.Id.Value}:{c.Id}:{c.ConnectorType}:"
                + (connector.ConnectorType != ConnectorType.Logical && c.ConnectorType != ConnectorType.Logical
                    ? connector.IsConnectedTo(c).ToString() : "logical")).Order());
        var system = connector.Domain is Domain.DomainHvac or Domain.DomainPiping or Domain.DomainElectrical
            ? connector.MEPSystem?.Id.Value ?? -1 : -1;
        // Logical ports have no geometric coordinate system in the Revit API.
        if (connector.ConnectorType == ConnectorType.Logical)
            return new(connector.Id, connector.Domain, connector.ConnectorType, system, refs, "", [], []);
        var frame = connector.CoordinateSystem;
        ModelPoint Point(XYZ p) => new(p.X, p.Y, p.Z);
        // Electrical ports have no duct/pipe profile; do not query inapplicable size properties.
        var shape = connector.Domain == Domain.DomainElectrical ? ConnectorProfileType.Invalid : connector.Shape;
        double[] size = shape switch
        {
            ConnectorProfileType.Round => [connector.Radius],
            ConnectorProfileType.Rectangular or ConnectorProfileType.Oval => [connector.Width, connector.Height],
            _ => []
        };
        if (size.Any(v => !double.IsFinite(v))) throw new InvalidOperationException(L.Get("Размер коннектора MEP не определён."));
        return new(connector.Id, connector.Domain, connector.ConnectorType, system, refs, $"{shape}:{connector.IsConnected}",
            [Point(connector.Origin), Point(frame.BasisX), Point(frame.BasisY), Point(frame.BasisZ)], size);
    }
}
