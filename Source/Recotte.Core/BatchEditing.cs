using System.Text.Json.Nodes;

#pragma warning disable CS1591 // Public record members are documented by their containing API types and README.

namespace Recotte.Core;

/// <summary>Identifies a supported high-level project operation.</summary>
public enum ProjectOperationKind
{
    UpdateSpeakerText,
    MoveTimelineObject,
    AddTextOnlySpeakerVoice,
    RemoveTimelineObject,
    AddImageAsset,
    AddVideoAsset,
    AddAnnotationText,
}

/// <summary>Base type for operations that can participate in an atomic batch.</summary>
public abstract record ProjectOperation
{
    /// <summary>Gets the stable machine-readable operation kind.</summary>
    public abstract ProjectOperationKind Kind { get; }
}

/// <summary>Base type for an unambiguous layer reference.</summary>
public abstract record LayerReference;
/// <summary>References the layer currently at an exact array index.</summary>
public sealed record LayerIndexReference(int LayerIndex) : LayerReference;
/// <summary>References the unique layer having the specified name.</summary>
public sealed record LayerNameReference(string Name) : LayerReference;
/// <summary>References the unique Speaker layer linked to a registered speaker key.</summary>
public sealed record SpeakerLayerReference(string SpeakerKey) : LayerReference;

/// <summary>Base type for an unambiguous timeline-object reference.</summary>
public abstract record TimelineObjectReference;
/// <summary>References an object by layer index and object key.</summary>
public sealed record TimelineObjectIdReference(TimelineObjectId ObjectId) : TimelineObjectReference;
/// <summary>References a project-wide unique object key; duplicate matches are rejected.</summary>
public sealed record ObjectKeyReference(int ObjectKey) : TimelineObjectReference;

public sealed record UpdateSpeakerTextOperation(TimelineObjectReference Target, string Text) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.UpdateSpeakerText;
}

public sealed record MoveTimelineObjectOperation(TimelineObjectReference Target, ProjectTime StartTime, ProjectTime EndTime) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.MoveTimelineObject;
}

public sealed record AddTextOnlySpeakerVoiceOperation(LayerReference Layer, string Text, ProjectTime StartTime, ProjectTime EndTime) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddTextOnlySpeakerVoice;
}

public sealed record RemoveTimelineObjectOperation(TimelineObjectReference Target) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.RemoveTimelineObject;
}

public sealed record AddImageAssetOperation(LayerReference Layer, string AssetPath, string ProjectPath,
    string DisplayName, ProjectTime StartTime, ProjectTime EndTime) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddImageAsset;
}

public sealed record AddVideoAssetOperation(LayerReference Layer, string AssetPath, string ProjectPath,
    string DisplayName, ProjectTime StartTime, ProjectTime EndTime) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddVideoAsset;
}

public sealed record AddAnnotationTextOperation(LayerReference Layer, string Text,
    ProjectTime StartTime, ProjectTime EndTime) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddAnnotationText;
}

/// <summary>Configures allocators and duration handling used by a document-level batch.</summary>
public sealed record ProjectBatchOptions
{
    public IObjectKeyAllocator? ObjectKeyAllocator { get; init; }
    public IProjectDurationPolicy? DurationPolicy { get; init; }
    public IFileItemKeyAllocator? FileItemKeyAllocator { get; init; }
}

