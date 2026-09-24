using Autodesk.Revit.DB;
using System.Linq.Expressions;

namespace ReLevel.Revit;

internal static class ElementIds
{
    private static readonly Func<ElementId, long> Read = CreateReader();
    private static readonly Func<long, ElementId> Factory = CreateFactory();

    public static long ToLong(this ElementId id) => Read(id);
    public static ElementId Create(long value) => Factory(value);

    private static Func<ElementId, long> CreateReader()
    {
        // Bind to the host API once; use the full 64-bit Value in Revit 2024.
        var id = Expression.Parameter(typeof(ElementId));
        var property = typeof(ElementId).GetProperty("Value") ?? typeof(ElementId).GetProperty("IntegerValue")
            ?? throw new NotSupportedException("Не найдено свойство значения ElementId.");
        return Expression.Lambda<Func<ElementId, long>>(
            Expression.Convert(Expression.Property(id, property), typeof(long)), id).Compile();
    }

    private static Func<long, ElementId> CreateFactory()
    {
        var value = Expression.Parameter(typeof(long));
        var constructor = typeof(ElementId).GetConstructor([typeof(long)]) ?? typeof(ElementId).GetConstructor([typeof(int)])
            ?? throw new NotSupportedException("Не найден числовой конструктор ElementId.");
        return Expression.Lambda<Func<long, ElementId>>(Expression.New(constructor,
            Expression.ConvertChecked(value, constructor.GetParameters()[0].ParameterType)), value).Compile();
    }
}
