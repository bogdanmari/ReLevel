using Autodesk.Revit.DB;
using ReLevel.Revit.Transfer;
using static ReLevel.Revit.Inspection.InspectionMarkdown;

namespace ReLevel.Revit.Inspection;

internal sealed record ElementInspectionResult(string Markdown, int ReadErrors);

internal sealed class ElementInspectionReport
{
    private readonly InspectionMarkdown output = new();
    private Document document = null!;

    public ElementInspectionResult Capture(Element element)
    {
        document = element.Document;
        output.Heading($"Элемент ID {element.Id.Value} — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        output.Section("Документ и элемент", () =>
        {
            output.Table("Свойство", "Значение");
            output.Field("Revit", () => $"{document.Application.VersionName}; {document.Application.VersionNumber}; build {document.Application.VersionBuild}");
            output.Field("Document.Title", () => document.Title);
            output.Field("Document.PathName", () => document.PathName);
            output.Field("ProjectInformation.UniqueId", () => document.ProjectInformation?.UniqueId);
            output.Field("Document.IsWorkshared", () => document.IsWorkshared.ToString());
            output.Field("Element.Id", () => element.Id.Value.ToString());
            output.Field("Element.UniqueId", () => element.UniqueId);
            output.Field("API class", () => element.GetType().FullName);
            output.Field("Name", () => element.Name);
            output.Field("Category", () => element.Category is { } category
                ? $"{category.Name}; ID {category.Id.Value}; {category.BuiltInCategory}" : null);
            output.Field("GetTypeId()", () => Describe(element.GetTypeId()));
            output.Field("LevelId", () => Describe(element.LevelId));
            output.Field("Pinned", () => element.Pinned.ToString());
            output.Field("GroupId", () => Describe(element.GroupId));
            output.Field("AssemblyInstanceId", () => Describe(element.AssemblyInstanceId));
            output.Field("DesignOption", () => Describe(element.DesignOption?.Id));
            output.Field("WorksetId", () => element.WorksetId.IntegerValue.ToString());
            output.Field("OwnerViewId", () => Describe(element.OwnerViewId));
            output.Field("ViewSpecific", () => element.ViewSpecific.ToString());
            output.Field("CreatedPhaseId", () => Describe(element.CreatedPhaseId));
            output.Field("DemolishedPhaseId", () => Describe(element.DemolishedPhaseId));
        });
        output.Section("Размещение", () => WriteLocation(element));
        if (element is FamilyInstance instance)
            output.Section("FamilyInstance — свойства API", () => WriteFamily(instance));
        if (element is FamilyInstance column && element.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns)
            output.Section("Колонна — присоединения и соединения", () => WriteColumn(column));
        output.Section("Проверка реализованных кейсов (без переноса)", () =>
        {
            output.Table("Кейс", "Принадлежность");
            foreach (var transferCase in TransferCases.All)
                output.Field(transferCase.Name, () => transferCase.UnsupportedReason(element) ?? "Соответствует условиям отбора");
            output.Field("Кейс 1: ограничение записи уровня", () => PointFamilyCase.UnsupportedReason(element) is null
                ? PointFamilyCase.WriteRestriction(element) ?? "Нет ограничения записи параметра уровня" : "Не применяется");
        });
        var parameters = new InspectionParameters(output, Describe);
        output.Section("Параметры экземпляра", () => parameters.Write(element));
        output.Section("Параметры типа", () =>
        {
            var type = document.GetElement(element.GetTypeId());
            if (type is not null) parameters.Write(type);
            else { output.Table("Свойство", "Значение"); output.Row("Тип", "Отсутствует"); }
        });
        output.Section("Непосредственные зависимые элементы (GetDependentElements)", () =>
        {
            var ids = element.GetDependentElements(null).OrderBy(id => id.Value).ToList();
            output.Table("Свойство", "Значение");
            output.Row("Количество", ids.Count.ToString());
            foreach (var id in ids) output.Field($"ID {id.Value}", () => Describe(id));
        });
        if (element is FamilyInstance or MEPCurve)
            output.Section("MEP-коннекторы", () => WriteConnectors(element));
        output.Section("Итог снимка", () =>
        {
            output.Table("Свойство", "Значение");
            output.Row("Ошибок чтения", output.ReadErrors.ToString());
            output.Row("Граница анализа", "Только чтение. Перенос и пробное удаление не выполнялись. Снимок не подтверждает сохранность геометрии и зависимостей при переносе.");
        });
        return new(output.ToString(), output.ReadErrors);
    }

    private string Describe(ElementId? id)
    {
        if (id is null) return "null";
        if (id == ElementId.InvalidElementId) return "-1 (InvalidElementId)";
        var referenced = document.GetElement(id);
        if (referenced is null) return $"ID {id.Value} (не разрешён в элемент документа; возможное служебное значение)";
        var result = $"ID {id.Value}; {referenced.GetType().Name}; {referenced.Name}";
        if (referenced is Level level)
            result += $"; Elevation={Number(level.Elevation)}; ProjectElevation={Number(level.ProjectElevation)} ft";
        return result;
    }

    private static string Point(XYZ point) => $"({Number(point.X)}, {Number(point.Y)}, {Number(point.Z)})";

