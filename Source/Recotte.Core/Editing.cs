using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>Identifies a Speaker layer by its stable index in the layers array.</summary>
public readonly record struct SpeakerLayerId(int LayerIndex);

/// <summary>Requests replacement of a text-only Speaker Voice body.</summary>
public sealed record UpdateSpeakerTextRequest(TimelineObjectId ObjectId, string Text);

/// <summary>Requests a new time range for a timeline object.</summary>
public sealed record MoveTimelineObjectRequest(TimelineObjectId ObjectId, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Requests creation of a text-only Speaker Voice.</summary>
public sealed record AddSpeakerTextRequest(SpeakerLayerId LayerId, string Text, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Describes one atomic JSON change made in an edit session.</summary>
public sealed record ProjectChange(string Operation, string Target, object? Before, object? After, string JsonPath);

/// <summary>Contains the structured outcome of an editing operation.</summary>
public sealed record EditResult
{
    /// <summary>Initializes an immutable editing result.</summary>
    public EditResult(bool success, IReadOnlyList<ProjectDiagnostic> diagnostics, IReadOnlyList<ProjectChange> changes)
    {
        Success = success;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        Changes = Array.AsReadOnly(changes.ToArray());
    }
    /// <summary>Gets whether the operation succeeded.</summary>
    public bool Success { get; }
    /// <summary>Gets immutable edit diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
    /// <summary>Gets immutable changes produced by the operation.</summary>
    public IReadOnlyList<ProjectChange> Changes { get; }
    /// <summary>Creates a successful result.</summary>
    public static EditResult Succeeded(IReadOnlyList<ProjectChange>? changes = null) => new(true, Array.Empty<ProjectDiagnostic>(), changes ?? Array.Empty<ProjectChange>());

    /// <summary>Creates a rejected result.</summary>
    public static EditResult Failed(string code, string message, string? path = null) => new(false, new[] { new ProjectDiagnostic(code, DiagnosticSeverity.Error, message, path) }, Array.Empty<ProjectChange>());
}

/// <summary>Allocates an object key independently from editing behavior.</summary>
public interface IObjectKeyAllocator
{
    /// <summary>Returns an unused object key or throws when no key remains.</summary>
    int Allocate(IReadOnlyCollection<int> existingKeys);
}

/// <summary>Implements the provisional maximum-plus-one project-wide object-key rule.</summary>
public sealed class ProvisionalObjectKeyAllocator : IObjectKeyAllocator
{
    /// <inheritdoc />
    public int Allocate(IReadOnlyCollection<int> existingKeys)
    {
        if (existingKeys.Count == 0)
        {
            return 1;
        }
        int maximum = existingKeys.Max();
        if (maximum == int.MaxValue)
        {
            throw new OverflowException("No object key is available after Int32.MaxValue.");
        }
        return checked(maximum + 1);
    }
}

/// <summary>Controls how edits extending beyond the current project duration are handled.</summary>
public interface IProjectDurationPolicy
{
    /// <summary>Decides whether and how to accommodate an object's required end time.</summary>
    ProjectDurationDecision Evaluate(decimal currentDuration, bool autoDuration, decimal requiredDuration);
}

/// <summary>Contains an immutable project-duration policy decision.</summary>
public sealed record ProjectDurationDecision(bool Accepted, decimal Duration, ProjectDiagnostic? Diagnostic);

/// <summary>Extends duration only when auto-duration is enabled and otherwise rejects the edit.</summary>
public sealed class AutoExtendProjectDurationPolicy : IProjectDurationPolicy
{
    /// <inheritdoc />
    public ProjectDurationDecision Evaluate(decimal currentDuration, bool autoDuration, decimal requiredDuration)
    {
        if (requiredDuration <= currentDuration)
        {
            return new(true, currentDuration, null);
        }
        if (!autoDuration)
        {
            ProjectDiagnostic diagnostic = new("RC4102", DiagnosticSeverity.Error, "The edit exceeds duration while auto-duration is disabled.", "$.setting.duration");
            return new(false, currentDuration, diagnostic);
        }
        return new(true, requiredDuration, null);
    }
}

/// <summary>Owns an isolated mutable project copy until it is validated and committed.</summary>
public sealed partial class ProjectEditSession : IDisposable
{
    private readonly RecotteProjectDocument document;
    private readonly JsonObject workingRoot;
    private bool completed;
    private bool batchApplied;
    private bool rollbackOnly;
    private readonly List<ProjectChange> changes = new();

    internal ProjectEditSession(RecotteProjectDocument document, JsonObject workingRoot, IObjectKeyAllocator? allocator,
        IProjectDurationPolicy? durationPolicy, IFileItemKeyAllocator? fileItemKeyAllocator)
    {
        this.document = document;
        this.workingRoot = workingRoot;
        Changes = changes.AsReadOnly();
        Editor = new ProjectEditor(workingRoot, changes, allocator ?? new ProvisionalObjectKeyAllocator(),
            durationPolicy ?? new AutoExtendProjectDurationPolicy(), fileItemKeyAllocator ?? new CryptographicFileItemKeyAllocator());
    }

    /// <summary>Gets the editor operating on the session's private JSON clone.</summary>
    public ProjectEditor Editor { get; }

    /// <summary>Gets changes successfully staged by this session.</summary>
    public IReadOnlyList<ProjectChange> Changes { get; }

    /// <summary>Validates the complete staged project.</summary>
    public ProjectValidationResult Validate() => ProjectValidator.Validate(workingRoot, Array.Empty<ProjectDiagnostic>());

    /// <summary>Atomically publishes the staged JSON if validation succeeds.</summary>
    public EditResult Commit()
    {
        ObjectDisposedException.ThrowIf(completed, this);
        if (rollbackOnly)
        {
            return EditResult.Failed("RC6104", "The edit session was rolled back after a failed batch and cannot be committed.");
        }
        ProjectValidationResult validation = Validate();
        if (!validation.IsValid)
        {
            return new(false, validation.Diagnostics, Changes);
        }
        document.CommitEdit(workingRoot);
        completed = true;
        document.EndEdit();
        return new(true, validation.Diagnostics, Changes);
    }

    /// <summary>Discards all uncommitted changes and releases the document edit lock.</summary>
    public void Dispose()
    {
        if (!completed)
        {
            completed = true;
            document.EndEdit();
        }
    }
}

/// <summary>Provides safe high-level operations over a private edit-session JSON tree.</summary>
public sealed partial class ProjectEditor
{
    private readonly JsonObject root;
    private readonly List<ProjectChange> changes;
    private readonly IObjectKeyAllocator allocator;
    private readonly IProjectDurationPolicy durationPolicy;
    private readonly IFileItemKeyAllocator fileItemKeyAllocator;

    internal ProjectEditor(JsonObject root, List<ProjectChange> changes, IObjectKeyAllocator allocator,
        IProjectDurationPolicy durationPolicy, IFileItemKeyAllocator fileItemKeyAllocator)
    {
        this.root = root;
        this.changes = changes;
        this.allocator = allocator;
        this.durationPolicy = durationPolicy;
        this.fileItemKeyAllocator = fileItemKeyAllocator;
    }

    /// <summary>Enumerates Speaker Voice objects without exposing mutable JSON.</summary>
    public IReadOnlyList<SpeakerVoiceView> SpeakerVoices => Layers().SelectMany(x => x.layer["layer-objects"] is JsonArray a
        ? a.Select((n, i) => n is JsonObject o && JsonAccess.TryGetString(o, "type", out string t) && t == "Speaker Voice" ? new SpeakerVoiceView(o, x.index, i) : null).OfType<SpeakerVoiceView>()
        : Array.Empty<SpeakerVoiceView>()).ToArray();

    /// <summary>Finds a timeline object by layer index and object key.</summary>
    public TimelineObjectView? FindTimelineObject(TimelineObjectId id)
    {
        var found = Find(id);
        return found is null ? null : TimelineObjectView.Create(found.Value.obj, id.LayerIndex, found.Value.objectIndex);
    }

    /// <summary>Synchronizes the supported text fields of a text-only Speaker Voice.</summary>
    public EditResult UpdateSpeakerText(UpdateSpeakerTextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = Find(request.ObjectId);
        EditResult? rejected = CheckSpeakerVoice(found);
        if (rejected is not null) return rejected;
        JsonObject obj = found!.Value.obj;
        string path = found.Value.path;
        if (obj.ContainsKey("audio") || !string.IsNullOrEmpty(JsonAccess.GetPropertyString(obj, "File")) || (JsonAccess.TryGetInt32(obj, "voice-hash", out int hash) && hash != 0))
            return EditResult.Failed("RC4205", "Text editing of an audio-backed Speaker Voice is not supported because voice-hash cannot be regenerated safely.", path);
        if (obj["text"] is not JsonObject text || text["stext"] is not JsonArray styled || !JsonAccess.TryGetString(text, "text", out string oldText))
            return EditResult.Failed("RC4203", "The required text structure is missing.", $"{path}.text");
        JsonObject[] fragments = styled.OfType<JsonObject>().Where(x => JsonAccess.TryGetString(x, "c", out string c) && c == "t").ToArray();
        if (fragments.Length != 1 || !JsonAccess.TryGetString(fragments[0], "text", out string fragmentText) || fragmentText != oldText || styled.OfType<JsonObject>().Any(x => JsonAccess.TryGetString(x, "c", out string c) && c is not ("s" or "t")))
            return EditResult.Failed("RC4204", "The styled text contains multiple fragments or unsupported decoration.", $"{path}.text.stext");
        List<ProjectChange> made = new();
        Change(obj, "name", request.Text, "UpdateSpeakerText", request.ObjectId.ToString(), $"{path}.name", made);
        Change(text, "text", request.Text, "UpdateSpeakerText", request.ObjectId.ToString(), $"{path}.text.text", made);
        Change(fragments[0], "text", request.Text, "UpdateSpeakerText", request.ObjectId.ToString(), $"{path}.text.stext[{styled.IndexOf(fragments[0])}].text", made);
        return EditResult.Succeeded(made);
    }

    /// <summary>Moves a Speaker Voice after validating locks, range, and duration policy.</summary>
    public EditResult MoveTimelineObject(MoveTimelineObjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartTime.TotalSeconds > request.EndTime.TotalSeconds) return EditResult.Failed("RC4301", "Start time must not be after end time.");
        var found = Find(request.ObjectId); EditResult? rejected = CheckSpeakerVoice(found); if (rejected is not null) return rejected;
        EditResult? durationError = ApplyDuration(request.EndTime.TotalSeconds); if (durationError is not null) return durationError;
        List<ProjectChange> made = new();
        Change(found!.Value.obj, "start-time", request.StartTime.TotalSeconds, "MoveTimelineObject", request.ObjectId.ToString(), $"{found.Value.path}.start-time", made);
        Change(found.Value.obj, "end-time", request.EndTime.TotalSeconds, "MoveTimelineObject", request.ObjectId.ToString(), $"{found.Value.path}.end-time", made);
        return EditResult.Succeeded(made);
    }

    /// <summary>Adds a text-only Speaker Voice by deep-cloning a compatible object in the target layer.</summary>
    public EditResult AddTextOnlySpeakerVoice(AddSpeakerTextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartTime.TotalSeconds > request.EndTime.TotalSeconds) return EditResult.Failed("RC4301", "Start time must not be after end time.");
        var layerPair = Layers().FirstOrDefault(x => x.index == request.LayerId.LayerIndex);
        JsonObject? layer = layerPair.layer;
        if (layer is null || !JsonAccess.TryGetString(layer, "type", out string type) || type != "Speaker") return EditResult.Failed("RC4401", "The target is not a Speaker layer.");
        if (IsLocked(layer)) return EditResult.Failed("RC4207", "The target layer is locked.");
        if (layer["layer-objects"] is not JsonArray objects) return EditResult.Failed("RC4203", "layer-objects is missing.");
        JsonObject? template = objects.OfType<JsonObject>().FirstOrDefault(IsTextOnlySpeakerVoice);
        if (template is null)
        {
            // The layer has no voice to clone, so fall back to the embedded template the same way assets do.
            if (!TextVoiceTemplateResources.Exists) return EditResult.Failed("RC4402", "No verified text-only Speaker Voice template exists in the target layer or in the built-in template.");
            template = TextVoiceTemplateResources.TryClone(root);
            if (template is null) return EditResult.Failed("RC4405", "The built-in text template and the target project define incompatible text styles or telop frames, so cloning it would create dangling references.");
            if (!IsTextOnlySpeakerVoice(template)) return EditResult.Failed("RC4402", "The built-in text template is not a verified text-only Speaker Voice.");
        }
        HashSet<int> keys = Layers().SelectMany(x => (x.layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>()).Where(x => JsonAccess.TryGetInt32(x, "objkey", out _)).Select(x => { JsonAccess.TryGetInt32(x, "objkey", out int k); return k; }).ToHashSet();
        int key; try { key = allocator.Allocate(keys); } catch (OverflowException ex) { return EditResult.Failed("RC4403", ex.Message); }
        if (keys.Contains(key)) return EditResult.Failed("RC4404", "The object-key allocator returned a duplicate key.");
        EditResult? durationError = ApplyDuration(request.EndTime.TotalSeconds); if (durationError is not null) return durationError;
        JsonObject added = (JsonObject)template.DeepClone();
        added["objkey"] = key; added["name"] = request.Text; added["start-time"] = request.StartTime.TotalSeconds; added["end-time"] = request.EndTime.TotalSeconds; added.Remove("audio"); added["voice-hash"] = 0;
        ((JsonObject)((JsonObject)added["properties"]!)["File"]!)["p-value"] = "";
        JsonObject text = (JsonObject)added["text"]!; text["text"] = request.Text;
        foreach (JsonObject fragment in ((JsonArray)text["stext"]!).OfType<JsonObject>().Where(x => JsonAccess.TryGetString(x, "c", out string c) && c == "t")) fragment["text"] = request.Text;
        objects.Add(added);
        string path = $"$.layers[{request.LayerId.LayerIndex}].layer-objects[{objects.Count - 1}]";
        ProjectChange change = new("AddTextOnlySpeakerVoice", key.ToString(), null, request.Text, path); changes.Add(change);
        return EditResult.Succeeded(new[] { change });
    }

    /// <summary>Removes any unlocked timeline object and prunes its file registration when it becomes unused.</summary>
    public EditResult RemoveTimelineObject(TimelineObjectId id)
    {
        var found = Find(id);
        EditResult? rejected = CheckTimelineObject(found);
        if (rejected is not null) return rejected;

        JsonObject timelineObject = found!.Value.obj;
        string? fileItemKey = JsonAccess.GetPropertyString(timelineObject, "File");
        object? before = Snapshot(timelineObject);
        JsonArray objects = (JsonArray)found.Value.layer["layer-objects"]!;
        objects.RemoveAt(found.Value.objectIndex);

        List<ProjectChange> made = new();
        ProjectChange removal = new("RemoveTimelineObject", id.ToString(), before, null, found.Value.path);
        changes.Add(removal);
        made.Add(removal);
        PruneFileItemIfUnreferenced(fileItemKey, made);
        return EditResult.Succeeded(made);
    }

    private IEnumerable<(JsonObject layer, int index)> Layers() => root["layers"] is JsonArray layers ? layers.Select((x, i) => (x as JsonObject, i)).Where(x => x.Item1 is not null).Select(x => (x.Item1!, x.i)) : Array.Empty<(JsonObject, int)>();
    private (JsonObject layer, JsonObject obj, int objectIndex, string path)? Find(TimelineObjectId id) { var p = Layers().FirstOrDefault(x => x.index == id.LayerIndex); if (p.layer?["layer-objects"] is not JsonArray a) return null; for (int i=0;i<a.Count;i++) if (a[i] is JsonObject o && JsonAccess.TryGetInt32(o,"objkey",out int k)&&k==id.ObjectKey) return (p.layer,o,i,$"$.layers[{id.LayerIndex}].layer-objects[{i}]"); return null; }
    private EditResult? CheckTimelineObject((JsonObject layer, JsonObject obj, int objectIndex, string path)? found) { if (found is null) return EditResult.Failed("RC4201","The target timeline object does not exist."); if (IsLocked(found.Value.layer)||IsLocked(found.Value.obj)) return EditResult.Failed("RC4207","The target object or layer is locked.",found.Value.path); return null; }
    private EditResult? CheckSpeakerVoice((JsonObject layer, JsonObject obj, int objectIndex, string path)? found) { if (found is null) return EditResult.Failed("RC4201","The target timeline object does not exist."); if (!JsonAccess.TryGetString(found.Value.obj,"type",out string t)||t!="Speaker Voice") return EditResult.Failed("RC4202","The target is not a Speaker Voice.",found.Value.path); if (IsLocked(found.Value.layer)||IsLocked(found.Value.obj)) return EditResult.Failed("RC4207","The target object or layer is locked.",found.Value.path); return null; }
    private void PruneFileItemIfUnreferenced(string? fileItemKey, List<ProjectChange> made)
    {
        if (string.IsNullOrEmpty(fileItemKey)) return;
        bool referenced = Layers().SelectMany(x => (x.layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
            .Any(item => string.Equals(JsonAccess.GetPropertyString(item, "File"), fileItemKey, StringComparison.Ordinal));
        if (referenced || root["file-items"] is not JsonArray fileItems) return;
        for (int index = 0; index < fileItems.Count; index++)
        {
            if (fileItems[index] is not JsonObject item || !JsonAccess.TryGetString(item, "ik", out string key) ||
                !string.Equals(key, fileItemKey, StringComparison.Ordinal)) continue;
            string path = $"$.file-items[{index}]";
            object? before = Snapshot(item);
            fileItems.RemoveAt(index);
            ProjectChange change = new("UnregisterFileItem", $"file:{fileItemKey}", before, null, path);
            changes.Add(change);
            made.Add(change);
            return;
        }
    }
    private static bool IsLocked(JsonObject o) => new[] { "locked", "tl-locked", "st-locked", "pv-locked" }.Any(n => o[n] is JsonValue v && v.TryGetValue(out bool b) && b);
    internal static bool IsTextOnlySpeakerVoice(JsonObject o) => JsonAccess.TryGetString(o,"type",out string t)&&t=="Speaker Voice"&&!o.ContainsKey("audio")&&string.IsNullOrEmpty(JsonAccess.GetPropertyString(o,"File"))&&JsonAccess.TryGetInt32(o,"voice-hash",out int h)&&h==0&&o["text"] is JsonObject tx&&tx["stext"] is JsonArray;
    private EditResult? ApplyDuration(decimal required) { if (root["setting"] is not JsonObject s || !JsonAccess.TryGetDecimal(s,"duration",out decimal current)) return EditResult.Failed("RC4101","Project duration is unavailable.","$.setting.duration"); bool automatic=s["auto-duration"] is JsonValue v&&v.TryGetValue(out bool b)&&b; ProjectDurationDecision d=durationPolicy.Evaluate(current,automatic,required); if (!d.Accepted) return new(false,d.Diagnostic is null?Array.Empty<ProjectDiagnostic>():new[]{d.Diagnostic},Array.Empty<ProjectChange>()); if (d.Duration>current) s["duration"]=d.Duration; return null; }
    private void Change(JsonObject node,string property,object value,string operation,string target,string path,List<ProjectChange> made) { object? before=Snapshot(node[property]); node[property]=JsonValue.Create(value); ProjectChange c=new(operation,target,before,value,path); changes.Add(c); made.Add(c); }
    private static object? Snapshot(JsonNode? node) { if (node is null) return null; if (node is JsonValue v) { if(v.TryGetValue(out string? s)) return s; if(v.TryGetValue(out int i)) return i; if(v.TryGetValue(out decimal d)) return d; if(v.TryGetValue(out double x)) return x; if(v.TryGetValue(out bool b)) return b; } return node.ToJsonString(); }
}
