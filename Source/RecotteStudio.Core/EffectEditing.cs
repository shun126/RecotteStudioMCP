using System.Text.Json.Nodes;

#pragma warning disable CS1591
namespace RecotteStudio.Core;

public sealed record AddClipEffectOperation(TimelineObjectReference Target, string EffectKey, ProjectTime Duration,
    ProjectTime? Start = null, IReadOnlyDictionary<string, decimal>? EffectProperties = null) : ProjectOperation
{ public override ProjectOperationKind Kind => ProjectOperationKind.AddClipEffect; }
public sealed record UpdateClipEffectOperation(TimelineObjectReference Target, int EffectIndex, ProjectTime? Start = null,
    ProjectTime? Duration = null, IReadOnlyDictionary<string, decimal>? EffectProperties = null) : ProjectOperation
{ public override ProjectOperationKind Kind => ProjectOperationKind.UpdateClipEffect; }
public sealed record RemoveClipEffectOperation(TimelineObjectReference Target, int EffectIndex) : ProjectOperation
{ public override ProjectOperationKind Kind => ProjectOperationKind.RemoveClipEffect; }
public sealed record ClipEffectView(int EffectIndex, string EffectKey, decimal Start, decimal Duration,
    JsonObject Properties, bool CanEdit);

public sealed partial class RecotteProjectDocument
{
    /// <summary>Lists clip effects with indices in the complete sub-objects array and copies of their property values.</summary>
    public IReadOnlyList<ClipEffectView> GetClipEffects(TimelineObjectId id)
    {
        JsonObject copy = CloneRoot();
        if (copy["layers"] is not JsonArray layers || id.LayerIndex < 0 || id.LayerIndex >= layers.Count)
            return Array.Empty<ClipEffectView>();
        JsonObject[] matches = (layers[id.LayerIndex]?["layer-objects"] as JsonArray)?.OfType<JsonObject>()
            .Where(item => JsonAccess.TryGetInt32(item, "objkey", out int key) && key == id.ObjectKey).ToArray() ?? Array.Empty<JsonObject>();
        if (matches.Length != 1) return Array.Empty<ClipEffectView>();
        return (matches[0]["sub-objects"] as JsonArray)?.Select((item, index) =>
        {
            if (item is not JsonObject effect || !ProjectEditor.IsClipEffect(effect) ||
                !JsonAccess.TryGetDecimal(effect, "start-time", out decimal start) ||
                !JsonAccess.TryGetDecimal(effect, "duration", out decimal duration)) return null;
            JsonObject values = new();
            if (effect["properties"] is JsonObject properties)
                foreach ((string name, JsonNode? entry) in properties)
                    values[name] = entry is JsonObject property ? property["p-value"]?.DeepClone() : null;
            return new ClipEffectView(index, effect["type-name"]!.GetValue<string>(), start, duration,
                values, ProjectEditor.IsSupportedClipEffect(effect));
        }).OfType<ClipEffectView>().ToArray() ?? Array.Empty<ClipEffectView>();
    }
}

public sealed partial class ProjectEditor
{
    public EditResult AddClipEffect(TimelineObjectId id, string effectKey, ProjectTime duration, ProjectTime? start = null,
        IReadOnlyDictionary<string, decimal>? effectProperties = null) => EditClipEffect(id, null, effectKey, start, duration, effectProperties);
    public EditResult UpdateClipEffect(TimelineObjectId id, int effectIndex, ProjectTime? start = null, ProjectTime? duration = null,
        IReadOnlyDictionary<string, decimal>? effectProperties = null) => EditClipEffect(id, effectIndex, null, start, duration, effectProperties);
    public EditResult RemoveClipEffect(TimelineObjectId id, int effectIndex) => EditClipEffect(id, effectIndex, null, null, null, null, remove: true);

    internal static bool IsClipEffect(JsonObject effect) =>
        JsonAccess.TryGetString(effect, "type-name", out _) &&
        JsonAccess.TryGetInt32(effect, "type", out int type) && type == 1 &&
        JsonAccess.TryGetString(effect, "category", out string category) && category is "InEffect" or "Effect" or "OutEffect";

    internal static bool IsSupportedClipEffect(JsonObject effect) => IsClipEffect(effect) &&
        JsonAccess.TryGetString(effect, "type-name", out string key) && ClipEffectCategory(key) is { } category &&
        effect["category"]!.GetValue<string>() == category &&
        JsonAccess.TryGetInt32(effect, "eff-obj-type", out int objectType) && objectType == ClipEffectType(key);

    private static string? ClipEffectCategory(string key) => key switch
    { "Alpha Fade" => "InEffect", "Alpha Fade Out" => "OutEffect", "Vibration" => "Effect", _ => null };
    private static int ClipEffectType(string key) => key switch { "Alpha Fade" => 0, "Alpha Fade Out" => 2, _ => 1 };

