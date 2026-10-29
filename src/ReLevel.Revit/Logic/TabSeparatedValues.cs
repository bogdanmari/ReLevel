using System.Text;

namespace ReLevel.Revit.Logic;

internal static class TabSeparatedValues
{
    public static string Format(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var output = new StringBuilder();
        void Row(IEnumerable<string> fields) => output.AppendJoin('\t', fields.Select(Escape)).Append("\r\n");
        Row(headers);
        foreach (var row in rows) Row(row);
        return output.ToString();
    }

    private static string Escape(string value) => value.IndexOfAny(['\t', '\r', '\n', '"']) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
