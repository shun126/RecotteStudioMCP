using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

internal static class ProjectValidator
{
    internal static ProjectValidationResult Validate(JsonObject root, IReadOnlyList<ProjectDiagnostic> loadDiagnostics)
    {
        List<ProjectDiagnostic> diagnostics = new(loadDiagnostics);
        RequireObject(root, "setting", "$.setting", diagnostics);
        JsonArray? fileItems = RequireArray(root, "file-items", "$.file-items", diagnostics);
        JsonArray? speakers = RequireArray(root, "speakers", "$.speakers", diagnostics);
        JsonArray? layers = RequireArray(root, "layers", "$.layers", diagnostics);

        HashSet<string> fileKeys = CollectStringKeys(fileItems, "ik");
        HashSet<string> speakerKeys = CollectStringKeys(speakers, "key");
        if (layers is not null)
        {
            ValidateLayers(layers, fileKeys, speakerKeys, diagnostics);
            WarnAboutTelopCoverage(root, layers, diagnostics);
        }

        return new ProjectValidationResult(diagnostics);
    }

    // Recotte Studio draws each dialogue line inside the voice's telop frame, on top of whatever image or video is on
    // screen at that moment. The asset stays valid, so this only reminds the author which region is hidden when an
    // asset smaller than the screen was placed across the frame.
    private static void WarnAboutTelopCoverage(JsonObject root, JsonArray layers, List<ProjectDiagnostic> diagnostics)
    {
        Dictionary<string, TelopFrameView> frames = new(StringComparer.Ordinal);
        foreach (TelopFrameView frame in TelopFrameView.ReadAll(root)) frames.TryAdd(frame.Name, frame);
        if (frames.Count == 0) return;

        List<(decimal Start, decimal End, TelopFrameView Frame)> telops = new();
        foreach ((JsonObject voice, _) in ObjectsOfType(layers, "Speaker Voice"))
        {
            if (voice["properties"] is JsonObject properties && properties["TelopOn"] is JsonObject telopOn &&
                telopOn["p-value"] is JsonValue shown && shown.TryGetValue(out bool isShown) && isShown &&
                JsonAccess.GetPropertyString(voice, "TelopFrame") is string frameName &&
                frames.TryGetValue(frameName, out TelopFrameView? frame) && TryGetTimeRange(voice, out decimal start, out decimal end))
            {
                telops.Add((start, end, frame));
            }
        }
        if (telops.Count == 0) return;

        (decimal Width, decimal Height)? screen = root["setting"] is JsonObject setting && setting["size"] is JsonArray screenSize &&
            screenSize.Count == 2 && JsonAccess.TryGetDecimal(screenSize[0], out decimal screenWidth) &&
            JsonAccess.TryGetDecimal(screenSize[1], out decimal screenHeight) ? (screenWidth, screenHeight) : null;
        foreach ((JsonObject asset, string path) in ObjectsOfType(layers, "Image", "Video"))
        {
            if (asset["properties"] is not JsonObject properties ||
                !JsonAccess.TryGetBounds(properties, out decimal x, out decimal y, out decimal width, out decimal height) ||
                !TryGetTimeRange(asset, out decimal start, out decimal end)) continue;
            // A background that fills the screen is always under the telop, so warning about it would only be noise.
            if (screen is { } size && x <= 0 && y <= 0 && x + width >= size.Width && y + height >= size.Height) continue;
            TelopFrameView[] covering = telops
                .Where(telop => telop.Start < end && start < telop.End &&
                    telop.Frame.X < x + width && x < telop.Frame.X + telop.Frame.Width &&
                    telop.Frame.Y < y + height && y < telop.Frame.Y + telop.Frame.Height)
                .Select(telop => telop.Frame).Distinct().ToArray();
            if (covering.Length == 0) continue;

            JsonAccess.TryGetString(asset, "type", out string type);
            JsonAccess.TryGetString(asset, "name", out string name);
            string regions = string.Join(", ", covering.Select(frame => FormattableString.Invariant(
                $"{frame.Name} (x {frame.X:0.##}, y {frame.Y:0.##}, {frame.Width:0.##}x{frame.Height:0.##})")));
            diagnostics.Add(Warning("RC2401",
                $"{type} '{name}' is on screen while dialogue is drawn over it in telop frame {regions}. Check that no " +
                "face, caption, or other important content sits in that region, and never draw the dialogue into the asset: " +
                "Recotte Studio renders it from the Speaker layer.", path));
        }
    }