/// <summary>Contains the result of one operation in a batch.</summary>
public sealed record ProjectOperationResult(
    int Index,
    ProjectOperationKind OperationType,
    bool Success,
    string? Target,
    TimelineObjectId? CreatedObjectId,
    string? CreatedFileItemKey,
    IReadOnlyList<ProjectChange> Changes,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

/// <summary>Contains the complete structured result of an atomic batch.</summary>
public sealed record ProjectBatchResult(
    bool Success,
    int AppliedOperationCount,
    int? FailedOperationIndex,
    IReadOnlyList<ProjectOperationResult> OperationResults,
    IReadOnlyList<ProjectChange> Changes,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    ProjectValidationResult Validation,
    bool CanCommit,
    bool Committed,
    bool RolledBack);

/// <summary>Contains a non-mutating batch simulation.</summary>
public sealed record ProjectBatchPreview(ProjectBatchResult BatchResult, ProjectTime? EstimatedDuration, bool CanSaveCopy);

internal sealed record OperationExecutionResult(EditResult EditResult, string? Target,
    TimelineObjectId? CreatedObjectId = null, string? CreatedFileItemKey = null);

public sealed partial class ProjectEditSession
{
    /// <summary>Applies a sequence once to a fresh session. Failure restores the working copy and makes the session rollback-only.</summary>
    public ProjectBatchResult ApplyOperations(IReadOnlyList<ProjectOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ObjectDisposedException.ThrowIf(completed, this);
        if (batchApplied || changes.Count != 0)
            throw new InvalidOperationException("A batch can only be applied once to a fresh edit session.");
        for (int index = 0; index < operations.Count; index++)
            if (operations[index] is null) throw new ArgumentException($"Operation {index} is null.", nameof(operations));

        batchApplied = true;
        JsonObject before = (JsonObject)workingRoot.DeepClone();
        List<ProjectOperationResult> results = new();
        List<ProjectDiagnostic> diagnostics = new();

        for (int index = 0; index < operations.Count; index++)
        {
            ProjectOperation operation = operations[index];
            OperationExecutionResult executed = Editor.ApplyOperation(operation);
            EditResult edit = executed.EditResult;
            results.Add(new(index, operation.Kind, edit.Success, executed.Target, executed.CreatedObjectId,
                executed.CreatedFileItemKey, edit.Changes, edit.Diagnostics));
            diagnostics.AddRange(edit.Diagnostics);
            if (!edit.Success)
            {
                RestoreWorkingRoot(before);
                rollbackOnly = true;
                return new(false, index, index, results.ToArray(), Array.Empty<ProjectChange>(), diagnostics.ToArray(),
                    Validate(), false, false, true);
            }
        }

        ProjectValidationResult validation = Validate();
        diagnostics.AddRange(validation.Diagnostics);
        if (!validation.IsValid)
        {
            RestoreWorkingRoot(before);
            rollbackOnly = true;
            return new(false, operations.Count, null, results.ToArray(), Array.Empty<ProjectChange>(),
                diagnostics.Distinct().ToArray(), validation, false, false, true);
        }

        return new(true, operations.Count, null, results.ToArray(), Changes.ToArray(),
            diagnostics.Distinct().ToArray(), validation, true, false, false);
    }

    private void RestoreWorkingRoot(JsonObject snapshot)
    {
        workingRoot.Clear();
        foreach ((string key, JsonNode? value) in snapshot)
            workingRoot[key] = value?.DeepClone();
        changes.Clear();
    }
}

public sealed partial class ProjectEditor
{
    internal OperationExecutionResult ApplyOperation(ProjectOperation operation)
    {
        return operation switch
        {
            UpdateSpeakerTextOperation value => WithObject(value.Target,
                id => new(UpdateSpeakerText(new(id, value.Text)), id.ToString())),
            MoveTimelineObjectOperation value => WithObject(value.Target,
                id => new(MoveTimelineObject(new(id, value.StartTime, value.EndTime)), id.ToString())),
            RemoveTimelineObjectOperation value => WithObject(value.Target,
                id => new(RemoveTimelineObject(id), id.ToString())),
            AddTextOnlySpeakerVoiceOperation value => WithLayer(value.Layer, layerIndex =>
            {
                EditResult edit = AddTextOnlySpeakerVoice(new(new(layerIndex), value.Text, value.StartTime, value.EndTime));
                TimelineObjectId? created = edit.Success && edit.Changes.LastOrDefault() is ProjectChange change &&
                    int.TryParse(change.Target, out int key) ? new(layerIndex, key) : null;
                return new(edit, $"layer:{layerIndex}", created);
            }),
            AddImageAssetOperation value => WithLayer(value.Layer, layerIndex =>
            {
                AssetAddResult asset = AddImageAsset(new(layerIndex, value.AssetPath, value.ProjectPath,
                    value.DisplayName, value.StartTime, value.EndTime));
                return new(asset.EditResult, $"layer:{layerIndex}", asset.ObjectId, asset.FileItemKey);
            }),
            AddVideoAssetOperation value => WithLayer(value.Layer, layerIndex =>
            {
                AssetAddResult asset = AddVideoAsset(new(layerIndex, value.AssetPath, value.ProjectPath,
                    value.DisplayName, value.StartTime, value.EndTime));
                return new(asset.EditResult, $"layer:{layerIndex}", asset.ObjectId, asset.FileItemKey);
            }),
            AddAnnotationTextOperation value => WithLayer(value.Layer, layerIndex =>
            {
                EditResult edit = AddAnnotationText(new(layerIndex, value.Text, value.StartTime, value.EndTime));
                TimelineObjectId? created = edit.Success && edit.Changes.LastOrDefault() is ProjectChange change &&
                    int.TryParse(change.Target, out int key) ? new(layerIndex, key) : null;
                return new(edit, $"layer:{layerIndex}", created);
            }),
            _ => new(EditResult.Failed("RC6101", "The operation type is not supported."), operation.GetType().Name),
        };
    }

    private OperationExecutionResult WithLayer(LayerReference reference, Func<int, OperationExecutionResult> action)
    {
        ArgumentNullException.ThrowIfNull(reference);
        int[] matches = reference switch
        {
            LayerIndexReference value => Layers().Where(item => item.index == value.LayerIndex).Select(item => item.index).ToArray(),
            LayerNameReference value => Layers().Where(item => JsonAccess.TryGetString(item.layer, "name", out string name) &&
                string.Equals(name, value.Name, StringComparison.Ordinal)).Select(item => item.index).ToArray(),
            SpeakerLayerReference value => Layers().Where(item => JsonAccess.TryGetString(item.layer, "type", out string type) &&
                type == "Speaker" && string.Equals(JsonAccess.GetPropertyString(item.layer, "Speaker"), value.SpeakerKey, StringComparison.Ordinal))
                .Select(item => item.index).ToArray(),
            _ => Array.Empty<int>(),
        };
        if (matches.Length == 0) return LookupFailure("RC6001", "The referenced layer was not found.", reference.ToString());
        if (matches.Length > 1) return LookupFailure("RC6002", "The referenced layer is ambiguous.", reference.ToString());
        return action(matches[0]);
    }

    private OperationExecutionResult WithObject(TimelineObjectReference reference, Func<TimelineObjectId, OperationExecutionResult> action)
    {
        ArgumentNullException.ThrowIfNull(reference);
        TimelineObjectId[] matches = Layers().SelectMany(layer =>
                (layer.layer["layer-objects"] as JsonArray)?.OfType<JsonObject>()
                    .Where(item => JsonAccess.TryGetInt32(item, "objkey", out _))
                    .Select(item => { JsonAccess.TryGetInt32(item, "objkey", out int key); return new TimelineObjectId(layer.index, key); })
                ?? Array.Empty<TimelineObjectId>())
            .Where(id => reference switch
            {
                TimelineObjectIdReference value => id == value.ObjectId,
                ObjectKeyReference value => id.ObjectKey == value.ObjectKey,
                _ => false,
            }).ToArray();
        if (matches.Length == 0) return LookupFailure("RC6001", "The referenced timeline object was not found.", reference.ToString());
        if (matches.Length > 1) return LookupFailure("RC6002", "The referenced timeline object is ambiguous.", reference.ToString());
        return action(matches[0]);
    }

    private static OperationExecutionResult LookupFailure(string code, string message, string? target) =>
        new(EditResult.Failed(code, message), target);
}

public sealed partial class RecotteProjectDocument
{
    /// <summary>Atomically applies and commits all operations, or leaves the document unchanged.</summary>
    public ProjectBatchResult ApplyOperations(IReadOnlyList<ProjectOperation> operations, ProjectBatchOptions? options = null)
    {
        ValidateOperationArguments(operations);
        if (!ProjectCompatibility.CanEdit)
            return UnsupportedBatch(operations, ProjectCompatibility.Reason);
        ProjectBatchOptions effective = options ?? new();
        using ProjectEditSession edit = BeginEdit(effective.ObjectKeyAllocator, effective.DurationPolicy, effective.FileItemKeyAllocator);
        ProjectBatchResult staged = edit.ApplyOperations(operations);
        if (!staged.Success) return staged;
        if (operations.Count == 0)
            return staged with { CanCommit = false, Committed = false, RolledBack = false };
        EditResult committed = edit.Commit();
        if (!committed.Success)
            return staged with { Success = false, CanCommit = false, RolledBack = true,
                Diagnostics = staged.Diagnostics.Concat(committed.Diagnostics).Distinct().ToArray(), Validation = new(committed.Diagnostics) };
        return staged with { CanCommit = false, Committed = true };
    }

    /// <summary>Simulates all operations without committing or writing a file.</summary>
    public ProjectBatchPreview PreviewOperations(IReadOnlyList<ProjectOperation> operations, ProjectBatchOptions? options = null)
    {
        ValidateOperationArguments(operations);
        if (!ProjectCompatibility.CanEdit)
            return new(UnsupportedBatch(operations, ProjectCompatibility.Reason), Settings?.Duration is decimal d ? new(d) : null, false);
        ProjectBatchOptions effective = options ?? new();
        using ProjectEditSession edit = BeginEdit(effective.ObjectKeyAllocator, effective.DurationPolicy, effective.FileItemKeyAllocator);
        ProjectBatchResult result = edit.ApplyOperations(operations);
        ProjectTime? duration = edit.Editor.GetProjectDuration();
        return new(result with { CanCommit = false, RolledBack = true }, duration,
            result.Success && ProjectCompatibility.CanSaveCopy && result.Validation.IsValid);
    }

    private static void ValidateOperationArguments(IReadOnlyList<ProjectOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        for (int index = 0; index < operations.Count; index++)
            if (operations[index] is null) throw new ArgumentException($"Operation {index} is null.", nameof(operations));
    }

    private ProjectBatchResult UnsupportedBatch(IReadOnlyList<ProjectOperation> operations, string reason)
    {
        ProjectDiagnostic diagnostic = new("RC6101", DiagnosticSeverity.Error, $"Batch editing is not supported: {reason}");
        return new(false, 0, operations.Count == 0 ? null : 0, Array.Empty<ProjectOperationResult>(),
            Array.Empty<ProjectChange>(), new[] { diagnostic }, Validate(), false, false, true);
    }
}

public sealed partial class ProjectEditor
{
    internal ProjectTime? GetProjectDuration() => root["setting"] is JsonObject settings &&
        JsonAccess.TryGetDecimal(settings, "duration", out decimal duration) && duration >= 0 ? new(duration) : null;
}
