# Recotte.Core

`Recotte.Core` is an unofficial .NET SDK for conservative access to Recotte Studio `.ccproj` JSON. It reads and validates projects, preserves unknown fields, offers transactional editing for verified Speaker Voice cases, saves verified copies, and overwrites a loaded source only with conflict detection and a backup. It has no console, UI, MCP, CLI, or GUI dependency; those belong in separate adapter layers.

It can also create a sanitized empty 1.8.5.0 project, register and place PNG/MP4 assets, add annotation-track text boxes from verified object templates, and import a character-only Speaker layer from a donor project that was previously saved by Recotte Studio.

## Compatibility

Capabilities come from an `IProjectFormatProfile`. Four-part versions in the `1.8.x.x` family use the same supported compatibility profile; other unknown versions never borrow it. The SDK performs no migration or `app_version` rewrite.

| Recotte Studio | Level | Load | Validate | Edit existing | Add text-only voice | SaveCopy | Backed-up Save |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1.8.5.0 | Supported (verified from repository samples) | Yes | Yes | Yes, constrained | Yes, template clone | Yes | Yes |
| Other 1.8.x.x | Supported (accepted as compatible) | Yes | Yes | Yes, constrained | Yes, template clone | Yes | Yes |
| 1.7.1.2 | ReadOnly (unverified) | Yes | Warning | No | No | No | No |
| Other/invalid | Unsupported | JSON permitting | Warning | No | No | No | No |

`document.ProjectCompatibility` exposes the level, reason, diagnostics, and explicit per-operation booleans. Loading an unsupported but parseable document is intentional so callers can inspect known read-only views and retained unknown data.

## Basic use

```csharp
RecotteProjectDocument document = RecotteProject.Load("Project.ccproj");
ProjectValidationResult validation = document.Validate();
foreach (ProjectDiagnostic diagnostic in validation.Diagnostics)
    Handle(diagnostic.Code, diagnostic.Severity, diagnostic.JsonPath);
```

Create an empty document without requiring a donor character project:

```csharp
RecotteProjectDocument empty = RecotteProject.Create(new ProjectCreationRequest("New Project", destinationDirectory));
empty.SaveCopy(Path.Combine(destinationDirectory, "New Project.ccproj"));
```

Creation fails before a document is returned when the embedded template or generated settings do not satisfy the
verified 1.8.5.0 creation contract. `ValidateCreatedProject` combines normal validation with the required version,
timestamp, GUID, project name, directory URI, canvas, initial layers, and style invariants. `SaveCopy` repeats these
creation checks after reloading its temporary file and publishes the destination only after they pass.

A donor project is required only when importing a specific character. It supplies the opaque speaker registration, actions, character object, and layer properties that cannot be reconstructed safely from an `.rsp` path alone. Speaker Voice and audio file items are never imported.

Paths passed to file APIs are made absolute before I/O. `Load(Stream)` and `TryLoad(Stream)` do not own, close, or dispose the supplied stream. A stream-loaded document has no `SourcePath`, so `Save()` is rejected.

Transactional editing isolates changes until complete validation succeeds:

```csharp
using ProjectEditSession edit = document.BeginEdit();
EditResult staged = edit.Editor.UpdateSpeakerText(
    new UpdateSpeakerTextRequest(new TimelineObjectId(1, 1000), "new text"));
if (staged.Success)
    edit.Commit();
```

Move an existing supported voice with `MoveTimelineObject`. Remove any unlocked timeline object with `RemoveTimelineObject`; when its file-item registration becomes unreferenced, the registration is pruned without deleting the asset file on disk. Add a voice with `AddTextOnlySpeakerVoice`; this deep-clones a verified text-only Speaker Voice from the same speaker layer and assigns the project-wide maximum `objkey` plus one. When the layer holds no voice yet, as in a newly created project, it falls back to the verified text-only voice embedded in this assembly. The embedded clone is offered only when the project's `text-styles`, `text-style-lib`, and `telop-frames` are semantically identical to the template's, because cloning it into a project with different definitions would leave dangling style and telop references that pass validation but break in Recotte Studio; a mismatch fails with `RC4405` instead of silently degrading. `Capabilities.CanAddTextOnlySpeakerVoice` reports whether a clone source is actually reachable, not only whether the version supports the operation. The allocation rule is provisional and injectable through `IObjectKeyAllocator`. Audio-backed voices, styled/fragmented text, locked content, and unsafe object types are rejected. Disposing without `Commit` rolls back.

