namespace Recotte.Core;

/// <summary>Identifies an operation that a project format profile may permit.</summary>
[Flags]
public enum CompatibilityCapability
{
    /// <summary>No operation is permitted.</summary>
    None = 0,
    /// <summary>The JSON document may be loaded.</summary>
    Load = 1 << 0,
    /// <summary>The document may be structurally validated.</summary>
    Validate = 1 << 1,
    /// <summary>Existing supported objects may be edited.</summary>
    EditExistingObjects = 1 << 2,
    /// <summary>Supported objects may be added.</summary>
    AddObjects = 1 << 3,
    /// <summary>Supported objects may be removed.</summary>
    RemoveObjects = 1 << 4,
    /// <summary>A project copy may be saved.</summary>
    SaveCopy = 1 << 5,
    /// <summary>The source project may be overwritten with a backup.</summary>
    SaveOverwrite = 1 << 6,
}

/// <summary>Describes explicit, per-operation compatibility for a project.</summary>
public sealed record ProjectCompatibility
{
    /// <summary>Initializes a compatibility description.</summary>
    public ProjectCompatibility(CompatibilityLevel level, CompatibilityCapability capabilities, string reason, IReadOnlyList<ProjectDiagnostic>? diagnostics = null)
    {
        Level = level;
        Capabilities = capabilities;
        Reason = reason;
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ProjectDiagnostic>()).ToArray());
    }

    /// <summary>Gets the overall support level.</summary>
    public CompatibilityLevel Level { get; }
    /// <summary>Gets the explicitly allowed operations.</summary>
    public CompatibilityCapability Capabilities { get; }
    /// <summary>Gets a human-readable explanation.</summary>
    public string Reason { get; }
    /// <summary>Gets immutable compatibility diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
    /// <summary>Gets whether loading is supported.</summary>
    public bool CanRead => Has(CompatibilityCapability.Load);
    /// <summary>Gets whether validation is supported.</summary>
    public bool CanValidate => Has(CompatibilityCapability.Validate);
    /// <summary>Gets whether existing objects may be edited.</summary>
    public bool CanEdit => Has(CompatibilityCapability.EditExistingObjects);
    /// <summary>Gets whether objects may be added.</summary>
    public bool CanAddObjects => Has(CompatibilityCapability.AddObjects);
    /// <summary>Gets whether objects may be removed.</summary>
    public bool CanRemoveObjects => Has(CompatibilityCapability.RemoveObjects);
    /// <summary>Gets whether a copy may be saved.</summary>
    public bool CanSaveCopy => Has(CompatibilityCapability.SaveCopy);
    /// <summary>Gets whether the source may be overwritten.</summary>
    public bool CanOverwrite => Has(CompatibilityCapability.SaveOverwrite);

    private bool Has(CompatibilityCapability capability) => (Capabilities & capability) == capability;
}

/// <summary>Defines version-specific format capabilities without exposing the mutable JSON DOM.</summary>
public interface IProjectFormatProfile
{
    /// <summary>Gets the exact Recotte Studio version handled by the profile.</summary>
    RecotteVersion Version { get; }
    /// <summary>Gets the profile's operation capabilities.</summary>
    ProjectCompatibility Compatibility { get; }
    /// <summary>Returns additional version-specific diagnostics for a document.</summary>
    IReadOnlyList<ProjectDiagnostic> Validate(RecotteProjectDocument document);
    /// <summary>Gets whether supported Speaker Voice fields may be edited.</summary>
    bool CanEditSpeakerVoice { get; }
    /// <summary>Gets whether a text-only Speaker Voice may be cloned from a verified in-project template.</summary>
    bool CanAddTextOnlySpeakerVoice { get; }
}

/// <summary>Provides lookup of known project format profiles, accepting the Recotte Studio 1.8 compatibility family.</summary>
public sealed class ProjectFormatProfileRegistry
{
    private readonly IReadOnlyDictionary<RecotteVersion, IProjectFormatProfile> profiles;

    /// <summary>Gets the built-in registry.</summary>
    public static ProjectFormatProfileRegistry Default { get; } = new(new IProjectFormatProfile[]
    {
        new Recotte1850FormatProfile(),
        new Recotte1712FormatProfile(),
    });

    /// <summary>Creates a registry from exact-version profiles.</summary>
    /// <exception cref="ArgumentException">Thrown when two profiles target the same version.</exception>
    public ProjectFormatProfileRegistry(IEnumerable<IProjectFormatProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        this.profiles = profiles.ToDictionary(profile => profile.Version);
    }

