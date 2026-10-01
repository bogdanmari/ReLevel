using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

// Each case supplies its own level/offset parameter pair.
internal sealed record LevelOffsetBinding(BuiltInParameter LevelParameter, BuiltInParameter OffsetParameter)
{
    public LevelOffsetState Capture(Element element)
    {
        var level = element.Document.GetElement(Require(element, LevelParameter, StorageType.ElementId).AsElementId()) as Level
            ?? throw new InvalidOperationException(L.Get("Привязка не указывает на существующий уровень."));
        var position = new LevelEndPosition(level.ProjectElevation, Require(element, OffsetParameter, StorageType.Double).AsDouble());
        return new(this, level.Id.Value, position);
    }

    public static Parameter Require(Element element, BuiltInParameter name, StorageType storage) =>
        element.get_Parameter(name) is { } parameter && parameter.StorageType == storage ? parameter
        : throw new InvalidOperationException(L.Format($"Недоступен параметр привязки: {name}."));
}

internal sealed record LevelOffsetState(LevelOffsetBinding Binding, long LevelId, LevelEndPosition Position)
{
    public bool WriteOffset { get; init; } = true;
    public bool Changes(ElementId source) => LevelId == source.Value;
    public bool CanWrite(Element element) => !LevelOffsetBinding.Require(element, Binding.LevelParameter, StorageType.ElementId).IsReadOnly
        && (!WriteOffset || !LevelOffsetBinding.Require(element, Binding.OffsetParameter, StorageType.Double).IsReadOnly);

    public void Apply(Element element, ElementId source, Level target)
    {
        if (!Changes(source)) return;
        if (!LevelOffsetBinding.Require(element, Binding.LevelParameter, StorageType.ElementId).Set(target.Id)
            || (WriteOffset && !LevelOffsetBinding.Require(element, Binding.OffsetParameter, StorageType.Double)
                .Set(Position.OffsetAt(target.ProjectElevation))))
            throw new InvalidOperationException(L.Get("Revit отклонил запись уровня или смещения."));
    }

}
