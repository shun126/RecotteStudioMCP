using Recotte.Core;

namespace Recotte.McpServer;

public sealed record OperationMapResult(bool Success, IReadOnlyList<ProjectOperation> Operations,
    string? ErrorCode, string? Message);

public sealed class ProjectOperationMapper
{
    public static readonly IReadOnlyList<string> SupportedTypes = new[]
    {
        "updateSpeakerText", "moveTimelineObject", "addTextOnlySpeakerVoice", "removeTimelineObject",
        "addImageObject", "addVideoObject", "addAnnotationText",
    };

    public OperationMapResult Map(IReadOnlyList<ProjectOperationDto>? values)
    {
        if (values is null) return Failure("MCP_OPERATIONS_REQUIRED", "Operations are required.");
        List<ProjectOperation> mapped = new(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            ProjectOperationDto value = values[index];
            try
            {
                mapped.Add(value.Type switch
                {
                    "updateSpeakerText" => new UpdateSpeakerTextOperation(Object(value.Target), Required(value.Text, "text")),
                    "moveTimelineObject" => new MoveTimelineObjectOperation(Object(value.Target), Time(value.Start, "start"), Time(value.End, "end")),
                    "addTextOnlySpeakerVoice" => new AddTextOnlySpeakerVoiceOperation(Layer(value.Layer), Required(value.Text, "text"), Time(value.Start, "start"), Time(value.End, "end")),
                    "removeTimelineObject" => new RemoveTimelineObjectOperation(Object(value.Target)),
                    "addImageObject" => new AddImageAssetOperation(Layer(value.Layer), Required(value.AssetPath, "assetPath"), Required(value.ProjectPath, "projectPath"), Required(value.DisplayName, "displayName"), Time(value.Start, "start"), Time(value.End, "end")),
                    "addVideoObject" => new AddVideoAssetOperation(Layer(value.Layer), Required(value.AssetPath, "assetPath"), Required(value.ProjectPath, "projectPath"), Required(value.DisplayName, "displayName"), Time(value.Start, "start"), Time(value.End, "end")),
                    "addAnnotationText" => new AddAnnotationTextOperation(Layer(value.Layer), Required(value.Text, "text"), Time(value.Start, "start"), Time(value.End, "end")),
                    _ => throw new ArgumentException($"Unsupported operation '{value.Type}'. Supported operations: {string.Join(", ", SupportedTypes)}."),
                });
            }
            catch (ArgumentException exception) { return Failure("MCP_OPERATION_INVALID", $"Operation {index}: {exception.Message}"); }
        }
        return new(true, mapped, null, null);
    }

    public LayerReference MapLayer(LayerTargetDto? value) => Layer(value);
    private static LayerReference Layer(LayerTargetDto? value)
    {
        if (value is null) throw new ArgumentException("layer is required.");
        int count = (value.LayerIndex.HasValue ? 1 : 0) + (!string.IsNullOrWhiteSpace(value.Name) ? 1 : 0);
        if (count != 1 || value.Type is not null) throw new ArgumentException("layer must contain exactly one of layerIndex or name; type cannot identify a layer.");
        return value.LayerIndex is int index ? new LayerIndexReference(index) : new LayerNameReference(value.Name!);
    }
    private static TimelineObjectReference Object(TimelineObjectTargetDto? value)
    {
        if (value?.ObjectKey is not int key) throw new ArgumentException("target.objectKey is required.");
        if (value.LayerName is not null) throw new ArgumentException("target.layerName cannot be combined with object identity.");
        return value.LayerIndex is int layer ? new TimelineObjectIdReference(new(layer, key)) : new ObjectKeyReference(key);
    }
    private static ProjectTime Time(decimal? value, string name) => value is decimal number && number >= 0
        ? new(number) : throw new ArgumentException($"{name} must be a non-negative number.");
    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException($"{name} is required.");
    private static OperationMapResult Failure(string code, string message) => new(false, Array.Empty<ProjectOperation>(), code, message);
}