    /// <summary>Finds an exact profile or accepts another four-part version in the 1.8 compatibility family.</summary>
    public bool TryGetProfile(RecotteVersion version, out IProjectFormatProfile? profile)
    {
        if (profiles.TryGetValue(version, out profile)) return true;
        if (version.Major == 1 && version.Minor == 8 && profiles.ContainsKey(RecotteVersion.SupportedVersion))
        {
            profile = new Recotte18CompatibleFormatProfile(version);
            return true;
        }
        return false;
    }

    internal IProjectFormatProfile Resolve(RecotteVersion? version) => version is RecotteVersion value && TryGetProfile(value, out IProjectFormatProfile? profile)
        ? profile!
        : new UnknownFormatProfile(version);
}

/// <summary>Implements the verified Recotte Studio 1.8.5.0 format profile.</summary>
public sealed class Recotte1850FormatProfile : IProjectFormatProfile
{
    /// <inheritdoc />
    public RecotteVersion Version => RecotteVersion.SupportedVersion;
    /// <inheritdoc />
    public ProjectCompatibility Compatibility { get; } = new(
        CompatibilityLevel.Supported,
        CompatibilityCapability.Load | CompatibilityCapability.Validate | CompatibilityCapability.EditExistingObjects |
        CompatibilityCapability.AddObjects | CompatibilityCapability.RemoveObjects | CompatibilityCapability.SaveCopy |
        CompatibilityCapability.SaveOverwrite,
        "Recotte Studio 1.8.5.0 is the verified format.");
    /// <inheritdoc />
    public bool CanEditSpeakerVoice => true;
    /// <inheritdoc />
    public bool CanAddTextOnlySpeakerVoice => true;
    /// <inheritdoc />
    public IReadOnlyList<ProjectDiagnostic> Validate(RecotteProjectDocument document) => Array.Empty<ProjectDiagnostic>();
}

internal sealed class Recotte1712FormatProfile : IProjectFormatProfile
{
    public RecotteVersion Version { get; } = new(1, 7, 1, 2);
    public ProjectCompatibility Compatibility { get; } = new(
        CompatibilityLevel.ReadOnly,
        CompatibilityCapability.Load | CompatibilityCapability.Validate,
        "Recotte Studio 1.7.1.2 can be inspected experimentally, but safe editing and saving are unverified.",
        new[] { new ProjectDiagnostic("RC3102", DiagnosticSeverity.Warning, "Recotte Studio 1.7.1.2 support is read-only and unverified.", "$.app_version") });
    public bool CanEditSpeakerVoice => false;
    public bool CanAddTextOnlySpeakerVoice => false;
    public IReadOnlyList<ProjectDiagnostic> Validate(RecotteProjectDocument document) => Compatibility.Diagnostics;
}

internal sealed class Recotte18CompatibleFormatProfile(RecotteVersion version) : IProjectFormatProfile
{
    public RecotteVersion Version { get; } = version;
    public ProjectCompatibility Compatibility { get; } = new(
        CompatibilityLevel.Supported,
        CompatibilityCapability.Load | CompatibilityCapability.Validate | CompatibilityCapability.EditExistingObjects |
        CompatibilityCapability.AddObjects | CompatibilityCapability.RemoveObjects | CompatibilityCapability.SaveCopy |
        CompatibilityCapability.SaveOverwrite,
        $"Recotte Studio {version} is accepted as part of the 1.8.x.x compatibility family.");
    public bool CanEditSpeakerVoice => true;
    public bool CanAddTextOnlySpeakerVoice => true;
    public IReadOnlyList<ProjectDiagnostic> Validate(RecotteProjectDocument document) => Array.Empty<ProjectDiagnostic>();
}

internal sealed class UnknownFormatProfile(RecotteVersion? version) : IProjectFormatProfile
{
    public RecotteVersion Version => version ?? default;
    public ProjectCompatibility Compatibility { get; } = new(
        CompatibilityLevel.Unsupported,
        CompatibilityCapability.Load | CompatibilityCapability.Validate,
        "No exact project format profile is registered for this version.",
        new[] { new ProjectDiagnostic("RC3101", DiagnosticSeverity.Warning, "The project version is unknown; editing and saving are disabled.", "$.app_version") });
    public bool CanEditSpeakerVoice => false;
    public bool CanAddTextOnlySpeakerVoice => false;
    public IReadOnlyList<ProjectDiagnostic> Validate(RecotteProjectDocument document) => Compatibility.Diagnostics;
}
