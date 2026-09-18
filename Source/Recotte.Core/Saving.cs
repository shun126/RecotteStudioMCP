using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>Captures the identity-relevant state of a source project file.</summary>
public sealed record SourceFileState
{
    /// <summary>Initializes an immutable source file state.</summary>
    public SourceFileState(string absolutePath, long length, DateTime lastWriteTimeUtc, string sha256)
    {
        AbsolutePath = absolutePath;
        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Sha256 = sha256;
    }

    /// <summary>Gets the absolute source path.</summary>
    public string AbsolutePath { get; }
    /// <summary>Gets the file size in bytes.</summary>
    public long Length { get; }
    /// <summary>Gets the UTC last-write timestamp.</summary>
    public DateTime LastWriteTimeUtc { get; }
    /// <summary>Gets the uppercase hexadecimal SHA-256 content hash.</summary>
    public string Sha256 { get; }

    internal static SourceFileState Capture(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        FileInfo info = new(fullPath);
        info.Refresh();
        return new(fullPath, info.Length, info.LastWriteTimeUtc, hash);
    }
}

/// <summary>Controls safe copy and backed-up source saving.</summary>
public sealed record ProjectSaveOptions
{
    /// <summary>Gets whether overwrite Save creates a backup. The initial SDK requires this to remain true.</summary>
    public bool CreateBackup { get; init; } = true;
    /// <summary>Gets an optional backup path. Relative paths are resolved before I/O.</summary>
    public string? BackupPath { get; init; }
    /// <summary>Gets whether validation errors prevent saving.</summary>
    public bool RequireValidProject { get; init; } = true;
    /// <summary>Gets whether SaveCopy may replace an existing destination.</summary>
    public bool AllowOverwrite { get; init; }
    /// <summary>Gets whether source changes prevent overwrite Save. The initial SDK requires this to remain true.</summary>
    public bool RequireUnchangedSource { get; init; } = true;
}

/// <summary>Describes a backup created before source replacement.</summary>
public sealed record BackupResult(string AbsolutePath, DateTime CreatedAtUtc);

/// <summary>Describes the non-mutating safety analysis for a save operation.</summary>
public sealed record SavePreview
{
    internal SavePreview(string destinationPath, bool destinationExists, bool sameAsSource, bool overwriteRequired,
        ProjectCompatibility compatibility, bool editInProgress, bool externalChangeDetected, string? backupPath,
        ProjectValidationResult validation, IReadOnlyList<ProjectDiagnostic> blockers)
    {
        DestinationPath = destinationPath;
        DestinationExists = destinationExists;
        IsSourceDestination = sameAsSource;
        OverwriteRequired = overwriteRequired;
        Compatibility = compatibility;
        EditInProgress = editInProgress;
        ExternalChangeDetected = externalChangeDetected;
        BackupPath = backupPath;
        Validation = validation;
        BlockingDiagnostics = Array.AsReadOnly(blockers.ToArray());
    }

    /// <summary>Gets the absolute destination path.</summary>
    public string DestinationPath { get; }
    /// <summary>Gets whether the destination currently exists.</summary>
    public bool DestinationExists { get; }
    /// <summary>Gets whether the destination denotes the loaded source.</summary>
    public bool IsSourceDestination { get; }
    /// <summary>Gets whether an existing destination must be replaced.</summary>
    public bool OverwriteRequired { get; }
    /// <summary>Gets the resolved format compatibility.</summary>
    public ProjectCompatibility Compatibility { get; }
    /// <summary>Gets whether an edit session prevents saving.</summary>
    public bool EditInProgress { get; }
    /// <summary>Gets whether the loaded source has externally changed.</summary>
    public bool ExternalChangeDetected { get; }
    /// <summary>Gets the planned absolute backup path for overwrite Save.</summary>
    public string? BackupPath { get; }
    /// <summary>Gets pre-save validation.</summary>
    public ProjectValidationResult Validation { get; }
    /// <summary>Gets immutable reasons that currently prevent saving.</summary>
    public IReadOnlyList<ProjectDiagnostic> BlockingDiagnostics { get; }
    /// <summary>Gets whether the analyzed operation can run.</summary>
    public bool CanSave => BlockingDiagnostics.Count == 0;
}

/// <summary>Describes a successfully persisted project.</summary>
public sealed record ProjectSaveResult
{
    internal ProjectSaveResult(string destinationPath, string? backupPath, ProjectValidationResult before,
        ProjectValidationResult after, long revision, DateTime savedAtUtc, IReadOnlyList<ProjectDiagnostic> warnings,
        bool externalChangeDetected)
    {
        DestinationPath = destinationPath;
        BackupPath = backupPath;
        ValidationBeforeSave = before;
        ValidationAfterSave = after;
        Revision = revision;
        SavedAtUtc = savedAtUtc;
        Warnings = Array.AsReadOnly(warnings.ToArray());
        ExternalChangeDetected = externalChangeDetected;
    }

    /// <summary>Gets the absolute saved path.</summary>
    public string DestinationPath { get; }
    /// <summary>Gets the absolute backup path, if an overwrite Save was used.</summary>
    public string? BackupPath { get; }
    /// <summary>Gets validation performed before writing.</summary>
    public ProjectValidationResult ValidationBeforeSave { get; }
    /// <summary>Gets validation performed after reloading the temporary file.</summary>
    public ProjectValidationResult ValidationAfterSave { get; }
    /// <summary>Gets the saved document revision.</summary>
    public long Revision { get; }
    /// <summary>Gets the UTC save time.</summary>
    public DateTime SavedAtUtc { get; }
    /// <summary>Gets non-fatal cleanup and compatibility warnings.</summary>
    public IReadOnlyList<ProjectDiagnostic> Warnings { get; }
    /// <summary>Gets whether an external source change was detected (always false for success).</summary>
    public bool ExternalChangeDetected { get; }
    /// <summary>Gets all successful-save diagnostics for backward-compatible inspection.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => ValidationBeforeSave.Diagnostics;
}

internal static class SemanticJsonComparer
{
    internal static bool Equals(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left is JsonObject lo && right is JsonObject ro)
        {
            if (lo.Count != ro.Count) return false;
            foreach ((string key, JsonNode? value) in lo)
                if (!ro.TryGetPropertyValue(key, out JsonNode? other) || !Equals(value, other)) return false;
            return true;
        }
        if (left is JsonArray la && right is JsonArray ra)
        {
            if (la.Count != ra.Count) return false;
            for (int i = 0; i < la.Count; i++) if (!Equals(la[i], ra[i])) return false;
            return true;
        }
        if (left is not JsonValue || right is not JsonValue) return false;
        using JsonDocument ld = JsonDocument.Parse(left.ToJsonString());
        using JsonDocument rd = JsonDocument.Parse(right.ToJsonString());
        JsonElement le = ld.RootElement;
        JsonElement re = rd.RootElement;
        if (le.ValueKind == JsonValueKind.Number && re.ValueKind == JsonValueKind.Number)
        {
            if (le.TryGetDecimal(out decimal lm) && re.TryGetDecimal(out decimal rm)) return lm == rm;
            return le.GetDouble().Equals(re.GetDouble());
        }
        if (le.ValueKind != re.ValueKind) return false;
        return le.ValueKind switch
        {
            JsonValueKind.String => le.GetString() == re.GetString(),
            JsonValueKind.True or JsonValueKind.False => le.GetBoolean() == re.GetBoolean(),
            JsonValueKind.Null => true,
            _ => le.GetRawText() == re.GetRawText(),
        };
    }
}
