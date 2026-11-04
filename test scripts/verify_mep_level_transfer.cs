// Run as an MCP C# body in Revit 2025+. Tests the exact Release DLL, always rolls back.
// Model-specific IDs from ReLevel-transfer.tsv. Does not replace the running UI command.
var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a =>
    a.GetName().Name == "ReLevel.Revit" && a.ManifestModule.ModuleVersionId ==
    new Guid("a54e448c-80a1-4b02-ba72-ac5307f4f203"));
var ids = new long[] {14208716,14208734,14208736,14208737,14208739,14208740,
    14208741,14208742,14208748,14208749,14208772,14208780,14208938,14208947,
    14208954,14209014,14209015,14209023,14209172,14209333,14209342,14209348,14209471};
var source = new ElementId(10518812L);
var target = new ElementId(1151099L);
if (doc.Title != "US-SVL-BRGUP123_MEP"
    || doc.GetElement(source)?.Name != "LEVEL 2" || doc.GetElement(target)?.Name != "LEVEL 1")
    throw new InvalidOperationException("Wrong diagnostic model or levels.");
Func<Element, ConnectorManager> manager = e => e is MEPCurve curve
    ? curve.ConnectorManager : (e as FamilyInstance)?.MEPModel?.ConnectorManager;
var neighborhood = new HashSet<long>(ids);
var frontier = new HashSet<long>(ids);
for (var depth = 0; depth < 3; depth++)
{
    var next = new HashSet<long>();
    foreach (var id in frontier)
    {
        var connectors = manager(doc.GetElement(new ElementId(id)));
        if (connectors == null) continue;
        foreach (Connector connector in connectors.Connectors)
        foreach (Connector connected in connector.AllRefs)
            if (neighborhood.Add(connected.Owner.Id.Value)) next.Add(connected.Owner.Id.Value);
    }
    frontier = next;
}
Func<XYZ,string> xyz = p => string.Join(",", new[]{p.X,p.Y,p.Z}.Select(v =>
    Math.Round(v,8).ToString("F8",System.Globalization.CultureInfo.InvariantCulture)));
Func<Dictionary<long,string>> snapshot = () => neighborhood.ToDictionary(id => id, id =>
{
    var e = doc.GetElement(new ElementId(id));
    if (e == null) return "missing";
    var state = e.Location is LocationCurve lc ? xyz(lc.Curve.GetEndPoint(0))+"/"+xyz(lc.Curve.GetEndPoint(1))
        : e.Location is LocationPoint lp ? xyz(lp.Point)+"/"+Math.Round(lp.Rotation,8) : "";
    var length = e.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_TAKEOFF_LENGTH);
    if (length is {StorageType:StorageType.Double,HasValue:true}) state += "/length:"+Math.Round(length.AsDouble(),8);
    var connectors = manager(e);
    if (connectors != null)
        state += string.Join(";",connectors.Connectors.Cast<Connector>()
            .Where(c => c.ConnectorType != ConnectorType.Logical).OrderBy(c => c.Id)
            .Select(c => c.Id+":"+xyz(c.Origin)+":"+xyz(c.CoordinateSystem.BasisZ)+":"+
                string.Join(",",c.AllRefs.Cast<Connector>().Select(r => r.Owner.Id.Value+":"+r.Id).OrderBy(s => s))));
    return state;
});
var before = snapshot();
var serviceType = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService",true);
var service = Activator.CreateInstance(serviceType,new object[]{doc});
var execute = serviceType.GetMethods().Single(m => m.Name == "Execute" && m.GetParameters().Length == 5);
Func<ElementId,ElementId,object> transfer = (from,to) =>
    execute.Invoke(service,new object[]{ids.Select(id => new ElementId(id)).ToList(),to,from,null,null});
Func<object,object[]> rows = report => ((System.Collections.IEnumerable)report.GetType()
    .GetProperty("Items").GetValue(report)).Cast<object>().Select(item => (object)new {
        id = item.GetType().GetProperty("Id").GetValue(item),
        status = item.GetType().GetProperty("Status").GetValue(item).ToString(),
        detail = item.ToString() }).ToArray();
var results = new List<object>();
string rollback;
using (var group = new TransactionGroup(doc,"ReLevel R94 validation - rollback"))
{
    group.Start();
    try
    {
        foreach (var direction in new[]{false,true})
        {
            var report = transfer(direction ? target : source, direction ? source : target);
            // An exception or a critical stop must escape before further model reads.
            if ((bool)report.GetType().GetProperty("Stopped").GetValue(report))
                throw new InvalidOperationException("Transfer stopped critically; ending diagnostic.");
            var after = snapshot();
            results.Add(new {reverse = direction,rows = rows(report),
                changed = neighborhood.Where(id => before[id] != after[id]).ToArray()});
            if ((int)report.GetType().GetProperty("Failed").GetValue(report) != 0) break;
        }
    }
    finally
    {
        rollback = group.RollBack().ToString();
        if (rollback != "RolledBack") throw new InvalidOperationException("Diagnostic rollback failed.");
    }
}
var restored = snapshot();
return new {mvid=assembly.ManifestModule.ModuleVersionId,build=app.Application.VersionBuild,
    checkedElements=neighborhood.Count,results,rollback,
    restored=neighborhood.All(id => before[id] == restored[id])};