    private EditResult EditClipEffect(TimelineObjectId id, int? index, string? key, ProjectTime? start, ProjectTime? duration,
        IReadOnlyDictionary<string, decimal>? values, bool remove = false)
    {
        int count = Layers().Where(layer => layer.index == id.LayerIndex)
            .SelectMany(layer => (layer.layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            .Count(item => JsonAccess.TryGetInt32(item, "objkey", out int objectKey) && objectKey == id.ObjectKey);
        if (count > 1) return EditResult.Failed("RC6002", "The referenced timeline object is ambiguous.");
        var found = Find(id);
        if (found is not { } target) return EditResult.Failed("RC4201", "The target timeline object does not exist.");
        if (!HasScreenBounds(target.layer, target.obj))
            return EditResult.Failed("RC4901", "Effects support Video, Image, Figure, and Speaker Character clips only.", target.path);
        if (IsTimelineLocked(target.layer, target.obj)) return EditResult.Failed("RC4207", "The target object or layer is locked.", target.path);
        if (target.obj["sub-objects"] is not JsonArray effects)
            return EditResult.Failed("RC4902", "The target has no valid sub-objects array.");
        JsonObject? selected = null;
        if (index.HasValue)
        {
            if (index < 0 || index >= effects.Count || effects[index.Value] is not JsonObject existing || !IsSupportedClipEffect(existing))
                return EditResult.Failed("RC4903", "effectIndex must identify an existing supported clip effect.");
            selected = existing;
        }
        if (!remove && index.HasValue && start is null && duration is null && (values is null || values.Count == 0))
            return EditResult.Failed("RC4906", "Specify start, duration, or effectProperties to change.");

        JsonObject? edited = null;
        if (!remove)
        {
            key ??= selected?["type-name"]!.GetValue<string>();
            if (key is null || ClipEffectCategory(key) is null) return EditResult.Failed("RC4904", "Unsupported effect key. Supported: Alpha Fade, Alpha Fade Out, Vibration.");
            if (!TryGetTimeRange(target.obj, out decimal clipStart, out decimal clipEnd))
                return EditResult.Failed("RC4905", "The clip has no valid time range.");
            decimal length = clipEnd - clipStart;
            if (selected is not null && (!JsonAccess.TryGetDecimal(selected, "start-time", out _) || !JsonAccess.TryGetDecimal(selected, "duration", out _)))
                return EditResult.Failed("RC4905", "The effect has no valid time range.");
            decimal seconds = duration?.TotalSeconds ?? selected!["duration"]!.GetValue<decimal>();
            decimal offset = start?.TotalSeconds ?? (selected is not null ? selected["start-time"]!.GetValue<decimal>() : key == "Alpha Fade Out" ? length - seconds : 0m);
            if (offset < 0 || seconds <= 0 || offset > length || seconds > length - offset)
                return EditResult.Failed("RC4905", "Effect start must be non-negative and duration positive; the complete effect must fit within the clip.");
            edited = selected is null ? CreateClipEffect(key) : (JsonObject)selected.DeepClone();
            if (values is not null)
                foreach ((string name, decimal value) in values)
                {
                    if (key != "Vibration" || name is not ("SwingWidth" or "cycle") || value < 0 || (name == "cycle" && value == 0))
                        return EditResult.Failed("RC4907", "Vibration accepts SwingWidth >= 0 and cycle > 0; other effect properties are unsupported.");
                    if (edited["properties"] is not JsonObject properties || properties[name] is not JsonObject property || HasKeyframes(property) ||
                        !JsonAccess.TryGetDecimal(property, "p-value", out _))
                        return EditResult.Failed("RC4907", $"The {name} property has no plain numeric value; animated properties cannot be changed.");
                    property["p-value"] = value;
                }
            edited["start-time"] = offset;
            edited["duration"] = seconds;
        }
        int targetIndex = index ?? effects.Count;
        string? before = selected?.ToJsonString();
        string operation = remove ? "RemoveClipEffect" : index.HasValue ? "UpdateClipEffect" : "AddClipEffect";
        if (remove) effects.RemoveAt(targetIndex);
        else if (index.HasValue) effects[targetIndex] = edited;
        else effects.Add(edited);
        ProjectChange change = new(operation, id.ToString(), before, edited?.ToJsonString(), $"{target.path}.sub-objects[{targetIndex}]");
        changes.Add(change);
        return EditResult.Succeeded(new[] { change });
    }

    private static JsonObject CreateClipEffect(string key)
    {
        if (key != "Vibration")
            return (JsonObject)ProjectTemplateResources.LoadRoot("RecotteStudio.Core.Templates.Figure.ccproj")["layers"]!.AsArray().OfType<JsonObject>()
                .SelectMany(layer => layer["layer-objects"]!.AsArray().OfType<JsonObject>())
                .SelectMany(item => (item["sub-objects"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                .First(item => IsSupportedClipEffect(item) && item["type-name"]!.GetValue<string>() == key).DeepClone();

        // Defaults and units come from effects/effects/vibration.lua. Float=8, pt=68, time=66 follow native properties.
        static JsonObject Number(decimal value, int subtype) => new()
        { ["p-type"] = 8, ["p-subtype"] = subtype, ["p-cig"] = false, ["p-value"] = value, ["keyframes"] = new JsonArray() };
        return new JsonObject
        {
            ["type"] = 1, ["type-name"] = key, ["category"] = ClipEffectCategory(key), ["eff-obj-type"] = ClipEffectType(key),
            ["start-time"] = 0m, ["duration"] = 1m,
            ["properties"] = new JsonObject { ["SwingWidth"] = Number(8m, 68), ["cycle"] = Number(0.1m, 66) }
        };
    }
}
