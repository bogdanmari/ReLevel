using System.Linq.Expressions;
using Autodesk.Revit.DB;

namespace ReLevel.Revit;

internal static class ParameterAccess
{
    private static readonly Func<Element, long, Parameter> Read = CreateReader();

    public static Parameter ParameterById(this Element element, long id) => Read(element, id);

    private static Func<Element, long, Parameter> CreateReader()
    {
        // BuiltInParameter changed from Int32 to Int64 in 2024. Generate the call
        // for the host's actual enum instead of embedding its old ABI in IL.
        var enumType = typeof(Element).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameter", true)!;
        var method = typeof(Element).GetMethod("get_Parameter", [enumType])
            ?? throw new NotSupportedException("Не найден доступ к встроенным параметрам Revit.");
        var element = Expression.Parameter(typeof(Element));
        var id = Expression.Parameter(typeof(long));
        return Expression.Lambda<Func<Element, long, Parameter>>(
            Expression.Call(element, method, Expression.ConvertChecked(id, enumType)), element, id).Compile();
    }
}
