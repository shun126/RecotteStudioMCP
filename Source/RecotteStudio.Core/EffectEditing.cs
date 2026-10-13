using System.Text.Json.Nodes;

#pragma warning disable CS1591
namespace RecotteStudio.Core;

public sealed record AddFadeEffectOperation(TimelineObjectReference Target, string EffectKey, ProjectTime Duration) : ProjectOperation
{ public override ProjectOperationKind Kind => ProjectOperationKind.AddFadeEffect; }
public sealed record UpdateFadeEffectOperation(TimelineObjectReference Target, int EffectIndex, string EffectKey, ProjectTime Duration) : ProjectOperation
{ public override ProjectOperationKind Kind => ProjectOperationKind.UpdateFadeEffect; }
public sealed record RemoveFadeEffectOperation(TimelineObjectReference Target, int EffectIndex) : ProjectOperation
{ public override ProjectOperationKind Kind => ProjectOperationKind.RemoveFadeEffect; }
public sealed record FadeEffectView(int EffectIndex, string EffectKey, decimal Start, decimal Duration);

public sealed partial class RecotteProjectDocument
{
    /// <summary>Lists fades using their index in the complete sub-objects array.</summary>
    public IReadOnlyList<FadeEffectView> GetFadeEffects(TimelineObjectId id)
    {
        JsonObject copy = CloneRoot();
        if (copy["layers"] is not JsonArray layers || id.LayerIndex < 0 || id.LayerIndex >= layers.Count)
            return Array.Empty<FadeEffectView>();
        JsonObject[] matches = (layers[id.LayerIndex]?["layer-objects"] as JsonArray)?.OfType<JsonObject>()
            .Where(item => JsonAccess.TryGetInt32(item, "objkey", out int key) && key == id.ObjectKey).ToArray() ?? Array.Empty<JsonObject>();
        if (matches.Length != 1) return Array.Empty<FadeEffectView>();
        JsonObject obj = matches[0];
        return (obj?["sub-objects"] as JsonArray)?.Select((item, index) =>
            item is JsonObject effect && ProjectEditor.IsFade(effect) &&
            JsonAccess.TryGetDecimal(effect, "start-time", out decimal start) && JsonAccess.TryGetDecimal(effect, "duration", out decimal duration)
                ? new FadeEffectView(index, effect["type-name"]!.GetValue<string>(), start, duration) : null)
            .OfType<FadeEffectView>().ToArray() ?? Array.Empty<FadeEffectView>();
    }
}

public sealed partial class ProjectEditor
{
    public EditResult AddFadeEffect(TimelineObjectId id, string effectKey, ProjectTime duration) => EditFade(id, null, effectKey, duration);
    public EditResult UpdateFadeEffect(TimelineObjectId id, int effectIndex, string effectKey, ProjectTime duration) => EditFade(id, effectIndex, effectKey, duration);
    public EditResult RemoveFadeEffect(TimelineObjectId id, int effectIndex) => EditFade(id, effectIndex, null, default);

    internal static bool IsFade(JsonObject effect) =>
        JsonAccess.TryGetString(effect, "type-name", out string key) && key is "Alpha Fade" or "Alpha Fade Out" &&
        JsonAccess.TryGetInt32(effect, "type", out int type) && type == 1 &&
        JsonAccess.TryGetString(effect, "category", out string category) && category == (key == "Alpha Fade" ? "InEffect" : "OutEffect") &&
        JsonAccess.TryGetInt32(effect, "eff-obj-type", out int objectType) && objectType == (key == "Alpha Fade" ? 0 : 2);

    private EditResult EditFade(TimelineObjectId id, int? index, string? key, ProjectTime duration)
    {
        int count = Layers().Where(layer => layer.index == id.LayerIndex)
            .SelectMany(layer => (layer.layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            .Count(item => JsonAccess.TryGetInt32(item, "objkey", out int objectKey) && objectKey == id.ObjectKey);
        if (count > 1) return EditResult.Failed("RC6002", "The referenced timeline object is ambiguous.");
        var found = Find(id);
        EditResult? error = CheckTimelineObject(found);
        if (error is not null) return error;
        JsonObject obj = found!.Value.obj;
        if (!JsonAccess.TryGetString(obj, "type", out string type) || type is not ("Image" or "Video"))
            return EditResult.Failed("RC4901", "Fades support Image and Video objects only.");
        if (obj["sub-objects"] is not JsonArray effects)
            return EditResult.Failed("RC4902", "The target has no valid sub-objects array.");
        if (index.HasValue && (index < 0 || index >= effects.Count || effects[index.Value] is not JsonObject selected || !IsFade(selected)))
            return EditResult.Failed("RC4903", "effectIndex must identify an existing supported fade.");
        decimal length = 0;
        if (key is not null)
        {
            if (key is not ("Alpha Fade" or "Alpha Fade Out")) return EditResult.Failed("RC4904", "Unsupported fade key.");
            if (!JsonAccess.TryGetDecimal(obj, "start-time", out decimal start) || !JsonAccess.TryGetDecimal(obj, "end-time", out decimal end) ||
                duration.TotalSeconds <= 0 || duration.TotalSeconds > (length = end - start))
                return EditResult.Failed("RC4905", "Fade duration must be positive and fit within the clip.");
        }
        string? before = index.HasValue ? effects[index.Value]!.ToJsonString() : null;
        string operation;
        int targetIndex = index ?? effects.Count;
        if (key is null) { effects.RemoveAt(targetIndex); operation = "RemoveFadeEffect"; }
        else
        {
            JsonObject effect;
            if (index.HasValue) effect = (JsonObject)effects[targetIndex]!.DeepClone();
            else
            {
                JsonObject template = ProjectTemplateResources.LoadRoot("RecotteStudio.Core.Templates.Figure.ccproj");
                effect = (JsonObject)template["layers"]!.AsArray().OfType<JsonObject>()
                    .SelectMany(layer => layer["layer-objects"]!.AsArray().OfType<JsonObject>())
                    .SelectMany(item => (item["sub-objects"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    .First(item => IsFade(item) && item["type-name"]!.GetValue<string>() == key).DeepClone();
            }
            effect["type-name"] = key;
            effect["category"] = key == "Alpha Fade" ? "InEffect" : "OutEffect";
            effect["eff-obj-type"] = key == "Alpha Fade" ? 0 : 2;
            effect["start-time"] = key == "Alpha Fade" ? 0m : length - duration.TotalSeconds;
            effect["duration"] = duration.TotalSeconds;
            if (index.HasValue) effects[targetIndex] = effect; else effects.Add(effect);
            operation = index.HasValue ? "UpdateFadeEffect" : "AddFadeEffect";
        }
        ProjectChange change = new(operation, id.ToString(), before, key is null ? null : effects[targetIndex]!.ToJsonString(),
            found.Value.path + $".sub-objects[{targetIndex}]");
        changes.Add(change);
        return EditResult.Succeeded(new[] { change });
    }
}
