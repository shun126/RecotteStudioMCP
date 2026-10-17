using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Describes an object's screen rectangle in project pixels: its top-left corner and its size.</summary>
public readonly record struct ObjectBounds(decimal X, decimal Y, decimal Width, decimal Height);

/// <summary>
/// Requests a new screen rectangle, in project pixels, for an Image or Figure on an Annot layer or a Video on a Video
/// layer. A null value keeps that part of the current rectangle, so an object can be moved without resizing it.
/// </summary>
public sealed record SetObjectBoundsRequest(TimelineObjectId ObjectId, decimal? X = null, decimal? Y = null,
    decimal? Width = null, decimal? Height = null);

/// <summary>Moves or resizes an Image or Figure on an Annot layer or a Video on a Video layer; null values keep the current ones.</summary>
public sealed record SetObjectBoundsOperation(TimelineObjectReference Target, decimal? X = null, decimal? Y = null,
    decimal? Width = null, decimal? Height = null) : ProjectOperation
{
    /// <summary>Gets the stable machine-readable operation kind.</summary>
    public override ProjectOperationKind Kind => ProjectOperationKind.SetObjectBounds;
}

public sealed partial class ProjectEditor
{
    /// <summary>
    /// Rewrites the Bounds of an Image or Figure (a stamp or an annotation text box) on an Annot layer, or of a Video on a
    /// Video layer. Only the layer lock and the object's timeline lock block the edit: Recotte Studio saves every video
    /// with st-locked and pv-locked set, and those flags are kept as they are.
    /// </summary>
    public EditResult SetObjectBounds(SetObjectBoundsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is { X: null, Y: null, Width: null, Height: null })
            return EditResult.Failed("RC4913", "At least one of x, y, width, and height is required.");
        if (request.Width <= 0 || request.Height <= 0)
            return EditResult.Failed("RC4913", "The width and height must be positive.");
        var found = Find(request.ObjectId);
        if (found is null) return EditResult.Failed("RC4201", "The target timeline object does not exist.");
        (JsonObject layer, JsonObject obj, _, string path) = found.Value;
        if (!HasBoundsTarget(layer, obj) || !TryGetBounds(obj, out ObjectBounds current))
            return EditResult.Failed("RC4911", "Only an Image or Figure on an Annot layer or a Video on a Video layer with a valid Bounds rectangle can be moved or resized.", path);
        if (HasBoundsKeyframes(obj))
            return EditResult.Failed("RC4912", "The Bounds of the target are animated with keyframes, which cannot be edited.", $"{path}.properties.Bounds");
        if (IsBoundsLocked(layer, obj)) return EditResult.Failed("RC4207", "The target object or layer is locked.", path);

        JsonObject entry = (JsonObject)obj["properties"]!["Bounds"]!;
        string before = entry["p-value"]!.ToJsonString();
        JsonArray bounds = new(request.X ?? current.X, request.Y ?? current.Y, request.Width ?? current.Width, request.Height ?? current.Height);
        entry["p-value"] = bounds;
        ProjectChange change = new("SetObjectBounds", request.ObjectId.ToString(), before, bounds.ToJsonString(),
            $"{path}.properties.Bounds.p-value");
        changes.Add(change);
        return EditResult.Succeeded(new[] { change });
    }

    /// <summary>Returns whether SetObjectBounds accepts the object: a supported type and layer, a valid unanimated Bounds, and no lock.</summary>
    internal static bool CanSetBounds(JsonObject layer, JsonObject obj) => HasBoundsTarget(layer, obj) && TryGetBounds(obj, out _) &&
        !HasBoundsKeyframes(obj) && !IsBoundsLocked(layer, obj);

    /// <summary>Reads an object's [x, y, width, height] Bounds.</summary>
    internal static bool TryGetBounds(JsonObject obj, out ObjectBounds bounds)
    {
        bounds = default;
        if (obj["properties"] is not JsonObject properties ||
            !JsonAccess.TryGetBounds(properties, out decimal x, out decimal y, out decimal width, out decimal height)) return false;
        bounds = new(x, y, width, height);
        return true;
    }

    private static bool HasBoundsTarget(JsonObject layer, JsonObject obj) =>
        JsonAccess.TryGetString(layer, "type", out string layerType) && JsonAccess.TryGetString(obj, "type", out string objectType) &&
        (layerType, objectType) is ("Annot", "Image" or "Figure") or ("Video", "Video");

    // Writing p-value would not move an animated object, whose rectangle comes from its keyframes.
    private static bool HasBoundsKeyframes(JsonObject obj) => obj["properties"]?["Bounds"]?["keyframes"] is JsonArray { Count: > 0 };

    // st-locked and pv-locked are left out: Recotte Studio sets both on every video it saves (005OneVideo, 013TwoVideos).
    private static bool IsBoundsLocked(JsonObject layer, JsonObject obj) => new[] { (layer, "locked"), (obj, "locked"), (obj, "tl-locked") }
        .Any(item => item.Item1[item.Item2] is JsonValue value && value.TryGetValue(out bool locked) && locked);
}
