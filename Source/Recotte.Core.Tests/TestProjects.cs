namespace Recotte.Core.Tests;

public static class TestProjects
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static IEnumerable<object[]> ValidProjects()
    {
        string samples = Path.Combine(RepositoryRoot, "Samples", "RecotteProjects");
        foreach (string path in Directory.EnumerateFiles(samples, "*.ccproj", SearchOption.AllDirectories).Order())
        {
            yield return new object[] { path };
        }

        string generated = Path.Combine(RepositoryRoot, "Tests", "MakuraNoSoshiReading", "MakuraNoSoshiReading.ccproj");
        if (File.Exists(generated)) yield return new object[] { generated };
    }

    public static IEnumerable<object[]> SupportedProjects()
    {
        foreach (object[] item in ValidProjects())
        {
            string path = (string)item[0];
            if (RecotteProject.Load(path).Compatibility == CompatibilityLevel.Supported)
                yield return item;
        }
    }

    internal static string EmptyProject => Path.Combine(
        RepositoryRoot,
        "Samples",
        "RecotteProjects",
        "001EmptyProject",
        "001EmptyProject.ccproj");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RecotteStudioMCP.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The repository root could not be located.");
    }
}
