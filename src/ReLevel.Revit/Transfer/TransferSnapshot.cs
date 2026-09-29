using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

// One snapshot owns the declared operation boundary. Nothing outside that boundary
// becomes permissible just because Revit reported a modification to it.
internal sealed class TransferSnapshot
{
    private readonly TransferPlan plan;
    private readonly ElementState root;
    private readonly Dictionary<long, ElementState> dependencies;
    private readonly Dictionary<long, ParameterValue> unchangedBindings;

    public TransferSnapshot(Element element, TransferPlan plan)
    {
        this.plan = plan;
        root = CaptureState(element, includeParameters: false);
        dependencies = plan.Dependencies.ToDictionary(d => d.Id.Value,
            d => CaptureState(element.Document.GetElement(d.Id)
                ?? throw new InvalidOperationException(L.Format($"Элемент ID {d.Id.Value} больше не существует.")), includeParameters: true));
        // Include inactive optional ends (for example an unconnected wall top).
        var changed = plan.Bindings.Where(b => b.Changes)
            .SelectMany(b => Parameters(b.Parameters)).ToHashSet();
        unchangedBindings = plan.Strategy!.GetBindings(element)
            .SelectMany(Parameters).Distinct().Where(p => !changed.Contains(p))
            .ToDictionary(p => (long)p, p => ParameterValue.Read(element.get_Parameter(p)));
    }

    public bool AllowsModification(ElementId id) => id == plan.Id
        || plan.Dependencies.Any(d => d.Id == id && d.AllowVerifiedChanges);

    private static ElementState CaptureState(Element element, bool includeParameters)
    {
        try
        {
            return new(element, includeParameters);
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException(L.Format($"Проверка ID {element.Id.Value}: {ex.Message}"), ex); }
    }

