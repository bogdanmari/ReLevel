using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed record AnnotationCheck(bool Error, string Message);

// Capture before copying: comparing two live objects could miss a change to the source.
internal sealed class AnnotationSnapshot
{
    // Internal feet: approximately 0.003 mm.
    private const double Tolerance = 1e-5;
    private readonly Dictionary<string, string> text = [];
    private readonly Dictionary<string, double> numbers = [];
    private readonly Dictionary<string, ModelPoint[]> points = [];
    private readonly List<string> uncheckedFields = [];

    public AnnotationSnapshot(Element e, View view)
    {
        void Read(string field, Action action)
        {
            try { action(); }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { uncheckedFields.Add(field + ": " + ex.Message); }
        }
        ModelPoint Project(XYZ p) => new(p.DotProduct(view.RightDirection), p.DotProduct(view.UpDirection), 0);
        void Point(string key, params XYZ[] values) => points[key] = values.Select(Project).ToArray();
        void References(string key, IEnumerable<Reference> references)
        {
            var values = new List<string>();
            foreach (var reference in references)
            {
                if (e.Document.GetElement(reference.ElementId) is { ViewSpecific: true })
                {
                    uncheckedFields.Add(L.Get("Ссылка на объект вида требует ручного сопоставления с его копией."));
                    continue;
                }
                values.Add(reference.ConvertToStableRepresentation(e.Document));
            }
            text[key] = string.Join("\n", values.Order());
        }
        text["Class"] = e.GetType().FullName ?? "";
        text["Type"] = e.GetTypeId().Value.ToString();
        switch (e)
        {
            case TextNote note:
                Read("Text", () => text["Text"] = note.Text);
                Read("Text placement", () => { Point("Text placement", note.Coord, note.BaseDirection); numbers["Width"] = note.Width; });
                Read("Text leaders", () => points["Text leaders"] = note.GetLeaders()
                    .SelectMany(l => new[] { l.Anchor, l.Elbow, l.End }).Select(Project).ToArray());
                uncheckedFields.Add(L.Get("Форматирование отдельных фрагментов текста требует ручной проверки."));
                break;
            case IndependentTag tag:
                Read("Tag text", () => text["Tag text"] = tag.TagText);
                Read("Tag state", () => text["Tag state"] = $"{tag.IsOrphaned}:{tag.HasLeader}:{tag.TagOrientation}:{tag.LeaderEndCondition}");
                Read("Tag position", () => Point("Tag position", tag.TagHeadPosition));
                Read("Tag references", () => References("References", tag.GetTaggedReferences()));
                Read("Tag leaders", () =>
                {
                    var leaderPoints = new List<XYZ>();
                    var visibility = new List<string>();
                    foreach (var reference in tag.GetTaggedReferences())
                    {
                        if (!tag.HasLeader) break;
                        visibility.Add(tag.IsLeaderVisible(reference).ToString());
                        if (!tag.IsLeaderVisible(reference)) continue;
                        visibility.Add(tag.HasLeaderElbow(reference).ToString());
                        if (tag.HasLeaderElbow(reference)) leaderPoints.Add(tag.GetLeaderElbow(reference));
                        if (tag.LeaderEndCondition == LeaderEndCondition.Free) leaderPoints.Add(tag.GetLeaderEnd(reference));
                    }
                    text["Leader visibility"] = string.Join(":", visibility);
                    points["Tag leaders"] = leaderPoints.Select(Project).ToArray();
                });
                Read("Tag link validity", () =>
                {
                    if (tag.IsOrphaned) uncheckedFields.Add(L.Get("Марка имеет потерянную ссылку; требуется исправление вручную."));
                    if (tag.HasLeader && tag.LeaderEndCondition != LeaderEndCondition.Free)
                        uncheckedFields.Add(L.Get("Конец присоединённой выноски марки требует ручной проверки."));
                });
                break;
            case SpatialElementTag spatial:
                Read("Spatial tag text", () => text["Tag text"] = spatial.TagText);
                Read("Spatial tag state", () =>
                {
                    text["Tag state"] = $"{spatial.IsOrphaned}:{spatial.HasLeader}:{spatial.TagOrientation}";
                    numbers["Rotation"] = spatial.RotationAngle;
                    if (spatial.IsOrphaned) uncheckedFields.Add(L.Get("Марка имеет потерянную ссылку; требуется исправление вручную."));
                });
                Read("Spatial tag reference", () =>
                {
                    if (spatial is Autodesk.Revit.DB.Architecture.RoomTag room)
                    {
                        var id = room.TaggedRoomId;
                        text["References"] = $"{id.HostElementId.Value}:{id.LinkInstanceId.Value}:{id.LinkedElementId.Value}";
                    }
                    else if (!spatial.IsTaggingLink && spatial is AreaTag { Area: { } area })
                        text["References"] = area.UniqueId;
                    else if (!spatial.IsTaggingLink && spatial is Autodesk.Revit.DB.Mechanical.SpaceTag { Space: { } space })
                        text["References"] = space.UniqueId;
                    else uncheckedFields.Add(L.Get("Ссылка пространственной марки требует ручной проверки."));
                });
                Read("Spatial tag placement", () =>
                {
                    Point("Tag position", spatial.TagHeadPosition);
                    if (spatial.HasLeader)
                    {
                        Point("Leader end", spatial.LeaderEnd);
                        if (spatial.HasElbow) Point("Leader elbow", spatial.LeaderElbow);
                    }
                });
                break;
            case Dimension dimension:
                Read("Dimension references", () => References("References", dimension.References.Cast<Reference>()));
                Read("Dimension values", () =>
                {
                    var segments = dimension.Segments?.Cast<DimensionSegment>().ToArray() ?? [];
                    if (segments.Length == 0)
                    {
                        text["Override"] = dimension.ValueOverride ?? "";
                        text["Prefix"] = dimension.Prefix ?? "";
                        text["Suffix"] = dimension.Suffix ?? "";
                        text["Above"] = dimension.Above ?? "";
                        text["Below"] = dimension.Below ?? "";
                        if (dimension.Value is { } value) numbers["Dimension value"] = value;
                    }
                    text["Segment count"] = segments.Length.ToString();
                    for (var i = 0; i < segments.Length; ++i)
                    {
                        var s = segments[i];
                        text[$"Segment {i} override"] = s.ValueOverride ?? "";
                        text[$"Segment {i} prefix"] = s.Prefix ?? "";
                        text[$"Segment {i} suffix"] = s.Suffix ?? "";
                        text[$"Segment {i} above"] = s.Above ?? "";
                        text[$"Segment {i} below"] = s.Below ?? "";
                        if (s.Value is { } segmentValue) numbers[$"Segment value {i}"] = segmentValue;
                        Point($"Segment origin {i}", s.Origin);
                    }
                });
                Read("Dimension position", () =>
                {
                    if (dimension is SpotDimension) Point("Dimension origin", dimension.Origin);
                    else points["Dimension curve"] = dimension.Curve.Tessellate().Select(Project).ToArray();
                    if (dimension.IsTextPositionAdjustable()) Point("Dimension text position", dimension.TextPosition);
                });
                Read("Dimension leader", () => text["HasLeader"] = dimension.HasLeader.ToString());
                if (dimension is SpotDimension)
                    Read("Spot elevation values", () =>
                    {
                        var count = 0;
                        foreach (var parameterId in new[] { BuiltInParameter.SPOT_ELEV_SINGLE_OR_UPPER_VALUE,
                            BuiltInParameter.SPOT_ELEV_LOWER_VALUE })
                            if (dimension.get_Parameter(parameterId) is { StorageType: StorageType.Double, HasValue: true } value)
                            { numbers[parameterId.ToString()] = value.AsDouble(); count++; }
                        if (count == 0) uncheckedFields.Add(L.Get("Значение высотной отметки недоступно для автоматического сравнения."));
                    });
                uncheckedFields.Add(L.Get("Выноски и размещение текстов сегментов размера требуют ручной проверки."));
                break;
            case DetailCurve curve:
                Read("Detail curve", () => points["Detail curve"] = curve.GeometryCurve.Tessellate().Select(Project).ToArray());
                Read("Line style", () => text["Line style"] = curve.LineStyle.Id.Value.ToString());
                break;
            case FilledRegion region:
                Read("Region boundaries", () =>
                {
                    var loops = region.GetBoundaries();
                    text["Loop count"] = loops.Count.ToString();
                    for (var i = 0; i < loops.Count; ++i)
                        points[$"Loop {i}"] = loops[i].SelectMany(c => c.Tessellate()).Select(Project).ToArray();
                });
                break;
            default:
                uncheckedFields.Add(L.Get("Содержимое этого типа аннотации автоматически не проверяется."));
                break;
        }
    }

