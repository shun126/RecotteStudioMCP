using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>
/// Represents a Recotte Studio project while preserving unknown JSON. Instances are not thread-safe and must not be
/// used concurrently; only one edit session may be active.
/// </summary>
public sealed partial class RecotteProjectDocument
{
    private readonly JsonObject root;
    private readonly IProjectFormatProfile profile;
    private readonly ProjectCreationRequest? creationRequest;
    private bool editInProgress;

    internal RecotteProjectDocument(JsonObject root, string? sourcePath, SourceFileState? sourceFileState,
        ProjectCreationRequest? creationRequest)
    {
        this.root = root;
        SourcePath = sourcePath;
        SourceFileState = sourceFileState;
        this.creationRequest = creationRequest;
        profile = ProjectFormatProfileRegistry.Default.Resolve(ApplicationVersion);
        LoadDiagnostics = Array.AsReadOnly(CreateLoadDiagnostics().ToArray());
    }

    /// <summary>Gets the absolute source path, or null when loaded from a stream.</summary>
    public string? SourcePath { get; }
    /// <summary>Gets the source state captured at load or after the latest successful overwrite Save.</summary>
    public SourceFileState? SourceFileState { get; private set; }
    /// <summary>Gets the unmodified app_version text.</summary>
    public string? ApplicationVersionText => JsonAccess.TryGetString(root, "app_version", out string value) ? value : null;
    /// <summary>Gets the parsed four-part application version.</summary>
    public RecotteVersion? ApplicationVersion => RecotteVersion.TryParse(ApplicationVersionText, out RecotteVersion value) ? value : null;
    /// <summary>Gets the complete version compatibility description.</summary>
    public ProjectCompatibility ProjectCompatibility => profile.Compatibility;
    /// <summary>Gets the overall compatibility level.</summary>
    public CompatibilityLevel Compatibility => profile.Compatibility.Level;
    /// <summary>Gets project settings without exposing mutable JSON.</summary>
    public ProjectSettingsView? Settings => root["setting"] is JsonObject settings ? new ProjectSettingsView(settings) : null;
    /// <summary>Gets an immutable snapshot of layers.</summary>
    public IReadOnlyList<LayerView> Layers => Array.AsReadOnly((root["layers"] is JsonArray layers
        ? layers.Select((item, index) => item is JsonObject jsonObject ? LayerView.Create(jsonObject, index) : null).OfType<LayerView>().ToArray()
        : Array.Empty<LayerView>()));
    /// <summary>Gets an immutable snapshot of registered file items.</summary>
    public IReadOnlyList<FileItemView> FileItems => Array.AsReadOnly((root["file-items"] is JsonArray items
        ? items.Select((item, index) => item is JsonObject jsonObject ? new FileItemView(jsonObject, index) : null).OfType<FileItemView>().ToArray()
        : Array.Empty<FileItemView>()));
    /// <summary>Gets an immutable snapshot of speakers.</summary>
    public IReadOnlyList<SpeakerView> Speakers => Array.AsReadOnly((root["speakers"] is JsonArray speakers
        ? speakers.Select((item, index) => item is JsonObject jsonObject ? new SpeakerView(jsonObject, index) : null).OfType<SpeakerView>().ToArray()
        : Array.Empty<SpeakerView>()));
    /// <summary>Gets immutable diagnostics produced while loading.</summary>
    public IReadOnlyList<ProjectDiagnostic> LoadDiagnostics { get; }
    /// <summary>Gets the revision, incremented once for each successful edit transaction.</summary>
    public long Revision { get; private set; }

    /// <summary>Starts an isolated editing transaction. Concurrent use of this document is unsupported.</summary>
    /// <exception cref="InvalidOperationException">The profile disallows editing or another session is active.</exception>
    public ProjectEditSession BeginEdit(IObjectKeyAllocator? objectKeyAllocator = null, IProjectDurationPolicy? durationPolicy = null,
        IFileItemKeyAllocator? fileItemKeyAllocator = null)
    {
        if (!profile.Compatibility.CanEdit)
            throw new InvalidOperationException($"Editing is not supported: {profile.Compatibility.Reason}");
        if (editInProgress)
            throw new InvalidOperationException("An edit session is already active for this document.");
        editInProgress = true;
        return new ProjectEditSession(this, (JsonObject)root.DeepClone(), objectKeyAllocator, durationPolicy, fileItemKeyAllocator);
    }

