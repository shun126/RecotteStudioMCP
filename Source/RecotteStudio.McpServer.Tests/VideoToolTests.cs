using RecotteStudio.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecotteStudio.McpServer.Tests;

public sealed class VideoToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-video-{Guid.NewGuid():N}");
    private readonly string project;
    private readonly string image;
    private readonly string output;
    private readonly FakeRunner runner = new();

    public VideoToolTests()
    {
        Directory.CreateDirectory(root);
        project = Path.Combine(root, "movie.ccproj");
        image = Path.Combine(root, "castle.png");
        output = Path.Combine(root, "castle.mp4");
        File.Copy(Fixture("Empty.ccproj"), project);
        File.Copy(Fixture("Sample.png"), image);
        File.WriteAllText(Path.Combine(root, "ffmpeg.exe"), "fake");
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task CreatesTheClipAndSuggestsHowToPlaceIt()
    {
        byte[] before = File.ReadAllBytes(project);

        McpToolResult<object> result = await Create(Service());

        Assert.True(result.Success, result.Message);
        Assert.Equal(FakeRunner.Encoded, File.ReadAllBytes(output));
        Assert.Equal(before, File.ReadAllBytes(project));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        IReadOnlyList<string> encode = runner.Calls[^1];
        Assert.Equal("file:" + image, encode[encode.ToList().IndexOf("-i") + 1]);
        JsonObject data = Data(result);
        Assert.Equal(300, (int?)data["frameCount"]);
        Assert.Equal(5m, (decimal?)data["duration"]);
        Assert.Equal("7.1-test", (string?)data["ffmpegVersion"]);
        JsonObject suggested = data["suggestedOperation"]!.AsObject();
        Assert.Equal("addVideoObject", (string?)suggested["type"]);
        Assert.Equal(output, (string?)suggested["assetPath"]);
        int videoLayer = RecotteProject.Load(project).Layers.OfType<VideoLayerView>().First().Index;
        Assert.Equal(videoLayer, (int?)suggested["layer"]!["layerIndex"]);
    }

    [Fact]
    public async Task MissingFfmpegIsReportedWithoutRunningAnything()
    {
        RecotteToolService service = Service(new FfmpegLocator(null, () => Array.Empty<string>()));

        McpToolResult<object> result = await Create(service);

        Assert.Equal("MCP_FFMPEG_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("settings window", result.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task AnExecutableThatIsNotFfmpegIsUnavailable()
    {
        runner.Version = "something else";

        McpToolResult<object> result = await Create(Service());

        Assert.Equal("MCP_FFMPEG_UNAVAILABLE", result.ErrorCode);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task FailureReportsFfmpegErrorsAndLeavesNoFile()
    {
        runner.Encode = arguments => { WriteOutput(arguments); return new(1, "", "Invalid data found when processing input", false); };

        McpToolResult<object> result = await Create(Service());

        Assert.Equal("MCP_FFMPEG_FAILED", result.ErrorCode);
        Assert.Contains("Invalid data found", result.Message);
        AssertNoOutput();
    }

    [Fact]
    public async Task TimeoutLeavesNoFile()
    {
        runner.Encode = arguments => { WriteOutput(arguments); return new(-1, "", "", true); };

        Assert.Equal("MCP_FFMPEG_TIMEOUT", (await Create(Service())).ErrorCode);
        AssertNoOutput();
    }

    [Fact]
    public async Task CancellationLeavesNoFile()
    {
        runner.Encode = arguments => { WriteOutput(arguments); throw new OperationCanceledException(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(Service()));
        AssertNoOutput();
    }

    [Fact]
    public async Task ExistingOutputIsReplacedOnlyWhenAllowed()
    {
        File.WriteAllText(output, "existing");

        Assert.Equal("MCP_PATH_OUTPUT_EXISTS", (await Create(Service())).ErrorCode);
        Assert.Equal("existing", File.ReadAllText(output));
        Assert.True((await Create(Service(), allowOverwrite: true)).Success);
        Assert.Equal(FakeRunner.Encoded, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedBeforeFfmpegRuns()
    {
        RecotteToolService service = Service();
        string webp = Path.Combine(root, "castle.webp");
        File.WriteAllText(webp, "x");

        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", (await Create(service, duration: 0.1m)).ErrorCode);
        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", (await Create(service, motion: "spin")).ErrorCode);
        Assert.Equal("MCP_VIDEO_IMAGE_UNSUPPORTED", (await Create(service, imagePath: webp)).ErrorCode);
        Assert.Equal("MCP_PATH_FILE_NOT_FOUND", (await Create(service, imagePath: Path.Combine(root, "missing.png"))).ErrorCode);
        Assert.Equal("MCP_PATH_EXTENSION", (await Create(service, outputPath: Path.Combine(root, "castle.mov"))).ErrorCode);
        Assert.Equal("MCP_PATH_OUTSIDE_WORKSPACE", (await Create(service, outputPath: Path.Combine(root, "..", "castle.mp4"))).ErrorCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task OnlyFullHdProjectsAreSupported()
    {
        JsonNode document = JsonNode.Parse(File.ReadAllText(project))!;
        document["setting"]!["size"] = new JsonArray(1280, 720);
        File.WriteAllText(project, document.ToJsonString());

        Assert.Equal("MCP_VIDEO_PROJECT_SIZE_UNSUPPORTED", (await Create(Service())).ErrorCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task InstalledFfmpegMakesAClipThatCoreCanPlace()
    {
        if (InstalledFfmpeg.Find() is not string installed) return; // ffmpeg is optional and absent on CI.
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(), ffmpeg: new FfmpegLocator(installed));

        McpToolResult<object> result = await service.CreateVideoFromImageAsync(project, image, output, false, 1m, "zoomIn", 1.2m,
            0.5m, 0.5m, null, null, null, default);

        Assert.True(result.Success, result.Message);
        string ffprobe = Path.Combine(Path.GetDirectoryName(installed)!, "ffprobe.exe");
        if (File.Exists(ffprobe))
        {
            FfmpegRunResult probe = await new FfmpegProcess().RunAsync(ffprobe, new[] { "-v", "error", "-show_entries",
                "stream=codec_name,profile,pix_fmt,width,height,r_frame_rate,nb_frames", "-of", "compact", output },
                TimeSpan.FromSeconds(30), default);
            Assert.Contains("codec_name=h264|profile=Constrained Baseline|width=1920|height=1080|pix_fmt=yuv420p|r_frame_rate=60/1|nb_frames=60", probe.StandardOutput);
            Assert.Contains("codec_name=aac", probe.StandardOutput);
        }

        JsonObject suggested = Data(result)["suggestedOperation"]!.AsObject();
        string placed = Path.Combine(root, "placed.ccproj");
        McpToolResult<object> saved = await service.ApplyOperationsAndSaveCopyAsync(project, placed, false, new[]
        {
            new ProjectOperationDto("addVideoObject", Layer: new LayerTargetDto(LayerIndex: (int)suggested["layer"]!["layerIndex"]!),
                AssetPath: (string)suggested["assetPath"]!, ProjectPath: placed, DisplayName: (string)suggested["displayName"]!,
                Start: 0m, End: (decimal)suggested["end"]!),
        }, default);
        Assert.True(saved.Success, saved.Message);
        Assert.Single(RecotteProject.Load(placed).GetTimelineEntries(), entry => entry.ObjectType == "Video");
    }

    [Fact]
    public async Task FlipbookPreparesEachDistinctImageOnceAndListsTheFrames()
    {
        string second = CopyImage("second.png");

        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[]
        {
            new(image), new(second, 0.25m), new(image),
        }, loops: 2);

        Assert.True(result.Success, result.Message);
        Assert.Equal(FakeRunner.Encoded, File.ReadAllBytes(output));
        Assert.Equal(1 + 2 + 1, runner.Calls.Count);
        Assert.Equal(new[] { "file:" + image, "file:" + second }, runner.Calls.Skip(1).Take(2).Select(call => call[call.ToList().IndexOf("-i") + 1]));
        Assert.Contains("file '0001.png'\noption framerate 60\nduration 0.133333333\nfile '0002.png'", runner.ConcatList);
        Assert.False(Directory.Exists(runner.WorkDirectory));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        JsonObject data = Data(result);
        Assert.Equal((8 + 15 + 8) * 2, (int?)data["frameCount"]);
        Assert.Equal(2, (int?)data["distinctImageCount"]);
        JsonArray frames = data["frames"]!.AsArray();
        Assert.Equal(new[] { 0m, 0.133333m, 0.383333m }, frames.Select(frame => (decimal)frame!["start"]!));
        Assert.Equal(15, (int?)frames[1]!["frameCount"]);
        Assert.Equal("addVideoObject", (string?)data["suggestedOperation"]!["type"]);
    }

    [Fact]
    public async Task FlipbookRejectsInvalidFramesBeforeFfmpegRuns()
    {
        RecotteToolService service = Service();
        string webp = Path.Combine(root, "frame.webp");
        File.WriteAllText(webp, "x");

        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", (await Flipbook(service, Array.Empty<FlipbookFrameDto>())).ErrorCode);
        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", (await Flipbook(service, Enumerable.Repeat(new FlipbookFrameDto(image), 601).ToArray())).ErrorCode);
        McpToolResult<object> badDuration = await Flipbook(service, new FlipbookFrameDto[] { new(image), new(image, 11m) });
        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", badDuration.ErrorCode);
        Assert.Contains("frames[1]", badDuration.Message);
        McpToolResult<object> unsupported = await Flipbook(service, new FlipbookFrameDto[] { new(image), new(webp) });
        Assert.Equal("MCP_VIDEO_IMAGE_UNSUPPORTED", unsupported.ErrorCode);
        Assert.Contains("frames[1]", unsupported.Message);
        Assert.Equal("MCP_PATH_FILE_NOT_FOUND", (await Flipbook(service, new FlipbookFrameDto[] { new(Path.Combine(root, "missing.png")) })).ErrorCode);
        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", (await Flipbook(service, new FlipbookFrameDto[] { new(image) }, loops: 0)).ErrorCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task FlipbookLongerThanSixtySecondsIsRejected()
    {
        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[] { new(image, 10m), new(image, 10m) }, loops: 4);

        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", result.ErrorCode);
        Assert.Contains("80", result.Message);
        Assert.Single(runner.Calls);
        AssertNoOutput();
    }

    [Fact]
    public async Task FlipbookReportsTheImageThatCouldNotBePrepared()
    {
        runner.Encode = arguments => arguments.Contains("concat") ? Succeed(arguments) : new(1, "", "Invalid PNG signature", false);

        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[] { new(image) });

        Assert.Equal("MCP_FFMPEG_FAILED", result.ErrorCode);
        Assert.Contains(image, result.Message);
        Assert.Contains("Invalid PNG signature", result.Message);
        Assert.False(Directory.Exists(runner.WorkDirectory));
        AssertNoOutput();
    }

    [Fact]
    public async Task InstalledFfmpegShowsEveryFlipbookFrameOnTime()
    {
        if (InstalledFfmpeg.Find() is not string installed) return; // ffmpeg is optional and absent on CI.
        FfmpegProcess process = new();
        string[] colors = { "red", "lime", "blue" };
        foreach (string color in colors)
        {
            FfmpegRunResult made = await process.RunAsync(installed, new[] { "-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={color}:s=640x480",
                "-frames:v", "1", Path.Combine(root, color + ".png") }, TimeSpan.FromSeconds(30), default);
            Assert.True(made.Succeeded, made.StandardError);
        }
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(), ffmpeg: new FfmpegLocator(installed));

        // 7, 8, and 1 frames at 60 fps, twice.
        McpToolResult<object> result = await service.CreateFlipbookVideoAsync(project, new FlipbookFrameDto[]
        {
            new(Path.Combine(root, "red.png"), 7m / 60m), new(Path.Combine(root, "lime.png"), 8m / 60m), new(Path.Combine(root, "blue.png"), 1m / 60m),
        }, output, false, 0.125m, 2, null, null, default);

        Assert.True(result.Success, result.Message);
        string raw = Path.Combine(root, "frames.rgb");
        FfmpegRunResult decoded = await process.RunAsync(installed, new[] { "-v", "error", "-y", "-i", output, "-vf", "scale=1:1:flags=area",
            "-f", "rawvideo", "-pix_fmt", "rgb24", raw }, TimeSpan.FromSeconds(30), default);
        Assert.True(decoded.Succeeded, decoded.StandardError);
        string shown = string.Concat(File.ReadAllBytes(raw).Chunk(3).Select(pixel =>
            pixel[0] > 150 && pixel[1] < 80 ? 'R' : pixel[1] > 150 && pixel[0] < 80 ? 'G' : pixel[2] > 150 && pixel[0] < 80 ? 'B' : '?'));
        Assert.Equal("RRRRRRRGGGGGGGGBRRRRRRRGGGGGGGGB", shown);
    }

    private Task<McpToolResult<object>> Flipbook(RecotteToolService service, IReadOnlyList<FlipbookFrameDto> frames, int loops = 1) =>
        service.CreateFlipbookVideoAsync(project, frames, output, false, 0.125m, loops, null, null, default);

    private string CopyImage(string name)
    {
        string path = Path.Combine(root, name);
        File.Copy(image, path);
        return path;
    }

    private static FfmpegRunResult Succeed(IReadOnlyList<string> arguments)
    {
        WriteOutput(arguments);
        return new(0, "", "", false);
    }

    private RecotteToolService Service(FfmpegLocator? locator = null) => new(new WorkspacePathPolicy(new(root)), new(), new(),
        ffmpeg: locator ?? new FfmpegLocator(Path.Combine(root, "ffmpeg.exe")), ffmpegRunner: runner);

    private Task<McpToolResult<object>> Create(RecotteToolService service, decimal duration = 5m, string? motion = null,
        string? imagePath = null, string? outputPath = null, bool allowOverwrite = false) =>
        service.CreateVideoFromImageAsync(project, imagePath ?? image, outputPath ?? output, allowOverwrite, duration, motion,
            1.2m, 0.5m, 0.5m, null, null, null, default);

    private void AssertNoOutput()
    {
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }

    private static JsonObject Data(McpToolResult<object> result) => JsonNode.Parse(JsonSerializer.Serialize(result.Data))!.AsObject();
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static void WriteOutput(IReadOnlyList<string> arguments) => File.WriteAllBytes(arguments[^1]["file:".Length..], FakeRunner.Encoded);

    private sealed class FakeRunner : IFfmpegRunner
    {
        public static readonly byte[] Encoded = { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p' };
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public string Version { get; set; } = "ffmpeg version 7.1-test Copyright (c) 2000-2024 the FFmpeg developers";
        public Func<IReadOnlyList<string>, FfmpegRunResult>? Encode { get; set; }
        public string? ConcatList { get; private set; }
        public string? WorkDirectory { get; private set; }

        public Task<FfmpegRunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            if (arguments.Contains("-update")) WorkDirectory = Path.GetDirectoryName(arguments[^1]["file:".Length..]);
            if (arguments.Contains("concat")) ConcatList = File.ReadAllText(arguments[arguments.ToList().IndexOf("concat") + 4]["file:".Length..]);
            if (arguments.SequenceEqual(new[] { "-version" })) return Task.FromResult(new FfmpegRunResult(0, Version + "\n", "", false));
            if (Encode is not null) return Task.FromResult(Encode(arguments));
            WriteOutput(arguments);
            return Task.FromResult(new FfmpegRunResult(0, "", "", false));
        }
    }
}
