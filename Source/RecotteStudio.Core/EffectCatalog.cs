using System.Globalization;
using System.Text.RegularExpressions;

#pragma warning disable CS1591
namespace RecotteStudio.Core;

public sealed record EffectDefinition(string Key, string Kind, string Tag, string? NameJa, string? NameEn,
    decimal? DefaultDuration, string RelativePath, IReadOnlyList<string> PropertyKeys)
{
    public bool CanEdit => Kind == "effects" && Key is "Alpha Fade" or "Alpha Fade Out";
}

/// <summary>Reads literal metadata from installed Lua definitions without executing scripts.</summary>
public sealed class EffectCatalog
{
    public string Directory { get; }
    public IReadOnlyList<EffectDefinition> Effects { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    private EffectCatalog(string directory, List<EffectDefinition> effects, List<string> diagnostics)
    { Directory = directory; Effects = effects.AsReadOnly(); Diagnostics = diagnostics.AsReadOnly(); }

    public static EffectCatalog Load(string directory)
    {
        string root = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(root)) throw new DirectoryNotFoundException($"Effects directory does not exist: {root}");
        List<EffectDefinition> effects = new();
        List<string> diagnostics = new();
        foreach (string kind in new[] { "effects", "text", "transitions" })
        {
            string folder = Path.Combine(root, kind);
            if (!System.IO.Directory.Exists(folder)) continue;
            string[] files;
            try { files = System.IO.Directory.EnumerateFiles(folder, "*.lua").Order(StringComparer.Ordinal).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { diagnostics.Add($"Cannot read {folder}: {exception.Message}"); continue; }
            foreach (string file in files)
            {
                try
                {
                    string script = File.ReadAllText(file);
                    // Preserve quoted text while removing comments (names can contain "--").
                    script = Regex.Replace(script, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|--\\[\\[[\\s\\S]*?\\]\\]|--[^\\r\\n]*",
                        match => match.Value.StartsWith("--", StringComparison.Ordinal) ? "" : match.Value);
                    string info = Regex.Match(script, @"function\s+GetInfo\s*\(\s*\)([\s\S]*?)\breturn\s+info\b").Groups[1].Value;
                    string? Literal(string key) => Regex.Match(info, @"\b" + key + "\\s*=\\s*\"([^\"]*)\"") is { Success: true } match ? match.Groups[1].Value : null;
                    string? key = Literal("name");
                    if (string.IsNullOrWhiteSpace(key)) { diagnostics.Add($"Unsupported metadata: {file}"); continue; }
                    if (effects.Any(item => item.Kind == kind && item.Key == key)) { diagnostics.Add($"Duplicate effect key: {file}"); continue; }
                    string duration = Regex.Match(script, @"\bSetDuration\(\s*([0-9.]+)\s*\)").Groups[1].Value;
                    decimal? seconds = decimal.TryParse(duration, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;
                    string[] properties = Regex.Matches(script, "\\bNewProperty\\(\\s*\"([^\"]+)\"").Select(match => match.Groups[1].Value).Distinct().ToArray();
                    effects.Add(new(key, kind, Literal("tag") ?? (kind == "transitions" ? "transition" : kind),
                        Literal("ja"), Literal("en"), seconds, Path.GetRelativePath(root, file).Replace('\\', '/'), Array.AsReadOnly(properties)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { diagnostics.Add($"Cannot read {file}: {exception.Message}"); }
            }
        }
        return new(root, effects, diagnostics);
    }
}
