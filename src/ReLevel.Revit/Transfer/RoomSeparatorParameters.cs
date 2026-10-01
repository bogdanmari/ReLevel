using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class RoomSeparatorParameters
{
    public static void Copy(Element source, Element target)
    {
        var destinations = target.Parameters.Cast<Parameter>().ToDictionary(p => p.Id.Value);
        foreach (var parameter in source.Parameters.Cast<Parameter>()
            .Where(p => !p.IsReadOnly && p.HasValue).OrderBy(PhaseOrder))
        {
            if (!destinations.TryGetValue(parameter.Id.Value, out var destination)
                || destination.StorageType != parameter.StorageType)
                throw new InvalidOperationException(L.Format($"Кейс 8: параметр недоступен для переноса: {parameter.Definition.Name}."));
            // Newly created lines already have some source values (notably phases).
            // Avoid no-op Set calls; this is part of copying, not post-transfer validation.
            if (destination.HasValue && SameValue(parameter, destination)) continue;
            if (destination.IsReadOnly)
                throw new InvalidOperationException(L.Format($"Кейс 8: параметр недоступен для переноса: {parameter.Definition.Name}."));
            var set = parameter.StorageType switch
            {
                StorageType.Double => destination.Set(parameter.AsDouble()),
                StorageType.Integer => destination.Set(parameter.AsInteger()),
                StorageType.ElementId => destination.Set(parameter.AsElementId()),
                StorageType.String => destination.Set(parameter.AsString() ?? ""),
                _ => false
            };
            if (!set)
                throw new InvalidOperationException(L.Format($"Кейс 8: Revit отклонил запись параметра: {parameter.Definition.Name}."));
        }
    }

    private static bool SameValue(Parameter source, Parameter target) => source.StorageType switch
    {
        StorageType.Double => source.AsDouble() == target.AsDouble(),
        StorageType.Integer => source.AsInteger() == target.AsInteger(),
        StorageType.ElementId => source.AsElementId() == target.AsElementId(),
        StorageType.String => source.AsString() == target.AsString(),
        _ => false
    };

    private static int PhaseOrder(Parameter parameter) => parameter.Id.Value switch
    {
        (long)BuiltInParameter.PHASE_CREATED => 0,
        (long)BuiltInParameter.PHASE_DEMOLISHED => 2,
        _ => 1
    };
}
