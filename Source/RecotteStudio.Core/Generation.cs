using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Requests a new empty project based on the verified Recotte Studio 1.8.5.0 template.</summary>
public sealed record ProjectCreationRequest(string ProjectName, string ProjectDirectory);

/// <summary>Allocates opaque file-item keys independently from asset editing behavior.</summary>
public interface IFileItemKeyAllocator
{
    /// <summary>Returns an unused lowercase 64-character hexadecimal key.</summary>
    string Allocate(IReadOnlyCollection<string> existingKeys);
}

/// <summary>Allocates a cryptographically random 256-bit opaque file-item key.</summary>
public sealed class CryptographicFileItemKeyAllocator : IFileItemKeyAllocator
{
    /// <inheritdoc />
    public string Allocate(IReadOnlyCollection<string> existingKeys)
    {
        ArgumentNullException.ThrowIfNull(existingKeys);
        for (int attempt = 0; attempt < 16; attempt++)
        {
            string candidate = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            if (!existingKeys.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
        throw new InvalidOperationException("A unique file-item key could not be allocated.");
    }
}

internal static class ProjectTemplateResources
{
    internal static JsonObject LoadRoot(string resourceName)
    {
        Assembly assembly = typeof(ProjectTemplateResources).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded project template '{resourceName}' is unavailable.");
        RecotteProjectDocument template = RecotteProject.Load(stream);
        return template.CloneRoot();
    }
}

/// <summary>
/// Supplies the verified text-only Speaker Voice embedded in this assembly so that a Speaker layer without an existing
/// voice can still receive one. The template's own text style and telop frame are never carried over: the clone is bound
/// to the target Speaker layer's settings, and is offered only when those resolve in the target project, because a
/// dangling reference would validate here and break when Recotte Studio opens the project.
/// </summary>
internal static class TextVoiceTemplateResources
{
    internal const string ResourceName = "RecotteStudio.Core.Templates.Text.ccproj";

    // Voice property <- Speaker layer property. Every Speaker Voice in the repository samples carries its layer's value.
    private static readonly (string Voice, string Layer)[] LayerDefaults =
    {
        ("AudioVolume", "InitialAudioVolume"), ("LipMorphLevel", "BaseLipMorphLevel"), ("TelopOn", "ShowTelopDefault"),
    };
    private static readonly Lazy<TemplateSource?> Source = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets whether the embedded template resolves to a verified text-only Speaker Voice.</summary>
    internal static bool Exists => Source.Value is not null;

    /// <summary>Returns whether the embedded voice can be bound to the supplied Speaker layer of the project root.</summary>
    internal static bool CanBindTo(JsonObject targetRoot, JsonObject layer) =>
        Source.Value is not null && TryResolveLayerTelop(targetRoot, layer, out _, out _);

    /// <summary>
    /// Returns a private clone of the embedded voice bound to the layer's text style, telop frame, and voice defaults, or
    /// null when the template is unavailable or the layer's style or frame is not defined in the project.
    /// </summary>
    internal static JsonObject? TryClone(JsonObject targetRoot, JsonObject layer)
    {
        if (Source.Value is not TemplateSource source ||
            !TryResolveLayerTelop(targetRoot, layer, out string style, out string frame)) return null;
        JsonObject voice = (JsonObject)source.Voice.DeepClone();
        if (voice["properties"] is not JsonObject properties || properties["TelopFrame"] is not JsonObject telopFrame ||
            voice["text"] is not JsonObject text || text["stext"] is not JsonArray styledText) return null;
        JsonObject[] styleRuns = styledText.OfType<JsonObject>()
            .Where(item => JsonAccess.TryGetString(item, "c", out string kind) && kind == "s").ToArray();
        if (styleRuns.Length == 0) return null;
        foreach (JsonObject run in styleRuns) run["style"] = style;
        telopFrame["p-value"] = frame;
        if (voice.ContainsKey("selected")) voice["selected"] = false;
        JsonObject layerProperties = (JsonObject)layer["properties"]!;
        foreach ((string voiceName, string layerName) in LayerDefaults)
            if (properties[voiceName] is JsonObject target && layerProperties[layerName] is JsonObject origin &&
                origin["p-value"] is JsonNode value) target["p-value"] = value.DeepClone();
        return voice;
    }

    private static bool TryResolveLayerTelop(JsonObject targetRoot, JsonObject layer, out string style, out string frame)
    {
        style = JsonAccess.GetPropertyString(layer, "DefaultTextStyle") ?? string.Empty;
        frame = JsonAccess.GetPropertyString(layer, "TelopFrame") ?? string.Empty;
        string styleName = style, frameName = frame;
        return styleName.Length > 0 && frameName.Length > 0 &&
            targetRoot["text-styles"] is JsonArray styles && styles.OfType<JsonObject>().Any(item =>
                JsonAccess.TryGetString(item, "StyleName", out string name) && name == styleName) &&
            targetRoot["telop-frames"] is JsonArray frames && frames.OfType<JsonObject>().Any(item =>
                item["FrameName"] is JsonObject property && JsonAccess.TryGetString(property, "p-value", out string name) &&
                name == frameName);
    }

    private static TemplateSource? Load()
    {
        JsonObject root;
        try { root = ProjectTemplateResources.LoadRoot(ResourceName); }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException) { return null; }
        JsonObject? voice = root["layers"] is JsonArray layers
            ? layers.OfType<JsonObject>()
                .SelectMany(layer => (layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
                .FirstOrDefault(ProjectEditor.IsTextOnlySpeakerVoice)
            : null;
        return voice is null ? null : new TemplateSource(voice);
    }

    private sealed record TemplateSource(JsonObject Voice);
}

/// <summary>Supplies the verified annotation text-box object and the text style referenced by it.</summary>
internal static class AnnotationTextTemplateResources
{
    internal const string ResourceName = "RecotteStudio.Core.Templates.AnnotationText.ccproj";
    private static readonly Lazy<TemplateSource?> Source = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static bool Exists => Source.Value is not null;

    internal static bool TryClone(JsonObject targetRoot, out JsonObject? annotation, out JsonObject? missingStyle,
        out string? error)
    {
        annotation = null;
        missingStyle = null;
        error = null;
        if (Source.Value is not TemplateSource source)
        {
            error = "The verified annotation text template is unavailable.";
            return false;
        }
        if (targetRoot["text-styles"] is not JsonArray styles)
        {
            error = "The project has no text-style registry.";
            return false;
        }

        JsonObject[] matches = styles.OfType<JsonObject>()
            .Where(style => JsonAccess.TryGetString(style, "StyleName", out string name) && name == source.StyleName)
            .ToArray();
        if (matches.Length > 1 || matches.Length == 1 && !SemanticJsonComparer.Equals(matches[0], source.Style))
        {
            error = $"The project defines an incompatible '{source.StyleName}' text style.";
            return false;
        }

        annotation = (JsonObject)source.Annotation.DeepClone();
        if (matches.Length == 0) missingStyle = (JsonObject)source.Style.DeepClone();
        return true;
    }

    private static TemplateSource? Load()
    {
        JsonObject root;
        try { root = ProjectTemplateResources.LoadRoot(ResourceName); }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException) { return null; }
        JsonObject? annotation = root["layers"] is JsonArray layers
            ? layers.OfType<JsonObject>()
                .SelectMany(layer => (layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
                .SingleOrDefault(IsAnnotationText)
            : null;
        string? styleName = annotation?["text"] is JsonObject text && text["stext"] is JsonArray styledText
            ? styledText.OfType<JsonObject>()
                .Where(item => JsonAccess.TryGetString(item, "c", out string kind) && kind == "s")
                .Select(item => JsonAccess.TryGetString(item, "style", out string name) ? name : null)
                .SingleOrDefault()
            : null;
        JsonObject? style = styleName is not null && root["text-styles"] is JsonArray styles
            ? styles.OfType<JsonObject>()
                .SingleOrDefault(item => JsonAccess.TryGetString(item, "StyleName", out string name) && name == styleName)
            : null;
        return annotation is null || style is null || styleName is null ? null : new(annotation, style, styleName);
    }

    private static bool IsAnnotationText(JsonObject value) =>
        JsonAccess.TryGetString(value, "type", out string type) && type == "Figure" &&
        JsonAccess.GetPropertyString(value, "FigureKey") == "text-box" && value["text"] is JsonObject;

    private sealed record TemplateSource(JsonObject Annotation, JsonObject Style, string StyleName);
}
