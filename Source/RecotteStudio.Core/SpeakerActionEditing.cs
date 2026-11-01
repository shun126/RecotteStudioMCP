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

/// <summary>
/// Requests a change to an existing Speaker Action: <paramref name="Action"/> replaces the action it plays (pack:name,
/// or the part after the colon when unique) and <paramref name="Loop"/> sets whether it repeats. A null value is kept.
/// </summary>
public sealed record UpdateSpeakerActionRequest(TimelineObjectId ObjectId, string? Action = null, bool? Loop = null);

/// <summary>Changes the action an existing Speaker Action plays, or its Loop; see <see cref="UpdateSpeakerActionRequest"/>.</summary>
public sealed record UpdateSpeakerActionOperation(TimelineObjectReference Target, string? Action = null, bool? Loop = null) : ProjectOperation
{
    /// <summary>Gets the stable machine-readable operation kind.</summary>
    public override ProjectOperationKind Kind => ProjectOperationKind.UpdateSpeakerAction;
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

    /// <summary>
    /// Changes the action an existing Speaker Action plays, its Loop, or both, keeping its time range and blends. The
    /// action must be one of the layer character's actions, as in <see cref="AddSpeakerAction"/>.
    /// </summary>
    public EditResult UpdateSpeakerAction(UpdateSpeakerActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action is null && request.Loop is null) return EditResult.Failed("RC4927", "Specify an action, loop, or both to change.");
        var found = Find(request.ObjectId);
        if (found is not { } target || !IsSpeakerAction(target.layer, target.obj))
            return EditResult.Failed("RC4926", "The target is not a Speaker Action.", found?.path);
        if (IsLocked(target.layer) || IsLocked(target.obj)) return EditResult.Failed("RC4207", "The target object or layer is locked.", target.path);
        if (target.obj["properties"] is not JsonObject properties || properties["Emotion"] is not JsonObject emotion ||
            properties["Loop"] is not JsonObject loop || HasKeyframes(emotion) || HasKeyframes(loop))
            return EditResult.Failed("RC4926", "The Speaker Action has no plain Emotion and Loop to change; edit it in Recotte Studio.", $"{target.path}.properties");

