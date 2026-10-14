using System.Text.Json;

namespace RecotteStudio.McpServer.Tests;

/// <summary>Finds a real ffmpeg for the tests that need one: PATH first, then the path saved in the settings window.</summary>
internal static class InstalledFfmpeg
{
    public static string? Find()
    {
        FfmpegResolution onPath = new FfmpegLocator().Resolve();
        if (onPath.Found) return onPath.ExecutablePath;
        string saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecotteStudioMCP", "ffmpeg.json");
        if (!File.Exists(saved)) return null;
        string? configured = JsonSerializer.Deserialize<string?>(File.ReadAllText(saved));
        return configured is not null && File.Exists(configured) ? configured : null;
    }
}
