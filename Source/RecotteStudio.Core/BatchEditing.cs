using System.Text.Json.Nodes;

#pragma warning disable CS1591 // Public record members are documented by their containing API types and README.

namespace RecotteStudio.Core;

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
    AddFigure,
    AddFadeEffect,
    UpdateFadeEffect,
    RemoveFadeEffect,
    AddVideoLayer,
    ImportCharacter,
    AddAnnotationLayer,
    MoveSpeakerVoiceToLayer,
    AddSpeakerLayer,
    MoveLayer,
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

public sealed record AddFigureOperation(LayerReference Layer, FigureDefinition Figure, ProjectTime StartTime,
    ProjectTime EndTime, decimal X, decimal Y, decimal Width, decimal Height) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddFigure;
}

/// <summary>Adds an empty Video layer after the last Video layer; later layers' indexes grow by one.</summary>
public sealed record AddVideoLayerOperation(string? Name = null, int? Index = null) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddVideoLayer;
}

/// <summary>Adds an empty Annot layer after the last Annot layer; later layers' indexes grow by one.</summary>
public sealed record AddAnnotationLayerOperation(string? Name = null, int? Index = null) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddAnnotationLayer;
}

/// <summary>
/// Imports a character and its Speaker layer from a donor project, reusing the target's empty Speaker layer or inserting
/// before the first Annot layer; an inserted layer makes every later layer's index grow by one.
/// </summary>
public sealed record ImportCharacterOperation(RecotteProjectDocument Donor, string SpeakerKey, ProjectTime StartTime,
    ProjectTime EndTime, string? LayerName = null) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.ImportCharacter;
}

/// <summary>Adds a Speaker layer without a character after the last Speaker layer; later layers' indexes grow by one.</summary>
public sealed record AddSpeakerLayerOperation(string? Name = null, int? Index = null) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.AddSpeakerLayer;
}

/// <summary>Moves a layer so that it ends up at TargetIndex; the layers in between shift by one.</summary>
public sealed record MoveLayerOperation(LayerReference Layer, int TargetIndex) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.MoveLayer;
}

/// <summary>Moves a Speaker Voice to another Speaker layer, keeping its object key, text, and time range.</summary>
public sealed record MoveSpeakerVoiceToLayerOperation(TimelineObjectReference Target, LayerReference Layer) : ProjectOperation
{
    public override ProjectOperationKind Kind => ProjectOperationKind.MoveSpeakerVoiceToLayer;
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
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    /// <summary>Gets the added layer's index in the final batch state.</summary>
    public int? CreatedLayerIndex { get; init; }
}

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
    bool RolledBack)
{
    /// <summary>
    /// Gets audio-backed Speaker Voice clips whose final text differs from the text present before this batch.
    /// Intermediate edits that are reverted or removed later in the same atomic batch are excluded.
    /// </summary>
    public IReadOnlyList<TimelineObjectId> AudioRegenerationRequired { get; init; } = Array.Empty<TimelineObjectId>();
}

/// <summary>Contains a non-mutating batch simulation.</summary>
public sealed record ProjectBatchPreview(ProjectBatchResult BatchResult, ProjectTime? EstimatedDuration, bool CanSaveCopy);

