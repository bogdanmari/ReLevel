using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace ReLevel.Revit.Transfer;

internal sealed class MepCurveStrategy : ITransferStrategy
{
    public bool Matches(Element e) => e is Pipe or Duct or CableTray or Conduit;
    public IReadOnlyList<TransferDependency> GetDependencies(Element e) => TransferDependencies.Collect(e, false, false);
    public string? UnsupportedReason(Element e) => e.Location is LocationCurve { Curve: Line }
        ? null : L.Get("Поддерживаются только прямые участки труб, воздуховодов, лотков и коробов.");
    public IReadOnlyList<LevelParameterPair> GetBindings(Element e)
    {
        if (e.get_Parameter(BuiltInParameter.RBS_START_OFFSET_PARAM) is { StorageType: StorageType.Double, HasValue: true }
            && e.get_Parameter(BuiltInParameter.RBS_END_OFFSET_PARAM) is { StorageType: StorageType.Double, HasValue: true })
        {
            if (e.get_Parameter(BuiltInParameter.RBS_END_LEVEL_PARAM) is
                { StorageType: StorageType.ElementId, HasValue: true } endLevel
                && e.Document.GetElement(endLevel.AsElementId()) is Level
                && (!endLevel.IsReadOnly || endLevel.AsElementId() != e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()))
                return [new(BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.RBS_START_OFFSET_PARAM, AllowDerivedOffsets: true),
                    new(BuiltInParameter.RBS_END_LEVEL_PARAM, BuiltInParameter.RBS_END_OFFSET_PARAM, AllowDerivedOffsets: true)];
            return [new(BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.RBS_START_OFFSET_PARAM,
                SecondOffset: BuiltInParameter.RBS_END_OFFSET_PARAM, AllowDerivedOffsets: true)];
        }
        // A single middle-elevation shift must preserve endpoints and slope; the
        // geometry and connector snapshots independently verify that assumption.
        return [new(BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.RBS_OFFSET_PARAM, AllowDerivedOffsets: true)];
    }
}
