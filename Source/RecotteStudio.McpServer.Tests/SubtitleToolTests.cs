using RecotteStudio.Core;
using System.Text;

namespace RecotteStudio.McpServer.Tests;

public sealed class SubtitleToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-subtitles-{Guid.NewGuid():N}");
    private readonly RecotteToolService service;
    private readonly string project;

    public SubtitleToolTests()
    {
        Directory.CreateDirectory(root);
        service = new(new WorkspacePathPolicy(new(root)), new(), new());
        project = Path.Combine(root, "dialogue.ccproj");
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task ExportWritesTheProjectDialogueAndLeavesTheSourceUnchanged()
    {
        await CreateDialogue();
        byte[] before = File.ReadAllBytes(project);
        string output = Path.Combine(root, "dialogue.srt");

        McpToolResult<object> result = await service.ExportSubtitlesAsync(project, output, false, null, default);

        Assert.True(result.Success, result.Message);
        Assert.Equal(before, File.ReadAllBytes(project));
        byte[] bytes = File.ReadAllBytes(output);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("1\r\n00:00:00,000 --> 00:00:01,500\r\nおはよう\r\n\r\n2\r\n00:00:01,500 --> 00:00:03,000\r\nいい天気だね\r\n\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task ExportReplacesEveryCueWithTheGivenTexts()
    {
        await CreateDialogue();
        SubtitleCue[] cues = RecotteProject.Load(project).GetSubtitleCues().ToArray();
        string output = Path.Combine(root, "dialogue.en.srt");

        McpToolResult<object> result = await service.ExportSubtitlesAsync(project, output, false, new SubtitleTextDto[]
        {
            new(cues[1].ObjectId.LayerIndex, cues[1].ObjectId.ObjectKey, "Nice weather,\nisn't it?"),
            new(cues[0].ObjectId.LayerIndex, cues[0].ObjectId.ObjectKey, "Good morning"),
        }, default);

        Assert.True(result.Success, result.Message);
        Assert.Equal("1\r\n00:00:00,000 --> 00:00:01,500\r\nGood morning\r\n\r\n2\r\n00:00:01,500 --> 00:00:03,000\r\nNice weather,\r\nisn't it?\r\n\r\n",
            File.ReadAllText(output));
    }

    [Fact]
    public async Task ExportRejectsTextsThatDoNotMatchTheCuesOneToOne()
    {
        await CreateDialogue();
        SubtitleCue[] cues = RecotteProject.Load(project).GetSubtitleCues().ToArray();
        SubtitleTextDto first = new(cues[0].ObjectId.LayerIndex, cues[0].ObjectId.ObjectKey, "Good morning");
        SubtitleTextDto second = new(cues[1].ObjectId.LayerIndex, cues[1].ObjectId.ObjectKey, "Nice weather");
        string output = Path.Combine(root, "dialogue.en.srt");

        Assert.Equal("MCP_SUBTITLE_TEXT_MISSING", (await Export(output)).ErrorCode); // an empty list is not an omitted one
        Assert.Equal("MCP_SUBTITLE_TEXT_MISSING", (await Export(output, first)).ErrorCode);
        Assert.Equal("MCP_SUBTITLE_TEXT_DUPLICATE", (await Export(output, first, first, second)).ErrorCode);
        Assert.Equal("MCP_SUBTITLE_TEXT_UNKNOWN", (await Export(output, first, second, new(99, 1, "x"))).ErrorCode);
        Assert.Equal("MCP_SUBTITLE_TEXT_EMPTY", (await Export(output, first, second with { Text = " " })).ErrorCode);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ExportOverwritesOnlyWhenAllowed()
    {
        await CreateDialogue();
        string output = Path.Combine(root, "dialogue.srt");
        File.WriteAllText(output, "existing");

        McpToolResult<object> refused = await service.ExportSubtitlesAsync(project, output, false, null, default);
        Assert.Equal("MCP_PATH_OUTPUT_EXISTS", refused.ErrorCode);
        Assert.Equal("existing", File.ReadAllText(output));

        Assert.True((await service.ExportSubtitlesAsync(project, output, true, null, default)).Success);
        Assert.StartsWith("1\r\n", File.ReadAllText(output));
    }

    [Fact]
    public async Task ExportRequiresAnSrtPathAndDialogue()
    {
        await CreateDialogue();
        Assert.Equal("MCP_PATH_EXTENSION", (await service.ExportSubtitlesAsync(project, Path.Combine(root, "dialogue.txt"), false, null, default)).ErrorCode);

        string empty = Path.Combine(root, "empty.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Empty.ccproj"), empty);
        Assert.Equal("MCP_SUBTITLE_NO_CUES", (await service.ExportSubtitlesAsync(empty, Path.Combine(root, "empty.srt"), false, null, default)).ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "empty.srt")));
    }

    [Fact]
    public async Task ExportPropagatesCancellationAfterTheOutputLockIsHeld()
    {
        await CreateDialogue();
        OutputPathLockManager locks = new();
        RecotteToolService cancellable = new(new WorkspacePathPolicy(new(root)), new(), locks);
        string output = Path.Combine(root, "dialogue.srt");
        using CancellationTokenSource cancellation = new();
        IAsyncDisposable held = await locks.AcquireAsync(output, default);

        Task<McpToolResult<object>> export = cancellable.ExportSubtitlesAsync(project, output, false, null, cancellation.Token);
        // Release grants the waiting export and resumes it asynchronously, so it is cancelled once inside its guarded block.
        await held.DisposeAsync();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task ListSubtitleCuesReturnsTheCueIdentifiers()
    {
        await CreateDialogue();
        McpToolResult<object> result = service.ListSubtitleCues(project);

        Assert.True(result.Success);
        string json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        Assert.Contains("\"layerIndex\":1", json);
        Assert.Contains("\"objectKey\":", json);
        Assert.Contains("\"start\":1.5", json);
    }

    private Task<McpToolResult<object>> Export(string output, params SubtitleTextDto[] texts) =>
        service.ExportSubtitlesAsync(project, output, false, texts, default);

    private async Task CreateDialogue()
    {
        McpToolResult<object> created = await service.CreateProjectAsync("dialogue", project, false, new ProjectOperationDto[]
        {
            new("addTextOnlySpeakerVoice", Layer: new LayerTargetDto(LayerIndex: 1), Text: "おはよう", Start: 0m, End: 1.5m),
            new("addTextOnlySpeakerVoice", Layer: new LayerTargetDto(LayerIndex: 1), Text: "いい天気だね", Start: 1.5m, End: 3m),
        }, default);
        Assert.True(created.Success, created.Message);
    }
}
