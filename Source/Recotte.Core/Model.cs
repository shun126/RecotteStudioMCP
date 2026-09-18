using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>Provides a typed, read-only view of the project settings.</summary>
public sealed class ProjectSettingsView
{
    private readonly JsonObject node;

    internal ProjectSettingsView(JsonObject node) => this.node = node;

    /// <summary>Gets the project name when present.</summary>
    public string? ProjectName => JsonAccess.TryGetString(node, "project-name", out string value) ? value : null;

    /// <summary>Gets the serialized project GUID when present.</summary>
    public string? ProjectGuid => JsonAccess.TryGetString(node, "guid", out string value) ? value : null;

    /// <summary>Gets project duration in seconds when numeric.</summary>
    public decimal? Duration => JsonAccess.TryGetDecimal(node, "duration", out decimal value) ? value : null;

    /// <summary>Gets the configured frame rate when numeric.</summary>
    public decimal? FramesPerSecond => JsonAccess.TryGetDecimal(node, "fps", out decimal value) ? value : null;

    /// <summary>Gets whether Recotte Studio automatically extends the project duration.</summary>
    public bool? AutoDuration => node["auto-duration"] is JsonValue value && value.TryGetValue(out bool automatic) ? automatic : null;

    /// <summary>Gets the project width when the verified size array is present.</summary>
    public int? Width => TryGetSize(0);

    /// <summary>Gets the project height when the verified size array is present.</summary>
    public int? Height => TryGetSize(1);

    private int? TryGetSize(int index) => node["size"] is JsonArray size && size.Count > index && size[index] is JsonValue value &&
        value.TryGetValue(out int result) ? result : null;
}

/// <summary>Provides a typed, read-only view of a timeline layer.</summary>
public class LayerView
{
    private readonly JsonObject node;

    internal LayerView(JsonObject node, int index)
    {
        this.node = node;
        Index = index;
    }

    /// <summary>Gets the stable array index.</summary>
    public int Index { get; }

    /// <summary>Gets the serialized layer type.</summary>
    public string? Type => JsonAccess.TryGetString(node, "type", out string value) ? value : null;

    /// <summary>Gets the layer name when present.</summary>
    public string? Name => JsonAccess.TryGetString(node, "name", out string value) ? value : null;

    /// <summary>Gets an immutable snapshot of timeline objects in serialized order.</summary>
    public IReadOnlyList<TimelineObjectView> Objects => Array.AsReadOnly((node["layer-objects"] is JsonArray objects
        ? objects.Select((item, index) => item is JsonObject jsonObject
            ? TimelineObjectView.Create(jsonObject, Index, index)
            : null).OfType<TimelineObjectView>().ToArray()
        : Array.Empty<TimelineObjectView>()));

    internal JsonObject JsonNode => node;

    internal static LayerView Create(JsonObject node, int index)
    {
        return JsonAccess.TryGetString(node, "type", out string type)
            ? type switch
            {
                "Video" => new VideoLayerView(node, index),
                "Speaker" => new SpeakerLayerView(node, index),
                "Annot" => new AnnotationLayerView(node, index),
                _ => new UnknownLayerView(node, index),
            }
            : new UnknownLayerView(node, index);
    }
}

/// <summary>Provides a typed, read-only view of a timeline object.</summary>
public class TimelineObjectView
{
    private readonly JsonObject node;

    internal TimelineObjectView(JsonObject node, int layerIndex, int index)
    {
        this.node = node;
        LayerIndex = layerIndex;
        Index = index;
    }

    /// <summary>Gets the containing layer index.</summary>
    public int LayerIndex { get; }

    /// <summary>Gets the serialized object index within the layer.</summary>
    public int Index { get; }

    /// <summary>Gets the serialized object type.</summary>
    public string? Type => JsonAccess.TryGetString(node, "type", out string value) ? value : null;

    /// <summary>Gets the display name when present.</summary>
    public string? Name => JsonAccess.TryGetString(node, "name", out string value) ? value : null;

    /// <summary>Gets the object key when it is an integer.</summary>
    public int? ObjectKey => JsonAccess.TryGetInt32(node, "objkey", out int value) ? value : null;

    /// <summary>Gets the start time in seconds.</summary>
    public decimal? StartTime => JsonAccess.TryGetDecimal(node, "start-time", out decimal value) ? value : null;

    /// <summary>Gets the end time in seconds.</summary>
    public decimal? EndTime => JsonAccess.TryGetDecimal(node, "end-time", out decimal value) ? value : null;