    public void Verify(Document document)
    {
        var element = document.GetElement(plan.Id)
            ?? throw new InvalidOperationException(L.Format($"Элемент ID {plan.Id.Value} больше не существует."));
        foreach (var binding in plan.Bindings)
        {
            var level = element.get_Parameter(binding.Parameters.Level);
            var offset = element.get_Parameter(binding.Parameters.Offset);
            if (level is not { StorageType: StorageType.ElementId, HasValue: true }
                || offset is not { StorageType: StorageType.Double, HasValue: true }
                || level.AsElementId() != binding.ResultLevelId
                || !double.IsFinite(offset.AsDouble())
                || Math.Abs(offset.AsDouble() - binding.ResultOffset) > GeometrySnapshot.Tolerance)
                throw new InvalidOperationException(L.Format($"ID {element.Id.Value}: привязка {binding.Parameters.Level} или смещение не совпадают с планом."));
            if (binding.Parameters.SecondOffset is { } secondId)
            {
                var value = element.get_Parameter(secondId)?.AsDouble();
                if (value is null || !double.IsFinite(value.Value)
                    || Math.Abs(value.Value - binding.ResultSecondOffset!.Value) > GeometrySnapshot.Tolerance)
                    throw new InvalidOperationException(L.Get("Конечное смещение MEP не совпадает с планом."));
            }
        }
        foreach (var (id, value) in unchangedBindings)
            if (ParameterValue.Read(element.get_Parameter((BuiltInParameter)id)) != value)
                throw new InvalidOperationException(L.Format($"ID {element.Id.Value}: изменена привязка вне плана переноса."));

        var replacements = plan.Bindings.Where(b => b.Changes).Select(b => b.OriginalLevelId.Value)
            .Distinct().ToDictionary(id => id, _ => plan.Context.TargetLevelId.Value);
        var ownerMaps = new Dictionary<long, IReadOnlyDictionary<long, long>> { [plan.Id.Value] = replacements };
        // Only hosted participants may follow an explicitly changed base/reference
        // level. A top-only wall change must not reassign the levels of its inserts.
        var hostChanges = plan.Bindings.Where(b => b.Changes && b.Parameters.Level is
            BuiltInParameter.WALL_BASE_CONSTRAINT or BuiltInParameter.LEVEL_PARAM or BuiltInParameter.FAMILY_BASE_LEVEL_PARAM
                or BuiltInParameter.FAMILY_LEVEL_PARAM or BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
            .Select(b => b.OriginalLevelId.Value).Distinct().ToDictionary(id => id, _ => plan.Context.TargetLevelId.Value);
        foreach (var dependency in plan.Dependencies.Where(d => d.FollowsHost))
            ownerMaps[dependency.Id.Value] = dependencies[dependency.Id.Value].InheritedChanges(document.GetElement(dependency.Id), hostChanges);
        root.Verify(element, ownerMaps);
        foreach (var (id, snapshot) in dependencies)
            snapshot.Verify(document.GetElement(new ElementId(id))
                ?? throw new InvalidOperationException(L.Format($"Элемент ID {id} больше не существует.")), ownerMaps);
        if (!plan.Strategy!.GetDependencies(element).OrderBy(d => d.Id.Value).SequenceEqual(plan.Dependencies.OrderBy(d => d.Id.Value)))
            throw new InvalidOperationException(L.Get("Состав зависимостей изменился при переносе."));
    }

    private static IEnumerable<BuiltInParameter> Parameters(LevelParameterPair pair)
        => pair.SecondOffset is { } second ? [pair.Level, pair.Offset, second] : [pair.Level, pair.Offset];

    private sealed record ParameterValue(StorageType Type, bool HasValue, object? Value)
    {
        public static ParameterValue Read(Parameter? parameter) => parameter is null
            ? new(StorageType.None, false, null)
            : new(parameter.StorageType, parameter.HasValue, !parameter.HasValue ? null : parameter.StorageType switch
            {
                StorageType.ElementId => parameter.AsElementId().Value,
                StorageType.Double => parameter.AsDouble(),
                StorageType.Integer => parameter.AsInteger(),
                StorageType.String => parameter.AsString(),
                _ => null
            });
    }

    private sealed class ElementState
    {
        private readonly ParticipantGeometry geometry;
        private readonly MepSnapshot mep;
        private readonly long levelId;
        private readonly string identity;
        private readonly LevelRelationResult relations;
        private readonly Dictionary<long, ParameterValue>? parameters;

        public ElementState(Element element, bool includeParameters)
        {
            geometry = new(element);
            mep = new(element);
            levelId = element.LevelId.Value;
            identity = Identity(element);
            relations = new LevelRelationFinder(element.Document).Find(element);
            if (relations.ReadFailed) throw new InvalidOperationException(string.Join(Environment.NewLine, relations.Notices));
            if (includeParameters) parameters = ReadParameters(element);
        }

        public IReadOnlyDictionary<long, long> InheritedChanges(Element element, IReadOnlyDictionary<long, long> permitted)
        {
            var result = new Dictionary<long, long>();
            if (element is not FamilyInstance || element.Pinned || element.GroupId != ElementId.InvalidElementId
                || element.AssemblyInstanceId != ElementId.InvalidElementId || element.DesignOption is not null) return result;
            if (permitted.TryGetValue(levelId, out var inheritedLevel) && element.LevelId.Value == inheritedLevel)
                result[levelId] = inheritedLevel;
            foreach (var r in relations.Relations.Where(r => r.Path.Count == 0 && r.Parameter is { } p && IsInheritedLevel((long)p)))
                if (permitted.TryGetValue(r.LevelId.Value, out var target)
                    && element.get_Parameter(r.Parameter!.Value)?.AsElementId().Value == target) result[r.LevelId.Value] = target;
            return result;
        }

        public void Verify(Element element, IReadOnlyDictionary<long, IReadOnlyDictionary<long, long>> ownerMaps)
        {
            try
            {
                geometry.Verify(element);
                mep.Verify(element);
                var replacements = ownerMaps.GetValueOrDefault(element.Id.Value) ?? new Dictionary<long, long>();
                var currentRelations = new LevelRelationFinder(element.Document).Find(element);
                if (currentRelations.ReadFailed) throw new InvalidOperationException(string.Join(Environment.NewLine, currentRelations.Notices));
                if (Identity(element) != identity || element.LevelId.Value != Replace(levelId, replacements)
                    || !RelationKeys(relations, ownerMaps).SequenceEqual(RelationKeys(currentRelations, new Dictionary<long, IReadOnlyDictionary<long, long>>()))
                    || !relations.Notices.Order().SequenceEqual(currentRelations.Notices.Order()))
                    throw new InvalidOperationException(L.Get("Хост, связи с уровнями или состояние элемента изменились вне плана."));
                if (parameters is not null)
                {
                    var current = ReadParameters(element);
                    if (current.Count != parameters.Count || parameters.Any(p => !current.TryGetValue(p.Key, out var value)
                        || !PermittedParameter(element.Document, p.Key, p.Value, value, replacements)))
                        throw new InvalidOperationException(L.Get("Параметры зависимого элемента изменились; откат связанной операции."));
                }
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { throw new InvalidOperationException(L.Format($"Проверка ID {element.Id.Value}: {ex.Message}"), ex); }
        }

        private static long Replace(long id, IReadOnlyDictionary<long, long> replacements)
            => replacements.TryGetValue(id, out var result) ? result : id;

        private static IEnumerable<string> RelationKeys(LevelRelationResult result, IReadOnlyDictionary<long, IReadOnlyDictionary<long, long>> ownerMaps)
            => result.Relations.Where(r => r.Path.Count > 0 || r.Parameter is not null).Select(r =>
                $"{r.OwnerId.Value}:{r.Kind}:{r.Parameter}:"
                + (ownerMaps.TryGetValue(r.OwnerId.Value, out var map) ? Replace(r.LevelId.Value, map) : r.LevelId.Value)
                + ":" + string.Join("/", r.Path.Select(p => $"{p.Route}:{p.ElementId.Value}"))).Order();

        private static string Identity(Element element)
        {
            var state = $"{element.UniqueId}|{element.Pinned}|{element.GroupId.Value}|{element.AssemblyInstanceId.Value}|{element.DesignOption?.Id.Value}";
            if (element is FamilyInstance f)
                state += $"|{f.Host?.UniqueId}|{f.HostFace?.ConvertToStableRepresentation(element.Document)}|{f.SuperComponent?.Id.Value}"
                    + $"|{f.Mirrored}|{f.HandFlipped}|{f.FacingFlipped}|" + string.Join(",", f.GetSubComponentIds().Select(id => id.Value).Order());
            state += "|" + ParameterValue.Read(element.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM));
            if (element is Wall { Location: LocationCurve location })
                for (var end = 0; end < 2; ++end)
                    state += "|" + string.Join(",", location.get_ElementsAtJoin(end).Cast<Element>().Select(e => e.Id.Value).Order());
            return element is MEPSystem or Opening ? state : state + "|"
                + string.Join(",", JoinGeometryUtils.GetJoinedElements(element.Document, element).OrderBy(id => id.Value)
                    .Select(id => $"{id.Value}:{JoinGeometryUtils.IsCuttingElementInJoin(element.Document, element, element.Document.GetElement(id))}"));
        }

        private static bool IsInheritedLevel(long id) => (BuiltInParameter)id is BuiltInParameter.FAMILY_LEVEL_PARAM
            or BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM or BuiltInParameter.SCHEDULE_LEVEL_PARAM or BuiltInParameter.LEVEL_PARAM;

        private static bool PermittedParameter(Document document, long id, ParameterValue before, ParameterValue after,
            IReadOnlyDictionary<long, long> replacements)
        {
            if (before == after) return true;
            if (IsInheritedLevel(id) && before.Value is long level && replacements.TryGetValue(level, out var target))
                return after == before with { Value = target };
            if ((BuiltInParameter)id == BuiltInParameter.INSTANCE_ELEVATION_PARAM && before.Value is double offset
                && after.Value is double actual && replacements.Count == 1)
            {
                var pair = replacements.Single();
                var expected = ReLevel.Revit.Logic.LevelTransfer.NewOffset(((Level)document.GetElement(new ElementId(pair.Key))).ProjectElevation,
                    ((Level)document.GetElement(new ElementId(pair.Value))).ProjectElevation, offset);
                return double.IsFinite(actual) && Math.Abs(expected - actual) <= GeometrySnapshot.Tolerance;
            }
            return false;
        }

        private static Dictionary<long, ParameterValue> ReadParameters(Element element)
            => element.Parameters.Cast<Parameter>().ToDictionary(p => p.Id.Value, ParameterValue.Read);
    }
}
