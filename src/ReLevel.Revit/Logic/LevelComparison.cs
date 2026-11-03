using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace ReLevel.Revit.Logic;

internal sealed record ComparisonLevel(string Name, double Elevation);
internal sealed record LevelComparisonResult(string Name, double? FileElevation, double? ModelElevation, string StatusKey);

internal static class LevelComparison
{
    public static string NormalizeName(string name) => name.Trim();
    public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    public const double ToleranceFeet = 0.001 / 304.8;

    public static IReadOnlyList<ComparisonLevel> Read(TextReader reader, Func<string, double?> parseElevation, Func<string, bool> acceptsHeaderUnit)
    {
        using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters("\t");
        var result = new List<ComparisonLevel>();
        bool first = true;
        while (!parser.EndOfData)
        {
            var line = parser.LineNumber;
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            if (fields.Length != 2) throw new FormatException($"Line {line}: expected two tab-separated fields.");
            var name = NormalizeName(fields[0].TrimStart('\uFEFF'));
            var height = fields[1].Trim();
            if (first && TryHeader(name, height, out var headerUnit))
            {
                if (!acceptsHeaderUnit(headerUnit)) throw new FormatException($"Line {line}: header units do not match Settings.");
                first = false; continue;
            }
            first = false;
            var elevation = parseElevation(height);
            if (name.Length == 0 || elevation is null || !double.IsFinite(elevation.Value))
                throw new FormatException($"Line {line}: invalid level name or elevation in the selected units.");
            result.Add(new(name, elevation.Value));
        }
        if (result.Count == 0) throw new FormatException("No level rows found.");
        return result;
    }

    private static bool TryHeader(string name, string height, out string unit)
    {
        unit = "";
        if (!new[] { "Name", "Level Name", "Level", "Название уровня", "Имя уровня", "Уровень" }.Contains(name, NameComparer)) return false;
        foreach (var prefix in new[] { "Elevation", "Отметка", "Высотная отметка" })
        {
            if (NameComparer.Equals(height, prefix)) return true;
            if (!height.StartsWith(prefix + ",", StringComparison.OrdinalIgnoreCase) &&
                !height.StartsWith(prefix + " (", StringComparison.OrdinalIgnoreCase)) continue;
            unit = height[prefix.Length..].Trim().TrimStart(',').Trim().Trim('(', ')').Trim();
            return true;
        }
        return false;
    }

    public static IReadOnlyList<LevelComparisonResult> Compare(IReadOnlyList<ComparisonLevel> file, IReadOnlyList<ComparisonLevel> model)
    {
        var fileGroups = file.GroupBy(row => NormalizeName(row.Name), NameComparer).ToDictionary(g => g.Key, g => g.ToList(), NameComparer);
        var modelGroups = model.GroupBy(row => NormalizeName(row.Name), NameComparer).ToDictionary(g => g.Key, g => g.ToList(), NameComparer);
        var result = new List<LevelComparisonResult>();
        foreach (var row in file)
        {
            var name = NormalizeName(row.Name);
            modelGroups.TryGetValue(name, out var matches);
            double? elevation = matches?.Count == 1 ? matches[0].Elevation : null;
            var status = fileGroups[name].Count > 1 ? "CompareDuplicateFile" : matches is null ? "CompareMissingModel"
                : matches.Count > 1 ? "CompareAmbiguous" : Math.Abs(row.Elevation - elevation!.Value) <= ToleranceFeet
                    ? "CompareMatch" : "CompareDifferent";
            result.Add(new(row.Name, row.Elevation, elevation, status));
        }
        result.AddRange(model.Where(row => !fileGroups.ContainsKey(NormalizeName(row.Name)))
            .Select(row => new LevelComparisonResult(row.Name, null, row.Elevation, "CompareMissingFile")));
        return result;
    }
}