    /// <summary>Validates general and exact-version-specific project structure.</summary>
    public ProjectValidationResult Validate()
    {
        List<ProjectDiagnostic> extra = new(LoadDiagnostics);
        extra.AddRange(profile.Validate(this));
        return ProjectValidator.Validate(root, Deduplicate(extra));
    }

    /// <summary>Validates both the general format and built-in-template invariants for a newly created project.</summary>
    public ProjectValidationResult ValidateCreatedProject()
    {
        ProjectValidationResult standard = Validate();
        if (creationRequest is null) return standard;
        return new ProjectValidationResult(Deduplicate(standard.Diagnostics.Concat(
            ProjectCreationValidator.Validate(root, creationRequest, false))));
    }

    /// <summary>Previews SaveCopy without writing any file. The destination is converted to an absolute path.</summary>
    public SavePreview PreviewSave(string destinationPath, ProjectSaveOptions? options = null) => BuildSavePlan(destinationPath, false, options ?? new());

    /// <summary>Previews a backed-up overwrite Save without changing the source.</summary>
    /// <exception cref="InvalidOperationException">The document was loaded from a stream.</exception>
    public SavePreview PreviewSave(ProjectSaveOptions? options = null)
    {
        if (SourcePath is null) throw new InvalidOperationException("Overwrite Save requires a document loaded from a file path.");
        return BuildSavePlan(SourcePath, true, options ?? new());
    }

    /// <summary>Saves a verified copy through a same-directory temporary file.</summary>
    /// <exception cref="RecotteProjectSaveException">A safety check or verified write fails.</exception>
    public ProjectSaveResult SaveCopy(string destinationPath, ProjectSaveOptions? options = null)
        => ExecuteSave(BuildSavePlan(destinationPath, false, options ?? new()), false, options ?? new());

    /// <summary>Overwrites the loaded source only after unchanged-source detection and a mandatory backup.</summary>
    /// <exception cref="InvalidOperationException">The document was loaded from a stream.</exception>
    /// <exception cref="RecotteProjectSaveException">A safety check, backup, verification, or replacement fails.</exception>
    public ProjectSaveResult Save(ProjectSaveOptions? options = null)
    {
        if (SourcePath is null) throw new InvalidOperationException("Overwrite Save requires a document loaded from a file path.");
        ProjectSaveOptions effective = options ?? new();
        return ExecuteSave(BuildSavePlan(SourcePath, true, effective), true, effective);
    }