internal sealed record OperationExecutionResult(EditResult EditResult, string? Target,
    TimelineObjectId? CreatedObjectId = null, string? CreatedFileItemKey = null, int? CreatedLayerIndex = null,
    int? InsertedLayerIndex = null, VoiceRelocation? MovedVoice = null, (int From, int To)? MovedLayer = null);

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
        JsonObject audioBaseline = (JsonObject)before.DeepClone();
        List<ProjectOperationResult> results = new();
        List<ProjectDiagnostic> diagnostics = new();
        // Where each moved voice was in audioBaseline, keyed by where it is now, so its audio can still be compared.
        Dictionary<TimelineObjectId, TimelineObjectId> origins = new();

        for (int index = 0; index < operations.Count; index++)
        {
            ProjectOperation operation = operations[index];
            OperationExecutionResult executed = Editor.ApplyOperation(operation);
            EditResult edit = executed.EditResult;
            // Align original voice text with the evolving layout without changing the rollback snapshot. A malformed project
            // may have no layers array; only a layer inserted or moved proves there is one.
            JsonArray? baselineLayers = audioBaseline["layers"] as JsonArray;
            Func<int, int>? remap = null;
            if (edit.Success && executed.InsertedLayerIndex is int insertedIndex && baselineLayers is not null)
            {
                baselineLayers.Insert(insertedIndex, new JsonObject());
                remap = LayerInsertionMetadata.Insertion(insertedIndex);
            }
            else if (edit.Success && executed.MovedLayer is (int fromLayer, int toLayer) && baselineLayers is not null)
            {
                JsonNode? movedBaseline = baselineLayers[fromLayer];
                baselineLayers.RemoveAt(fromLayer);
                baselineLayers.Insert(toLayer, movedBaseline);
                remap = LayerInsertionMetadata.Move(fromLayer, toLayer);
            }
            if (remap is not null)
            {
                for (int prior = 0; prior < results.Count; prior++)
                {
                    ProjectOperationResult result = results[prior];
                    results[prior] = result with
                    {
                        Target = LayerInsertionMetadata.Target(result.Target, remap),
                        CreatedObjectId = result.CreatedObjectId is TimelineObjectId id ? Remap(id, remap) : null,
                        CreatedLayerIndex = result.CreatedLayerIndex is int layer ? remap(layer) : null,
                        Changes = result.Changes.Select(change => LayerInsertionMetadata.Change(change, remap)).ToArray(),
                        Diagnostics = result.Diagnostics.Select(diagnostic => LayerInsertionMetadata.Diagnostic(diagnostic, remap)).ToArray(),
                    };
                }
                for (int prior = 0; prior < diagnostics.Count; prior++)
                    diagnostics[prior] = LayerInsertionMetadata.Diagnostic(diagnostics[prior], remap);
                origins = origins.ToDictionary(pair => Remap(pair.Key, remap), pair => Remap(pair.Value, remap));
            }
            if (edit.Success && executed.MovedVoice is VoiceRelocation moved)
            {
                (TimelineObjectId from, TimelineObjectId to) = (moved.From, moved.To);
                origins[to] = origins.Remove(from, out TimelineObjectId origin) ? origin : from;
                // Earlier results that named the voice now name where it ended up, and their locations follow it.
                for (int prior = 0; prior < results.Count; prior++)
                {
                    ProjectOperationResult result = results[prior];
                    results[prior] = result with
                    {
                        Target = result.Target == from.ToString() ? to.ToString() : result.Target,
                        CreatedObjectId = result.CreatedObjectId == from ? to : result.CreatedObjectId,
                        Changes = result.Changes.Select(change => LayerInsertionMetadata.Change(change, moved)).ToArray(),
                        Diagnostics = result.Diagnostics.Select(diagnostic => LayerInsertionMetadata.Diagnostic(diagnostic, moved)).ToArray(),
                    };
                }
                for (int prior = 0; prior < diagnostics.Count; prior++)
                    diagnostics[prior] = LayerInsertionMetadata.Diagnostic(diagnostics[prior], moved);
            }
            results.Add(new(index, operation.Kind, edit.Success, executed.Target, executed.CreatedObjectId,
                executed.CreatedFileItemKey, edit.Changes, edit.Diagnostics) { CreatedLayerIndex = executed.CreatedLayerIndex });
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

        (IReadOnlyList<TimelineObjectId> audioRegenerationRequired, IReadOnlyList<ProjectDiagnostic> finalDiagnostics) =
            FinalizeAudioRegeneration(audioBaseline, workingRoot, results, diagnostics, origins);
        return new(true, operations.Count, null, results.ToArray(), Changes.ToArray(),
            finalDiagnostics, validation, true, false, false)
        {
            AudioRegenerationRequired = audioRegenerationRequired,
        };
    }

    private const string StaleAudioDiagnosticCode = "RC4208";

    private static TimelineObjectId Remap(TimelineObjectId id, Func<int, int> map) => id with { LayerIndex = map(id.LayerIndex) };

    // A voice's audio is stale when its final text differs from the text it was generated for, or when it now sits on a
    // layer whose speaker differs from the one that spoke it. Moved voices are traced back through origins.
    private static (IReadOnlyList<TimelineObjectId> Targets, IReadOnlyList<ProjectDiagnostic> Diagnostics)
        FinalizeAudioRegeneration(JsonObject before, JsonObject after, IReadOnlyList<ProjectOperationResult> results,
            IEnumerable<ProjectDiagnostic> diagnostics, IReadOnlyDictionary<TimelineObjectId, TimelineObjectId> origins)
    {
        HashSet<string> candidates = results
            .Where(result => result.Success && result.Target is not null &&
                result.Diagnostics.Any(diagnostic => diagnostic.Code == StaleAudioDiagnosticCode))
            .Select(result => result.Target!)
            .ToHashSet(StringComparer.Ordinal);

        List<TimelineObjectId> targets = new();
        List<ProjectDiagnostic> moved = new();
        foreach ((TimelineObjectId id, JsonObject afterLayer, JsonObject afterObject) in TimelineObjects(after))
        {
            if (!candidates.Contains(id.ToString()) || !ProjectEditor.HasAudioMarker(afterObject) ||
                !TryGetSpeakerText(afterObject, out string afterText))
                continue;

            TimelineObjectId origin = origins.TryGetValue(id, out TimelineObjectId start) ? start : id;
            (TimelineObjectId Id, JsonObject Layer, JsonObject Object) beforeItem = TimelineObjects(before)
                .FirstOrDefault(item => item.Id == origin);
            if (beforeItem.Object is null || !ProjectEditor.HasAudioMarker(beforeItem.Object) ||
                !TryGetSpeakerText(beforeItem.Object, out string beforeText))
                continue;
            bool sameText = string.Equals(beforeText, afterText, StringComparison.Ordinal);
            bool sameSpeaker = string.Equals(JsonAccess.GetPropertyString(beforeItem.Layer, "Speaker"),
                JsonAccess.GetPropertyString(afterLayer, "Speaker"), StringComparison.Ordinal);
            if (sameText && sameSpeaker) continue;

            targets.Add(id);
            // Each move warns from the layer it left, which after an earlier move in this batch is not where the audio
            // was made, so a voice whose speaker changed gets one warning from where it started instead.
            if (!sameSpeaker)
                moved.Add(ProjectEditor.MovedAudioDiagnostic(id.ObjectKey, beforeItem.Layer, afterLayer,
                    $"$.layers[{id.LayerIndex}].layer-objects[{((JsonArray)afterLayer["layer-objects"]!).IndexOf(afterObject)}]"));
        }

        HashSet<string> keptTargets = targets.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        ProjectDiagnostic[] finalDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Code != StaleAudioDiagnosticCode)
            .Concat(results.Where(result => result.Target is not null && keptTargets.Contains(result.Target) &&
                    result.OperationType != ProjectOperationKind.MoveSpeakerVoiceToLayer)
                .SelectMany(result => result.Diagnostics.Where(diagnostic => diagnostic.Code == StaleAudioDiagnosticCode)))
            .Concat(moved)
            .Distinct()
            .ToArray();
        return (targets.ToArray(), finalDiagnostics);
    }

    private static IEnumerable<(TimelineObjectId Id, JsonObject Layer, JsonObject Object)> TimelineObjects(JsonObject root)
    {
        if (root["layers"] is not JsonArray layers) yield break;
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            if (layers[layerIndex] is not JsonObject layer || layer["layer-objects"] is not JsonArray objects) continue;
            foreach (JsonObject item in objects.OfType<JsonObject>())
                if (JsonAccess.TryGetInt32(item, "objkey", out int objectKey))
                    yield return (new(layerIndex, objectKey), layer, item);
        }
    }

    private static bool TryGetSpeakerText(JsonObject value, out string text)
    {
        text = string.Empty;
        return value["text"] is JsonObject textObject && JsonAccess.TryGetString(textObject, "text", out text);
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
            AddFadeEffectOperation value => WithObject(value.Target,
                id => new(AddFadeEffect(id, value.EffectKey, value.Duration), id.ToString())),
            UpdateFadeEffectOperation value => WithObject(value.Target,
                id => new(UpdateFadeEffect(id, value.EffectIndex, value.EffectKey, value.Duration), id.ToString())),
            RemoveFadeEffectOperation value => WithObject(value.Target,
                id => new(RemoveFadeEffect(id, value.EffectIndex), id.ToString())),
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
            MoveSpeakerVoiceToLayerOperation value => WithObject(value.Target, id => WithLayer(value.Layer, layerIndex =>
            {
                SpeakerVoiceMoveResult move = MoveSpeakerVoiceToLayer(new(id, new(layerIndex)));
                return new(move.EditResult, (move.ObjectId ?? id).ToString(), MovedVoice: move.Relocation);
            })),
            AddVideoLayerOperation value => LayerAdded(AddVideoLayer(new(value.Name, value.Index))),
            AddSpeakerLayerOperation value => LayerAdded(AddSpeakerLayer(new(value.Name, value.Index))),
            MoveLayerOperation value => WithLayer(value.Layer, layerIndex =>
            {
                LayerMoveResult move = MoveLayer(new(layerIndex, value.TargetIndex));
                return new(move.EditResult, $"layer:{move.LayerIndex ?? layerIndex}",
                    MovedLayer: move.Success && move.LayerIndex is int to && to != layerIndex ? (layerIndex, to) : null);
            }),
            AddAnnotationLayerOperation value => LayerAdded(AddAnnotationLayer(new(value.Name, value.Index))),
            ImportCharacterOperation value => CharacterImported(ImportCharacter(new(value.Donor, value.SpeakerKey,
                value.StartTime, value.EndTime, value.LayerName))),
            AddFigureOperation value => WithLayer(value.Layer, layerIndex =>
            {
                AssetAddResult asset = AddFigure(new(layerIndex, value.Figure, value.StartTime, value.EndTime,
                    value.X, value.Y, value.Width, value.Height));
                return new(asset.EditResult, $"layer:{layerIndex}", asset.ObjectId, asset.FileItemKey);
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

    private static OperationExecutionResult LayerAdded(LayerAddResult layer) => new(layer.EditResult,
        layer.LayerIndex is int index ? $"layer:{index}" : null, CreatedLayerIndex: layer.LayerIndex,
        InsertedLayerIndex: layer.LayerIndex);

    // An import that fills the empty Speaker layer still reports it, but only an inserted layer shifts the layers after it.
    private static OperationExecutionResult CharacterImported(CharacterImportResult character) => new(character.EditResult,
        character.LayerId is { } layer ? $"layer:{layer.LayerIndex}" : null, character.ObjectId,
        CreatedLayerIndex: character.LayerId?.LayerIndex,
        InsertedLayerIndex: character.InsertedLayer ? character.LayerId?.LayerIndex : null);

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
                AudioRegenerationRequired = Array.Empty<TimelineObjectId>(),
                Diagnostics = staged.Diagnostics.Where(diagnostic => diagnostic.Code != "RC4208")
                    .Concat(committed.Diagnostics).Distinct().ToArray(), Validation = new(committed.Diagnostics) };
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
