using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>
/// Requests a new empty Video layer; a null or omitted name selects the next free default name. Layers draw in array
/// order, index 0 at the back: a null index places the layer after the last Video layer, and 0 to the layer count
/// inserts it at that index.
/// </summary>
public sealed record AddVideoLayerRequest(string? Name = null, int? Index = null);

/// <summary>
/// Requests a new empty Annot layer; a null or omitted name selects the next free default name. Layers draw in array
/// order, index 0 at the back: a null index places the layer after the last Annot layer, and 0 to the layer count
/// inserts it at that index, so 0 puts its images behind every other layer.
/// </summary>
public sealed record AddAnnotationLayerRequest(string? Name = null, int? Index = null);

/// <summary>
/// Requests a new Speaker layer without a character; a null or omitted name selects the next free default name. A null
/// index places the layer after the last Speaker layer, and 0 to the layer count inserts it at that index.
/// </summary>
public sealed record AddSpeakerLayerRequest(string? Name = null, int? Index = null);

/// <summary>Requests moving the layer at LayerIndex so that it ends up at TargetIndex (0 to the layer count minus one).</summary>
public sealed record MoveLayerRequest(int LayerIndex, int TargetIndex);

/// <summary>Contains the result of moving a layer; LayerIndex is where the layer ended up.</summary>
public sealed record LayerMoveResult(EditResult EditResult, int? LayerIndex, string? Name)
{
    /// <summary>Gets whether the layer was moved.</summary>
    public bool Success => EditResult.Success;
    /// <summary>Gets the operation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => EditResult.Diagnostics;
    /// <summary>Gets the staged project changes.</summary>
    public IReadOnlyList<ProjectChange> Changes => EditResult.Changes;
}

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
    internal const string SpeakerLayerTemplateResource = "RecotteStudio.Core.Templates.Empty.ccproj";

    /// <summary>
    /// Adds an empty Video layer, by default directly after the last Video layer, where Recotte Studio places one
    /// (013TwoVideos), so it draws above the existing videos. Every later layer's index grows by one.
    /// </summary>
    public LayerAddResult AddVideoLayer(AddVideoLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Recotte Studio names Video layers 映像・音声1, 映像・音声2, ... (001EmptyProject, 013TwoVideos).
        return AddLayer("Video", "映像・音声", VideoLayerTemplateResource, request.Name, request.Index, "AddVideoLayer");
    }

    /// <summary>
    /// Adds an empty Annot layer, by default directly after the last Annot layer, where Recotte Studio places one
    /// (012TwoImages), so its images, notes, and stamps draw above every existing layer. Every later layer's index grows by one.
    /// </summary>
    public LayerAddResult AddAnnotationLayer(AddAnnotationLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Recotte Studio names Annot layers 注釈1, 注釈2, ... (001EmptyProject, 012TwoImages).
        return AddLayer("Annot", "注釈", AnnotationLayerTemplateResource, request.Name, request.Index, "AddAnnotationLayer");
    }

    /// <summary>
    /// Adds a Speaker layer without a character, as Recotte Studio writes the one of a new project (001EmptyProject), by
    /// default directly after the last Speaker layer. A speaker belongs to its layer, so this is how one layer's lines are
    /// split between speakers: assign the layer's character in Recotte Studio, or fill it with ImportCharacter, and move
    /// lines to it with MoveSpeakerVoiceToLayer. Every later layer's index grows by one.
    /// </summary>
    public LayerAddResult AddSpeakerLayer(AddSpeakerLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Recotte Studio names Speaker layers 話者1, 話者2, ... (001EmptyProject, 008TwoCharacters).
        return AddLayer("Speaker", "話者", SpeakerLayerTemplateResource, request.Name, request.Index, "AddSpeakerLayer");
    }

    // Clones the last layer of the given type from a sample in which Recotte Studio added it, empties it, and inserts it at
    // the requested index, or else after the target's last layer of that type (at the end when the target has none).
    private LayerAddResult AddLayer(string type, string defaultPrefix, string templateResource, string? requestedName,
        int? requestedIndex, string operation)
    {
        if (root["layers"] is not JsonArray layers) return LayerFailed("RC4001", "The project has no layers array.");
        if (requestedIndex is int requested && (requested < 0 || requested > layers.Count))
            return LayerFailed("RC4003", $"The layer index {requested} must be between 0 and the layer count {layers.Count}.");
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

        // A Speaker layer draws its lines with its DefaultTextStyle and TelopFrame, so both must exist in this project.
        if (type == "Speaker" && MissingSpeakerLayerDefinition(root, template) is string missing)
            return LayerFailed("RC4004", $"The project does not define {missing}, which a new Speaker layer uses.");

        JsonObject added = (JsonObject)template.DeepClone();
        added["name"] = name;
        added["selected"] = false;
        added["layer-objects"] = new JsonArray();
        if (type == "Speaker") SpeakerVoiceFolder.SetDefault(added, projectDirectory);

        int[] sameType = Layers().Where(item => JsonAccess.TryGetString(item.layer, "type", out string layerType) && layerType == type)
            .Select(item => item.index).ToArray();
        int index = requestedIndex ?? (sameType.Length > 0 ? sameType.Max() + 1 : type switch
        {
            "Video" => 0,
            // Without a Speaker layer the new one goes right above the videos, where a new project keeps it.
            "Speaker" => Layers().Where(item => JsonAccess.TryGetString(item.layer, "type", out string layerType) && layerType == "Video")
                .Select(item => item.index + 1).DefaultIfEmpty(0).Max(),
            _ => layers.Count,
        });
        layers.Insert(index, added);
        for (int prior = 0; prior < changes.Count; prior++)
            changes[prior] = LayerInsertionMetadata.Change(changes[prior], index);
        ProjectChange change = new(operation, $"layer:{index}", null, name, $"$.layers[{index}]");
        changes.Add(change);
        return new(EditResult.Succeeded(new[] { change }), index, name);
    }

    /// <summary>
    /// Moves a layer to another position. Layers draw in array order, index 0 at the back, so this changes what draws over
    /// what; the layer itself is unchanged, as when Recotte Studio swaps two tracks (008TwoCharacters to 014SwapTwoCharacters).
    /// Every layer between the old and new positions shifts by one. Moving a Speaker layer also reorders the speaker registry
    /// to follow the Speaker layers, which Recotte Studio does when it saves.
    /// </summary>
    public LayerMoveResult MoveLayer(MoveLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (root["layers"] is not JsonArray layers) return LayerMoveFailed("RC4001", "The project has no layers array.");
        if (request.LayerIndex < 0 || request.LayerIndex >= layers.Count || layers[request.LayerIndex] is not JsonObject layer)
            return LayerMoveFailed("RC4005", $"Layer {request.LayerIndex} does not exist.");
        if (request.TargetIndex < 0 || request.TargetIndex >= layers.Count)
            return LayerMoveFailed("RC4003", $"The layer index {request.TargetIndex} must be between 0 and {layers.Count - 1}.");
        if (IsLocked(layer)) return LayerMoveFailed("RC4005", $"Layer {request.LayerIndex} is locked.");
        string name = LayerName(layer);
        if (request.TargetIndex == request.LayerIndex) return new(EditResult.Succeeded(), request.LayerIndex, name);

        layers.RemoveAt(request.LayerIndex);
        layers.Insert(request.TargetIndex, layer);
        Func<int, int> map = LayerInsertionMetadata.Move(request.LayerIndex, request.TargetIndex);
        for (int prior = 0; prior < changes.Count; prior++)
            changes[prior] = LayerInsertionMetadata.Change(changes[prior], map);
        List<ProjectChange> made = new();
        ProjectChange change = new("MoveLayer", $"layer:{request.TargetIndex}", request.LayerIndex, request.TargetIndex,
            $"$.layers[{request.TargetIndex}]");
        changes.Add(change);
        made.Add(change);
        if (JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker") OrderSpeakersByLayers(made);
        return new(EditResult.Succeeded(made), request.TargetIndex, name);
    }

    // 014SwapTwoCharacters lists its speakers in the order of the Speaker layers showing them; speakers no layer shows keep
    // their place after those.
    private void OrderSpeakersByLayers(List<ProjectChange> made)
    {
        if (root["speakers"] is not JsonArray speakers) return;
        List<string> layerOrder = Layers()
            .Where(item => JsonAccess.TryGetString(item.layer, "type", out string type) && type == "Speaker")
            .Select(item => JsonAccess.GetPropertyString(item.layer, "Speaker"))
            .OfType<string>().Where(key => key.Length > 0).Distinct().ToList();
        JsonNode?[] before = speakers.ToArray();
        int Rank(JsonNode? node) => node is JsonObject speaker && JsonAccess.TryGetString(speaker, "key", out string key) &&
            layerOrder.IndexOf(key) is int rank and >= 0 ? rank : layerOrder.Count;
        JsonNode?[] after = before.Select((node, index) => (node, index)).OrderBy(pair => Rank(pair.node)).ThenBy(pair => pair.index)
            .Select(pair => pair.node).ToArray();
        if (before.SequenceEqual(after)) return;
        string?[] Keys(IEnumerable<JsonNode?> nodes) => nodes.Select(node => node is JsonObject speaker &&
            JsonAccess.TryGetString(speaker, "key", out string key) ? key : null).ToArray();
        speakers.Clear();
        foreach (JsonNode? node in after) speakers.Add(node);
        ProjectChange change = new("OrderSpeakers", "speakers", Keys(before), Keys(after), "$.speakers");
        changes.Add(change);
        made.Add(change);
    }

    private static LayerMoveResult LayerMoveFailed(string code, string message) => new(EditResult.Failed(code, message), null, null);

    /// <summary>Gets whether a Speaker layer cloned from the verified template would find its text style and telop frame in the project.</summary>
    internal static bool CanAddSpeakerLayerTo(JsonObject root)
    {
        try
        {
            return ProjectTemplateResources.LoadRoot(SpeakerLayerTemplateResource)["layers"] is JsonArray templateLayers &&
                templateLayers.OfType<JsonObject>().LastOrDefault(layer =>
                    JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker") is JsonObject template &&
                MissingSpeakerLayerDefinition(root, template) is null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException) { return false; }
    }

    private static string? MissingSpeakerLayerDefinition(JsonObject root, JsonObject layer)
    {
        string? style = JsonAccess.GetPropertyString(layer, "DefaultTextStyle");
        bool styleDefined = root["text-styles"] is JsonArray styles && styles.OfType<JsonObject>()
            .Any(item => JsonAccess.TryGetString(item, "StyleName", out string name) && name == style);
        if (!string.IsNullOrEmpty(style) && !styleDefined) return $"the text style '{style}'";
        string? frame = JsonAccess.GetPropertyString(layer, "TelopFrame");
        if (!string.IsNullOrEmpty(frame) && !TelopFrameView.ReadAll(root).Any(item => item.Name == frame)) return $"the telop frame '{frame}'";
        return null;
    }

    private static LayerAddResult LayerFailed(string code, string message) => new(EditResult.Failed(code, message), null, null);
}
