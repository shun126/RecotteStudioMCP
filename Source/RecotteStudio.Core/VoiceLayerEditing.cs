using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Requests moving a Speaker Voice to another Speaker layer, keeping its text and time range.</summary>
public sealed record MoveSpeakerVoiceToLayerRequest(TimelineObjectId ObjectId, SpeakerLayerId TargetLayer);

/// <summary>Contains the result of moving a Speaker Voice between Speaker layers.</summary>
public sealed record SpeakerVoiceMoveResult(EditResult EditResult, TimelineObjectId? ObjectId)
{
    // Where the voice sat in the layers' object arrays before and after the move, for rebasing recorded locations.
    internal VoiceRelocation? Relocation { get; init; }

    /// <summary>Gets whether the voice was moved.</summary>
    public bool Success => EditResult.Success;
    /// <summary>Gets the operation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => EditResult.Diagnostics;
    /// <summary>Gets the staged project changes.</summary>
    public IReadOnlyList<ProjectChange> Changes => EditResult.Changes;
}

public sealed partial class ProjectEditor
{
    /// <summary>
    /// Moves a Speaker Voice to another Speaker layer, so that layer's speaker says it. The speaker belongs to the layer
    /// (its Speaker property), not to the voice, so the voice keeps its object key, text, and time range; the text style and
    /// telop frame it takes from the source layer's defaults switch to the target layer's. An audio-backed voice keeps its
    /// audio, so moving it to a layer with a different speaker carries an RC4208 warning. The project does not record whose
    /// voice the audio has, so the warning names only the source layer's speaker; within one batch, the batch result reports
    /// one warning per voice from where it started.
    /// </summary>
    public SpeakerVoiceMoveResult MoveSpeakerVoiceToLayer(MoveSpeakerVoiceToLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = Find(request.ObjectId);
        EditResult? rejected = CheckSpeakerVoice(found);
        if (rejected is not null) return new(rejected, null);
        (JsonObject source, JsonObject voice, int objectIndex, _) = found!.Value;

        int targetIndex = request.TargetLayer.LayerIndex;
        if (targetIndex == request.ObjectId.LayerIndex)
            return MoveFailed("RC4209", "The Speaker Voice is already on the target layer.");
        JsonObject? target = Layers().FirstOrDefault(item => item.index == targetIndex).layer;
        if (target is null || !JsonAccess.TryGetString(target, "type", out string type) || type != "Speaker" || IsLocked(target) ||
            target["layer-objects"] is not JsonArray targetObjects)
            return MoveFailed("RC4209", $"Layer {targetIndex} is not an unlocked Speaker layer.");
        int originalKey = request.ObjectId.ObjectKey;
        int key = originalKey;
        // Recotte Studio numbers objects per layer (both voices of 011TwoVoices are 1001), so a key the target layer already
        // uses is replaced by one unused anywhere in the project.
        if (targetObjects.OfType<JsonObject>().Any(item => JsonAccess.TryGetInt32(item, "objkey", out int other) && other == key))
        {
            try { key = allocator.Allocate(AllObjectKeys()); }
            catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
            { return MoveFailed("RC4210", exception.Message); }
            if (AllObjectKeys().Contains(key)) return MoveFailed("RC4210", "The object-key allocator returned a key already in use.");
        }
        if (!JsonAccess.TryGetDecimal(voice, "start-time", out decimal start) || !JsonAccess.TryGetDecimal(voice, "end-time", out decimal end))
            return MoveFailed("RC4203", "The Speaker Voice has no time range.");
        // One speaker says one line at a time, so the voice must fit between the target layer's own lines.
        JsonObject? overlapping = targetObjects.OfType<JsonObject>().FirstOrDefault(item =>
            JsonAccess.TryGetString(item, "type", out string itemType) && itemType == "Speaker Voice" &&
            JsonAccess.TryGetDecimal(item, "start-time", out decimal otherStart) &&
            JsonAccess.TryGetDecimal(item, "end-time", out decimal otherEnd) && otherStart < end && start < otherEnd);
        if (overlapping is not null)
        {
            JsonAccess.TryGetInt32(overlapping, "objkey", out int otherKey);
            return MoveFailed("RC4211", $"The Speaker Voice overlaps the Speaker Voice with object key {otherKey} on the target layer.");
        }

        JsonArray sourceObjects = (JsonArray)source["layer-objects"]!;
        sourceObjects.RemoveAt(objectIndex);
        targetObjects.Add(voice);
        if (key != originalKey) voice["objkey"] = key;
        TimelineObjectId moved = new(targetIndex, key);
        VoiceRelocation relocation = new(request.ObjectId, objectIndex, moved, targetObjects.Count - 1);
        // Changes staged earlier located the voice, and the objects behind it on the source layer, where they no longer are.
        for (int prior = 0; prior < changes.Count; prior++)
            changes[prior] = LayerInsertionMetadata.Change(changes[prior], relocation);
        string path = $"$.layers[{targetIndex}].layer-objects[{relocation.ToIndex}]";
        List<ProjectChange> made = new();
        ProjectChange change = new("MoveSpeakerVoiceToLayer", moved.ToString(), request.ObjectId.ToString(), moved.ToString(), path);
        changes.Add(change);
        made.Add(change);
        RebindVoiceToLayer(voice, source, target, moved, path, made);
        // start-snap names the object on the same layer that a clip's start snaps to (the Transition of 009FullProject snaps
        // to video 1001); that object stays behind, so both the voice's snap and any snap to the voice are released.
        if (JsonAccess.TryGetInt32(voice, "start-snap", out int snap) && snap != 0)
            Change(voice, "start-snap", 0, "MoveSpeakerVoiceToLayer", moved.ToString(), $"{path}.start-snap", made);
        for (int index = 0; index < sourceObjects.Count; index++)
            if (sourceObjects[index] is JsonObject other && JsonAccess.TryGetInt32(other, "start-snap", out int otherSnap) && otherSnap == originalKey)
                Change(other, "start-snap", 0, "MoveSpeakerVoiceToLayer", moved.ToString(),
                    $"$.layers[{request.ObjectId.LayerIndex}].layer-objects[{index}].start-snap", made);

        string? fromSpeaker = JsonAccess.GetPropertyString(source, "Speaker");
        string? toSpeaker = JsonAccess.GetPropertyString(target, "Speaker");
        if (!HasAudioMarker(voice) || string.Equals(fromSpeaker, toSpeaker, StringComparison.Ordinal))
            return new(EditResult.Succeeded(made), moved) { Relocation = relocation };
        return new(new(true, new[] { MovedAudioDiagnostic(key, source, target, path) }, made), moved) { Relocation = relocation };
    }

