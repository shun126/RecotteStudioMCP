using RecotteStudio.Core;

namespace RecotteStudio.McpServer;

/// <summary>Locates and reads the stamps installed with Recotte Studio.</summary>
public sealed class FigureCatalogProvider(string? figuresDirectory = null)
{
    /// <summary>Gets the figures directory of the default Recotte Studio installation.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RecotteStudio", "figures");

    public string Directory { get; } = Path.GetFullPath(figuresDirectory ?? DefaultDirectory);

    // Read on every call so stamps added while the server runs are found without a restart.
    public FigureCatalog Load() => FigureCatalog.Load(Directory);
}
