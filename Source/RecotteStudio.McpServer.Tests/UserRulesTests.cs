using System.Text;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class UserRulesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"recotte-user-rules-{Guid.NewGuid():N}");
    private readonly ActivityLog activity = new();
    private readonly List<ActivityEntry> entries = new();

    public UserRulesTests()
    {
        Directory.CreateDirectory(directory);
        activity.Recorded += entries.Add;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string RulesPath => Path.Combine(directory, UserRulesProvider.FileName);

    [Fact]
    public void LivesWithTheLocalSettingsApartFromTheDistributedRules()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RecotteStudioMCP", "UserRules.md"), UserRulesProvider.DefaultPath);
        Assert.DoesNotContain(GenerationRulesProvider.SearchDirectories(), item => string.Equals(
            Path.TrimEndingDirectorySeparator(item), UserRulesProvider.DefaultDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void MissingOrBlankFileIsTheUsualStateAndLogsNothing(string? content)
    {
        if (content is not null) File.WriteAllText(RulesPath, content, Encoding.UTF8);

        Assert.Null(UserRulesProvider.Read(activity, directory));
        Assert.Empty(entries);
    }

    [Fact]
    public void ReadsTheCurrentFileOnEveryRequest()
    {
        File.WriteAllText(RulesPath, "# 自分のルール\n- 立ち絵は右に置く", Encoding.UTF8);
        Assert.Equal("# 自分のルール\n- 立ち絵は右に置く", UserRulesProvider.Read(activity, directory));

        File.WriteAllText(RulesPath, "- 立ち絵は左に置く", Encoding.UTF8);
        Assert.Equal("- 立ち絵は左に置く", UserRulesProvider.Read(activity, directory));

        File.Delete(RulesPath);
        Assert.Null(UserRulesProvider.Read(activity, directory));
        Assert.Empty(entries);
    }

    [Fact]
    public void UnreadableFileLogsAWarning()
    {
        File.WriteAllText(RulesPath, "rule", Encoding.UTF8);
        using (new FileStream(RulesPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(UserRulesProvider.Read(activity, directory));

        ActivityEntry warning = Assert.Single(entries);
        Assert.Equal(ActivityKind.Warning, warning.Kind);
        Assert.Contains(RulesPath, warning.Detail);
    }

    [Fact]
    public void ExactlyMaxLengthDoesNotLogAWarning()
    {
        string content = new string('a', UserRulesProvider.MaxLength);
        File.WriteAllText(RulesPath, content, Encoding.UTF8);

        Assert.Equal(content, UserRulesProvider.Read(activity, directory));
        Assert.Empty(entries);
    }

    [Fact]
    public void LargeFileReturnsOnlyTheCappedPrefix()
    {
        // A multi-megabyte file should never be fully decoded or copied into a string.
        const int fileSize = 8 * 1024 * 1024;
        using (FileStream stream = File.Create(RulesPath))
        {
            byte[] header = Encoding.UTF8.GetBytes("# user rules\n");
            stream.Write(header);
            stream.SetLength(fileSize);
        }

        string? rules = UserRulesProvider.Read(activity, directory);

        Assert.NotNull(rules);
        Assert.Equal(UserRulesProvider.MaxLength, rules.Length);
        Assert.StartsWith("# user rules", rules);
        Assert.Equal(ActivityKind.Warning, Assert.Single(entries).Kind);
    }

    [Fact]
    public void Utf8CharactersAreDecodedAcrossTheLengthBoundary()
    {
        string content = new string('あ', UserRulesProvider.MaxLength) + "後";
        File.WriteAllText(RulesPath, content, Encoding.UTF8);

        Assert.Equal(new string('あ', UserRulesProvider.MaxLength),
            UserRulesProvider.Read(activity, directory));
        Assert.Equal(ActivityKind.Warning, Assert.Single(entries).Kind);
    }

    [Fact]
    public void OversizedFileIsCutWithoutSplittingACharacter()
    {
        string content = new string('a', UserRulesProvider.MaxLength - 1) + "😀" + "tail";
        File.WriteAllText(RulesPath, content, Encoding.UTF8);

        string? rules = UserRulesProvider.Read(activity, directory);

        Assert.Equal(new string('a', UserRulesProvider.MaxLength - 1), rules);
        ActivityEntry warning = Assert.Single(entries);
        Assert.Equal(ActivityKind.Warning, warning.Kind);
    }
}
