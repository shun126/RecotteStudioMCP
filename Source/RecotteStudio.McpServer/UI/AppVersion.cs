using System.Globalization;
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
            if (BuildTime is DateTimeOffset built)
                parts.Add($"ビルド {built.ToLocalTime():yyyy-MM-dd HH:mm}");
            return string.Join(" ・ ", parts);
        }
    }

    // The project embeds the build time, because downloading or extracting a release rewrites the file's own dates.
    private static DateTimeOffset? BuildTime =>
        typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => item.Key == "BuildTime")?.Value is string value &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset time)
            ? time : null;
}