    /// <summary>Gets plain text when the known text structure is present.</summary>
    public string? Text => node["text"] is JsonObject text && JsonAccess.TryGetString(text, "text", out string value)
        ? value
        : null;

    /// <summary>Gets the referenced file-item key when present.</summary>
    public string? FileItemKey => JsonAccess.GetPropertyString(node, "File");

    internal JsonObject JsonNode => node;

    internal static TimelineObjectView Create(JsonObject node, int layerIndex, int index)
    {
        return JsonAccess.TryGetString(node, "type", out string type)
            ? type switch
            {
                "Speaker Voice" => new SpeakerVoiceView(node, layerIndex, index),
                "Speaker Character" => new SpeakerCharacterView(node, layerIndex, index),
                "Image" => new ImageObjectView(node, layerIndex, index),
                "Video" => new VideoObjectView(node, layerIndex, index),
                _ => new UnknownTimelineObjectView(node, layerIndex, index),
            }
            : new UnknownTimelineObjectView(node, layerIndex, index);
    }
}

/// <summary>Represents a video layer.</summary>
public sealed class VideoLayerView : LayerView
{
    internal VideoLayerView(JsonObject node, int index) : base(node, index) { }
}

/// <summary>Represents a speaker layer.</summary>
public sealed class SpeakerLayerView : LayerView
{
    internal SpeakerLayerView(JsonObject node, int index) : base(node, index) { }
}

/// <summary>Represents an annotation layer.</summary>
public sealed class AnnotationLayerView : LayerView
{
    internal AnnotationLayerView(JsonObject node, int index) : base(node, index) { }
}

/// <summary>Preserves an unrecognized layer without interpreting it.</summary>
public sealed class UnknownLayerView : LayerView
{
    internal UnknownLayerView(JsonObject node, int index) : base(node, index) { }
}

/// <summary>Represents a speaker voice timeline object.</summary>
public sealed class SpeakerVoiceView : TimelineObjectView
{
    internal SpeakerVoiceView(JsonObject node, int layerIndex, int index) : base(node, layerIndex, index) { }
}

/// <summary>Represents a speaker character timeline object.</summary>
public sealed class SpeakerCharacterView : TimelineObjectView
{
    internal SpeakerCharacterView(JsonObject node, int layerIndex, int index) : base(node, layerIndex, index) { }
}

/// <summary>Represents an image timeline object.</summary>
public sealed class ImageObjectView : TimelineObjectView
{
    internal ImageObjectView(JsonObject node, int layerIndex, int index) : base(node, layerIndex, index) { }
}

/// <summary>Represents a video timeline object.</summary>
public sealed class VideoObjectView : TimelineObjectView
{
    internal VideoObjectView(JsonObject node, int layerIndex, int index) : base(node, layerIndex, index) { }
}

/// <summary>Preserves an unrecognized timeline object without interpreting it.</summary>
public sealed class UnknownTimelineObjectView : TimelineObjectView
{
    internal UnknownTimelineObjectView(JsonObject node, int layerIndex, int index) : base(node, layerIndex, index) { }
}

/// <summary>Provides a typed, read-only view of an external file registration.</summary>
public sealed class FileItemView
{
    private readonly JsonObject node;

    internal FileItemView(JsonObject node, int index)
    {
        this.node = node;
        Index = index;
    }

    /// <summary>Gets the serialized registry index.</summary>
    public int Index { get; }

    /// <summary>Gets the registered file key.</summary>
    public string? Key => JsonAccess.TryGetString(node, "ik", out string value) ? value : null;

    /// <summary>Gets the serialized relative asset path.</summary>
    public string? RelativePath => JsonAccess.TryGetString(node, "rpath", out string value) ? value : null;

    /// <summary>Gets the serialized absolute asset path.</summary>
    public string? AbsolutePath => JsonAccess.TryGetString(node, "apath", out string value) ? value : null;
}

/// <summary>Provides a typed, read-only view of a speaker registration.</summary>
public sealed class SpeakerView
{
    private readonly JsonObject node;

    internal SpeakerView(JsonObject node, int index)
    {
        this.node = node;
        Index = index;
    }

    /// <summary>Gets the serialized registry index.</summary>
    public int Index { get; }

    /// <summary>Gets the registered speaker key.</summary>
    public string? Key => JsonAccess.TryGetString(node, "key", out string value) ? value : null;

    /// <summary>Gets the speaker name.</summary>
    public string? Name => JsonAccess.TryGetString(node, "name", out string value) ? value : null;

    /// <summary>Gets the speaker definition file when present.</summary>
    public string? File => JsonAccess.TryGetString(node, "file", out string value) ? value : null;
}