        string? action = null;
        if (request.Action is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Action)) return EditResult.Failed("RC4922", "An action is required.", target.path);
            string[] available = LayerActions(target.layer);
            action = ResolveAction(available, request.Action);
            if (action is null)
                return EditResult.Failed("RC4922", $"The character on layer {request.ObjectId.LayerIndex} has no action '{request.Action}'. Available actions: {string.Join(", ", available)}.", target.path);
        }

        string id = request.ObjectId.ToString();
        List<ProjectChange> made = new();
        if (action is not null)
            Change(emotion, "p-value", action, "UpdateSpeakerAction", id, $"{target.path}.properties.Emotion.p-value", made);
        if (request.Loop is bool repeat)
            Change(loop, "p-value", repeat, "UpdateSpeakerAction", id, $"{target.path}.properties.Loop.p-value", made);
        return EditResult.Succeeded(made);
    }

    /// <summary>
    /// Sets the time range of a Speaker Action. Its InWeight blend stays at the start and its OutWeight blend moves to
    /// the new end, so the action must still last at least both blends together.
    /// </summary>
    // A line moved with moveActions keeps the snap of an action that snaps to it, since both move together.
    private EditResult MoveSpeakerAction(MoveTimelineObjectRequest request, JsonObject layer, JsonObject obj, string path,
        int keepSnapTo = 0)
    {
        decimal start = request.StartTime.TotalSeconds, end = request.EndTime.TotalSeconds;
        EditResult? rejected = CheckSpeakerActionMove(layer, obj, path, start, end, out decimal oldStart, out JsonObject? blendOut,
            out decimal outDuration);
        if (rejected is not null) return rejected;

        EditResult? durationError = ApplyDuration(end);
        if (durationError is not null) return durationError;
        decimal length = end - start;
        string target = request.ObjectId.ToString();
        List<ProjectChange> made = new();
        Change(obj, "start-time", start, "MoveTimelineObject", target, $"{path}.start-time", made);
        Change(obj, "end-time", end, "MoveTimelineObject", target, $"{path}.end-time", made);
        if (start != oldStart && JsonAccess.TryGetInt32(obj, "start-snap", out int snap) && snap != 0 && snap != keepSnapTo)
            Change(obj, "start-snap", 0, "MoveTimelineObject", target, $"{path}.start-snap", made);
        if (blendOut is not null)
        {
            JsonArray subObjects = (JsonArray)obj["sub-objects"]!;
            Change(blendOut, "start-time", length - outDuration, "MoveTimelineObject", target,
                $"{path}.sub-objects[{subObjects.IndexOf(blendOut)}].start-time", made);
        }
        return EditResult.Succeeded(made);
    }

    // Checks a new time range for a Speaker Action; blendOut is set when the length changes and the OutWeight must move.
    private static EditResult? CheckSpeakerActionMove(JsonObject layer, JsonObject obj, string path, decimal start, decimal end,
        out decimal oldStart, out JsonObject? blendOut, out decimal outDuration)
    {
        blendOut = null;
        outDuration = 0;
        oldStart = 0;
        if (start >= end) return EditResult.Failed("RC4301", "The start time of a Speaker Action must be before its end time.", path);
        if (IsTimelineLocked(layer, obj)) return EditResult.Failed("RC4207", "The target object or layer is locked.", path);
        if (!TryGetTimeRange(obj, out oldStart, out decimal oldEnd))
            return EditResult.Failed("RC4203", "The target has no valid time range.", path);
        if (JoiningObject(layer, obj, oldStart, oldEnd) is int joined)
            return EditResult.Failed("RC4303", $"The target is joined to the object with key {joined} on the same layer, which would be left out of place. Move it in Recotte Studio.", path);
        if (end - start == oldEnd - oldStart) return null;

        JsonObject? blendIn = Weight(obj, "InWeight");
        blendOut = Weight(obj, "OutWeight");
        if (blendIn is null || blendOut is null || !JsonAccess.TryGetDecimal(blendIn, "duration", out decimal inDuration) ||
            !JsonAccess.TryGetDecimal(blendOut, "duration", out outDuration))
            return EditResult.Failed("RC4302", "The Speaker Action has no plain InWeight and OutWeight blends to follow a new length; keep its length or edit it in Recotte Studio.", $"{path}.sub-objects");
        if (end - start < inDuration + outDuration)
            return EditResult.Failed("RC4923", FormattableString.Invariant(
                $"The action must last at least {inDuration + outDuration:0.###} seconds, the length of its blend-in and blend-out."), path);
        return null;
    }

    // Recotte Studio saves times as doubles (0.9999999999999998 for 1), so an action placed with a line may miss it slightly.
    private const decimal FollowTolerance = 0.001m;

    /// <summary>
    /// Finds the Speaker Actions that lie within a Speaker Voice on its layer and maps each onto the line's new time range,
    /// keeping where it sits in the line: an action placed with the line's start and end takes the new start and end, and
    /// one in part of the line keeps its share. An action reaching outside the line, such as one held over several lines,
    /// stays. Every follower is checked before anything changes, so a locked or too short action fails the whole move.
    /// </summary>
    private EditResult? FollowingActions(MoveTimelineObjectRequest request, JsonObject layer, JsonObject voice,
        List<(MoveTimelineObjectRequest move, JsonObject action, string path)> followers)
    {
        if (!TryGetTimeRange(voice, out decimal oldStart, out decimal oldEnd) || layer["layer-objects"] is not JsonArray objects) return null;
        decimal newStart = request.StartTime.TotalSeconds, newEnd = request.EndTime.TotalSeconds;
        decimal scale = oldEnd > oldStart ? (newEnd - newStart) / (oldEnd - oldStart) : 1m;
        if (newStart == oldStart && newEnd == oldEnd) return null;
        for (int index = 0; index < objects.Count; index++)
        {
            if (objects[index] is not JsonObject action || !IsSpeakerAction(layer, action) ||
                !TryGetTimeRange(action, out decimal start, out decimal end) || !JsonAccess.TryGetInt32(action, "objkey", out int key) ||
                start < oldStart - FollowTolerance || end > oldEnd + FollowTolerance) continue;
            decimal mappedStart = Math.Abs(start - oldStart) <= FollowTolerance ? newStart : newStart + (start - oldStart) * scale;
            decimal mappedEnd = Math.Abs(end - oldEnd) <= FollowTolerance ? newEnd : newStart + (end - oldStart) * scale;
            string path = $"$.layers[{request.ObjectId.LayerIndex}].layer-objects[{index}]";
            EditResult? rejected = CheckSpeakerActionMove(layer, action, path, mappedStart, mappedEnd, out _, out _, out _);
            if (rejected is not null)
            {
                ProjectDiagnostic cause = rejected.Diagnostics[0];
                return EditResult.Failed(cause.Code, $"The Speaker Action with object key {key} lies within the line and cannot move with it: {cause.Message} Pass moveActions false to move the line alone.", path);
            }
            followers.Add((new(new(request.ObjectId.LayerIndex, key), new(mappedStart), new(mappedEnd)), action, path));
        }
        return null;
    }

    /// <summary>Returns whether the object is a Speaker Action on a Speaker layer.</summary>
    internal static bool IsSpeakerAction(JsonObject layer, JsonObject obj) =>
        JsonAccess.TryGetString(layer, "type", out string layerType) && layerType == "Speaker" &&
        JsonAccess.TryGetString(obj, "type", out string objectType) && objectType == "Speaker Action";

    /// <summary>Returns whether UpdateSpeakerAction can change the Speaker Action's action and Loop.</summary>
    internal static bool CanUpdateSpeakerAction(JsonObject layer, JsonObject obj) => IsSpeakerAction(layer, obj) &&
        !IsLocked(layer) && !IsLocked(obj) && obj["properties"] is JsonObject properties &&
        properties["Emotion"] is JsonObject emotion && !HasKeyframes(emotion) &&
        properties["Loop"] is JsonObject loop && !HasKeyframes(loop);

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
