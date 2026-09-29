using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class TransferAnalyzer
{
    private readonly ITransferStrategy[] strategies = [new ColumnStrategy(), new MepCurveStrategy(),
        new PointFamilyStrategy(), new HorizontalHostStrategy(true), new HorizontalHostStrategy(false), new WallStrategy()];

    public TransferPlan Analyze(Element e, TransferContext context)
    {
        var name = $"{e.Category?.Name}: {e.Name}";
        TransferPlan Skip(string reason) => TransferPlan.Skip(e.Id, name, context, reason);
        var document = e.Document;
        if (document.GetElement(context.TargetLevelId) is not Level target
            || context.Mode == TransferMode.SourceLevel && (context.SourceLevelId is null
                || document.GetElement(context.SourceLevelId) is not Level || context.SourceLevelId == target.Id)
            || context.Mode == TransferMode.Selection && context.SourceLevelId is not null)
            return Skip(L.Get("Некорректный режим или исходный/целевой уровень переноса."));
        if (e is Level or View || e.ViewSpecific || e is ElementType)
            return Skip(L.Get("Перенос поддерживается только для элементов модели."));
        if (e.Pinned) return Skip(L.Get("Элемент закреплён."));
        if (e.GroupId != ElementId.InvalidElementId || e.AssemblyInstanceId != ElementId.InvalidElementId)
            return Skip(L.Get("Элемент входит в группу или сборку."));
        if (e.DesignOption is not null) return Skip(L.Get("Элементы вариантов конструкции пока не поддерживаются."));
        if (e is FamilyInstance family && (family.StructuralType is Autodesk.Revit.DB.Structure.StructuralType.Beam
            or Autodesk.Revit.DB.Structure.StructuralType.Brace))
            return Skip(L.Get("Балки и раскосы не поддерживаются."));

        var relations = new LevelRelationFinder(document).Find(e);
        if (relations.ReadFailed) return Skip(string.Join(Environment.NewLine, relations.Notices));
        var levels = relations.Relations.Select(r => r.LevelId).Distinct().ToArray();
        if (levels.Length == 0) return Skip(L.Get("Не удалось определить исходный уровень."));
        if (context.Mode == TransferMode.Selection && levels.Length != 1)
            return Skip(L.Get("Найдено несколько разных уровней. Выберите исходный уровень вместо режима выделения."));
        var sourceId = context.Mode == TransferMode.SourceLevel ? context.SourceLevelId! : levels[0];
        if (!relations.IsOnLevel(sourceId)) return Skip(L.Get("Связь с исходным уровнем больше не найдена."));
        if (!relations.Relations.Any(r => r.LevelId == sourceId && r.Path.Count == 0))
            return Skip(L.Get("Зависит от хоста или рабочей плоскости; самостоятельная привязка к исходному уровню не найдена."));

        var strategy = strategies.FirstOrDefault(s => s.Matches(e));
        if (strategy is null) return Skip(L.Get("Механизм привязки этой категории пока не поддерживается."));
        if (strategy.UnsupportedReason(e) is { } reason) return Skip(reason);

        var bindings = new List<TransferBinding>();
        foreach (var pair in strategy.GetBindings(e))
        {
            if (bindings.Any(b => b.Parameters.Level == pair.Level || b.Parameters.Offset == pair.Offset))
                return Skip(L.Get("Механизм содержит пересекающиеся пары параметров привязки."));
            var levelParameter = e.get_Parameter(pair.Level);
            if (levelParameter is not { StorageType: StorageType.ElementId, HasValue: true })
                return Skip(L.Get("Параметры уровня/смещения отсутствуют или недоступны для записи."));
            var originalId = levelParameter.AsElementId();
            // Optional means an explicitly unconnected end, never a missing parameter.
            if (pair.Optional && originalId == ElementId.InvalidElementId) continue;
            if (document.GetElement(originalId) is not Level originalLevel)
                return Skip(L.Get("Не удалось определить исходный уровень."));
            var offsetParameter = e.get_Parameter(pair.Offset);
            if (offsetParameter is not { StorageType: StorageType.Double, HasValue: true })
                return Skip(L.Get("Параметры уровня/смещения отсутствуют или недоступны для записи."));
            var change = originalId == sourceId && originalId != target.Id;
            if (change && (levelParameter.IsReadOnly || pair.CompensateOffset && offsetParameter.IsReadOnly && !pair.AllowDerivedOffsets))
                return Skip(L.Get("Параметры уровня/смещения отсутствуют или недоступны для записи."));
            var originalOffset = offsetParameter.AsDouble();
            // Validate finite numbers for unchanged bindings as well.
            var resultOffset = LevelTransfer.NewOffset(originalLevel.ProjectElevation,
                change && pair.CompensateOffset ? target.ProjectElevation : originalLevel.ProjectElevation, originalOffset);
            double? originalSecond = null, resultSecond = null;
            if (pair.SecondOffset is { } secondId)
            {
                var second = e.get_Parameter(secondId);
                if (second is not { StorageType: StorageType.Double, HasValue: true } || change && second.IsReadOnly && !pair.AllowDerivedOffsets)
                    return Skip(L.Get("Параметры уровня/смещения отсутствуют или недоступны для записи."));
                originalSecond = second.AsDouble();
                resultSecond = LevelTransfer.NewOffset(originalLevel.ProjectElevation,
                    change ? target.ProjectElevation : originalLevel.ProjectElevation, originalSecond.Value);
            }
            bindings.Add(new(pair, originalId, originalOffset, change ? target.Id : originalId, resultOffset, originalSecond, resultSecond));
        }
        if (!bindings.Any(b => b.Changes))
            return Skip(sourceId == target.Id ? L.Get("Элемент уже на целевом уровне.")
                : L.Get("Связь с источником найдена, но этот механизм не изменяет соответствующую привязку."));

        var dependencies = strategy.GetDependencies(e).ToArray();
        if (dependencies.Any(d => d.Id == e.Id || document.GetElement(d.Id) is null)
            || dependencies.Select(d => d.Id).Distinct().Count() != dependencies.Length)
            return Skip(L.Get("Состав зависимостей переноса некорректен или содержит недоступные объекты."));
        return new(e.Id, name, context, strategy, bindings.ToArray(), dependencies, null);
    }
}
