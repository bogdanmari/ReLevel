using System.Globalization;
using System.Text;

namespace ReLevel.Revit.Inspection;

internal sealed class InspectionMarkdown
{
    private readonly StringBuilder text = new();
    public int ReadErrors { get; private set; }
    public override string ToString() => text.ToString();

    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    public static string Escape(string? value) => (value ?? "null")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("|", "&#124;").Replace("`", "&#96;")
        .Replace("\\", "&#92;").Replace("[", "&#91;").Replace("]", "&#93;")
        .Replace("*", "&#42;").Replace("_", "&#95;")
        .Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "<br>");

    public string Read(Func<string?> read)
    {
        try { return read() ?? "null"; }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            ReadErrors++;
            return $"ОШИБКА ЧТЕНИЯ ({ex.GetType().Name}): {ex.Message}";
        }
    }

    public void Section(string title, Action write)
    {
        text.AppendLine().Append("### ").AppendLine(Escape(title)).AppendLine();
        var error = Read(() => { write(); return ""; });
        if (error.Length > 0) text.AppendLine().AppendLine(Escape(error));
    }

    public void Heading(string title) => text.Append("## ").AppendLine(Escape(title));
    public void Table(params string[] columns)
    {
        Row(columns);
        Row(columns.Select(_ => "---").ToArray());
    }
    public void Row(params string[] values) => text.Append("| ")
        .Append(string.Join(" | ", values.Select(Escape))).AppendLine(" |");
    public void Field(string name, Func<string?> read) => Row(name, Read(read));
}
