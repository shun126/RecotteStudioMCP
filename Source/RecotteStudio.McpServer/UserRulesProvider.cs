using System.Text;

namespace RecotteStudio.McpServer;

/// <summary>
/// Reads the generation rules the user writes in UserRules.md under %LOCALAPPDATA%\RecotteStudioMCP on every request.
/// Each release replaces the distributed GenerationRules.md, so the user's own rules are kept apart from it, where no
/// update reaches. Having no file, or an empty one, is the usual state and is not reported.
/// </summary>
public static class UserRulesProvider
{
    public const string FileName = "UserRules.md";

    /// <summary>The most characters returned, so that an oversized file cannot crowd out the client's context.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>The folder that also holds the app's local settings.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecotteStudioMCP");

    public static string DefaultPath { get; } = Path.Combine(DefaultDirectory, FileName);

    /// <summary>
    /// Reads current Markdown from UserRules.md in <see cref="DefaultDirectory"/>, or in <paramref name="directory"/>
    /// when given. Returns null when the file is missing, blank, or unreadable; only an unreadable or oversized file
    /// is logged as a warning, and an oversized one is cut to <see cref="MaxLength"/> characters.
    /// </summary>
    public static string? Read(ActivityLog activity, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        string path = directory is null ? DefaultPath : Path.Combine(directory, FileName);
        string markdown;
        try
        {
            if (!File.Exists(path)) return null;
            markdown = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            activity.Record(ActivityKind.Warning, "ユーザールールを読み込めません", $"{path} — {exception.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(markdown)) return null;
        if (markdown.Length <= MaxLength) return markdown;

        // Never split a surrogate pair at the cut.
        int length = char.IsHighSurrogate(markdown[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        activity.Record(ActivityKind.Warning, "ユーザールールが長すぎます",
            $"{path} — 先頭の {length} 文字だけを渡します（{markdown.Length} 文字）");
        return markdown[..length];
    }
}
