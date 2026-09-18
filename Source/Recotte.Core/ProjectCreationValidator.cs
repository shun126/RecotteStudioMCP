using System.Globalization;
using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>Validates invariants required for projects created from the built-in Recotte Studio 1.8.5.0 template.</summary>
internal static class ProjectCreationValidator
{
    internal static IReadOnlyList<ProjectDiagnostic> Validate(JsonObject root, ProjectCreationRequest request, bool requireEmpty)
    {
        List<ProjectDiagnostic> diagnostics = new();
        RequireString(root, "app_version", "$.app_version", diagnostics, value => value == "1.8.5.0", "must be '1.8.5.0'");
        RequireString(root, "time", "$.time", diagnostics, value => DateTime.TryParseExact(value,
            "yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "has an invalid format");
        JsonObject? setting = RequireObject(root, "setting", "$.setting", diagnostics);
        RequireObject(root, "ui-setting", "$.ui-setting", diagnostics);
        JsonArray? speakers = RequireArray(root, "speakers", "$.speakers", diagnostics);
        JsonArray? fileItems = RequireArray(root, "file-items", "$.file-items", diagnostics);
        RequireNonEmptyArray(root, "named-colors", "$.named-colors", diagnostics);
        RequireNonEmptyArray(root, "named-fig-styles", "$.named-fig-styles", diagnostics);
        RequireNonEmptyArray(root, "text-styles", "$.text-styles", diagnostics);
        JsonArray? layers = RequireArray(root, "layers", "$.layers", diagnostics);

        if (setting is not null) ValidateSettings(setting, request, diagnostics);
        if (layers is not null) ValidateInitialLayers(layers, requireEmpty, diagnostics);
        if (requireEmpty && speakers is { Count: > 0 }) diagnostics.Add(Error("RC2306", "A new empty project must not contain speakers.", "$.speakers"));
        if (requireEmpty && fileItems is { Count: > 0 }) diagnostics.Add(Error("RC2306", "A new empty project must not contain file items.", "$.file-items"));
        return diagnostics;
    }

    private static void ValidateSettings(JsonObject setting, ProjectCreationRequest request, List<ProjectDiagnostic> diagnostics)
    {
        RequireString(setting, "guid", "$.setting.guid", diagnostics, value => Guid.TryParse(value, out _), "must be a GUID", "RC2303");
        RequireString(setting, "project-name", "$.setting.project-name", diagnostics,
            value => value == request.ProjectName && !string.IsNullOrWhiteSpace(value), "does not match the requested project name", "RC2304");
        bool hasDuration = JsonAccess.TryGetDecimal(setting, "duration", out decimal duration) && duration >= 0;
        if (!hasDuration) diagnostics.Add(Error("RC2302", "duration has an invalid numeric value.", "$.setting.duration"));
        if (!JsonAccess.TryGetDecimal(setting, "position", out decimal position) || position < 0 || hasDuration && position > duration)
            diagnostics.Add(Error("RC2302", "position must be between zero and duration.", "$.setting.position"));
        RequireNumber(setting, "fps", "$.setting.fps", diagnostics, value => value > 0);
        RequireNumber(setting, "dpi", "$.setting.dpi", diagnostics, value => value > 0);
        if (!JsonAccess.TryGetInt32(setting, "frame-counter", out int frameCounter) || frameCounter < 0)
            diagnostics.Add(Error("RC2302", "frame-counter must be a non-negative integer.", "$.setting.frame-counter"));
        if (setting["size"] is not JsonArray size || size.Count != 2 || !TryPositiveNumber(size[0]) || !TryPositiveNumber(size[1]))
            diagnostics.Add(Error("RC2302", "size must contain two positive numbers.", "$.setting.size"));

        string expected = DirectoryUri(request.ProjectDirectory);
        RequireString(setting, "file-explorer-path", "$.setting.file-explorer-path", diagnostics,
            value => string.Equals(value.TrimEnd('/'), expected, StringComparison.OrdinalIgnoreCase),
            "does not identify the requested project directory", "RC2305");
    }

    private static void ValidateInitialLayers(JsonArray layers, bool requireEmpty, List<ProjectDiagnostic> diagnostics)
    {
        string[] expected = ["Video", "Speaker", "Annot"];
        if (layers.Count < expected.Length)
        {
            diagnostics.Add(Error("RC2306", "The required Video, Speaker, and Annot layers are missing.", "$.layers"));
            return;
        }
        for (int index = 0; index < expected.Length; index++)
        {
            string path = $"$.layers[{index}]";
            if (layers[index] is not JsonObject layer || !JsonAccess.TryGetString(layer, "type", out string type) || type != expected[index])
                diagnostics.Add(Error("RC2306", $"Layer {index} must be '{expected[index]}'.", $"{path}.type"));
            else if (layer["layer-objects"] is not JsonArray objects)
                diagnostics.Add(Error("RC2306", "layer-objects must be an array.", $"{path}.layer-objects"));
            else if (requireEmpty && objects.Count != 0)
                diagnostics.Add(Error("RC2306", "A new empty project layer must not contain objects.", $"{path}.layer-objects"));
        }
    }

    internal static string DirectoryUri(string directory) =>
        new Uri(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');

    private static JsonObject? RequireObject(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics)
    {
        if (root[name] is JsonObject value) return value;
        diagnostics.Add(Error("RC2301", $"{name} must be an object.", path));
        return null;
    }

    private static JsonArray? RequireArray(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics)
    {
        if (root[name] is JsonArray value) return value;
        diagnostics.Add(Error("RC2301", $"{name} must be an array.", path));
        return null;
    }

    private static void RequireNonEmptyArray(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics)
    {
        if (root[name] is not JsonArray { Count: > 0 }) diagnostics.Add(Error("RC2307", $"{name} must be a non-empty array.", path));
    }

    private static void RequireString(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics,
        Func<string, bool> predicate, string failure, string code = "RC2301")
    {
        if (!JsonAccess.TryGetString(root, name, out string value) || !predicate(value))
            diagnostics.Add(Error(code, $"{name} {failure}.", path));
    }

    private static void RequireNumber(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics, Func<decimal, bool> predicate)
    {
        if (!JsonAccess.TryGetDecimal(root, name, out decimal value) || !predicate(value))
            diagnostics.Add(Error("RC2302", $"{name} has an invalid numeric value.", path));
    }

    private static bool TryPositiveNumber(JsonNode? node) => node is JsonValue && decimal.TryParse(node.ToJsonString(),
        NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && value > 0;

    private static ProjectDiagnostic Error(string code, string message, string path) =>
        new(code, DiagnosticSeverity.Error, message, path);
}
