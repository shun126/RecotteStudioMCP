using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

public sealed partial class ProjectEditor
{
    /// <summary>
    /// Sets the time range of an Image or Figure on an Annot layer or a Video on a Video layer. Parts stored relative to
    /// the clip follow a new length: an out-effect such as Alpha Fade Out stays at the end, and a Video keeps its playback
    /// speed, so the part of its source file that plays grows or shrinks with the clip.
    /// </summary>
    private EditResult MoveClip(MoveTimelineObjectRequest request, JsonObject layer, JsonObject obj, string path)
    {
        decimal start = request.StartTime.TotalSeconds, end = request.EndTime.TotalSeconds;
        if (start >= end) return EditResult.Failed("RC4301", "The start time of an image, figure, or video must be before its end time.");
        if (IsTimelineLocked(layer, obj)) return EditResult.Failed("RC4207", "The target object or layer is locked.", path);
        if (!TryGetTimeRange(obj, out decimal oldStart, out decimal oldEnd))
            return EditResult.Failed("RC4203", "The target has no valid time range.", path);
        if (JoiningObject(layer, obj, oldStart, oldEnd) is int joined)
            return EditResult.Failed("RC4303", $"The target is joined to the object with key {joined} on the same layer, such as a transition between two clips, which would be left out of place. Remove that object first, or move the clips in Recotte Studio.", path);

        decimal oldLength = oldEnd - oldStart, length = end - start;
        bool resized = length != oldLength;
        JsonObject[] outEffects = Array.Empty<JsonObject>();
        JsonObject? sourceEnd = null;
        decimal sourceStartTime = 0, sourceEndTime = 0;
        if (resized)
        {
            if (obj["sub-objects"] is JsonArray effects)
            {
                foreach (JsonNode? node in effects)
                {
                    // Effects are timed from the clip's start: an in-effect stays at the start and an out-effect ends with the clip.
                    if (node is not JsonObject effect || !JsonAccess.TryGetString(effect, "category", out string category) ||
                        category is not ("InEffect" or "OutEffect") || !JsonAccess.TryGetDecimal(effect, "duration", out decimal duration))
                        return EditResult.Failed("RC4302", "The target has an effect whose timing cannot follow a new length; keep its length or edit it in Recotte Studio.", $"{path}.sub-objects");
                    if (duration > length)
                    {
                        string name = JsonAccess.TryGetString(effect, "type-name", out string key) && key.Length > 0 ? key : category;
                        return EditResult.Failed("RC4302", $"The effect '{name}' lasts {duration} seconds, longer than the new length of {length} seconds. Shorten or remove it first.", $"{path}.sub-objects");
                    }
                    if (category == "OutEffect") outEffects = outEffects.Append(effect).ToArray();
                }
            }
            if (JsonAccess.TryGetString(obj, "type", out string type) && type == "Video")
            {
                if (obj["properties"] is not JsonObject properties || properties["StartTime"] is not JsonObject sourceStart ||
                    properties["EndTime"] is not JsonObject endEntry || HasKeyframes(sourceStart) || HasKeyframes(endEntry) ||
                    !JsonAccess.TryGetDecimal(sourceStart, "p-value", out sourceStartTime) ||
                    !JsonAccess.TryGetDecimal(endEntry, "p-value", out sourceEndTime) || sourceEndTime < sourceStartTime)
                    return EditResult.Failed("RC4304", "The video has no plain StartTime and EndTime source range, so its length cannot be changed.", $"{path}.properties");
                sourceEnd = endEntry;
            }
        }

        EditResult? durationError = ApplyDuration(end);
        if (durationError is not null) return durationError;
        string target = request.ObjectId.ToString();
        List<ProjectChange> made = new();
        Change(obj, "start-time", start, "MoveTimelineObject", target, $"{path}.start-time", made);
        Change(obj, "end-time", end, "MoveTimelineObject", target, $"{path}.end-time", made);
        // The clip no longer starts where the object it snapped to put it, so the snap is released.
        if (start != oldStart && JsonAccess.TryGetInt32(obj, "start-snap", out int snap) && snap != 0)
            Change(obj, "start-snap", 0, "MoveTimelineObject", target, $"{path}.start-snap", made);
        if (!resized) return EditResult.Succeeded(made);

        JsonArray subObjects = (obj["sub-objects"] as JsonArray)!;
        foreach (JsonObject effect in outEffects)
        {
            JsonAccess.TryGetDecimal(effect, "duration", out decimal duration);
            Change(effect, "start-time", length - duration, "MoveTimelineObject", target, $"{path}.sub-objects[{subObjects.IndexOf(effect)}].start-time", made);
        }
        if (sourceEnd is not null)
        {
            // Recotte Studio stretches StartTime..EndTime of the source over the clip (SpeedStretch), so scaling the range
            // with the clip keeps the speed: a clip added at normal speed plays as much more or less of its file.
            decimal range = oldLength > 0 ? (sourceEndTime - sourceStartTime) * length / oldLength : length;
            Change(sourceEnd, "p-value", sourceStartTime + range, "MoveTimelineObject", target, $"{path}.properties.EndTime.p-value", made);
        }
        return EditResult.Succeeded(made);
    }

    /// <summary>Returns whether MoveTimelineObject can move and resize the Image, Figure, or Video in time.</summary>
    internal static bool CanMoveClip(JsonObject layer, JsonObject obj) => IsVisualClip(layer, obj) && !IsTimelineLocked(layer, obj) &&
        TryGetTimeRange(obj, out decimal start, out decimal end) && JoiningObject(layer, obj, start, end) is null;

    private static bool TryGetTimeRange(JsonObject obj, out decimal start, out decimal end)
    {
        end = 0;
        return JsonAccess.TryGetDecimal(obj, "start-time", out start) && JsonAccess.TryGetDecimal(obj, "end-time", out end) && start <= end;
    }

    // A Transition spans the overlap of two clips and snaps its start to the later one (in 009FullProject, Transition 1002
    // snaps to video 1001 and ends where video 1000 ends), so moving either clip alone would leave it out of place.
    private static int? JoiningObject(JsonObject layer, JsonObject obj, decimal start, decimal end)
    {
        if (layer["layer-objects"] is not JsonArray objects || !JsonAccess.TryGetInt32(obj, "objkey", out int key)) return null;
        foreach (JsonObject other in objects.OfType<JsonObject>())
        {
            if (ReferenceEquals(other, obj) || !JsonAccess.TryGetInt32(other, "objkey", out int otherKey)) continue;
            if (JsonAccess.TryGetInt32(other, "start-snap", out int snap) && snap == key) return otherKey;
            if (JsonAccess.TryGetString(other, "type", out string type) && type == "Transition" &&
                TryGetTimeRange(other, out decimal otherStart, out decimal otherEnd) && otherStart < end && start < otherEnd) return otherKey;
        }
        return null;
    }

    private static bool HasKeyframes(JsonObject property) => property["keyframes"] is JsonArray { Count: > 0 };
}
