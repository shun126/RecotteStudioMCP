using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Chooses how an image or video that filled the old screen fills a screen of another shape.</summary>
public enum ScreenFit
{
    /// <summary>Fills the new screen and crops the parts that no longer fit, so nothing is stretched or letterboxed.</summary>
    Cover,
    /// <summary>Keeps the whole picture at its shape, centered, leaving bands at the sides or at the top and bottom.</summary>
    Contain,
}

/// <summary>Screen sizes accepted for a project, in pixels.</summary>
public static class ProjectScreenSize
{
    /// <summary>The smallest accepted width or height.</summary>
    public const int Minimum = 256;
    /// <summary>The largest accepted width or height.</summary>
    public const int Maximum = 7680;
    /// <summary>The landscape size of the built-in template.</summary>
    public const int DefaultWidth = 1920, DefaultHeight = 1080;

    /// <summary>Returns whether both sides are within <see cref="Minimum"/> and <see cref="Maximum"/>.</summary>
    public static bool IsValid(int width, int height) => width is >= Minimum and <= Maximum && height is >= Minimum and <= Maximum;
}

/// <summary>Requests a new screen size, relaying out telop frames, characters, and placed images, stamps, and videos.</summary>
public sealed record SetProjectSizeRequest(int Width, int Height, ScreenFit Fit = ScreenFit.Cover);

/// <summary>Changes the screen size and relays out what is placed on it; see <see cref="ProjectEditor.SetProjectSize"/>.</summary>
public sealed record SetProjectSizeOperation(int Width, int Height, ScreenFit Fit = ScreenFit.Cover) : ProjectOperation
{
    /// <summary>Gets the stable machine-readable operation kind.</summary>
    public override ProjectOperationKind Kind => ProjectOperationKind.SetProjectSize;
}

public sealed partial class ProjectEditor
{
    /// <summary>
    /// Changes <c>setting.size</c> and relays out the screen to match. An Image or Video that filled the old screen fills
    /// the new one as <see cref="SetProjectSizeRequest.Fit"/> says. Telop frames scale with the screen width and keep their
    /// height. Characters, stamps, annotation texts, and other images and videos keep their shape and, when the shorter
    /// screen side keeps its length, their size; they shrink only to fit. Every rectangle keeps how its free space is split
    /// on each side, so whatever touched an edge still touches it. Locked and keyframe-animated rectangles are left as
    /// they are and reported with RC4932 warnings.
    /// </summary>
    public EditResult SetProjectSize(SetProjectSizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ProjectScreenSize.IsValid(request.Width, request.Height))
            return EditResult.Failed("RC4931", $"The width and height must be whole numbers from {ProjectScreenSize.Minimum} to {ProjectScreenSize.Maximum}.");
        if (!Enum.IsDefined(request.Fit)) return EditResult.Failed("RC4931", "The fit is not supported.");
        if (!ScreenLayout.TryGetSize(root, out int oldWidth, out int oldHeight))
            return EditResult.Failed("RC4931", "The project has no valid screen size to change.", "$.setting.size");
        if (oldWidth == request.Width && oldHeight == request.Height) return EditResult.Succeeded();

        List<ProjectChange> made = new();
        List<ProjectDiagnostic> warnings = new();
        ScreenLayout.Resize(root, request.Width, request.Height, request.Fit, respectLocks: true, made, warnings);
        changes.AddRange(made);
        return new EditResult(true, warnings, made);
    }
}

/// <summary>Moves screen rectangles from one screen size to another; shared by resizing, creation, and imports.</summary>
internal static class ScreenLayout
{
    private const decimal Tolerance = 0.5m;

    private readonly record struct Rect(decimal X, decimal Y, decimal Width, decimal Height)
    {
        public JsonArray ToJson() => new(Round(X), Round(Y), Round(Width), Round(Height));
    }

    internal static bool TryGetSize(JsonObject root, out int width, out int height)
    {
        width = height = 0;
        return root["setting"] is JsonObject setting && setting["size"] is JsonArray { Count: 2 } size &&
            size[0] is JsonValue w && w.TryGetValue(out width) && size[1] is JsonValue h && h.TryGetValue(out height) &&
            width > 0 && height > 0;
    }

