using Autodesk.Revit.DB;

namespace ReLevel.Revit.Transfer;

internal static class ViewRecreationSupport
{
    // Shared by the table and execution; target level and name are checked on execution.
    public static string? UnsupportedReason(View view)
    {
        if (view is not ViewPlan || view.IsTemplate
            || view.ViewType is not (ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan))
            return L.Get("Поддерживаются только планы этажей, потолков и конструкций.");
        if (view.GetPrimaryViewId() != ElementId.InvalidElementId || view.GetDependentViewIds().Count > 0)
            return L.Get("Зависимые виды и виды с зависимыми видами пока не поддерживаются.");
        return view.GenLevel is null ? L.Get("Не удалось определить исходный уровень.") : null;
    }
}
