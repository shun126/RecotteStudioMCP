using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Selects how an object is mirrored on screen.</summary>
public enum ObjectFlip
{
    /// <summary>Not mirrored (saved as none).</summary>
    None,
    /// <summary>Mirrored left to right, so a character faces the other way (saved as x).</summary>
    Horizontal,
    /// <summary>Mirrored top to bottom (saved as y).</summary>
    Vertical,
    /// <summary>Mirrored both ways (saved as xy).</summary>
    Both,
}

/// <summary>
/// Reports how an object looks: values the object does not have are null. <see cref="Flip"/> is none, horizontal,
/// vertical, or both, and <see cref="DefaultAction"/> is the action a Speaker Character plays when no Speaker Action
/// does (empty for the character's own default).
/// </summary>
public sealed record ObjectAppearance(decimal? Opacity, decimal? Angle, string? Flip, int? Brightness, int? Contrast,
    decimal? Gamma, string? DefaultAction);

/// <summary>
/// Requests a change to how an Image, Video, Figure, or Speaker Character looks; a null value is kept.
/// <paramref name="Opacity"/> runs from 0 (transparent) to 1 (opaque) and <paramref name="Angle"/> is a rotation in
/// degrees. <paramref name="Brightness"/> and <paramref name="Contrast"/> are whole numbers where 0 is unchanged, and
/// <paramref name="Gamma"/> is 1 when unchanged. <paramref name="DefaultAction"/> applies to a Speaker Character: one of
/// its layer's actions, or an empty string for the character's own default.
/// </summary>
public sealed record SetObjectAppearanceRequest(TimelineObjectId ObjectId, decimal? Opacity = null, decimal? Angle = null,
    ObjectFlip? Flip = null, int? Brightness = null, int? Contrast = null, decimal? Gamma = null, string? DefaultAction = null);

/// <summary>Changes how an object looks; see <see cref="SetObjectAppearanceRequest"/>.</summary>
public sealed record SetObjectAppearanceOperation(TimelineObjectReference Target, decimal? Opacity = null, decimal? Angle = null,
    ObjectFlip? Flip = null, int? Brightness = null, int? Contrast = null, decimal? Gamma = null, string? DefaultAction = null)
    : ProjectOperation
{
    /// <summary>Gets the stable machine-readable operation kind.</summary>
    public override ProjectOperationKind Kind => ProjectOperationKind.SetObjectAppearance;
}

public sealed partial class ProjectEditor
{
    // Recotte Studio's own limits for brightness, contrast, and gamma are not known, so these stay well inside any
    // plausible slider range: an out-of-range value could look wrong or be clamped when the project is opened.
    internal const decimal MaxAngle = 360m, MinGamma = 0.2m, MaxGamma = 5m;
    internal const int MaxBrightness = 100, MaxContrast = 100;

    /// <summary>
    /// Sets the Opacity, Angle, Flip, Brightness, Contrast, Gamma, or DefaultAction of an Image or Figure on an Annot
    /// layer, a Video on a Video layer, or a Speaker Character. Each value goes to the property of that name, which the
    /// object must already have (a Figure has only Angle; a Speaker Character has Angle, Flip, and DefaultAction), so its
    /// stored type is kept. Every value is checked before anything changes.
    /// </summary>
    public EditResult SetObjectAppearance(SetObjectAppearanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is { Opacity: null, Angle: null, Flip: null, Brightness: null, Contrast: null, Gamma: null, DefaultAction: null })
            return EditResult.Failed("RC4943", "Specify at least one of opacity, angle, flip, brightness, contrast, gamma, and defaultAction.");
        if (request.Opacity is < 0m or > 1m) return EditResult.Failed("RC4943", "opacity must be from 0 (transparent) to 1 (opaque).");
        if (request.Angle is < -MaxAngle or > MaxAngle) return EditResult.Failed("RC4943", $"angle must be from -{MaxAngle} to {MaxAngle} degrees.");
        if (request.Brightness is < -MaxBrightness or > MaxBrightness)
            return EditResult.Failed("RC4943", $"brightness must be from -{MaxBrightness} to {MaxBrightness}; 0 leaves it unchanged.");
        if (request.Contrast is < -MaxContrast or > MaxContrast)
            return EditResult.Failed("RC4943", $"contrast must be from -{MaxContrast} to {MaxContrast}; 0 leaves it unchanged.");
        if (request.Gamma is < MinGamma or > MaxGamma)
            return EditResult.Failed("RC4943", FormattableString.Invariant($"gamma must be from {MinGamma} to {MaxGamma}; 1 leaves it unchanged."));
        if (request.Flip is ObjectFlip flip && !Enum.IsDefined(flip)) return EditResult.Failed("RC4943", "flip must be none, horizontal, vertical, or both.");

