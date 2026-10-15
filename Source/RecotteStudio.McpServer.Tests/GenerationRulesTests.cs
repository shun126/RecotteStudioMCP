using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class GenerationRulesTests
{
    [Fact]
    public void EmbeddedGuidanceMatchesPackagedSource()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenerationRules.md"));
        Assert.Equal(source.Replace("\r\n", "\n"), GenerationRulesProvider.Markdown.Replace("\r\n", "\n"));
        Assert.Contains("recotte_create_video_from_image", GenerationRulesProvider.Markdown);
        Assert.Contains("recotte_create_flipbook_video", GenerationRulesProvider.Markdown);
        Assert.Contains("下側 20%", GenerationRulesProvider.Markdown);
        Assert.Contains("recotte_get_generation_rules", McpServerRegistration.ServerInstructions);
    }
}