    // The audio may have been made for a speaker before the source layer's, so the message states only where the voice came from.
    internal static ProjectDiagnostic MovedAudioDiagnostic(int key, JsonObject source, JsonObject target, string path) =>
        new("RC4208", DiagnosticSeverity.Warning,
            $"The audio-backed Speaker Voice with object key {key} was moved from layer '{LayerName(source)}' to layer " +
            $"'{LayerName(target)}'. It came from a layer whose speaker is '{JsonAccess.GetPropertyString(source, "Speaker")}', " +
            $"so its existing audio may not have the voice of the speaker on layer '{LayerName(target)}'; regenerate the audio for this clip in Recotte Studio.", path);

    // A voice that uses its layer's default text style or telop frame follows the layer it is on; a voice given its own
    // style or frame keeps it.
    private void RebindVoiceToLayer(JsonObject voice, JsonObject source, JsonObject target, TimelineObjectId moved,
        string path, List<ProjectChange> made)
    {
        string? fromStyle = JsonAccess.GetPropertyString(source, "DefaultTextStyle");
        string? toStyle = JsonAccess.GetPropertyString(target, "DefaultTextStyle");
        if (!string.IsNullOrEmpty(fromStyle) && !string.IsNullOrEmpty(toStyle) && fromStyle != toStyle &&
            voice["text"] is JsonObject text && text["stext"] is JsonArray styled)
        {
            for (int index = 0; index < styled.Count; index++)
                if (styled[index] is JsonObject fragment && JsonAccess.TryGetString(fragment, "c", out string kind) && kind == "s" &&
                    JsonAccess.TryGetString(fragment, "style", out string style) && style == fromStyle)
                    Change(fragment, "style", toStyle, "MoveSpeakerVoiceToLayer", moved.ToString(), $"{path}.text.stext[{index}].style", made);
        }

        string? fromFrame = JsonAccess.GetPropertyString(source, "TelopFrame");
        string? toFrame = JsonAccess.GetPropertyString(target, "TelopFrame");
        if (!string.IsNullOrEmpty(fromFrame) && !string.IsNullOrEmpty(toFrame) && fromFrame != toFrame &&
            voice["properties"] is JsonObject properties && properties["TelopFrame"] is JsonObject frame &&
            JsonAccess.TryGetString(frame, "p-value", out string current) && current == fromFrame)
            Change(frame, "p-value", toFrame, "MoveSpeakerVoiceToLayer", moved.ToString(), $"{path}.properties.TelopFrame.p-value", made);
    }

    private static string LayerName(JsonObject layer) => JsonAccess.TryGetString(layer, "name", out string name) ? name : string.Empty;
    private static SpeakerVoiceMoveResult MoveFailed(string code, string message) => new(EditResult.Failed(code, message), null);
}

// A Speaker Voice taken from index FromIndex of its layer's objects and appended to another layer's objects at ToIndex.
internal sealed record VoiceRelocation(TimelineObjectId From, int FromIndex, TimelineObjectId To, int ToIndex);