```csharp
EditResult added = edit.Editor.AddTextOnlySpeakerVoice(new AddSpeakerTextRequest(
    new SpeakerLayerId(1), "new voice", new ProjectTime(2m), new ProjectTime(3m)));
```

Editor notes such as BGM, cut, image-change, and review suggestions can be placed on an `Annot` layer as text-box Figures. `AddAnnotationText` clones the object and its dedicated style from `010OneAnnotationText`; it registers the style only when absent and rejects a conflicting style definition.

```csharp
EditResult note = edit.Editor.AddAnnotationText(new AddAnnotationTextRequest(
    2, "編集メモ：ここからBGMを明るくする", new ProjectTime(5m), new ProjectTime(9m)));
```

## Atomic batch editing

The batch API is a general-purpose Core API for CLI, GUI, MCP, and other adapters; it contains no MCP SDK types or dependencies. Operations execute in their supplied order on one private edit-session clone. The first failure stops execution, restores the clone, marks the session rollback-only, and leaves the source document and its `Revision` unchanged. A non-empty document-level batch commits exactly once only after every operation and whole-project validation succeed; an empty batch validates successfully without incrementing `Revision`.

```csharp
ProjectOperation[] operations =
[
    new UpdateSpeakerTextOperation(
        new TimelineObjectIdReference(new TimelineObjectId(1, 1000)), "updated text"),
    new MoveTimelineObjectOperation(
        new ObjectKeyReference(1000), new ProjectTime(2m), new ProjectTime(4m)),
];

ProjectBatchResult batch = document.ApplyOperations(operations);
if (!batch.Success)
    Handle(batch.FailedOperationIndex, batch.Diagnostics);
```

`ProjectBatchResult` reports operation count, zero-based failed index, per-operation and aggregate changes, diagnostics, validation, generated object/file identifiers, commit eligibility, commit state, and rollback state. `ProjectEditSession.ApplyOperations` stages a batch for an explicit caller-controlled Commit; it is accepted only once on a fresh session.

```csharp
ProjectBatchPreview preview = document.PreviewOperations(operations);
if (preview.BatchResult.Success)
    Show(preview.BatchResult.Changes, preview.EstimatedDuration);
```

Preview never changes the document or creates a file and always releases its edit lock. For generated opaque FileItem keys, preview and execution guarantee the same outcome, ordering, operation/change kinds, counts, and validation—not the same randomly allocated key text.

## Safe lookup and inspection

References never claim a stable identifier that the verified format does not contain. A layer can be referenced by current index, unique name, or linked Speaker key. Timeline objects can use `(layerIndex, objectKey)` or a project-wide unique `objectKey`. Missing and duplicate matches return `NotFound` and `Ambiguous`; the first candidate is never selected implicitly.

```csharp
LookupResult<LayerView> layer = document.FindLayer(new LayerNameReference("朗読"));
LookupResult<TimelineObjectView> item = document.FindTimelineObject(new ObjectKeyReference(1000));
ProjectSummary summary = document.GetSummary();
IReadOnlyList<TimelineEntryView> timeline = document.GetTimelineEntries();
```

Query overloads filter layers, objects, Speakers, and FileItems by their safely exposed fields. Timeline entries are ordered by start time, layer index, then in-layer index. Time containment and overlap use half-open `[start, end)` ranges. `FindEmptyRanges` reports gaps in one uniquely resolved layer. `document.Capabilities` gives granular operation availability; unsupported audio generation and general character creation are explicitly false.

## Consecutive text and batch SaveCopy

`AddTextSequence` converts text items to text-only Speaker Voice operations and applies them atomically. Each item may override `DefaultDuration`; `Gap` is added before the next item.

```csharp
AddTextSequenceResult sequence = document.AddTextSequence(new AddTextSequenceRequest(
    new LayerNameReference("朗読"),
    [new("春はあけぼの。", new ProjectTime(3m)), new("やうやう白くなりゆく山ぎは、")],
    new ProjectTime(0m), new ProjectTime(0.25m), new ProjectTime(4m)));
```

For a single edit-through-save call, Core first checks the destination, applies and validates the batch, commits it, then calls the existing verified `SaveCopy` path. Edit or validation failure creates no output. Existing destinations still require explicit `ProjectSaveOptions.AllowOverwrite`.

