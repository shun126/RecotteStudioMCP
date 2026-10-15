using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Requests a new empty Video layer; a null or omitted name selects the next free default name.</summary>
public sealed record AddVideoLayerRequest(string? Name = null);

/// <summary>Requests a new empty Annot layer; a null or omitted name selects the next free default name.</summary>
public sealed record AddAnnotationLayerRequest(string? Name = null);

/// <summary>Contains the result of adding a layer.</summary>
public sealed record LayerAddResult(EditResult EditResult, int? LayerIndex, string? Name)
{
    /// <summary>Gets whether the layer was added.</summary>
    public bool Success => EditResult.Success;
    /// <summary>Gets the operation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => EditResult.Diagnostics;
    /// <summary>Gets the staged project changes.</summary>
    public IReadOnlyList<ProjectChange> Changes => EditResult.Changes;
}

public sealed partial class ProjectEditor
{
    internal const string VideoLayerTemplateResource = "RecotteStudio.Core.Templates.VideoLayer.ccproj";
    internal const string AnnotationLayerTemplateResource = "RecotteStudio.Core.Templates.AnnotationLayer.ccproj";

    /// <summary>
    /// Adds an empty Video layer directly after the last Video layer, where Recotte Studio places one (013TwoVideos), so
    /// it draws above the existing videos and below Speaker and Annot layers. Every later layer's index grows by one.
    /// </summary>
    public LayerAddResult AddVideoLayer(AddVideoLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Recotte Studio names Video layers 映像・音声1, 映像・音声2, ... (001EmptyProject, 013TwoVideos).
        return AddLayer("Video", "映像・音声", VideoLayerTemplateResource, request.Name, "AddVideoLayer");
    }

    /// <summary>
    /// Adds an empty Annot layer directly after the last Annot layer, where Recotte Studio places one (012TwoImages), so
    /// its images, notes, and stamps draw above every existing layer. Every later layer's index grows by one.
    /// </summary>
    public LayerAddResult AddAnnotationLayer(AddAnnotationLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Recotte Studio names Annot layers 注釈1, 注釈2, ... (001EmptyProject, 012TwoImages).
        return AddLayer("Annot", "注釈", AnnotationLayerTemplateResource, request.Name, "AddAnnotationLayer");
    }

    // Clones the last layer of the given type from a sample in which Recotte Studio added it, empties it, and inserts it
    // after the target's last layer of that type, or at the end when the target has none.
    private LayerAddResult AddLayer(string type, string defaultPrefix, string templateResource, string? requestedName, string operation)
    {
        if (root["layers"] is not JsonArray layers) return LayerFailed("RC4001", "The project has no layers array.");
        HashSet<string> names = Layers()
            .Where(item => JsonAccess.TryGetString(item.layer, "name", out _))
            .Select(item => { JsonAccess.TryGetString(item.layer, "name", out string value); return value; })
            .ToHashSet(StringComparer.Ordinal);
        string name;
        if (requestedName is null)
        {
            int number = 1;
            while (names.Contains(defaultPrefix + number)) number++;
            name = defaultPrefix + number;
        }
        else if (string.IsNullOrWhiteSpace(requestedName))
            return LayerFailed("RC4002", "The layer name must not be blank.");
        else if (names.Contains(requestedName))
            return LayerFailed("RC4002", $"A layer named '{requestedName}' already exists; layer names must be unique.");
        else name = requestedName;

        JsonObject? template;
        try
        {
            template = ProjectTemplateResources.LoadRoot(templateResource)["layers"] is JsonArray templateLayers
                ? templateLayers.OfType<JsonObject>()
                    .LastOrDefault(layer => JsonAccess.TryGetString(layer, "type", out string layerType) && layerType == type)
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException)
        { return LayerFailed("RC4001", exception.Message); }
        if (template is null) return LayerFailed("RC4001", $"The verified {type} layer template is unavailable.");

        JsonObject added = (JsonObject)template.DeepClone();
        added["name"] = name;
        added["selected"] = false;
        added["layer-objects"] = new JsonArray();

        int[] sameType = Layers().Where(item => JsonAccess.TryGetString(item.layer, "type", out string layerType) && layerType == type)
            .Select(item => item.index).ToArray();
        int index = sameType.Length > 0 ? sameType.Max() + 1 : type == "Video" ? 0 : layers.Count;
        layers.Insert(index, added);
        ProjectChange change = new(operation, $"layer:{index}", null, name, $"$.layers[{index}]");
        changes.Add(change);
        return new(EditResult.Succeeded(new[] { change }), index, name);
    }

    private static LayerAddResult LayerFailed(string code, string message) => new(EditResult.Failed(code, message), null, null);
}
