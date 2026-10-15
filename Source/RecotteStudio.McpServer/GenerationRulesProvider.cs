using System.Text;

namespace RecotteStudio.McpServer;

/// <summary>
/// Reads the user-editable GenerationRules.md next to the executable on every request.
/// Guidance is optional: a missing or unreadable file never prevents MCP operations.
/// </summary>
public static class GenerationRulesProvider
{
    public const string FileName = "GenerationRules.md";

    /// <summary>Reads current Markdown; missing or unreadable guidance is logged as a warning and returns null.</summary>
    public static string? Read(ActivityLog activity, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        string path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
        try
        {
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);

            activity.Record(ActivityKind.Warning, "生成ルールが見つかりません", path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            activity.Record(ActivityKind.Warning, "生成ルールを読み込めません", $"{path} — {exception.Message}");
        }

        return null;
    }
}
