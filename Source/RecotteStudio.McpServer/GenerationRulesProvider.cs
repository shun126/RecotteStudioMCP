using System.Text;

namespace RecotteStudio.McpServer;

/// <summary>
/// Reads the user-editable GenerationRules.md next to the executable on every request.
/// Guidance is optional: a missing or unreadable file never prevents MCP operations.
/// </summary>
public static class GenerationRulesProvider
{
    public const string FileName = "GenerationRules.md";

    /// <summary>
    /// Reads current Markdown from the first readable file in <see cref="SearchDirectories"/>, or only in
    /// <paramref name="directory"/> when given; missing or unreadable guidance is logged as a warning and returns null.
    /// </summary>
    public static string? Read(ActivityLog activity, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        string[] paths = (directory is null ? SearchDirectories() : new[] { directory })
            .Select(item => Path.Combine(item, FileName)).ToArray();
        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                activity.Record(ActivityKind.Warning, "生成ルールを読み込めません", $"{path} — {exception.Message}");
            }
        }

        activity.Record(ActivityKind.Warning, "生成ルールが見つかりません", string.Join(", ", paths));
        return null;
    }

    /// <summary>
    /// Lists the directories searched in order: the executable's directory, then AppContext.BaseDirectory. A single-file
    /// build that extracts its content runs from a temporary directory, so only the first is where the user keeps the file.
    /// </summary>
    public static IReadOnlyList<string> SearchDirectories()
    {
        List<string> directories = new();
        if (Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } executable)
            directories.Add(executable);
        string baseDirectory = AppContext.BaseDirectory;
        if (!directories.Any(item => string.Equals(Path.TrimEndingDirectorySeparator(item),
                Path.TrimEndingDirectorySeparator(baseDirectory), StringComparison.OrdinalIgnoreCase)))
            directories.Add(baseDirectory);
        return directories;
    }
}