    private SavePreview BuildSavePlan(string destinationPath, bool overwriteSource, ProjectSaveOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string fullDestination = Path.GetFullPath(destinationPath);
        bool exists = File.Exists(fullDestination);
        bool same = SourcePath is not null && PathsDenoteSameFile(SourcePath, fullDestination);
        ProjectValidationResult validation = ValidateCreatedProject();
        bool externalChange = overwriteSource && HasExternalSourceChange();
        List<ProjectDiagnostic> blockers = new();
        bool capability = overwriteSource ? profile.Compatibility.CanOverwrite : profile.Compatibility.CanSaveCopy;
        if (!capability) blockers.Add(Block("RC5101", profile.Compatibility.Reason));
        if (editInProgress) blockers.Add(Block("RC5102", "An edit session must be committed or disposed before saving."));
        if (options.RequireValidProject && !validation.IsValid) blockers.Add(Block("RC5103", "The project has validation errors."));
        if (!overwriteSource && same) blockers.Add(Block("RC5104", "SaveCopy cannot overwrite the source project."));
        if (!overwriteSource && exists && !options.AllowOverwrite) blockers.Add(Block("RC5105", "The destination exists and overwrite was not allowed."));
        if (overwriteSource && (!options.CreateBackup || !options.RequireUnchangedSource)) blockers.Add(Block("RC5106", "Overwrite Save requires both backup creation and unchanged-source detection."));
        if (overwriteSource && externalChange) blockers.Add(Block("RC5201", "The source file changed after it was loaded."));
        string? directory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) blockers.Add(Block("RC5107", "The destination directory does not exist."));
        string? backup = overwriteSource ? GetBackupPath(fullDestination, options.BackupPath) : null;
        if (backup is not null && PathsDenoteSameFile(fullDestination, backup)) blockers.Add(Block("RC5301", "The backup path must differ from the source."));
        if (backup is not null && !string.Equals(Path.GetDirectoryName(backup), directory, PathComparison)) blockers.Add(Block("RC5302", "The backup must be created beside the source."));
        if (backup is not null && File.Exists(backup)) blockers.Add(Block("RC5303", "The backup path already exists and will not be overwritten."));
        return new(fullDestination, exists, same, exists, profile.Compatibility, editInProgress, externalChange, backup, validation, blockers);
    }

    private ProjectSaveResult ExecuteSave(SavePreview plan, bool overwriteSource, ProjectSaveOptions options)
    {
        if (!plan.CanSave) ThrowFirstBlocker(plan);
        // Close the preview/use race as far as possible without exposing an unsafe bypass.
        ProjectSaveOptions recheckOptions = overwriteSource ? options with { BackupPath = plan.BackupPath } : options;
        SavePreview current = BuildSavePlan(plan.DestinationPath, overwriteSource, recheckOptions);
        if (!current.CanSave) ThrowFirstBlocker(current);
        string directory = Path.GetDirectoryName(current.DestinationPath)!;
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(current.DestinationPath)}.{Guid.NewGuid():N}.tmp");
        string? backupPath = null;
        ProjectValidationResult after;
        try
        {
            if (overwriteSource)
            {
                backupPath = current.BackupPath!;
                try { File.Copy(current.DestinationPath, backupPath, false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { throw new RecotteProjectSaveException(ProjectSaveFailureKind.BackupFailed, "The mandatory backup could not be created.", ex); }
            }
            try { WriteJson(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { throw new RecotteProjectSaveException(ProjectSaveFailureKind.TemporaryWriteFailed, "The temporary project could not be written.", ex); }

            ProjectLoadResult loaded = RecotteProject.TryLoad(temporaryPath);
            if (!loaded.Success || loaded.Document is null)
                throw new RecotteProjectSaveException(ProjectSaveFailureKind.VerificationLoadFailed, "The temporary project could not be reloaded.");
            after = creationRequest is null ? loaded.Document.Validate() : new ProjectValidationResult(Deduplicate(
                loaded.Document.Validate().Diagnostics.Concat(ProjectCreationValidator.Validate(loaded.Document.root, creationRequest, false))));
            if (!after.IsValid)
                throw new RecotteProjectSaveException(ProjectSaveFailureKind.VerificationValidationFailed, "The reloaded temporary project failed validation.");
            if (!SemanticJsonComparer.Equals(root, loaded.Document.root))
                throw new RecotteProjectSaveException(ProjectSaveFailureKind.RoundTripMismatch, "The saved JSON was not semantically identical to the document.");

            try { File.Move(temporaryPath, current.DestinationPath, !overwriteSource && options.AllowOverwrite || overwriteSource); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (overwriteSource && !File.Exists(current.DestinationPath) && backupPath is not null)
                {
                    try { File.Copy(backupPath, current.DestinationPath, false); }
                    catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                    { throw new RecotteProjectSaveException(ProjectSaveFailureKind.RestoreFailed, "Replacement and backup restoration both failed.", new AggregateException(ex, restore)); }
                }
                throw new RecotteProjectSaveException(ProjectSaveFailureKind.ReplaceFailed, "The destination could not be replaced; the backup was retained.", ex);
            }

            DateTime now = DateTime.UtcNow;
            if (overwriteSource) SourceFileState = SourceFileState.Capture(current.DestinationPath);
            return new(current.DestinationPath, backupPath, current.Validation, after, Revision, now, Array.Empty<ProjectDiagnostic>(), false);
        }
        catch (RecotteProjectSaveException failure)
        {
            ProjectDiagnostic? cleanupWarning = TryDeleteTemporary(temporaryPath);
            if (cleanupWarning is null) throw;
            throw new RecotteProjectSaveException(failure.Kind, failure.Message, failure, new[] { cleanupWarning });
        }
    }

    private bool HasExternalSourceChange()
    {
        if (SourcePath is null || SourceFileState is null || !File.Exists(SourcePath)) return true;
        try { return SourceFileState.Capture(SourcePath) != SourceFileState; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static string GetBackupPath(string sourcePath, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);
        string directory = Path.GetDirectoryName(sourcePath)!;
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        for (int suffix = 0; ; suffix++)
        {
            string name = $"{Path.GetFileName(sourcePath)}.{stamp}{(suffix == 0 ? "" : $"-{suffix}")}.bak";
            string candidate = Path.Combine(directory, name);
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static bool PathsDenoteSameFile(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string l = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        string r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        if (l.Equals(r, comparison)) return true;
        try
        {
            FileSystemInfo? lt = File.Exists(l) ? new FileInfo(l).ResolveLinkTarget(true) : null;
            FileSystemInfo? rt = File.Exists(r) ? new FileInfo(r).ResolveLinkTarget(true) : null;
            return lt is not null && rt is not null && Path.GetFullPath(lt.FullName).Equals(Path.GetFullPath(rt.FullName), comparison);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static ProjectDiagnostic? TryDeleteTemporary(string temporaryPath)
    {
        if (!File.Exists(temporaryPath)) return null;
        try { File.Delete(temporaryPath); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new ProjectDiagnostic("RC5401", DiagnosticSeverity.Warning, $"Temporary file cleanup failed: {ex.Message}"); }
    }

    private static void ThrowFirstBlocker(SavePreview plan)
    {
        ProjectDiagnostic first = plan.BlockingDiagnostics[0];
        ProjectSaveFailureKind kind = first.Code switch
        {
            "RC5101" => ProjectSaveFailureKind.UnsupportedVersion,
            "RC5102" => ProjectSaveFailureKind.EditInProgress,
            "RC5103" => ProjectSaveFailureKind.ValidationFailed,
            "RC5201" => ProjectSaveFailureKind.SourceChanged,
            _ => ProjectSaveFailureKind.InvalidPath,
        };
        throw new RecotteProjectSaveException(kind, first.Message);
    }

    private IReadOnlyList<ProjectDiagnostic> CreateLoadDiagnostics()
    {
        List<ProjectDiagnostic> diagnostics = new();
        if (!JsonAccess.TryGetString(root, "app_version", out string versionText))
            diagnostics.Add(new("RC1101", DiagnosticSeverity.Error, "app_version must be a string.", "$.app_version"));
        else if (!RecotteVersion.TryParse(versionText, out _))
            diagnostics.Add(new("RC1102", DiagnosticSeverity.Warning, $"The application version '{versionText}' is not recognized.", "$.app_version"));
        diagnostics.AddRange(profile.Compatibility.Diagnostics);
        return Deduplicate(diagnostics);
    }

    private static IReadOnlyList<ProjectDiagnostic> Deduplicate(IEnumerable<ProjectDiagnostic> diagnostics) => diagnostics
        .DistinctBy(item => (item.Code, item.Severity, item.Message, item.JsonPath)).ToArray();
    private static ProjectDiagnostic Block(string code, string message) => new(code, DiagnosticSeverity.Error, message);

    private void WriteJson(string path)
    {
        JsonSerializerOptions serializerOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
        string json = root.ToJsonString(serializerOptions).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
    }

    internal void CommitEdit(JsonObject editedRoot)
    {
        root.Clear();
        foreach ((string key, JsonNode? value) in editedRoot.ToArray()) root[key] = value?.DeepClone();
        capabilities = null;
        Revision++;
    }

    internal void EndEdit() => editInProgress = false;

    internal JsonObject CloneRoot() => (JsonObject)root.DeepClone();

    internal ProjectCreationRequest? CreationRequest => creationRequest;
}
