using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Describes one stamp (figure) shipped in the Recotte Studio figures directory.</summary>
/// <param name="Key">The name a Figure object's FigureKey refers to.</param>
/// <param name="Tag">The category, such as emotion, arrow, or onomatopoeia.</param>
/// <param name="DisplayNameJa">The Japanese display name, when defined.</param>
/// <param name="DisplayNameEn">The English display name, when defined.</param>
/// <param name="StyleName">The named figure style, when the definition uses one.</param>
/// <param name="SvgRelativePath">The SVG path relative to the figures directory with forward slashes, when the figure is an SVG.</param>
/// <param name="SvgPath">The absolute SVG path, when the figure is an SVG.</param>
public sealed record FigureDefinition(string Key, string Tag, string? DisplayNameJa, string? DisplayNameEn,
    string? StyleName, string? SvgRelativePath, string? SvgPath)
{
    /// <summary>
    /// Gets whether this figure matches the only shape verified against a project saved by Recotte Studio: an SVG drawn
    /// with the plain "figure" style. Script-drawn figures, bordered styles, and text boxes are listed but not placed.
    /// </summary>
    public bool CanPlace => SvgPath is not null && SvgRelativePath is not null && StyleName == "figure";
}

/// <summary>Reads the stamp definitions installed with Recotte Studio.</summary>
public sealed class FigureCatalog
{
    private FigureCatalog(string directory, IReadOnlyList<FigureDefinition> figures)
    {
        Directory = directory;
        Figures = figures;
    }

    /// <summary>Gets the absolute figures directory that was read.</summary>
    public string Directory { get; }

    /// <summary>Gets every readable figure definition ordered by tag and key.</summary>
    public IReadOnlyList<FigureDefinition> Figures { get; }

    /// <summary>Returns the figure having the exact key, or null.</summary>
    public FigureDefinition? Find(string key) =>
        Figures.FirstOrDefault(figure => string.Equals(figure.Key, key, StringComparison.Ordinal));

    /// <summary>Reads every figure definition below the directory; unreadable or malformed files are skipped.</summary>
    /// <exception cref="DirectoryNotFoundException">The figures directory does not exist.</exception>
    public static FigureCatalog Load(string figuresDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(figuresDirectory);
        string root = Path.GetFullPath(figuresDirectory);
        if (!System.IO.Directory.Exists(root))
            throw new DirectoryNotFoundException($"The Recotte Studio figures directory does not exist: {root}");

        Dictionary<string, FigureDefinition> figures = new(StringComparer.Ordinal);
        // Unreadable subdirectories are skipped like unreadable files instead of aborting the whole enumeration.
        EnumerationOptions options = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (string file in System.IO.Directory.EnumerateFiles(root, "*.json", options).Order(StringComparer.Ordinal))
        {
            JsonObject? definition;
            try { definition = JsonNode.Parse(File.ReadAllText(file)) as JsonObject; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { continue; }
            if (definition is null || !JsonAccess.TryGetString(definition, "name", out string key) || key.Length == 0) continue;

            JsonAccess.TryGetString(definition, "tag", out string tag);
            string? svgRelativePath = null;
            string? svgPath = definition["figure"] is JsonObject figure && JsonAccess.TryGetString(figure, "svg", out string svg)
                ? ResolveSvg(root, file, svg) : null;
            if (svgPath is not null) svgRelativePath = Path.GetRelativePath(root, svgPath).Replace('\\', '/');
            figures.TryAdd(key, new(key, tag, DisplayName(definition, "ja"), DisplayName(definition, "en"),
                definition["figure-style"] is JsonObject style && JsonAccess.TryGetString(style, "style-name", out string styleName)
                    ? styleName : null,
                svgRelativePath, svgPath));
        }
        return new(root, Array.AsReadOnly(figures.Values
            .OrderBy(figure => figure.Tag, StringComparer.Ordinal).ThenBy(figure => figure.Key, StringComparer.Ordinal).ToArray()));
    }

    // Returns null, like a missing SVG, when the path cannot be resolved (for example, it contains a NUL character).
    private static string? ResolveSvg(string root, string file, string svg)
    {
        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, svg)); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException) { return null; }
        // The file item stores the path relative to the figures directory, so an SVG outside it cannot be referenced.
        // A path on another root (another drive, UNC, or \\?\) comes back absolute rather than starting with "..".
        string relative = Path.GetRelativePath(root, candidate);
        return File.Exists(candidate) && !Path.IsPathRooted(relative) && !relative.StartsWith("..", StringComparison.Ordinal)
            ? candidate : null;
    }

    private static string? DisplayName(JsonObject definition, string language) =>
        definition["display-name"] is JsonObject names && JsonAccess.TryGetString(names, language, out string name) ? name : null;
}
