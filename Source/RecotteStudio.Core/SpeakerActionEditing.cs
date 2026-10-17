using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>
/// Requests a Speaker Action that plays one of the layer character's actions, such as an expression or a gesture, from
/// <paramref name="StartTime"/> to <paramref name="EndTime"/>. <paramref name="Action"/> is an entry of the layer's
/// actions: its full name (pack:name) or, when unique, the part after the colon. A looping action repeats until the clip ends.
/// </summary>
public sealed record AddSpeakerActionRequest(SpeakerLayerId LayerId, string Action, ProjectTime StartTime, ProjectTime EndTime,
    bool Loop = false);

/// <summary>Adds a Speaker Action that plays one of the layer character's actions; see <see cref="AddSpeakerActionRequest"/>.</summary>
public sealed record AddSpeakerActionOperation(LayerReference Layer, string Action, ProjectTime StartTime, ProjectTime EndTime,
    bool Loop = false) : ProjectOperation
{
    /// <summary>Gets the stable machine-readable operation kind.</summary>
    public override ProjectOperationKind Kind => ProjectOperationKind.AddSpeakerAction;
}

/// <summary>Contains the result of adding a Speaker Action.</summary>
public sealed record SpeakerActionAddResult(EditResult EditResult, TimelineObjectId? ObjectId, string? Action)
{
    /// <summary>Gets whether the action was added.</summary>
    public bool Success => EditResult.Success;
    /// <summary>Gets the operation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => EditResult.Diagnostics;
    /// <summary>Gets the staged project changes.</summary>
    public IReadOnlyList<ProjectChange> Changes => EditResult.Changes;
}

public sealed partial class ProjectEditor
{
    internal const string SpeakerActionTemplateResource = "RecotteStudio.Core.Templates.SpeakerAction.ccproj";

    /// <summary>
    /// Adds a Speaker Action to a Speaker layer that holds a character, cloned from 015SpeakerActions. Recotte Studio blends
    /// an action in and out over the InWeight and OutWeight sub-objects at the clip's two ends, so the clip must be at least
    /// as long as both blends together, and OutWeight is moved to the clip's new end. Actions may overlap one another and
    /// the layer's Speaker Voices, as they do in 015SpeakerActions.
    /// </summary>
    public SpeakerActionAddResult AddSpeakerAction(AddSpeakerActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Action)) return ActionFailed("RC4922", "An action is required.");
        decimal start = request.StartTime.TotalSeconds;
        decimal end = request.EndTime.TotalSeconds;
        if (start >= end) return ActionFailed("RC4923", "The action time range is invalid.");

        var layerPair = Layers().FirstOrDefault(item => item.index == request.LayerId.LayerIndex);
        JsonObject? layer = layerPair.layer;
        if (layer is null || !JsonAccess.TryGetString(layer, "type", out string type) || type != "Speaker" || IsLocked(layer) ||
            layer["layer-objects"] is not JsonArray objects)
            return ActionFailed("RC4921", $"Layer {request.LayerId.LayerIndex} is not an unlocked Speaker layer.");
        string[] available = LayerActions(layer);
        if (string.IsNullOrEmpty(JsonAccess.GetPropertyString(layer, "Speaker")) || available.Length == 0)
            return ActionFailed("RC4921", $"Layer {request.LayerId.LayerIndex} holds no character with actions; import one with importCharacter first.");
        string? action = ResolveAction(available, request.Action);
        if (action is null)
            return ActionFailed("RC4922", $"The character on layer {request.LayerId.LayerIndex} has no action '{request.Action}'. Available actions: {string.Join(", ", available)}.");

        JsonObject? template;
        try
        {
            template = ProjectTemplateResources.LoadRoot(SpeakerActionTemplateResource)["layers"] is JsonArray templateLayers
                ? templateLayers.OfType<JsonObject>()
                    .SelectMany(item => (item["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
                    .FirstOrDefault(item => JsonAccess.TryGetString(item, "type", out string itemType) && itemType == "Speaker Action")
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException)
        { return ActionFailed("RC4924", exception.Message); }
        JsonObject? added = template?.DeepClone() as JsonObject;
        JsonObject? blendIn = Weight(added, "InWeight");
        JsonObject? blendOut = Weight(added, "OutWeight");
        if (added is null || blendIn is null || blendOut is null ||
            !JsonAccess.TryGetDecimal(blendIn, "duration", out decimal inDuration) ||
            !JsonAccess.TryGetDecimal(blendOut, "duration", out decimal outDuration))
            return ActionFailed("RC4924", "The verified Speaker Action template is unavailable.");
        if (end - start < inDuration + outDuration)
            return ActionFailed("RC4923", FormattableString.Invariant(
                $"The action must last at least {inDuration + outDuration:0.###} seconds, the length of its blend-in and blend-out."));

        int objectKey;
        try { objectKey = allocator.Allocate(AllObjectKeys()); }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        { return ActionFailed("RC4925", exception.Message); }
        if (AllObjectKeys().Contains(objectKey)) return ActionFailed("RC4925", "The object-key allocator returned a key already in use.");

        EditResult? durationError = ApplyDuration(end);
        if (durationError is not null) return new(durationError, null, null);

        added["objkey"] = objectKey;
        added["start-time"] = start;
        added["end-time"] = end;
        added["selected"] = false;
        SetPropertyValue(added, "Emotion", JsonValue.Create(action));
        SetPropertyValue(added, "Loop", JsonValue.Create(request.Loop));
        blendOut["start-time"] = end - start - outDuration;
        objects.Add(added);
        TimelineObjectId objectId = new(request.LayerId.LayerIndex, objectKey);
        ProjectChange change = new("AddSpeakerAction", objectId.ToString(), null, action,
            $"$.layers[{request.LayerId.LayerIndex}].layer-objects[{objects.Count - 1}]");
        changes.Add(change);
        return new(EditResult.Succeeded(new[] { change }), objectId, action);
    }

    /// <summary>Lists the full names of the actions a Speaker layer's character can play.</summary>
    internal static string[] LayerActions(JsonObject layer) => (layer["actions"] as JsonArray)?.OfType<JsonObject>()
        .Select(item => JsonAccess.TryGetString(item, "name", out string name) ? name : null)
        .OfType<string>().Where(name => name.Length > 0).ToArray() ?? Array.Empty<string>();

    // The full name is pack:name (tsurumaki_maki:Smile_1.vmd); the name alone is accepted when only one pack has it.
    private static string? ResolveAction(string[] available, string requested)
    {
        if (available.Contains(requested, StringComparer.Ordinal)) return requested;
        string[] matches = available.Where(name => name.EndsWith(":" + requested, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static JsonObject? Weight(JsonObject? action, string category) => (action?["sub-objects"] as JsonArray)?.OfType<JsonObject>()
        .SingleOrDefault(item => JsonAccess.TryGetString(item, "category", out string value) && value == category);

    private static SpeakerActionAddResult ActionFailed(string code, string message) => new(EditResult.Failed(code, message), null, null);
}