        var found = Find(request.ObjectId);
        if (found is null) return EditResult.Failed("RC4201", "The target timeline object does not exist.");
        (JsonObject layer, JsonObject obj, _, string path) = found.Value;
        if (!HasAppearance(layer, obj) || obj["properties"] is not JsonObject properties)
            return EditResult.Failed("RC4941", "Only an Image or Figure on an Annot layer, a Video on a Video layer, or a Speaker Character can change how it looks.", path);
        if (IsTimelineLocked(layer, obj)) return EditResult.Failed("RC4207", "The target object or layer is locked.", path);

        string? defaultAction = null;
        if (request.DefaultAction is not null)
        {
            if (request.DefaultAction.Length == 0) defaultAction = "";
            else
            {
                string[] available = LayerActions(layer);
                defaultAction = ResolveAction(available, request.DefaultAction);
                if (defaultAction is null)
                    return EditResult.Failed("RC4944", $"The character on layer {request.ObjectId.LayerIndex} has no action '{request.DefaultAction}'. Available actions: {string.Join(", ", available)}.", path);
            }
        }

        List<(string property, object value)> values = new();
        if (request.Opacity is decimal opacity) values.Add(("Opacity", opacity));
        if (request.Angle is decimal angle) values.Add(("Angle", angle));
        if (request.Flip is ObjectFlip mirror) values.Add(("Flip", FlipValue(mirror)));
        if (request.Brightness is int brightness) values.Add(("Brightness", brightness));
        if (request.Contrast is int contrast) values.Add(("Contrast", contrast));
        if (request.Gamma is decimal gamma) values.Add(("Gamma", gamma));
        if (defaultAction is not null) values.Add(("DefaultAction", defaultAction));

        string type = JsonAccess.TryGetString(obj, "type", out string objectType) ? objectType : "object";
        foreach ((string property, _) in values)
        {
            if (properties[property] is not JsonObject entry)
                return EditResult.Failed("RC4941", $"The {type} has no {property}. It can change: {string.Join(", ", AppearanceProperties.Where(name => properties[name] is JsonObject))}.", $"{path}.properties");
            if (HasKeyframes(entry))
                return EditResult.Failed("RC4942", $"The {property} of the target is animated with keyframes, which cannot be edited.", $"{path}.properties.{property}");
        }

        string target = request.ObjectId.ToString();
        List<ProjectChange> made = new();
        foreach ((string property, object value) in values)
            Change((JsonObject)properties[property]!, "p-value", value, "SetObjectAppearance", target, $"{path}.properties.{property}.p-value", made);
        return EditResult.Succeeded(made);
    }

    private static readonly string[] AppearanceProperties = { "Opacity", "Angle", "Flip", "Brightness", "Contrast", "Gamma", "DefaultAction" };

    /// <summary>Returns whether SetObjectAppearance can change the object: a supported type with at least one plain value, and no lock.</summary>
    internal static bool CanSetAppearance(JsonObject layer, JsonObject obj) => HasAppearance(layer, obj) &&
        !IsTimelineLocked(layer, obj) && obj["properties"] is JsonObject properties &&
        AppearanceProperties.Any(name => properties[name] is JsonObject entry && !HasKeyframes(entry));

    /// <summary>Reads how an object looks, or null for objects SetObjectAppearance does not change.</summary>
    internal static ObjectAppearance? ReadAppearance(JsonObject layer, JsonObject obj)
    {
        if (!HasAppearance(layer, obj) || obj["properties"] is not JsonObject properties) return null;
        decimal? Number(string name) => properties[name] is JsonObject entry && JsonAccess.TryGetDecimal(entry, "p-value", out decimal value) ? value : null;
        int? Whole(string name) => properties[name] is JsonObject entry && JsonAccess.TryGetInt32(entry, "p-value", out int value) ? value : null;
        string? Text(string name) => properties[name] is JsonObject entry && JsonAccess.TryGetString(entry, "p-value", out string value) ? value : null;
        return new(Number("Opacity"), Number("Angle"), Text("Flip") is string flip ? FlipName(flip) : null, Whole("Brightness"),
            Whole("Contrast"), Number("Gamma"), Text("DefaultAction"));
    }

    // The visual clips SetObjectBounds places, plus the character a Speaker layer draws.
    private static bool HasAppearance(JsonObject layer, JsonObject obj) => HasScreenBounds(layer, obj);

    // Recotte Studio stores the flip modes なし, 左右反転, 上下反転, and 上下左右反転 as none, x, y, and xy.
    private static string FlipValue(ObjectFlip flip) => flip switch
    {
        ObjectFlip.Horizontal => "x",
        ObjectFlip.Vertical => "y",
        ObjectFlip.Both => "xy",
        _ => "none",
    };

    private static string? FlipName(string value) => value switch
    {
        "none" => "none",
        "x" => "horizontal",
        "y" => "vertical",
        "xy" => "both",
        _ => null,
    };
}
