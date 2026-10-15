using System.Reflection;
using System.Text;

namespace RecotteStudio.McpServer;

/// <summary>
/// Reads the guidance embedded at build time. The same Markdown is also included as a loose release file
/// for users; the MCP tool intentionally never depends on the workspace or executable location.
/// </summary>
public static class GenerationRulesProvider
{
    private const string ResourceName = "RecotteStudio.McpServer.GenerationRules.md";
    private static readonly Lazy<string> Content = new(Load);

    /// <summary>Returns the generation guidance as Markdown.</summary>
    public static string Markdown => Content.Value;

    private static string Load()
    {
        Assembly assembly = typeof(GenerationRulesProvider).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded generation rules resource is missing: {ResourceName}");
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