    /// <summary>
    /// Resizes the screen of <paramref name="root"/> and relays out its telop frames and placed rectangles. With
    /// <paramref name="respectLocks"/> false, locks are ignored, as for a donor that is only read.
    /// </summary>
    internal static void Resize(JsonObject root, int width, int height, ScreenFit fit, bool respectLocks,
        List<ProjectChange> changes, List<ProjectDiagnostic> warnings)
    {
        if (!TryGetSize(root, out int oldWidth, out int oldHeight) || root["setting"] is not JsonObject setting) return;
        Screen from = new(oldWidth, oldHeight), to = new(width, height);
        JsonArray size = new(width, height);
        changes.Add(new("SetProjectSize", "project", setting["size"]!.ToJsonString(), size.ToJsonString(), "$.setting.size"));
        setting["size"] = size;
        // [0, 0] means the export follows the screen; any other export size was chosen for the old screen.
        if (setting["export-video-size"] is JsonArray { Count: 2 } export &&
            export.Any(item => item is JsonValue value && value.TryGetValue(out decimal number) && number != 0))
        {
            JsonArray exportSize = new(width, height);
            changes.Add(new("SetExportVideoSize", "project", export.ToJsonString(), exportSize.ToJsonString(), "$.setting.export-video-size"));
            setting["export-video-size"] = exportSize;
        }

        if (root["telop-frames"] is JsonArray frames)
            for (int index = 0; index < frames.Count; index++)
            {
                if (frames[index] is not JsonObject frame || frame["Bounds"] is not JsonObject bounds) continue;
                string name = frame["FrameName"] is JsonObject property && JsonAccess.TryGetString(property, "p-value", out string value) ? value : index.ToString();
                Move(bounds, $"$.telop-frames[{index}].Bounds", $"telop-frame:{name}", "RelayoutTelopFrame", warnings, changes,
                    rect => Region(rect, from, to));
            }

        if (root["layers"] is not JsonArray layers) return;
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            if (layers[layerIndex] is not JsonObject layer) continue;
            string layerPath = $"$.layers[{layerIndex}]";
            bool layerLocked = respectLocks && IsLocked(layer, "locked");
            if (layer["properties"] is JsonObject layerProperties && layerProperties["CharBounds"] is JsonObject layerCharacter)
            {
                if (layerLocked) Skip(warnings, $"{layerPath}.properties.CharBounds", "its layer is locked");
                else Move(layerCharacter, $"{layerPath}.properties.CharBounds", $"layer:{layerIndex}", "RelayoutLayerCharacter",
                    warnings, changes, rect => Box(rect, from, to));
            }
            if (layer["layer-objects"] is not JsonArray objects) continue;
            for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                if (objects[objectIndex] is not JsonObject obj || obj["properties"] is not JsonObject properties ||
                    !JsonAccess.TryGetString(obj, "type", out string type)) continue;
                string path = $"{layerPath}.layer-objects[{objectIndex}]";
                string target = JsonAccess.TryGetInt32(obj, "objkey", out int key) ? new TimelineObjectId(layerIndex, key).ToString() : path;
                string? property = type switch
                {
                    "Speaker Character" => "CharBounds",
                    "Image" or "Video" or "Figure" => "Bounds",
                    _ => null,
                };
                if (property is null || properties[property] is not JsonObject entry) continue;
                if (layerLocked || respectLocks && (IsLocked(obj, "locked") || IsLocked(obj, "tl-locked")))
                {
                    Skip(warnings, $"{path}.properties.{property}", "it or its layer is locked");
                    continue;
                }
                if (type is "Image" or "Video" && TryRead(entry, out Rect current) && FillsScreen(current, from))
                    FillScreen(properties, entry, path, target, from, to, fit, warnings, changes);
                else
                    Move(entry, $"{path}.properties.{property}", target, "RelayoutObject", warnings, changes, rect => Box(rect, from, to));
            }
        }
    }

    /// <summary>Fits a rectangle placed on a screen of one size into a screen of another size, keeping its shape.</summary>
    internal static JsonArray? AdaptBox(JsonArray bounds, int fromWidth, int fromHeight, int toWidth, int toHeight)
    {
        if (!TryRead(bounds, out Rect rect)) return null;
        return Box(rect, new(fromWidth, fromHeight), new(toWidth, toHeight)).ToJson();
    }

    private readonly record struct Screen(decimal Width, decimal Height);

    // Telop frames are text areas whose font does not scale, so the width follows the screen and the height stays.
    private static Rect Region(Rect rect, Screen from, Screen to)
    {
        decimal width = rect.Width * to.Width / from.Width, height = Math.Min(rect.Height, to.Height);
        return new(Place(rect.X, rect.Width, from.Width, width, to.Width), Place(rect.Y, rect.Height, from.Height, height, to.Height), width, height);
    }

    // Pictures keep their shape. They scale with the shorter screen side, which a landscape-portrait swap keeps, and shrink to fit.
    private static Rect Box(Rect rect, Screen from, Screen to)
    {
        decimal scale = Math.Min(to.Width, to.Height) / Math.Min(from.Width, from.Height);
        decimal width = rect.Width * scale, height = rect.Height * scale;
        decimal fit = Math.Min(1m, Math.Min(to.Width / width, to.Height / height));
        width *= fit;
        height *= fit;
        return new(Place(rect.X, rect.Width, from.Width, width, to.Width), Place(rect.Y, rect.Height, from.Height, height, to.Height), width, height);
    }

    // Keeps how the free space is split before and after the rectangle, so one touching an edge still touches it.
    // A rectangle as large as the screen has no free space and keeps its center instead.
    private static decimal Place(decimal position, decimal size, decimal oldSpan, decimal newSize, decimal newSpan)
    {
        decimal oldFree = oldSpan - size, newFree = newSpan - newSize;
        return oldFree > 0m && newFree >= 0m
            ? position * newFree / oldFree
            : (position + size / 2m) * newSpan / oldSpan - newSize / 2m;
    }

    private static bool FillsScreen(Rect rect, Screen screen) => Math.Abs(rect.X) <= Tolerance && Math.Abs(rect.Y) <= Tolerance &&
        Math.Abs(rect.Width - screen.Width) <= Tolerance && Math.Abs(rect.Height - screen.Height) <= Tolerance;

    private static void FillScreen(JsonObject properties, JsonObject bounds, string path, string target, Screen from, Screen to,
        ScreenFit fit, List<ProjectDiagnostic> warnings, List<ProjectChange> changes)
    {
        if (HasKeyframes(bounds))
        {
            Skip(warnings, $"{path}.properties.Bounds", "its Bounds are animated with keyframes");
            return;
        }
        if (fit == ScreenFit.Cover)
        {
            if (properties["CropBounds"] is JsonObject crop && !HasKeyframes(crop) && TryRead(crop, out Rect source) &&
                source.Width > 0 && source.Height > 0)
            {
                // The crop is shown stretched over the old screen; keep that stretch and take the centered part the new screen shows.
                decimal stretchX = from.Width / source.Width, stretchY = from.Height / source.Height;
                decimal zoom = Math.Max(to.Width / (stretchX * source.Width), to.Height / (stretchY * source.Height));
                decimal width = to.Width / (zoom * stretchX), height = to.Height / (zoom * stretchY);
                Rect cropped = new(source.X + (source.Width - width) / 2m, source.Y + (source.Height - height) / 2m, width, height);
                Write(crop, cropped, $"{path}.properties.CropBounds", target, "CropToScreen", changes);
                Write(bounds, new(0m, 0m, to.Width, to.Height), $"{path}.properties.Bounds", target, "FillScreen", changes);
                return;
            }
            warnings.Add(new("RC4932", DiagnosticSeverity.Warning,
                "The object has no plain CropBounds to crop, so it was fitted inside the new screen instead.", $"{path}.properties.CropBounds"));
        }
        decimal scale = Math.Min(to.Width / from.Width, to.Height / from.Height);
        decimal fittedWidth = from.Width * scale, fittedHeight = from.Height * scale;
        Write(bounds, new((to.Width - fittedWidth) / 2m, (to.Height - fittedHeight) / 2m, fittedWidth, fittedHeight),
            $"{path}.properties.Bounds", target, "FitInsideScreen", changes);
    }

    private static void Move(JsonObject entry, string path, string target, string operation, List<ProjectDiagnostic> warnings,
        List<ProjectChange> changes, Func<Rect, Rect> map)
    {
        if (HasKeyframes(entry))
        {
            Skip(warnings, path, "it is animated with keyframes");
            return;
        }
        if (!TryRead(entry, out Rect rect) || rect.Width <= 0 || rect.Height <= 0) return;
        Write(entry, map(rect), path, target, operation, changes);
    }

    private static void Write(JsonObject entry, Rect rect, string path, string target, string operation, List<ProjectChange> changes)
    {
        JsonArray value = rect.ToJson();
        changes.Add(new(operation, target, entry["p-value"]!.ToJsonString(), value.ToJsonString(), $"{path}.p-value"));
        entry["p-value"] = value;
    }

    private static void Skip(List<ProjectDiagnostic> warnings, string path, string reason) =>
        warnings.Add(new("RC4932", DiagnosticSeverity.Warning, $"Left where it was because {reason}; check it on the new screen.", path));

    private static bool TryRead(JsonObject entry, out Rect rect)
    {
        rect = default;
        return entry["p-value"] is JsonArray values && TryRead(values, out rect);
    }

    private static bool TryRead(JsonArray values, out Rect rect)
    {
        rect = default;
        if (values.Count != 4 || !JsonAccess.TryGetDecimal(values[0], out decimal x) || !JsonAccess.TryGetDecimal(values[1], out decimal y) ||
            !JsonAccess.TryGetDecimal(values[2], out decimal width) || !JsonAccess.TryGetDecimal(values[3], out decimal height)) return false;
        rect = new(x, y, width, height);
        return true;
    }

    private static bool HasKeyframes(JsonObject entry) => entry["keyframes"] is JsonArray { Count: > 0 };

    private static bool IsLocked(JsonObject value, string name) => value[name] is JsonValue json && json.TryGetValue(out bool locked) && locked;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
