using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recotte.McpServer;

[McpServerToolType]
public sealed class RecotteMcpTools(RecotteToolService service)
{
    private const string ReadOnly = "Reads one .ccproj through Recotte.Core without changing or creating files.";
    private const string Removal = " removeTimelineObject removes any unlocked timeline object, prunes a file-item registration only when it becomes unused, and never deletes asset files from disk.";
    private const string OperationTypes = " Supported operation types are updateSpeakerText, moveTimelineObject, addTextOnlySpeakerVoice, removeTimelineObject, addImageObject, addVideoObject, and addAnnotationText. addAnnotationText places an editor hint text box on an Annot layer.";
    private const string Editing = "Atomically applies supported Recotte.Core operations and saves the result to a different .ccproj file. The source project is never modified. Recotte Studio is not launched." + OperationTypes + Removal;
    private const string Overwriting = "Atomically applies supported Recotte.Core operations and safely overwrites the loaded source .ccproj using Recotte.Core Save(). A mandatory backup and external-change detection are used. Recotte Studio is not launched." + OperationTypes + Removal;

    [McpServerTool(Name = "recotte_inspect_project"), Description(ReadOnly + " Returns its summary, capabilities, and validation diagnostics.")]
    public McpToolResult<object> InspectProject(string projectPath) => service.InspectProject(projectPath);

    [McpServerTool(Name = "recotte_validate_project"), Description(ReadOnly + " Validation errors are returned as structured results, not protocol errors.")]
    public McpToolResult<object> ValidateProject(string projectPath) => service.ValidateProject(projectPath);

    [McpServerTool(Name = "recotte_list_layers"), Description(ReadOnly + " Lists Core's safe layer views.")]
    public McpToolResult<object> ListLayers(string projectPath) => service.ListLayers(projectPath);

    [McpServerTool(Name = "recotte_list_timeline"), Description(ReadOnly + " Lists Core timeline entries in Core-defined order and overlap semantics.")]
    public McpToolResult<object> ListTimeline(string projectPath, string? layerName = null, string? objectType = null,
        decimal? start = null, decimal? end = null) => service.ListTimeline(projectPath, layerName, objectType, start, end);

    [McpServerTool(Name = "recotte_find_timeline_objects"), Description(ReadOnly + " Finds timeline candidates and never silently selects an ambiguous edit target.")]
    public McpToolResult<object> FindTimelineObjects(string projectPath, string? layerName = null, string? objectType = null,
        string? textContains = null, decimal? start = null, decimal? end = null) =>
        service.FindTimelineObjects(projectPath, layerName, objectType, textContains, start, end);

    [McpServerTool(Name = "recotte_get_capabilities"), Description(ReadOnly + " Returns the exact Core capabilities for the loaded project version; call before editing.")]
    public McpToolResult<object> GetCapabilities(string projectPath) => service.GetCapabilities(projectPath);

    [McpServerTool(Name = "recotte_inspect_assets"), Description(ReadOnly + " Returns unsupported unless Recotte.Core exposes safe asset inspection; it never infers ccproj references.")]
    public McpToolResult<object> InspectAssets(string projectPath) => service.InspectAssets(projectPath);

    [McpServerTool(Name = "recotte_preview_operations"), Description("Atomically previews supported Recotte.Core operations without changing the source, revision, or filesystem. Audio and character creation are unsupported." + OperationTypes + Removal)]
    public McpToolResult<object> PreviewOperations(string projectPath, IReadOnlyList<ProjectOperationDto> operations) =>
        service.PreviewOperations(projectPath, operations);

    [McpServerTool(Name = "recotte_preview_operations_and_save"), Description("Atomically previews supported operations and Core's SaveCopy plan without creating or changing files. Audio and character creation are unsupported." + OperationTypes + Removal)]
    public McpToolResult<object> PreviewOperationsAndSave(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations) => service.PreviewOperationsAndSave(projectPath, outputPath, allowOverwrite, operations);

    [McpServerTool(Name = "recotte_apply_operations_and_save_copy"), Description(Editing)]
    public Task<McpToolResult<object>> ApplyOperationsAndSaveCopy(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        service.ApplyOperationsAndSaveCopyAsync(projectPath, outputPath, allowOverwrite, operations, cancellationToken);

    [McpServerTool(Name = "recotte_preview_operations_and_overwrite"), Description("Previews supported operations and Recotte.Core's safe overwrite plan without changing any file. Mandatory backup, validation, and external-change checks are reported. Recotte Studio is not launched." + OperationTypes + Removal)]
    public McpToolResult<object> PreviewOperationsAndOverwrite(string projectPath,
        IReadOnlyList<ProjectOperationDto> operations) => service.PreviewOperationsAndOverwrite(projectPath, operations);

    [McpServerTool(Name = "recotte_apply_operations_and_overwrite"), Description(Overwriting)]
    public Task<McpToolResult<object>> ApplyOperationsAndOverwrite(string projectPath,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        service.ApplyOperationsAndOverwriteAsync(projectPath, operations, cancellationToken);

    [McpServerTool(Name = "recotte_add_text_sequence_and_save_copy"), Description(Editing + " Core calculates all sequence times from start, durations, and gap.")]
    public Task<McpToolResult<object>> AddTextSequenceAndSaveCopy(string projectPath, string outputPath, LayerTargetDto layer,
        decimal start, decimal defaultDuration, decimal gap, bool allowOverwrite, IReadOnlyList<TextSequenceItemDto> items,
        CancellationToken cancellationToken) => service.AddTextSequenceAndSaveCopyAsync(projectPath, outputPath, layer,
            start, defaultDuration, gap, allowOverwrite, items, cancellationToken);

    [McpServerTool(Name = "recotte_create_project_from_template"), Description(Editing + " Treats the source as a verified template and preserves unknown fields through Core SaveCopy.")]
    public Task<McpToolResult<object>> CreateProjectFromTemplate(string templatePath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        service.CreateProjectFromTemplateAsync(templatePath, outputPath, allowOverwrite, operations, cancellationToken);

    [McpServerTool(Name = "recotte_create_project"), Description("Creates a validated Recotte Studio 1.8.5.0 project from Core's built-in template, atomically applies supported operations, and publishes it only after a verified SaveCopy." + OperationTypes + Removal)]
    public Task<McpToolResult<object>> CreateProject(string projectName, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        service.CreateProjectAsync(projectName, outputPath, allowOverwrite, operations, cancellationToken);
}
