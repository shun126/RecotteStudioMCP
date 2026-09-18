using Recotte.Core;

namespace Recotte.McpServer;

public sealed class RecotteToolService(WorkspacePathPolicy paths, ProjectOperationMapper operationMapper,
    OutputPathLockManager outputLocks)
{
    public McpToolResult<object> InspectProject(string projectPath) => WithDocument(projectPath, (path, document) =>
    {
        ProjectSummary summary = document.GetSummary();
        return Ok(new { projectPath = path, summary = Summary(summary), capabilities = summary.Capabilities });
    });

    public McpToolResult<object> ValidateProject(string projectPath) => WithDocument(projectPath, (_, document) =>
    {
        ProjectValidationResult validation = document.Validate();
        return new(validation.IsValid, validation.IsValid ? "success" : "validation", null, null,
            new { isValid = validation.IsValid, compatibilityLevel = document.Compatibility.ToString() }, Diagnostics(validation.Diagnostics));
    });

    public McpToolResult<object> ListLayers(string projectPath) => WithDocument(projectPath, (_, document) => Ok(new
    {
        layers = document.Layers.Select(layer => new
        {
            layerIndex = layer.Index, layer.Name, layer.Type, objectCount = layer.Objects.Count,
        }).ToArray(),
    }));

    public McpToolResult<object> ListTimeline(string projectPath, string? layerName = null, string? objectType = null,
        decimal? start = null, decimal? end = null) => WithDocument(projectPath, (_, document) =>
    {
        TimelineQuery query = new()
        {
            Layer = layerName is null ? null : new LayerNameReference(layerName),
            ObjectType = objectType,
            OverlappingRange = Range(start, end),
        };
        return Ok(new { entries = document.GetTimelineEntries(query).Select(Timeline).ToArray() });
    });

    public McpToolResult<object> FindTimelineObjects(string projectPath, string? layerName = null,
        string? objectType = null, string? textContains = null, decimal? start = null, decimal? end = null) =>
        WithDocument(projectPath, (_, document) =>
        {
            TimelineQuery query = new()
            {
                Layer = layerName is null ? null : new LayerNameReference(layerName), ObjectType = objectType,
                OverlappingRange = Range(start, end),
            };
            IEnumerable<TimelineEntryView> entries = document.GetTimelineEntries(query);
            if (textContains is not null) entries = entries.Where(item => item.Text?.Contains(textContains, StringComparison.OrdinalIgnoreCase) == true);
            return Ok(new { entries = entries.Select(Timeline).ToArray() });
        });

    public McpToolResult<object> GetCapabilities(string projectPath) => WithDocument(projectPath, (_, document) => Ok(new
    {
        applicationVersion = document.ApplicationVersionText,
        compatibilityLevel = document.Compatibility.ToString(),
        capabilities = document.Capabilities,
    }));

    public McpToolResult<object> InspectAssets(string projectPath) => WithDocument(projectPath, (_, document) =>
        Fail("unsupported", "MCP_ASSET_INSPECTION_UNSUPPORTED",
            "Recotte.Core does not expose a safe asset-inspection API; no references were inferred.", document.LoadDiagnostics));

    public McpToolResult<object> PreviewOperations(string projectPath, IReadOnlyList<ProjectOperationDto>? operations) =>
        WithMappedDocument(projectPath, operations, (_, document, mapped) =>
        {
            ProjectBatchPreview preview = document.PreviewOperations(mapped);
            return Batch(preview.BatchResult, new { estimatedDuration = preview.EstimatedDuration?.TotalSeconds, preview.CanSaveCopy });
        });

    public McpToolResult<object> PreviewOperationsAndSave(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto>? operations)
    {
        PathPolicyResult output = paths.ValidateOutput(outputPath);
        if (!output.Success) return PathFailure(output);
        return WithMappedDocument(projectPath, operations, (_, document, mapped) =>
        {
            ProjectBatchSavePreview preview = document.PreviewOperationsAndSaveCopy(output.FullPath!, mapped,
                new() { SaveOptions = new() { AllowOverwrite = allowOverwrite } });
            return Batch(preview.Operations.BatchResult, new
            {
                canSave = preview.CanSave,
                savePreview = preview.SavePreview is null ? null : new
                {
                    preview.SavePreview.DestinationPath, preview.SavePreview.DestinationExists,
                    preview.SavePreview.OverwriteRequired, preview.SavePreview.CanSave,
                },
            }, preview.Diagnostics);
        });
    }

    public async Task<McpToolResult<object>> ApplyOperationsAndSaveCopyAsync(string projectPath, string outputPath,
        bool allowOverwrite, IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken)
    {
        PathPolicyResult input = paths.ValidateInput(projectPath);
        if (!input.Success) return PathFailure(input);
        PathPolicyResult output = paths.ValidateOutput(outputPath);
        if (!output.Success) return PathFailure(output);
        if (PathsEqual(input.FullPath!, output.FullPath!)) return Fail("path", "MCP_PATH_SOURCE_DESTINATION_SAME", "The source and output paths must differ.");
        OperationMapResult map = operationMapper.Map(operations);
        if (!map.Success) return Fail("argument", map.ErrorCode!, map.Message!);
        cancellationToken.ThrowIfCancellationRequested();
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(input.FullPath!);
            cancellationToken.ThrowIfCancellationRequested();
            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output.FullPath!, map.Operations,
                new() { SaveOptions = new() { AllowOverwrite = allowOverwrite } });
            return BatchSave(input.FullPath!, output.FullPath!, result);
        }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    public McpToolResult<object> PreviewOperationsAndOverwrite(string projectPath,
        IReadOnlyList<ProjectOperationDto>? operations) => WithMappedDocument(projectPath, operations, (path, document, mapped) =>
    {
        ProjectBatchSavePreview preview = document.PreviewOperationsAndOverwrite(mapped);
        return Batch(preview.Operations.BatchResult, new
        {
            canSave = preview.CanSave, saveMode = "overwrite", projectPath = path,
            sourceChanged = preview.SavePreview?.ExternalChangeDetected,
            backupPath = preview.SavePreview?.BackupPath,
            blockingDiagnostics = Diagnostics(preview.SavePreview?.BlockingDiagnostics),
        }, preview.Diagnostics);
    });

    public async Task<McpToolResult<object>> ApplyOperationsAndOverwriteAsync(string projectPath,
        IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken)
    {
        PathPolicyResult input = paths.ValidateInput(projectPath);
        if (!input.Success) return PathFailure(input);
        OperationMapResult map = operationMapper.Map(operations);
        if (!map.Success) return Fail("argument", map.ErrorCode!, map.Message!);
        cancellationToken.ThrowIfCancellationRequested();
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(input.FullPath!, cancellationToken);
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(input.FullPath!);
            ProjectBatchSaveResult result = document.ApplyOperationsAndOverwrite(map.Operations);
            return OverwriteResult(input.FullPath!, result);
        }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    public async Task<McpToolResult<object>> AddTextSequenceAndSaveCopyAsync(string projectPath, string outputPath,
        LayerTargetDto layer, decimal start, decimal defaultDuration, decimal gap, bool allowOverwrite,
        IReadOnlyList<TextSequenceItemDto> items, CancellationToken cancellationToken)
    {
        List<ProjectOperationDto> operations = new(items.Count);
        // Placement itself is deliberately delegated to Core below; this list is only used for shared path/map validation.
        PathPolicyResult input = paths.ValidateInput(projectPath); if (!input.Success) return PathFailure(input);
        PathPolicyResult output = paths.ValidateOutput(outputPath); if (!output.Success) return PathFailure(output);
        if (PathsEqual(input.FullPath!, output.FullPath!)) return Fail("path", "MCP_PATH_SOURCE_DESTINATION_SAME", "The source and output paths must differ.");
        try
        {
            LayerReference mappedLayer = operationMapper.MapLayer(layer);
            AddTextSequenceRequest request = new(mappedLayer, items.Select(item => new TextSequenceItem(item.Text,
                item.Duration is decimal duration ? new ProjectTime(duration) : null)).ToArray(), new(start), new(gap), new(defaultDuration));
            await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
            RecotteProjectDocument document = RecotteProject.Load(input.FullPath!);
            cancellationToken.ThrowIfCancellationRequested();
            AddTextSequenceResult sequence = document.AddTextSequence(request);
            if (!sequence.Success) return Batch(sequence.BatchResult, new { finalEndTime = sequence.FinalEndTime.TotalSeconds, objectIds = sequence.ObjectIds });
            ProjectSaveResult saved = document.SaveCopy(output.FullPath!, new() { AllowOverwrite = allowOverwrite });
            return Ok(new { projectPath = input.FullPath, outputPath = output.FullPath, finalEndTime = sequence.FinalEndTime.TotalSeconds,
                objectIds = sequence.ObjectIds, batch = BatchData(sequence.BatchResult), save = Save(saved) }, sequence.BatchResult.Diagnostics.Concat(saved.Warnings));
        }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    public Task<McpToolResult<object>> CreateProjectFromTemplateAsync(string templatePath, string outputPath,
        bool allowOverwrite, IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken) =>
        ApplyOperationsAndSaveCopyAsync(templatePath, outputPath, allowOverwrite, operations, cancellationToken);

    public async Task<McpToolResult<object>> CreateProjectAsync(string projectName, string outputPath,
        bool allowOverwrite, IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken)
    {
        PathPolicyResult output = paths.ValidateOutput(outputPath);
        if (!output.Success) return PathFailure(output);
        OperationMapResult map = operationMapper.Map(operations);
        if (!map.Success) return Fail("argument", map.ErrorCode!, map.Message!);
        string? directory = Path.GetDirectoryName(output.FullPath!);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return Fail("path", "MCP_PATH_DIRECTORY_MISSING", "The output directory does not exist.");
        cancellationToken.ThrowIfCancellationRequested();
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        try
        {
            RecotteProjectDocument document = RecotteProject.Create(new(projectName, directory));
            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output.FullPath!, map.Operations,
                new() { SaveOptions = new() { AllowOverwrite = allowOverwrite } });
            return BatchSave(string.Empty, output.FullPath!, result);
        }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    private McpToolResult<object> WithMappedDocument(string path, IReadOnlyList<ProjectOperationDto>? operations,
        Func<string, RecotteProjectDocument, IReadOnlyList<ProjectOperation>, McpToolResult<object>> action)
    {
        OperationMapResult map = operationMapper.Map(operations);
        return !map.Success ? Fail("argument", map.ErrorCode!, map.Message!) :
            WithDocument(path, (full, document) => action(full, document, map.Operations));
    }

    private McpToolResult<object> WithDocument(string path, Func<string, RecotteProjectDocument, McpToolResult<object>> action)
    {
        PathPolicyResult checkedPath = paths.ValidateInput(path);
        if (!checkedPath.Success) return PathFailure(checkedPath);
        try { return action(checkedPath.FullPath!, RecotteProject.Load(checkedPath.FullPath!)); }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    private static McpToolResult<object> BatchSave(string source, string output, ProjectBatchSaveResult result) =>
        new(result.Success, result.Success ? "success" : result.SaveFailureKind is null ? "edit" : "save",
            result.SaveFailureKind?.ToString(), result.Success ? null : "The atomic edit or SaveCopy operation failed.",
            new { projectPath = source, outputPath = output, batch = BatchData(result.BatchResult), save = result.SaveResult is null ? null : Save(result.SaveResult) },
            Diagnostics(result.Diagnostics));
    private static McpToolResult<object> OverwriteResult(string path, ProjectBatchSaveResult result)
    {
        ProjectSaveResult? save = result.SaveResult;
        string? errorCode = result.SaveFailureKind?.ToString();
        string category = result.Success ? "success" : result.SaveFailureKind == ProjectSaveFailureKind.SourceChanged ? "conflict" :
            result.SaveFailureKind is null ? "edit" : "save";
        return new(result.Success, category, errorCode,
            result.Success ? null : result.SaveFailureKind == ProjectSaveFailureKind.SourceChanged
                ? "The source project changed after it was loaded." : "The atomic edit or overwrite operation failed.",
            new { saveMode = "overwrite", projectPath = path, destinationPath = save?.DestinationPath,
                backupPath = save?.BackupPath, revision = save?.Revision,
                validation = save is null ? null : new { isValid = save.ValidationAfterSave.IsValid,
                    diagnostics = Diagnostics(save.ValidationAfterSave.Diagnostics) } }, Diagnostics(result.Diagnostics));
    }
    private static McpToolResult<object> Batch(ProjectBatchResult result, object? extra = null, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(result.Success, result.Success ? "success" : "edit", null, result.Success ? null : "The atomic operation preview failed.",
            new { batch = BatchData(result), extra }, Diagnostics(diagnostics ?? result.Diagnostics));
    private static object BatchData(ProjectBatchResult value) => new { value.Success, value.AppliedOperationCount, value.FailedOperationIndex,
        value.OperationResults, value.Changes, validation = new { value.Validation.IsValid, diagnostics = Diagnostics(value.Validation.Diagnostics) },
        value.CanCommit, value.Committed, value.RolledBack };
    private static object Save(ProjectSaveResult value) => new { value.DestinationPath, value.BackupPath, value.Revision, value.SavedAtUtc,
        warnings = Diagnostics(value.Warnings), validation = new { value.ValidationAfterSave.IsValid, diagnostics = Diagnostics(value.ValidationAfterSave.Diagnostics) } };
    private static object Summary(ProjectSummary value) => new { value.ApplicationVersion, compatibilityLevel = value.Compatibility.ToString(),
        value.CompatibilityReason, value.Revision, duration = value.Duration?.TotalSeconds, value.AutoDuration, value.Width, value.Height,
        frameRate = value.FramesPerSecond, value.LayerCount, value.ObjectCount, value.SpeakerCount, value.FileItemCount,
        value.ObjectCountsByType, value.ErrorCount, value.WarningCount, value.MissingAssetCount };
    private static object Timeline(TimelineEntryView value) => new { value.LayerIndex, value.LayerName, value.LayerType,
        objectKey = value.ObjectId?.ObjectKey, value.ObjectIndex, value.ObjectType, value.Name, value.Text,
        start = value.StartTime?.TotalSeconds, end = value.EndTime?.TotalSeconds, duration = value.Duration?.TotalSeconds,
        locked = value.IsLocked, value.Capabilities };
    private static ProjectTimeRange? Range(decimal? start, decimal? end) => start is null && end is null ? null :
        start is decimal a && end is decimal b ? new(new(a), new(b)) : throw new ArgumentException("start and end must be supplied together.");
    private static McpToolResult<object> Ok(object data, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(true, "success", null, null, data, Diagnostics(diagnostics));
    private static McpToolResult<object> Fail(string category, string code, string message, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(false, category, code, message, null, Diagnostics(diagnostics));
    private static McpToolResult<object> PathFailure(PathPolicyResult result) => Fail("path", result.ErrorCode!, result.Message!);
    private static IReadOnlyList<McpDiagnosticDto> Diagnostics(IEnumerable<ProjectDiagnostic>? values) => (values ?? Array.Empty<ProjectDiagnostic>())
        .Select(value => new McpDiagnosticDto(value.Code, value.Severity.ToString(), value.Message, value.JsonPath)).ToArray();
    private static bool PathsEqual(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static McpToolResult<object> ExceptionResult(Exception exception) => exception switch
    {
        RecotteProjectSaveException save => Fail("save", save.Kind.ToString(), save.Message, save.Warnings),
        RecotteProjectLoadException load => Fail("load", "MCP_PROJECT_LOAD_FAILED", load.Message),
        RecotteProjectCreationException creation => Fail("creation", "MCP_PROJECT_CREATION_FAILED", creation.Message, creation.Diagnostics),
        ArgumentException argument => Fail("argument", "MCP_ARGUMENT_INVALID", argument.Message),
        IOException io => Fail("save", "MCP_IO_FAILED", io.Message),
        _ => Fail("unexpected", "MCP_UNEXPECTED", $"{exception.GetType().Name}: {exception.Message} CorrelationId: {Guid.NewGuid():N}"),
    };
}
