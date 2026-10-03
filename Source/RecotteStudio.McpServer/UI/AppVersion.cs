using System.Reflection;

namespace RecotteStudio.McpServer.UI;

/// <summary>Identifies the running build so that a stale process can be told apart from a fresh one.</summary>
internal static class AppVersion
{
    // The SDK writes "<Version>+<commit hash>" here; the hash is absent when the build has no source control data.
    private static readonly string[] Informational = (typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "不明").Split('+', 2);

    /// <summary>Gets the product version declared by the project.</summary>
    internal static string Version => Informational[0];

    /// <summary>Gets the commit and the time the running executable was built.</summary>
    internal static string BuildDescription
    {
        get
        {
            List<string> parts = new();
            if (Informational.Length > 1 && Informational[1].Length > 0)
                parts.Add($"コミット {Informational[1][..Math.Min(7, Informational[1].Length)]}");
            // The launcher executable is not rewritten by an incremental build, so the assembly is dated. It has no
            // location inside a single-file bundle, where the executable is the build output itself.
            string location = typeof(AppVersion).Assembly.Location;
            string? path = location.Length > 0 ? location : Environment.ProcessPath;
            if (path is not null && File.Exists(path))
                parts.Add($"ビルド {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}");
            return string.Join(" ・ ", parts);
        }
    }
}