    private static IEnumerable<(JsonObject Object, string Path)> ObjectsOfType(JsonArray layers, params string[] types)
    {
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            if (layers[layerIndex] is not JsonObject layer || layer["layer-objects"] is not JsonArray objects) continue;
            for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                if (objects[objectIndex] is JsonObject item && JsonAccess.TryGetString(item, "type", out string type) &&
                    types.Contains(type))
                {
                    yield return (item, $"$.layers[{layerIndex}].layer-objects[{objectIndex}]");
                }
            }
        }
    }

    private static bool TryGetTimeRange(JsonObject timelineObject, out decimal start, out decimal end)
    {
        end = default;
        return JsonAccess.TryGetDecimal(timelineObject, "start-time", out start) &&
            JsonAccess.TryGetDecimal(timelineObject, "end-time", out end) && start < end;
    }

    private static void ValidateLayers(JsonArray layers, HashSet<string> fileKeys, HashSet<string> speakerKeys, List<ProjectDiagnostic> diagnostics)
    {
        Dictionary<int, string> projectKeys = new();
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            string layerPath = $"$.layers[{layerIndex}]";
            if (layers[layerIndex] is not JsonObject layer)
            {
                diagnostics.Add(Error("RC2001", "A layer must be an object.", layerPath));
                continue;
            }

            if (!JsonAccess.TryGetString(layer, "type", out string layerType))
            {
                diagnostics.Add(Error("RC2001", "Layer type must be a string.", $"{layerPath}.type"));
            }
            else if (layerType is not ("Video" or "Speaker" or "Annot"))
            {
                diagnostics.Add(Warning("RC3001", $"Unknown layer type '{layerType}' was preserved.", $"{layerPath}.type"));
            }

            if (layerType == "Speaker")
            {
                string? speakerKey = JsonAccess.GetPropertyString(layer, "Speaker");
                if (!string.IsNullOrEmpty(speakerKey) && !speakerKeys.Contains(speakerKey))
                {
                    diagnostics.Add(Error("RC2102", $"Speaker key '{speakerKey}' is not registered.", $"{layerPath}.properties.Speaker.p-value"));
                }
            }

            if (layer["layer-objects"] is not JsonArray objects)
            {
                diagnostics.Add(Error("RC2001", "layer-objects must be an array.", $"{layerPath}.layer-objects"));
                continue;
            }

            HashSet<int> layerKeys = new();
            for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                ValidateObject(objects[objectIndex], layerIndex, objectIndex, layerKeys, projectKeys, fileKeys, diagnostics);
            }
        }
    }

    private static void ValidateObject(JsonNode? item, int layerIndex, int objectIndex, HashSet<int> layerKeys, Dictionary<int, string> projectKeys, HashSet<string> fileKeys, List<ProjectDiagnostic> diagnostics)
    {
        string path = $"$.layers[{layerIndex}].layer-objects[{objectIndex}]";
        if (item is not JsonObject timelineObject)
        {
            diagnostics.Add(Error("RC2001", "A timeline object must be an object.", path));
            return;
        }

        if (!JsonAccess.TryGetString(timelineObject, "type", out string objectType))
        {
            diagnostics.Add(Error("RC2001", "Timeline object type must be a string.", $"{path}.type"));
        }
        else if (objectType is not ("Speaker Voice" or "Speaker Character" or "Speaker Action" or "Image" or "Video" or "Transition" or "Figure"))
        {
            diagnostics.Add(Warning("RC3002", $"Unknown timeline object type '{objectType}' was preserved.", $"{path}.type"));
        }

        if (!JsonAccess.TryGetInt32(timelineObject, "objkey", out int objectKey))
        {
            diagnostics.Add(Error("RC2001", "objkey must be an integer.", $"{path}.objkey"));
        }
        else
        {
            if (!layerKeys.Add(objectKey))
            {
                diagnostics.Add(Error("RC2202", $"Object key {objectKey} is duplicated within the layer.", $"{path}.objkey"));
            }

            if (projectKeys.TryGetValue(objectKey, out string? previousPath) && !previousPath.StartsWith($"$.layers[{layerIndex}]", StringComparison.Ordinal))
            {
                diagnostics.Add(Warning("RC2203", $"Object key {objectKey} is also used in another layer.", $"{path}.objkey"));
            }
            else
            {
                projectKeys[objectKey] = path;
            }
        }

        bool hasStart = JsonAccess.TryGetDecimal(timelineObject, "start-time", out decimal start);
        bool hasEnd = JsonAccess.TryGetDecimal(timelineObject, "end-time", out decimal end);
        if (!hasStart)
        {
            diagnostics.Add(Error("RC2001", "start-time must be numeric.", $"{path}.start-time"));
        }
        if (!hasEnd)
        {
            diagnostics.Add(Error("RC2001", "end-time must be numeric.", $"{path}.end-time"));
        }
        if (hasStart && hasEnd && (start < 0 || end < 0 || start > end))
        {
            diagnostics.Add(Error("RC2201", "The timeline object has an invalid time range.", path));
        }

        string? fileKey = JsonAccess.GetPropertyString(timelineObject, "File");
        if (!string.IsNullOrEmpty(fileKey) && !fileKeys.Contains(fileKey))
        {
            diagnostics.Add(Error("RC2101", $"File item key '{fileKey}' is not registered.", $"{path}.properties.File.p-value"));
        }
    }

    private static JsonObject? RequireObject(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics)
    {
        if (root[name] is JsonObject value)
        {
            return value;
        }
        diagnostics.Add(Error("RC2001", $"{name} must be an object.", path));
        return null;
    }

    private static JsonArray? RequireArray(JsonObject root, string name, string path, List<ProjectDiagnostic> diagnostics)
    {
        if (root[name] is JsonArray value)
        {
            return value;
        }
        diagnostics.Add(Error("RC2001", $"{name} must be an array.", path));
        return null;
    }

    private static HashSet<string> CollectStringKeys(JsonArray? array, string propertyName)
    {
        HashSet<string> values = new(StringComparer.Ordinal);
        if (array is null)
        {
            return values;
        }
        foreach (JsonObject item in array.OfType<JsonObject>())
        {
            if (JsonAccess.TryGetString(item, propertyName, out string value))
            {
                values.Add(value);
            }
        }
        return values;
    }

    private static ProjectDiagnostic Error(string code, string message, string path) => new(code, DiagnosticSeverity.Error, message, path);

    private static ProjectDiagnostic Warning(string code, string message, string path) => new(code, DiagnosticSeverity.Warning, message, path);
}
