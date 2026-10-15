#pragma warning disable CS1591 // Public record members are documented by their containing API types and README.

namespace RecotteStudio.Core;

/// <summary>Identifies the directory a file item's relative path is based on.</summary>
public enum AssetLocationKind
{
    /// <summary>loc 0: relative to the project file's directory.</summary>
    Project,
    /// <summary>loc 1: relative to the Recotte Studio figures directory.</summary>
    Figures,
    /// <summary>A loc value whose base directory has not been verified; the relative path is not resolved.</summary>
    Unknown,
}

/// <summary>Summarizes whether a registered file can be found on disk.</summary>
public enum AssetReferenceStatus
{
    /// <summary>At least one of the relative or absolute paths names an existing file.</summary>
    Found,
    /// <summary>Every stored path was resolved and none names an existing file.</summary>
    Missing,
    /// <summary>No resolved path exists, and at least one stored path could not be resolved.</summary>
    Unresolved,
}

/// <summary>Identifies a timeline object that references a file item through its File property.</summary>
public sealed record AssetReferencingObject(int LayerIndex, int ObjectIndex, int? ObjectKey, string? ObjectType, string? Name,
    string FileItemKey);

/// <summary>
/// Describes one registered file item and where its stored paths resolve. ResolvedRelativePath is built from RelativePath and
/// the base directory when both are known; ResolvedAbsolutePath is the local path of AbsolutePath when it is a file URI.
/// </summary>
public sealed record AssetReference(
    int Index,
    string? Key,
    int? Location,
    AssetLocationKind LocationKind,
    string? RelativePath,
    string? AbsolutePath,
    string? ResolvedRelativePath,
    bool RelativePathExists,
    string? ResolvedAbsolutePath,
    bool AbsolutePathExists,
    AssetReferenceStatus Status,
    IReadOnlyList<AssetReferencingObject> ReferencedBy);

/// <summary>Contains every registered file item and every object whose File key is not registered.</summary>
public sealed record AssetInspection(IReadOnlyList<AssetReference> Assets, IReadOnlyList<AssetReferencingObject> DanglingReferences);

public sealed partial class RecotteProjectDocument
{
    /// <summary>
    /// Resolves the stored paths of every registered file item without changing the project. Only verified loc values are
    /// resolved: 0 against the project directory and 1 against <paramref name="figuresDirectory"/>. Other relative paths, and
    /// project-relative paths of a document that has no source path, are reported unresolved rather than guessed.
    /// </summary>
    /// <param name="figuresDirectory">The Recotte Studio figures directory, or null to leave loc 1 relative paths unresolved.</param>
    public AssetInspection InspectAssets(string? figuresDirectory = null)
    {
        AssetReferencingObject[] referencing = Layers.SelectMany(layer => layer.Objects)
            .Where(item => !string.IsNullOrEmpty(item.FileItemKey))
            .Select(item => new AssetReferencingObject(item.LayerIndex, item.Index, item.ObjectKey, item.Type, item.Name, item.FileItemKey!))
            .ToArray();
        string? projectDirectory = SourcePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(SourcePath));
        AssetReference[] assets = FileItems.Select(item => Resolve(item, projectDirectory, figuresDirectory,
            referencing.Where(reference => item.Key is not null && IsSameFileKey(reference.FileItemKey, item.Key)).ToArray())).ToArray();
        AssetReferencingObject[] dangling = referencing.Where(reference =>
            !assets.Any(asset => asset.Key is not null && IsSameFileKey(reference.FileItemKey, asset.Key))).ToArray();
        return new(Array.AsReadOnly(assets), Array.AsReadOnly(dangling));
    }

    private static AssetReference Resolve(FileItemView item, string? projectDirectory, string? figuresDirectory,
        IReadOnlyList<AssetReferencingObject> referencedBy)
    {
        AssetLocationKind kind = item.Location switch { 0 => AssetLocationKind.Project, 1 => AssetLocationKind.Figures, _ => AssetLocationKind.Unknown };
        string? baseDirectory = kind switch
        {
            AssetLocationKind.Project => projectDirectory,
            AssetLocationKind.Figures => figuresDirectory,
            _ => null,
        };
        string? relative = item.RelativePath is not null && baseDirectory is not null ? TryCombine(baseDirectory, item.RelativePath) : null;
        string? absolute = item.AbsolutePath is not null && Uri.TryCreate(item.AbsolutePath, UriKind.Absolute, out Uri? uri) && uri.IsFile
            ? uri.LocalPath : null;
        bool relativeExists = relative is not null && File.Exists(relative);
        bool absoluteExists = absolute is not null && File.Exists(absolute);
        bool unresolved = (item.RelativePath is not null && relative is null) || (item.AbsolutePath is not null && absolute is null) ||
            (item.RelativePath is null && item.AbsolutePath is null);
        AssetReferenceStatus status = relativeExists || absoluteExists ? AssetReferenceStatus.Found
            : unresolved ? AssetReferenceStatus.Unresolved : AssetReferenceStatus.Missing;
        return new(item.Index, item.Key, item.Location, kind, item.RelativePath, item.AbsolutePath, relative, relativeExists,
            absolute, absoluteExists, status, Array.AsReadOnly(referencedBy.ToArray()));
    }

    private static string? TryCombine(string baseDirectory, string relativePath)
    {
        try { return Path.GetFullPath(relativePath, baseDirectory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    // Matches the ordinal comparison the editor uses when pruning an unreferenced file item.
    private static bool IsSameFileKey(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
}