```csharp
ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(
    "Makuranosoushi.ccproj",
    [
        new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("朗読"),
            "春はあけぼの。", new ProjectTime(0m), new ProjectTime(3m)),
        new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("朗読"),
            "やうやう白くなりゆく山ぎは、", new ProjectTime(3m), new ProjectTime(7m)),
    ]);
```

`PreviewOperationsAndSaveCopy` evaluates the edited snapshot with the same save planner without Commit or file creation. If saving fails after a successful batch Commit, `ProjectBatchSaveResult` retains both the committed batch and `ProjectSaveFailureKind`; the loaded source file is never overwritten by this API.

## Safe saving

`SaveCopy` cannot target the loaded source. Existing destinations require `AllowOverwrite`. Both Save paths validate before writing, write UTF-8 without BOM using CRLF, reload the same-directory random temporary file, run full validation on the reloaded document, and compare all JSON semantically (object key order, formatting, line endings, and equivalent number notation are ignored; types, arrays, ordering, known data, and unknown data must match).

```csharp
ProjectSaveResult copy = document.SaveCopy("Project-copy.ccproj");
ProjectSaveResult replaced = document.Save(); // source path load only
```

`Save()` requires backup creation and unchanged-source checking. At load, `SourceFileState` records the absolute path, byte length, UTC write time, and SHA-256. Save checks all fields, so an equal-length/equal-time content change is still rejected. The SHA-256 is internal safety metadata and is never used as a Recotte `file-items[].ik`. The backup uses `Project.ccproj.yyyyMMdd-HHmmss-fff.bak`, never overwrites an existing backup, and is retained if replacement fails.

`ProjectSaveResult` contains absolute destination/backup paths, before/after validation, revision, UTC time, warnings, and external-change status. `RecotteProjectSaveException.Kind` distinguishes validation, compatibility, edit, conflict, backup, temporary write/reload/validation, semantic mismatch, replacement, and restoration failures. Cleanup failures are warnings rather than the primary failure.

Use Preview before either operation without modifying files:

```csharp
SavePreview copyPreview = document.PreviewSave("Project-copy.ccproj");
SavePreview overwritePreview = document.PreviewSave();
if (!overwritePreview.CanSave)
    foreach (ProjectDiagnostic blocker in overwritePreview.BlockingDiagnostics) Handle(blocker.Code);
```

Preview and Save share the same planning logic, but another process can change filesystem state after Preview; Save always repeats safety checks.

## Safety and limitations

All public views and diagnostic collections are immutable snapshots; mutable `JsonNode`/`JsonObject` instances are not exposed. Unknown JSON fields and ordering-sensitive arrays survive load, edit, and save. File path equality follows OS case rules and resolves existing final symbolic links where the platform supports it. It cannot provide a universal cross-platform guarantee against every hard-link, mount, network-filesystem, or time-of-check/time-of-use alias.

`RecotteProjectDocument`, `ProjectEditSession`, and their views are not thread-safe. Do not concurrently use one instance. The SDK enforces one active edit session and refuses Save/SaveCopy while it is active; it does not claim general thread safety through partial locking.

Asset registration supports only PNG images and MP4 videos. When a PNG image is added, its `CropBounds` uses the original image width and height. File-item keys are opaque random 256-bit identifiers, not content hashes; this rule remains provisional until verified by opening and re-saving generated projects in Recotte Studio. Not supported: audio voice generation, general character creation without a verified donor, `voice-hash` generation, arbitrary file types, `.rsp` parsing, keyframe authoring, `additional-actions`, version migration, or forced unsupported-version saves. The SDK does not infer unknown Recotte Studio structures. The existing donor-based character import remains a separate constrained API and is not a batch operation.

Diagnostic codes are the machine-readable contract; do not parse message text. See [Diagnostics](../../Documents/RecotteCore/Diagnostics.md).

## Recotte Studio manual verification

Recotte Studio was not run in the automated implementation environment. On a machine with 1.8.5.0: edit text/time and add a text-only voice, SaveCopy and open it, verify display/playback, Save As from Studio and reopen, validate that file with this SDK, then repeat with backed-up `Save()` and open both the source and backup. Record the installed Studio version and outcomes; do not treat automated JSON tests as application-level verification.

## Packaging and API change

The project packs as prerelease-stage `Recotte.Core` version `0.5.0` with symbols, this README, and verified project templates embedded in the assembly. Tests, standalone samples, media, models, and backups are outside the package. Version 0.5 adds atomic batch editing, preview, structured lookup and inspection, consecutive text placement, and edit-through-SaveCopy without changing the existing SaveCopy call shape.
