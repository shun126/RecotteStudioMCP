using RecotteStudio.Core;

namespace RecotteStudio.McpServer;

public sealed record OperationMapResult(bool Success, IReadOnlyList<ProjectOperation> Operations,
    string? ErrorCode, string? Message);

public sealed class ProjectOperationMapper(FigureCatalogProvider? figureCatalog = null, WorkspacePathPolicy? paths = null)
{
    public const decimal DefaultFigureSize = 128m;
    private readonly FigureCatalogProvider figureCatalog = figureCatalog ?? new();
    private readonly WorkspacePathPolicy paths = paths ?? new(new McpServerOptions((string?)null));

    public static readonly IReadOnlyList<string> SupportedTypes = new[]
    {
        "updateSpeakerText", "moveTimelineObject", "addTextOnlySpeakerVoice", "removeTimelineObject",
        "addImageObject", "addVideoObject", "addAnnotationText", "addFigureObject",
        "addFadeEffect", "updateFadeEffect", "removeFadeEffect", "addVideoLayer", "importCharacter", "addAnnotationLayer",
    };

    public OperationMapResult Map(IReadOnlyList<ProjectOperationDto>? values)
    {
        if (values is null) return Failure("MCP_OPERATIONS_REQUIRED", "Operations are required.");
        List<ProjectOperation> mapped = new(values.Count);
        Dictionary<string, RecotteProjectDocument> donors = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < values.Count; index++)
        {
            ProjectOperationDto value = values[index];
            try
            {
                mapped.Add(value.Type switch
                {
                    "addFadeEffect" => new AddFadeEffectOperation(Object(value.Target), Required(value.EffectKey, "effectKey"), Time(value.Duration, "duration")),
                    "updateFadeEffect" => new UpdateFadeEffectOperation(Object(value.Target), value.EffectIndex ?? throw new ArgumentException("effectIndex is required."), Required(value.EffectKey, "effectKey"), Time(value.Duration, "duration")),
                    "removeFadeEffect" => new RemoveFadeEffectOperation(Object(value.Target), value.EffectIndex ?? throw new ArgumentException("effectIndex is required.")),
                    "updateSpeakerText" => new UpdateSpeakerTextOperation(Object(value.Target), Required(value.Text, "text")),
                    "moveTimelineObject" => new MoveTimelineObjectOperation(Object(value.Target), Time(value.Start, "start"), Time(value.End, "end")),
                    "addTextOnlySpeakerVoice" => new AddTextOnlySpeakerVoiceOperation(Layer(value.Layer), Required(value.Text, "text"), Time(value.Start, "start"), Time(value.End, "end")),
                    "removeTimelineObject" => new RemoveTimelineObjectOperation(Object(value.Target)),
                    "addImageObject" => new AddImageAssetOperation(Layer(value.Layer), Required(value.AssetPath, "assetPath"), Required(value.ProjectPath, "projectPath"), Required(value.DisplayName, "displayName"), Time(value.Start, "start"), Time(value.End, "end")),
                    "addVideoObject" => new AddVideoAssetOperation(Layer(value.Layer), Required(value.AssetPath, "assetPath"), Required(value.ProjectPath, "projectPath"), Required(value.DisplayName, "displayName"), Time(value.Start, "start"), Time(value.End, "end")),
                    "addAnnotationText" => new AddAnnotationTextOperation(Layer(value.Layer), Required(value.Text, "text"), Time(value.Start, "start"), Time(value.End, "end")),
                    "addFigureObject" => Figure(value),
                    "addVideoLayer" => new AddVideoLayerOperation(value.LayerName),
                    "addAnnotationLayer" => new AddAnnotationLayerOperation(value.LayerName),
                    "importCharacter" => new ImportCharacterOperation(Donor(value.DonorProjectPath, donors), Required(value.SpeakerKey, "speakerKey"), Time(value.Start, "start"), Time(value.End, "end"), value.LayerName),
                    _ => throw new ArgumentException($"Unsupported operation '{value.Type}'. Supported operations: {string.Join(", ", SupportedTypes)}."),
                });
            }
            catch (ArgumentException exception) { return Failure(exception is CodedArgumentException coded ? coded.Code : "MCP_OPERATION_INVALID", $"Operation {index}: {exception.Message}"); }
        }
        return new(true, mapped, null, null);
    }

    private AddFigureOperation Figure(ProjectOperationDto value)
    {
        string key = Required(value.FigureKey, "figureKey");
        FigureDefinition figure;
        try
        {
            figure = figureCatalog.Load().Find(key)
                ?? throw new ArgumentException($"figureKey '{key}' is not an installed figure; call recotte_list_figures for valid keys.");
        }
        catch (DirectoryNotFoundException exception) { throw new ArgumentException(exception.Message); }
        if (value.X is not decimal x || value.Y is not decimal y) throw new ArgumentException("x and y are required.");
        decimal width = value.Width ?? DefaultFigureSize;
        return new(Layer(value.Layer), figure, Time(value.Start, "start"), Time(value.End, "end"), x, y, width, value.Height ?? width);
    }

    // The donor is read whole and copied into the output, so it must pass the same workspace check as any input project.
    // A batch that imports several characters from one donor loads it once.
    private RecotteProjectDocument Donor(string? path, Dictionary<string, RecotteProjectDocument> donors)
    {
        PathPolicyResult donor = paths.ValidateInput(path);
        if (!donor.Success) throw new CodedArgumentException(donor.ErrorCode!, $"donorProjectPath: {donor.Message}");
        if (donors.TryGetValue(donor.FullPath!, out RecotteProjectDocument? loaded)) return loaded;
        try { loaded = RecotteProject.Load(donor.FullPath!); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or RecotteProjectLoadException)
        { throw new CodedArgumentException("MCP_PROJECT_LOAD_FAILED", $"donorProjectPath: {exception.Message}"); }
        donors[donor.FullPath!] = loaded;
        return loaded;
    }

    private sealed class CodedArgumentException(string code, string message) : ArgumentException(message)
    {
        public string Code { get; } = code;
    }

    public LayerReference MapLayer(LayerTargetDto? value) => Layer(value);
    public TimelineObjectReference MapObject(TimelineObjectTargetDto? value) => Object(value);
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
