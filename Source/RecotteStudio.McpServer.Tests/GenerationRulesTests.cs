using System.Text;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class GenerationRulesTests
{
    [Fact]
    public void DistributedGuidanceMatchesSourceAndRemainsSeparate()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenerationRules.md"));
        ActivityLog activity = new();
        string? guidance = GenerationRulesProvider.Read(activity);
        Assert.NotNull(guidance);
        Assert.Equal(source.Replace("\r\n", "\n"), guidance.Replace("\r\n", "\n"));
        Assert.Contains("recotte_create_video_from_image", guidance);
        Assert.Contains("recotte_create_flipbook_video", guidance);
        Assert.Contains("recotte_get_generation_rules", McpServerRegistration.ServerInstructions);
    }

    [Fact]
    public void SearchesNextToTheExecutableBeforeTheBaseDirectory()
    {
        IReadOnlyList<string> directories = GenerationRulesProvider.SearchDirectories();

        Assert.Equal(Path.GetDirectoryName(Environment.ProcessPath), directories[0]);
        Assert.Contains(directories, item => string.Equals(Path.TrimEndingDirectorySeparator(item),
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(directories.Count, directories.Select(Path.TrimEndingDirectorySeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MissingGuidanceOnlyLogsWarningAndCanBeAddedLaterWithoutRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"recotte-guidance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            ActivityLog activity = new();
            List<ActivityEntry> entries = new();
            activity.Recorded += entries.Add;

            Assert.Null(GenerationRulesProvider.Read(activity, directory));
            ActivityEntry warning = Assert.Single(entries);
            Assert.Equal(ActivityKind.Warning, warning.Kind);
            Assert.Contains("GenerationRules.md", warning.Detail);
            Assert.Null(warning.Duration);

            string file = Path.Combine(directory, GenerationRulesProvider.FileName);
            File.WriteAllText(file, "custom rule 1", Encoding.UTF8);
            Assert.Equal("custom rule 1", GenerationRulesProvider.Read(activity, directory));
            File.WriteAllText(file, "custom rule 2", Encoding.UTF8);
            Assert.Equal("custom rule 2", GenerationRulesProvider.Read(activity, directory));
            Assert.Single(entries); // No warning once the file is readable.

            File.Delete(file);
            Assert.Null(GenerationRulesProvider.Read(activity, directory));
            Assert.Equal(2, entries.Count);
            Assert.All(entries, item => Assert.Equal(ActivityKind.Warning, item.Kind));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
