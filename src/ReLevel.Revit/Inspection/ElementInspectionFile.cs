using System.IO;
using System.Reflection;
using System.Text;

namespace ReLevel.Revit.Inspection;

internal static class ElementInspectionFile
{
    public static string ResolvePath()
    {
        var root = typeof(ElementInspectionFile).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "ReLevelProjectDirectory").Value;
        if (string.IsNullOrWhiteSpace(root) || !File.Exists(Path.Combine(root, "ReLevel.sln")))
            throw new IOException(L.Format($"Папка проекта из сборки недоступна: {root}. Пересоберите плагин из текущей папки проекта."));
        return Path.Combine(root, "ELEMENTS.md");
    }

    public static void Append(string path, string markdown)
    {
        // Exclusive writer: preserve all previous records and any handwritten notes.
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var originalLength = stream.Length;
        var text = (originalLength == 0 ? "# ReLevel — инспекция элементов\n" : "") + "\n\n" + markdown + "\n";
        var bytes = new UTF8Encoding(false).GetBytes(text);
        try
        {
            stream.Position = originalLength;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception writeError)
        {
            try { stream.SetLength(originalLength); stream.Flush(flushToDisk: true); }
            catch (Exception rollbackError) { throw new AggregateException(writeError, rollbackError); }
            throw;
        }
    }
}
