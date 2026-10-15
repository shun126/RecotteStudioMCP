using RecotteStudio.Core;

namespace RecotteStudio.McpServer;

public sealed class EffectCatalogProvider(string? effectsDirectory = null)
{
    public string Directory { get; } = Path.GetFullPath(effectsDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RecotteStudio", "effects"));
    public EffectCatalog Load() => EffectCatalog.Load(Directory);
}
