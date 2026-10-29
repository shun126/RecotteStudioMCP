using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

// LinkFolder is a file URI, as in the Speaker layers saved by Recotte Studio in 011TwoVoices.
internal static class SpeakerVoiceFolder
{
    internal static void SetDefault(JsonObject layer, string? projectDirectory)
    {
        if (layer["properties"] is not JsonObject properties || properties["LinkFolder"] is not JsonObject entry)
            return;

        // A stream has no project location. In particular, never retain the donor's folder on import.
        string value = string.Empty;
        if (projectDirectory is not null && JsonAccess.TryGetString(layer, "name", out string name))
        {
            string folder = new(name.Select(character => character < 32 || "<>:\"/\\|?*".Contains(character)
                ? '_' : character).ToArray());
            folder = folder.TrimEnd(' ', '.');
            if (folder.Length == 0) folder = "_";
            string stem = folder.Split('.')[0].TrimEnd(' ');
            if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                folder = "_" + folder;
            Uri uri = new(Path.Combine(projectDirectory, "voice", folder));
            // Recotte Studio writes readable paths and uses file://D:/... for drive paths.
            // Keep URI delimiters such as # and literal % escaped so LocalPath still round-trips.
            value = uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped);
            if (value.StartsWith("file:///", StringComparison.Ordinal) && uri.LocalPath.Length >= 2 && uri.LocalPath[1] == ':')
                value = "file://" + value["file:///".Length..];
        }
        entry["p-value"] = value;
    }

    // Creates the missing <project directory>/voice/<folder> directories that Speaker layers link to.
    // Links elsewhere, such as a donor's folder on another machine, are left alone.
    internal static IReadOnlyList<ProjectDiagnostic> EnsureDirectories(JsonObject root, string? projectDirectory)
    {
        if (projectDirectory is null || root["layers"] is not JsonArray layers) return Array.Empty<ProjectDiagnostic>();
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string voiceRoot = Path.Combine(Path.GetFullPath(projectDirectory), "voice");
        List<ProjectDiagnostic> warnings = new();
        for (int index = 0; index < layers.Count; index++)
        {
            if (layers[index] is not JsonObject layer || !JsonAccess.TryGetString(layer, "type", out string type) || type != "Speaker" ||
                JsonAccess.GetPropertyString(layer, "LinkFolder") is not { Length: > 0 } link ||
                !Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
                continue;
            string path = link;
            try
            {
                // Existing file URIs may decode to invalid paths even when URI parsing succeeds.
                path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(uri.LocalPath));
                if (!string.Equals(Path.GetDirectoryName(path), voiceRoot, comparison) || Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                warnings.Add(new("RC5402", DiagnosticSeverity.Warning, $"The voice folder could not be created: {path}: {ex.Message}",
                    $"$.layers[{index}].properties.LinkFolder.p-value"));
            }
        }
        return warnings;
    }
}
