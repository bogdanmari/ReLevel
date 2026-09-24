using Autodesk.Revit.DB;

namespace ReLevel.Revit;

internal static class FloorApi
{
    public static SlabShapeEditor GetSlabShapeEditor(this Floor floor) => floor.SlabShapeEditor;
}