    private void WriteColumn(FamilyInstance column)
    {
        output.Table("Свойство", "Значение");
        output.Field("IsSlantedColumn", () => column.IsSlantedColumn.ToString());
        foreach (var end in new[] { 0, 1 })
            output.Field(end == 0 ? "ColumnAttachment — низ" : "ColumnAttachment — верх", () =>
            {
                using var attachment = ColumnAttachment.GetColumnAttachment(column, end);
                return attachment is null ? "null" : $"Target={Describe(attachment.TargetId)}; Offset={Number(attachment.AttachOffset)} ft; CutStyle={attachment.CutStyle}; Justification={attachment.Justification}";
            });
        output.Field("JoinGeometryUtils.GetJoinedElements", () => string.Join("; ", JoinGeometryUtils.GetJoinedElements(document, column).Select(Describe)));
        output.Field("GetCopingIds", () => string.Join("; ", column.GetCopingIds().Select(Describe)));
        output.Field("SolidSolidCutUtils.GetCuttingSolids", () => string.Join("; ", SolidSolidCutUtils.GetCuttingSolids(column).Select(Describe)));
        output.Field("SolidSolidCutUtils.GetSolidsBeingCut", () => string.Join("; ", SolidSolidCutUtils.GetSolidsBeingCut(column).Select(Describe)));
        output.Field("InstanceVoidCutUtils.GetCuttingVoidInstances", () => string.Join("; ", InstanceVoidCutUtils.GetCuttingVoidInstances(column).Select(Describe)));
    }

    private void WriteLocation(Element element)
    {
        output.Table("Свойство", "Значение");
        var location = element.Location;
        output.Field("Location class", () => location?.GetType().FullName);
        if (location is LocationPoint point)
        {
            output.Field("LocationPoint.Point (ft)", () => Point(point.Point));
            output.Field("LocationPoint.Rotation (rad)", () => Number(point.Rotation));
        }
        else if (location is LocationCurve curveLocation)
        {
            var curve = curveLocation.Curve;
            output.Field("LocationCurve.Curve class", () => curve?.GetType().FullName);
            if (curve is not null)
            {
                output.Field("Curve.IsBound", () => curve.IsBound.ToString());
                output.Field("Curve.GetEndPoint(0) (ft)", () => curve.IsBound ? Point(curve.GetEndPoint(0)) : "Не ограничена");
                output.Field("Curve.GetEndPoint(1) (ft)", () => curve.IsBound ? Point(curve.GetEndPoint(1)) : "Не ограничена");
                output.Field("Curve.Length (ft)", () => curve.IsBound ? Number(curve.Length) : "Не ограничена");
            }
        }
        output.Field("BoundingBox (model, ft)", () => element.get_BoundingBox(null) is { } box
            ? $"Min={Point(box.Min)}; Max={Point(box.Max)}; Origin={Point(box.Transform.Origin)}; BasisX={Point(box.Transform.BasisX)}; BasisY={Point(box.Transform.BasisY)}; BasisZ={Point(box.Transform.BasisZ)}" : null);
    }

    private void WriteFamily(FamilyInstance instance)
    {
        output.Table("Свойство", "Значение");
        output.Field("Symbol.Family.Name", () => instance.Symbol.Family.Name);
        output.Field("Family.IsInPlace", () => instance.Symbol.Family.IsInPlace.ToString());
        output.Field("Family.FamilyPlacementType", () => instance.Symbol.Family.FamilyPlacementType.ToString());
        output.Field("Host (свойство, не параметр)", () => Describe(instance.Host?.Id));
        output.Field("HostFace", () => instance.HostFace is { } face
            ? $"ElementId={face.ElementId.Value}; LinkedElementId={face.LinkedElementId.Value}; StableReference={face.ConvertToStableRepresentation(document)}" : null);
        output.Field("SuperComponent", () => Describe(instance.SuperComponent?.Id));
        output.Field("GetSubComponentIds()", () => string.Join("; ", instance.GetSubComponentIds().OrderBy(id => id.Value).Select(Describe)));
        output.Field("Mirrored", () => instance.Mirrored.ToString());
        output.Field("HandFlipped", () => instance.HandFlipped.ToString());
        output.Field("FacingFlipped", () => instance.FacingFlipped.ToString());
        output.Field("HandOrientation", () => Point(instance.HandOrientation));
        output.Field("FacingOrientation", () => Point(instance.FacingOrientation));
        output.Field("GetTransform()", () =>
        {
            var transform = instance.GetTransform();
            return $"Origin={Point(transform.Origin)} ft; BasisX={Point(transform.BasisX)}; BasisY={Point(transform.BasisY)}; BasisZ={Point(transform.BasisZ)}";
        });
    }

    private void WriteConnectors(Element element)
    {
        var manager = element switch
        {
            FamilyInstance family => family.MEPModel?.ConnectorManager,
            MEPCurve curve => curve.ConnectorManager,
            _ => null
        };
        output.Table("Свойство", "Значение");
        if (manager is null) { output.Row("ConnectorManager", "null"); return; }
        var connectors = manager.Connectors.Cast<Connector>().OrderBy(c => c.Id).ToList();
        output.Row("Количество", connectors.Count.ToString());
        foreach (var connector in connectors)
        {
            var prefix = $"Connector {connector.Id}";
            output.Field(prefix + ".Domain", () => connector.Domain.ToString());
            output.Field(prefix + ".ConnectorType", () => connector.ConnectorType.ToString());
            output.Field(prefix + ".Origin (ft)", () => Point(connector.Origin));
            output.Field(prefix + ".IsConnected", () => connector.IsConnected.ToString());
            output.Field(prefix + ".AllRefs (включая логические)", () => string.Join("; ",
                connector.AllRefs.Cast<Connector>().Select(other => $"{Describe(other.Owner.Id)}; Connector {other.Id}")));
        }
    }
}