    public IReadOnlyList<AnnotationCheck> Verify(Element copy, View target)
    {
        var after = new AnnotationSnapshot(copy, target);
        var checks = uncheckedFields.Concat(after.uncheckedFields).Distinct().Select(message => new AnnotationCheck(false, message)).ToList();
        foreach (var (key, value) in text)
            if (after.text.TryGetValue(key, out var actual) && actual != value)
                checks.Add(new(true, L.Format($"Не совпадает содержимое аннотации: {key}.")));
        foreach (var (key, value) in numbers)
            if (after.numbers.TryGetValue(key, out var actual)
                && (!double.IsFinite(actual) || !double.IsFinite(value) || Math.Abs(value - actual) > Tolerance))
                checks.Add(new(true, L.Format($"Не совпадает значение аннотации: {key}.")));
        foreach (var (key, value) in points)
            if (after.points.TryGetValue(key, out var actual) && (value.Length != actual.Length
                || value.Where((p, i) => !p.IsWithin(actual[i], Tolerance)).Any()))
                checks.Add(new(true, L.Format($"Не совпадает положение аннотации: {key}.")));
        if (!text.Keys.Order().SequenceEqual(after.text.Keys.Order()) || !numbers.Keys.Order().SequenceEqual(after.numbers.Keys.Order())
            || !points.Keys.Order().SequenceEqual(after.points.Keys.Order()))
            checks.Add(new(false, L.Get("Не все свойства аннотации доступны для сравнения.")));
        return checks;
    }
}
