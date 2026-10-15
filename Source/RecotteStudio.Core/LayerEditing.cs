using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Requests a new empty Video layer; a null or omitted name selects the next free default name.</summary>
public sealed record AddVideoLayerRequest(string? Name = null);

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
    // Recotte Studio names Video layers 映像・音声1, 映像・音声2, ... (001EmptyProject, 013TwoVideos).
    private const string DefaultVideoLayerPrefix = "映像・音声";

    /// <summary>
    /// Adds an empty Video layer directly after the last Video layer, where Recotte Studio places one (013TwoVideos), so
    /// it draws above the existing videos and below Speaker and Annot layers. Every later layer's index grows by one.
    /// </summary>
    public LayerAddResult AddVideoLayer(AddVideoLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (root["layers"] is not JsonArray layers) return LayerFailed("RC4001", "The project has no layers array.");
        HashSet<string> names = Layers()
            .Where(item => JsonAccess.TryGetString(item.layer, "name", out _))
            .Select(item => { JsonAccess.TryGetString(item.layer, "name", out string value); return value; })
            .ToHashSet(StringComparer.Ordinal);
        string name;
        if (request.Name is null)
        {
            int number = 1;
            while (names.Contains(DefaultVideoLayerPrefix + number)) number++;
            name = DefaultVideoLayerPrefix + number;
        }
        else if (string.IsNullOrWhiteSpace(request.Name))
            return LayerFailed("RC4002", "The layer name must not be blank.");
        else if (names.Contains(request.Name))
            return LayerFailed("RC4002", $"A layer named '{request.Name}' already exists; layer names must be unique.");
        else name = request.Name;

        JsonObject? template;
        try
        {
            template = ProjectTemplateResources.LoadRoot(VideoLayerTemplateResource)["layers"] is JsonArray templateLayers
                ? templateLayers.OfType<JsonObject>()
                    .LastOrDefault(layer => JsonAccess.TryGetString(layer, "type", out string type) && type == "Video")
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException)
        { return LayerFailed("RC4001", exception.Message); }
        if (template is null) return LayerFailed("RC4001", "The verified Video layer template is unavailable.");

        JsonObject added = (JsonObject)template.DeepClone();
        added["name"] = name;
        added["selected"] = false;
        added["layer-objects"] = new JsonArray();

        int index = Layers().Where(item => JsonAccess.TryGetString(item.layer, "type", out string type) && type == "Video")
            .Select(item => item.index + 1).DefaultIfEmpty(0).Max();
        layers.Insert(index, added);
        ProjectChange change = new("AddVideoLayer", $"layer:{index}", null, name, $"$.layers[{index}]");
        changes.Add(change);
        return new(EditResult.Succeeded(new[] { change }), index, name);
    }

    private static LayerAddResult LayerFailed(string code, string message) => new(EditResult.Failed(code, message), null, null);
}
