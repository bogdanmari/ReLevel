global using ReLevel.Revit.Compatibility;

namespace ReLevel.Revit.Compatibility
{
    internal static class Numeric
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal static class FrameworkExtensions
    {
        public static bool Contains(this string value, string part, StringComparison comparison) => value.IndexOf(part, comparison) >= 0;
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> values) => values.OrderBy(value => value);
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> values, TKey key, TValue fallback)
            => values.TryGetValue(key, out var value) ? value : fallback;
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
