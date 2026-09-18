namespace Recotte.Core;

/// <summary>Specifies the severity of a project diagnostic.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational context that does not affect validity.</summary>
    Information,
    /// <summary>A condition callers should inspect.</summary>
    Warning,
    /// <summary>A condition that makes the operation invalid.</summary>
    Error,
}

/// <summary>Describes a structural, compatibility, or reference issue in a project.</summary>
public sealed record ProjectDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? JsonPath = null);

/// <summary>Contains the diagnostics produced while loading a project.</summary>
public sealed record ProjectLoadResult
{
    /// <summary>Initializes an immutable load result.</summary>
    public ProjectLoadResult(RecotteProjectDocument? document, IReadOnlyList<ProjectDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Gets the loaded document, if parsing succeeded.</summary>
    public RecotteProjectDocument? Document { get; }
    /// <summary>Gets immutable load diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
    /// <summary>Gets whether a document was loaded without errors.</summary>
    public bool Success => Document is not null && Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

/// <summary>Contains the diagnostics produced while validating a project.</summary>
public sealed record ProjectValidationResult
{
    /// <summary>Initializes an immutable validation result.</summary>
    public ProjectValidationResult(IReadOnlyList<ProjectDiagnostic> diagnostics) => Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    /// <summary>Gets immutable validation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
    /// <summary>Gets whether the project has no error diagnostics.</summary>
    public bool IsValid => Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

/// <summary>Identifies the degree of support available for a project version.</summary>
public enum CompatibilityLevel
{
    /// <summary>The format is verified for the explicitly reported capabilities.</summary>
    Supported,
    /// <summary>The format may be inspected but cannot safely be changed or saved.</summary>
    ReadOnly,
    /// <summary>The format has explicitly limited, not fully verified support.</summary>
    Experimental,
    /// <summary>The format has no safe editing or saving support.</summary>
    Unsupported,
}

/// <summary>Identifies a structured save failure category.</summary>
public enum ProjectSaveFailureKind
{
    /// <summary>The document is invalid.</summary>
    ValidationFailed,
    /// <summary>The format does not permit the requested save.</summary>
    UnsupportedVersion,
    /// <summary>An editing session is still active.</summary>
    EditInProgress,
    /// <summary>The source was changed outside this document.</summary>
    SourceChanged,
    /// <summary>The source path or destination is unsafe.</summary>
    InvalidPath,
    /// <summary>A backup could not be created.</summary>
    BackupFailed,
    /// <summary>The temporary file could not be written.</summary>
    TemporaryWriteFailed,
    /// <summary>The temporary file could not be loaded.</summary>
    VerificationLoadFailed,
    /// <summary>The reloaded temporary file failed validation.</summary>
    VerificationValidationFailed,
    /// <summary>The reloaded JSON was not semantically identical.</summary>
    RoundTripMismatch,
    /// <summary>The destination replacement failed.</summary>
    ReplaceFailed,
    /// <summary>Restoring from backup also failed.</summary>
    RestoreFailed,
}

/// <summary>Thrown when a safe save cannot be completed.</summary>
public sealed class RecotteProjectSaveException : IOException
{
    /// <summary>Initializes a structured save exception.</summary>
    public RecotteProjectSaveException(ProjectSaveFailureKind kind, string message, Exception? innerException = null, IReadOnlyList<ProjectDiagnostic>? warnings = null)
        : base(message, innerException)
    {
        Kind = kind;
        Warnings = Array.AsReadOnly((warnings ?? Array.Empty<ProjectDiagnostic>()).ToArray());
    }

    /// <summary>Gets the machine-readable failure category.</summary>
    public ProjectSaveFailureKind Kind { get; }
    /// <summary>Gets non-fatal cleanup warnings that accompanied the primary failure.</summary>
    public IReadOnlyList<ProjectDiagnostic> Warnings { get; }
}

/// <summary>Thrown when a project cannot be loaded.</summary>
public sealed class RecotteProjectLoadException : Exception
{
    /// <summary>Initializes an exception for invalid Recotte project input.</summary>
    public RecotteProjectLoadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when a built-in template cannot produce a structurally valid new project.</summary>
public sealed class RecotteProjectCreationException : Exception
{
    /// <summary>Initializes a creation exception with immutable validation diagnostics.</summary>
    public RecotteProjectCreationException(string message, IReadOnlyList<ProjectDiagnostic> diagnostics)
        : base(message) => Diagnostics = Array.AsReadOnly(diagnostics.ToArray());

    /// <summary>Gets the diagnostics that prevented creation.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
}
