namespace Recotte.Core;

#pragma warning disable CS1591 // Public record members are documented by their containing API types and README.

/// <summary>Describes one text item placed by a sequence operation.</summary>
public sealed record TextSequenceItem(string Text, ProjectTime? Duration = null);

/// <summary>Requests consecutive text-only Speaker Voice placement.</summary>
public sealed record AddTextSequenceRequest(LayerReference Layer, IReadOnlyList<TextSequenceItem> Items,
    ProjectTime StartTime, ProjectTime Gap, ProjectTime DefaultDuration);

/// <summary>Contains the atomic batch result and generated identities for a text sequence.</summary>
public sealed record AddTextSequenceResult(ProjectBatchResult BatchResult,
    IReadOnlyList<TimelineObjectId> ObjectIds, ProjectTime FinalEndTime)
{
    public bool Success => BatchResult.Success;
}

/// <summary>Configures an edit-and-SaveCopy operation.</summary>
public sealed record ProjectBatchSaveOptions
{
    public ProjectBatchOptions BatchOptions { get; init; } = new();
    public ProjectSaveOptions SaveOptions { get; init; } = new();
}

/// <summary>Contains both the committed edit result and verified SaveCopy result.</summary>
public sealed record ProjectBatchSaveResult(
    bool Success,
    ProjectBatchResult BatchResult,
    ProjectSaveResult? SaveResult,
    ProjectSaveFailureKind? SaveFailureKind,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

/// <summary>Contains a non-mutating preview of edits and their SaveCopy plan.</summary>
public sealed record ProjectBatchSavePreview(
    ProjectBatchPreview Operations,
    SavePreview? SavePreview,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    bool CanSave);

public sealed partial class ProjectEditSession
{
    internal RecotteProjectDocument CreateSnapshotDocument() =>
        new((System.Text.Json.Nodes.JsonObject)workingRoot.DeepClone(), document.SourcePath, document.SourceFileState,
            document.CreationRequest);
}

public sealed partial class RecotteProjectDocument
{
    /// <summary>Adds consecutive text-only Speaker Voice objects in one atomic transaction.</summary>
    public AddTextSequenceResult AddTextSequence(AddTextSequenceRequest request, ProjectBatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Layer);
        ArgumentNullException.ThrowIfNull(request.Items);
        if (request.DefaultDuration.TotalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "DefaultDuration must be greater than zero.");

        List<ProjectOperation> operations = new(request.Items.Count);
        decimal cursor = request.StartTime.TotalSeconds;
        for (int index = 0; index < request.Items.Count; index++)
        {
            TextSequenceItem item = request.Items[index] ?? throw new ArgumentException($"Item {index} is null.", nameof(request));
            decimal duration = (item.Duration ?? request.DefaultDuration).TotalSeconds;
            if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(request), $"Item {index} duration must be greater than zero.");
            ProjectTime end = new(cursor + duration);
            operations.Add(new AddTextOnlySpeakerVoiceOperation(request.Layer, item.Text, new(cursor), end));
            cursor = end.TotalSeconds + request.Gap.TotalSeconds;
        }

        decimal finalEnd = request.Items.Count == 0 ? request.StartTime.TotalSeconds : cursor - request.Gap.TotalSeconds;
        ProjectBatchResult result = ApplyOperations(operations, options);
        TimelineObjectId[] ids = result.Success
            ? result.OperationResults.Select(item => item.CreatedObjectId).OfType<TimelineObjectId>().ToArray()
            : Array.Empty<TimelineObjectId>();
        return new(result, ids, new(finalEnd));
    }

    /// <summary>Atomically edits the document and then writes a verified copy through the existing safe-save path.</summary>
    public ProjectBatchSaveResult ApplyOperationsAndSaveCopy(string destinationPath,
        IReadOnlyList<ProjectOperation> operations, ProjectBatchSaveOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateOperationArguments(operations);
        ProjectBatchSaveOptions effective = options ?? new();
        SavePreview initialPlan = PreviewSave(destinationPath, effective.SaveOptions);
        if (!initialPlan.CanSave)
        {
            ProjectBatchResult blocked = SaveBlockedBatch(initialPlan);
            return new(false, blocked, null, MapSaveFailure(initialPlan.BlockingDiagnostics.FirstOrDefault()),
                initialPlan.BlockingDiagnostics);
        }

        ProjectBatchResult batch = ApplyOperations(operations, effective.BatchOptions);
        if (!batch.Success)
            return new(false, batch, null, null, batch.Diagnostics);

        try
        {
            ProjectSaveResult saved = SaveCopy(destinationPath, effective.SaveOptions);
            return new(true, batch, saved, null, batch.Diagnostics.Concat(saved.Warnings).Distinct().ToArray());
        }
        catch (RecotteProjectSaveException exception)
        {
            ProjectDiagnostic failure = new("RC6202", DiagnosticSeverity.Error, exception.Message);
            return new(false, batch, null, exception.Kind,
                batch.Diagnostics.Concat(exception.Warnings).Append(failure).Distinct().ToArray());
        }
    }

    /// <summary>Atomically edits the document and safely overwrites its loaded source through <see cref="Save(ProjectSaveOptions?)"/>.</summary>
    public ProjectBatchSaveResult ApplyOperationsAndOverwrite(IReadOnlyList<ProjectOperation> operations,
        ProjectBatchOptions? options = null)
    {
        ValidateOperationArguments(operations);
        SavePreview initialPlan = PreviewSave();
        if (!initialPlan.CanSave)
        {
            ProjectBatchResult blocked = SaveBlockedBatch(initialPlan);
            return new(false, blocked, null, MapSaveFailure(initialPlan.BlockingDiagnostics.FirstOrDefault()),
                initialPlan.BlockingDiagnostics);
        }

        ProjectBatchResult batch = ApplyOperations(operations, options);
        if (!batch.Success) return new(false, batch, null, null, batch.Diagnostics);
        try
        {
            ProjectSaveResult saved = Save();
            return new(true, batch, saved, null, batch.Diagnostics.Concat(saved.Warnings).Distinct().ToArray());
        }
        catch (RecotteProjectSaveException exception)
        {
            ProjectDiagnostic failure = new("RC6203", DiagnosticSeverity.Error, exception.Message);
            return new(false, batch, null, exception.Kind,
                batch.Diagnostics.Concat(exception.Warnings).Append(failure).Distinct().ToArray());
        }
    }

    /// <summary>Previews edits and the mandatory backed-up overwrite plan without changing files.</summary>
    public ProjectBatchSavePreview PreviewOperationsAndOverwrite(IReadOnlyList<ProjectOperation> operations,
        ProjectBatchOptions? options = null)
    {
        ValidateOperationArguments(operations);
        ProjectBatchOptions effective = options ?? new();
        if (!ProjectCompatibility.CanEdit)
        {
            ProjectBatchPreview unsupported = PreviewOperations(operations, effective);
            return new(unsupported, null, unsupported.BatchResult.Diagnostics, false);
        }

        using ProjectEditSession edit = BeginEdit(effective.ObjectKeyAllocator, effective.DurationPolicy,
            effective.FileItemKeyAllocator);
        ProjectBatchResult batch = edit.ApplyOperations(operations);
        ProjectBatchPreview operationPreview = new(batch with { CanCommit = false, RolledBack = true },
            edit.Editor.GetProjectDuration(), batch.Success && ProjectCompatibility.CanOverwrite && batch.Validation.IsValid);
        if (!batch.Success) return new(operationPreview, null, batch.Diagnostics, false);

        RecotteProjectDocument snapshot = edit.CreateSnapshotDocument();
        SavePreview save = snapshot.PreviewSave();
        IReadOnlyList<ProjectDiagnostic> diagnostics = batch.Diagnostics.Concat(save.BlockingDiagnostics).Distinct().ToArray();
        return new(operationPreview, save, diagnostics, save.CanSave);
    }

    /// <summary>Previews edits against a detached document and applies the existing SaveCopy planner without writing.</summary>
    public ProjectBatchSavePreview PreviewOperationsAndSaveCopy(string destinationPath,
        IReadOnlyList<ProjectOperation> operations, ProjectBatchSaveOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateOperationArguments(operations);
        ProjectBatchSaveOptions effective = options ?? new();
        if (!ProjectCompatibility.CanEdit)
        {
            ProjectBatchPreview unsupported = PreviewOperations(operations, effective.BatchOptions);
            return new(unsupported, null, unsupported.BatchResult.Diagnostics, false);
        }

        using ProjectEditSession edit = BeginEdit(effective.BatchOptions.ObjectKeyAllocator,
            effective.BatchOptions.DurationPolicy, effective.BatchOptions.FileItemKeyAllocator);
        ProjectBatchResult batch = edit.ApplyOperations(operations);
        ProjectTime? duration = edit.Editor.GetProjectDuration();
        ProjectBatchPreview operationPreview = new(batch with { CanCommit = false, RolledBack = true }, duration,
            batch.Success && ProjectCompatibility.CanSaveCopy && batch.Validation.IsValid);
        if (!batch.Success) return new(operationPreview, null, batch.Diagnostics, false);

        RecotteProjectDocument snapshot = edit.CreateSnapshotDocument();
        SavePreview save = snapshot.PreviewSave(destinationPath, effective.SaveOptions);
        IReadOnlyList<ProjectDiagnostic> diagnostics = batch.Diagnostics.Concat(save.BlockingDiagnostics).Distinct().ToArray();
        return new(operationPreview, save, diagnostics, save.CanSave);
    }

    private ProjectBatchResult SaveBlockedBatch(SavePreview plan) => new(false, 0, null,
        Array.Empty<ProjectOperationResult>(), Array.Empty<ProjectChange>(), plan.BlockingDiagnostics,
        plan.Validation, false, false, true);

    private static ProjectSaveFailureKind? MapSaveFailure(ProjectDiagnostic? diagnostic) => diagnostic?.Code switch
    {
        "RC5101" => ProjectSaveFailureKind.UnsupportedVersion,
        "RC5102" => ProjectSaveFailureKind.EditInProgress,
        "RC5103" => ProjectSaveFailureKind.ValidationFailed,
        "RC5201" => ProjectSaveFailureKind.SourceChanged,
        null => null,
        _ => ProjectSaveFailureKind.InvalidPath,
    };
}
