using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecotteStudio.McpServer.Tests;

public sealed class VideoFrameExtractionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-frames-{Guid.NewGuid():N}");
    private readonly Runner runner = new();
    private string Video => Path.Combine(root, "プレイ 動画.mp4");
    private string Manifest => Path.Combine(root, "analysis.json");

    public VideoFrameExtractionTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Video, "original video");
        File.WriteAllText(Path.Combine(root, "ffmpeg.exe"), "fake");
    }

    public void Dispose() => Directory.Delete(root, true);

    private RecotteToolService Service(IFfmpegRunner? process = null, FfmpegLocator? locator = null) =>
        new(new(new(root)), new(), new(), ffmpeg: locator ?? new(Path.Combine(root, "ffmpeg.exe")), ffmpegRunner: process ?? runner);

    private Task<McpToolResult<object>> Extract(VideoFrameExtraction? settings = null) =>
        Service().ExtractVideoFramesAsync(Video, Manifest, settings ?? new(), default);

    [Fact]
    public async Task SavesImagesWithActualSourceTimesAndPreservesVideo()
    {
        var result = await Extract(new(Start: 10));
        Assert.True(result.Success, result.Message);
        var data = JsonSerializer.SerializeToNode(result.Data)!;
        var saved = JsonNode.Parse(File.ReadAllText(Manifest))!;
        Assert.Equal(data.ToJsonString(), saved.ToJsonString());
        Assert.Equal(10.125m, (decimal)data["frames"]![0]!["sourceTime"]!);
        Assert.Equal(15.25m, (decimal)data["frames"]![1]!["sourceTime"]!);
        foreach (var frame in data["frames"]!.AsArray()) Assert.True(File.Exists((string)frame!["imagePath"]!));
        Assert.Equal("original video", File.ReadAllText(Video));
        Assert.Contains("file:" + Video, runner.Calls.Last());
    }

    [Theory]
    [InlineData("interval", 0, 60, 0)]
    [InlineData("unknown", 0, 60, 5)]
    [InlineData("scene", -1, 60, 5)]
    [InlineData("scene", 0, 601, 5)]
    public async Task InvalidArgumentsDoNotRunFfmpeg(string mode, int start, int duration, int interval)
    {
        Assert.Equal("MCP_FRAME_ARGUMENT_INVALID", (await Extract(new(mode, start, duration, interval))).ErrorCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task PathsAndExistingManifestAreProtected()
    {
        Assert.Equal("MCP_PATH_OUTSIDE_WORKSPACE", (await Service().ExtractVideoFramesAsync(Video,
            Path.Combine(root, "..", "analysis.json"), new(), default)).ErrorCode);
        File.WriteAllText(Manifest, "keep");
        Assert.Equal("MCP_PATH_OUTPUT_EXISTS", (await Extract()).ErrorCode);
        Assert.Equal("keep", File.ReadAllText(Manifest));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task MissingFfmpegIsReported()
    {
        var result = await Service(locator: new(null, () => [])).ExtractVideoFramesAsync(Video, Manifest, new(), default);
        Assert.Equal("MCP_FFMPEG_UNAVAILABLE", result.ErrorCode);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("failure", "MCP_FFMPEG_FAILED")]
    [InlineData("timeout", "MCP_FFMPEG_TIMEOUT")]
    [InlineData("timestamps", "MCP_FRAME_TIMESTAMPS_INVALID")]
    [InlineData("empty", "MCP_FRAME_NO_FRAMES")]
    public async Task FailedExtractionRemovesPartialImages(string outcome, string code)
    {
        runner.Outcome = outcome;
        Assert.Equal(code, (await Extract(new(Start: 10))).ErrorCode);
        Assert.False(File.Exists(Manifest));
        Assert.Empty(Directory.GetDirectories(root));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task CancellationRemovesPartialImages()
    {
        runner.Outcome = "cancel";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Extract());
        Assert.Empty(Directory.GetDirectories(root));
        Assert.False(File.Exists(Manifest));
    }

    [Theory]
    [InlineData("interval", 0.5, 1, 2)]
    [InlineData("scene", 0, 5, 3)]
    [InlineData("combined", 0, 5, 3)]
    [InlineData("combined", 0, 0.7, 6)]
    public async Task RealFfmpegExtractsTimedFramesAndSceneCuts(string mode, double start, double interval, int expected)
    {
        if (InstalledFfmpeg.Find() is not string installed) return;
        FfmpegProcess process = new();
        FfmpegRunResult made = await process.RunAsync(installed,
            ["-v", "error", "-y", "-f", "lavfi", "-i", "color=red:s=320x180:r=10:d=1",
             "-f", "lavfi", "-i", "color=blue:s=320x180:r=10:d=1", "-f", "lavfi", "-i", "color=lime:s=320x180:r=10:d=1",
             "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]", "-map", "[v]", "-c:v", "libx264", Video], TimeSpan.FromSeconds(30), default);
        Assert.True(made.Succeeded, made.StandardError);
        var result = await Service(process, new(installed)).ExtractVideoFramesAsync(Video, Manifest,
            new(mode, (decimal)start, 3, (decimal)interval, 0.1m, mode == "interval" ? 2 : 60, 160), default);
        Assert.True(result.Success, result.Message);
        var data = JsonSerializer.SerializeToNode(result.Data)!;
        var frames = data["frames"]!.AsArray();
        Assert.Equal(expected, frames.Count);
        Assert.Equal((decimal)start, (decimal)frames[0]!["sourceTime"]!);
        Assert.Equal(mode == "interval" ? 1.5m : interval < 1 ? 0.7m : 1m, (decimal)frames[1]!["sourceTime"]!);
        Assert.Equal(mode == "interval", (bool)data["limitReached"]!);
        foreach (var frame in frames) Assert.True(new FileInfo((string)frame!["imagePath"]!).Length > 0);
    }

    private sealed class Runner : IFfmpegRunner
    {
        public string Outcome = "success";
        public List<IReadOnlyList<string>> Calls = [];
        public Task<FfmpegRunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            if (arguments.Contains("-version")) return Task.FromResult(new FfmpegRunResult(0, "ffmpeg version test", "", false));
            string directory = Path.GetDirectoryName(arguments.Last())!;
            if (Outcome != "empty")
                for (int i = 1; i <= 2; i++) File.WriteAllText(Path.Combine(directory, $"frame-{i:0000}.jpg"), "jpeg");
            if (Outcome == "cancel") throw new OperationCanceledException();
            string log = Outcome == "timestamps" ? "" :
                "[Parsed_showinfo_4 @ 000001] n: 0 pts: 10125 pts_time:10.125 duration:1\n" +
                "[Parsed_showinfo_4 @ 000001] n: 1 pts: 15250 pts_time:15.25 duration:1";
            return Task.FromResult(new FfmpegRunResult(Outcome == "failure" ? 1 : 0, "", log, Outcome == "timeout"));
        }
    }
}
