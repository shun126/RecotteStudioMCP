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
    public async Task AVerticalProjectGetsAVerticalClip()
    {
        SetProjectSize(1080, 1920);

        McpToolResult<object> result = await Create(Service());

        Assert.True(result.Success, result.Message);
        JsonObject data = Data(result);
        Assert.Equal((1080, 1920), ((int)data["width"]!, (int)data["height"]!));
        Assert.Contains(":s=1080x1920:", runner.Calls[^1][runner.Calls[^1].ToList().IndexOf("-filter_complex") + 1]);
    }

    [Theory]
    [InlineData(1081, 1920)]
    [InlineData(3840, 2160)]
    public async Task ScreensVideoCannotBeMadeForAreRejected(int width, int height)
    {
        SetProjectSize(width, height);

        Assert.Equal("MCP_VIDEO_PROJECT_SIZE_UNSUPPORTED", (await Create(Service())).ErrorCode);
        Assert.Empty(runner.Calls);
    }

    private void SetProjectSize(int width, int height)
    {
        JsonNode document = JsonNode.Parse(File.ReadAllText(project))!;
        document["setting"]!["size"] = new JsonArray(width, height);
        File.WriteAllText(project, document.ToJsonString());
    }

    [Fact]
    public async Task InstalledFfmpegMakesAClipThatCoreCanPlace()
    {
        if (InstalledFfmpeg.Find() is not string installed) return; // ffmpeg is optional and absent on CI.
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(), ffmpeg: new FfmpegLocator(installed));

        McpToolResult<object> result = await service.CreateVideoFromImageAsync(project, image, output, false, 5m, "zoomIn", 1.2m,
            0.5m, 0.5m, null, null, null, default);

        Assert.True(result.Success, result.Message);
        string ffprobe = Path.Combine(Path.GetDirectoryName(installed)!, "ffprobe.exe");
        if (File.Exists(ffprobe))
        {
            FfmpegRunResult probe = await new FfmpegProcess().RunAsync(ffprobe, new[] { "-v", "error", "-show_entries",
                "stream=codec_name,profile,pix_fmt,width,height,r_frame_rate,nb_frames", "-of", "compact", output },
                TimeSpan.FromSeconds(30), default);
            Assert.Contains("codec_name=h264|profile=Constrained Baseline|width=1920|height=1080|pix_fmt=yuv420p|r_frame_rate=60/1|nb_frames=300", probe.StandardOutput);
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
    public async Task WithoutTheSettingClipsUseLibx264AndNoEncoderIsTested()
    {
        McpToolResult<object> result = await Create(Service());

        Assert.True(result.Success, result.Message);
        Assert.Equal("libx264", (string?)Data(result)["videoEncoder"]);
        Assert.DoesNotContain(runner.Calls, IsProbe);
    }

    [Fact]
    public async Task AutoEncodesWithTheFirstWorkingGpuAndTestsItOnlyOnce()
    {
        runner.WorkingEncoders.Add("h264_qsv");
        RecotteToolService service = Service(hardware: HardwareEncoding.Auto);

        McpToolResult<object> first = await Create(service);
        McpToolResult<object> second = await Create(service, allowOverwrite: true);

        Assert.True(second.Success, second.Message);
        Assert.Equal("h264_qsv", (string?)Data(first)["videoEncoder"]);
        Assert.Equal("h264_qsv", Codec(runner.Calls[^1]));
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv" }, runner.Calls.Where(IsProbe).Select(Codec));
    }

    [Fact]
    public async Task AGpuEncoderThatFailsIsReplacedByLibx264AndNotChosenAgain()
    {
        runner.WorkingEncoders.UnionWith(new[] { "h264_nvenc", "h264_qsv" });
        runner.Encode = arguments => Codec(arguments) == "h264_nvenc" ? new(1, "", "OpenEncodeSessionEx failed", false) : Succeed(arguments);
        RecotteToolService service = Service(hardware: HardwareEncoding.Auto);

        McpToolResult<object> first = await Create(service);
        McpToolResult<object> second = await Create(service, allowOverwrite: true);

        Assert.True(first.Success, first.Message);
        Assert.Equal("libx264", (string?)Data(first)["videoEncoder"]);
        Assert.Contains("h264_nvenc failed", (string?)Data(first)["videoEncoderFallback"]);
        Assert.Equal(FakeRunner.Encoded, File.ReadAllBytes(output));
        Assert.Equal("h264_qsv", (string?)Data(second)["videoEncoder"]);
        Assert.Null(Data(second)["videoEncoderFallback"]);
    }

    [Theory]
    [InlineData(false, VideoEncoder.Software, 1)]
    [InlineData(true, VideoEncoder.Nvidia, 2)]
    public async Task SettingsChecksWaitForFallbackAndOnlyExplicitRechecksClearItsFailure(bool recheck,
        VideoEncoder expected, int probes)
    {
        runner.WorkingEncoders.Add("h264_nvenc");
        VideoEncoderSelector selector = new(HardwareEncoding.Nvidia);
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(),
            ffmpeg: new FfmpegLocator(Path.Combine(root, "ffmpeg.exe")), ffmpegRunner: runner, videoEncoders: selector);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Intercept = async arguments =>
        {
            if (IsProbe(arguments)) return new(0, "", "", false);
            if (Codec(arguments) == "libx264") return Succeed(arguments);
            started.SetResult();
            await finish.Task;
            return new(1, "", "GPU encode failed", false);
        };

        Task<McpToolResult<object>> clip = Create(service);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<VideoEncoder> check = selector.CheckAsync(runner, Path.Combine(root, "ffmpeg.exe"), default, recheck);
        try
        {
            Assert.False(check.IsCompleted);
            Assert.Single(runner.Calls, IsProbe);
        }
        finally { finish.TrySetResult(); }

        Assert.True((await clip.WaitAsync(TimeSpan.FromSeconds(10))).Success);
        Assert.Equal(expected, await check.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(probes, runner.Calls.Count(IsProbe));
    }

    [Fact]
    public async Task VideoJobsWaitForSettingsProbesAndReuseTheirResult()
    {
        VideoEncoderSelector selector = new(HardwareEncoding.Nvidia);
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(),
            ffmpeg: new FfmpegLocator(Path.Combine(root, "ffmpeg.exe")), ffmpegRunner: runner, videoEncoders: selector);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Intercept = async arguments =>
        {
            if (!IsProbe(arguments)) return Succeed(arguments);
            started.TrySetResult();
            await finish.Task;
            return new(0, "", "", false);
        };

        Task<VideoEncoder> check = selector.CheckAsync(runner, Path.Combine(root, "ffmpeg.exe"), default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<McpToolResult<object>> clip = Create(service);
        try
        {
            Assert.False(clip.IsCompleted);
            Assert.Single(runner.Calls, IsProbe);
            Assert.DoesNotContain(runner.Calls, call => !IsProbe(call) && call.Contains("-c:v"));
        }
        finally { finish.TrySetResult(); }

        Assert.Equal(VideoEncoder.Nvidia, await check.WaitAsync(TimeSpan.FromSeconds(10)));
        McpToolResult<object> result = await clip.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success, result.Message);
        Assert.Equal("h264_nvenc", (string?)Data(result)["videoEncoder"]);
        Assert.Single(runner.Calls, IsProbe);
    }

    [Fact]
    public async Task AnInputThatNoEncoderCanReadKeepsTheGpuEncoder()
    {
        runner.WorkingEncoders.Add("h264_nvenc");
        runner.Encode = _ => new(1, "", "Invalid data found when processing input", false);
        RecotteToolService service = Service(hardware: HardwareEncoding.Nvidia);

        McpToolResult<object> failed = await Create(service);
        runner.Encode = null;
        McpToolResult<object> retried = await Create(service);

        Assert.Equal("MCP_FFMPEG_FAILED", failed.ErrorCode);
        Assert.True(retried.Success, retried.Message);
        Assert.Equal("h264_nvenc", (string?)Data(retried)["videoEncoder"]);
    }

    [Fact]
    public async Task AGpuEncoderThatTimesOutIsNotRunAgainWithLibx264()
    {
        runner.WorkingEncoders.Add("h264_nvenc");
        runner.Encode = _ => new(-1, "", "", true);

        McpToolResult<object> result = await Create(Service(hardware: HardwareEncoding.Nvidia));

        Assert.Equal("MCP_FFMPEG_TIMEOUT", result.ErrorCode);
        Assert.Single(runner.Calls, call => !IsProbe(call) && call.Contains("-c:v"));
        AssertNoOutput();
    }

    [Fact]
    public async Task FlipbookEncodesWithTheChosenGpu()
    {
        runner.WorkingEncoders.Add("h264_amf");

        McpToolResult<object> result = await Flipbook(Service(hardware: HardwareEncoding.Amd), new FlipbookFrameDto[] { new(image, 1m) });

        Assert.True(result.Success, result.Message);
        Assert.Equal("h264_amf", Codec(runner.Calls[^1]));
        Assert.Equal("h264_amf", (string?)Data(result)["videoEncoder"]);
    }

    [Fact]
    public async Task InstalledFfmpegMakesABaselineClipWithAWorkingGpu()
    {
        if (InstalledFfmpeg.Find() is not string installed) return; // ffmpeg is optional and absent on CI.
        VideoEncoderSelector encoders = new(HardwareEncoding.Auto);
        if (await encoders.ChooseAsync(new FfmpegProcess(), installed, default) == VideoEncoder.Software) return; // No usable GPU here.
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(), ffmpeg: new FfmpegLocator(installed),
            videoEncoders: encoders);

        McpToolResult<object> result = await service.CreateVideoFromImageAsync(project, image, output, false, 5m, "zoomIn", 1.2m,
            0.5m, 0.5m, null, null, null, default);

        Assert.True(result.Success, result.Message);
        Assert.NotEqual("libx264", (string?)Data(result)["videoEncoder"]);
        string ffprobe = Path.Combine(Path.GetDirectoryName(installed)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) return;
        FfmpegRunResult probe = await new FfmpegProcess().RunAsync(ffprobe, new[] { "-v", "error", "-select_streams", "v", "-show_entries",
            "stream=codec_name,profile,pix_fmt,width,height,has_b_frames,nb_frames", "-of", "default=noprint_wrappers=1", output },
            TimeSpan.FromSeconds(30), default);
        Assert.Contains("codec_name=h264", probe.StandardOutput);
        Assert.Matches(@"profile=(Constrained )?Baseline\r?\n", probe.StandardOutput);
        Assert.Contains("width=1920", probe.StandardOutput);
        Assert.Contains("pix_fmt=yuv420p", probe.StandardOutput);
        Assert.Contains("has_b_frames=0", probe.StandardOutput);
        Assert.Contains("nb_frames=300", probe.StandardOutput);
    }

    [Fact]
    public async Task FlipbookPreparesEachDistinctImageOnceAndListsTheFrames()
    {
        string second = CopyImage("second.png");

        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[]
        {
            new(image), new(second, 0.25m), new(image),
        }, loops: 10);

        Assert.True(result.Success, result.Message);
        Assert.Equal(FakeRunner.Encoded, File.ReadAllBytes(output));
        Assert.Equal(1 + 2 + 1, runner.Calls.Count);
        Assert.Equal(new[] { "file:" + image, "file:" + second }, runner.Calls.Skip(1).Take(2).Select(call => call[call.ToList().IndexOf("-i") + 1]));
        Assert.Contains("file '0001.png'\noption framerate 60\nduration 0.133333333\nfile '0002.png'", runner.ConcatList);
        Assert.False(Directory.Exists(runner.WorkDirectory));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        JsonObject data = Data(result);
        Assert.Equal((8 + 15 + 8) * 10, (int?)data["frameCount"]);
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
    public async Task FlipbookOfOneSecondIsAccepted()
    {
        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[] { new(image, 1m) });

        Assert.True(result.Success, result.Message);
        Assert.Equal(1m, (decimal?)Data(result)["duration"]);
        Assert.Equal(TimeSpan.FromSeconds(70), runner.Timeouts[^1]);
    }

    [Fact]
    public async Task FlipbookShorterThanOneSecondIsRejected()
    {
        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[] { new(image, 0.98m) });

        Assert.Equal("MCP_VIDEO_ARGUMENT_INVALID", result.ErrorCode);
        Assert.Contains("at least 1 second", result.Message);
        Assert.Single(runner.Calls);
        AssertNoOutput();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60.1)]
    [InlineData(3600)]
    [InlineData(500000)]
    public async Task VideoDurationHasNoUpperPolicyLimitAndExtendsEncodingTimeout(double seconds)
    {
        decimal duration = (decimal)seconds;

        McpToolResult<object> result = await Create(Service(), duration);

        Assert.True(result.Success, result.Message);
        Assert.Equal(duration, (decimal?)Data(result)["duration"]);
        Assert.Equal(TimeSpan.FromSeconds(60 + seconds * 10), runner.Timeouts[^1]);
    }

    [Fact]
    public async Task FlipbookLongerThanSixtySecondsUsesALongerEncodingTimeout()
    {
        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[] { new(image, 10m), new(image, 10m) }, loops: 4);

        Assert.True(result.Success, result.Message);
        Assert.Equal(80m, (decimal?)Data(result)["duration"]);
        Assert.Equal(TimeSpan.FromSeconds(860), runner.Timeouts[^1]);
    }

    [Fact]
    public async Task FlipbookReportsTheImageThatCouldNotBePrepared()
    {
        runner.Encode = arguments => arguments.Contains("concat") ? Succeed(arguments) : new(1, "", "Invalid PNG signature", false);

        McpToolResult<object> result = await Flipbook(Service(), new FlipbookFrameDto[] { new(image, 5m) });

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
        FfmpegRunResult version = await process.RunAsync(installed, new[] { "-version" }, TimeSpan.FromDays(60), default);
        Assert.True(version.Succeeded, version.StandardError);
        string[] colors = { "red", "lime", "blue" };
        foreach (string color in colors)
        {
            FfmpegRunResult made = await process.RunAsync(installed, new[] { "-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={color}:s=640x480",
                "-frames:v", "1", Path.Combine(root, color + ".png") }, TimeSpan.FromSeconds(30), default);
            Assert.True(made.Succeeded, made.StandardError);
        }
        RecotteToolService service = new(new WorkspacePathPolicy(new(root)), new(), new(), ffmpeg: new FfmpegLocator(installed));

        // 7, 8, and 1 frames at 60 fps, repeated to make at least five seconds.
        McpToolResult<object> result = await service.CreateFlipbookVideoAsync(project, new FlipbookFrameDto[]
        {
            new(Path.Combine(root, "red.png"), 7m / 60m), new(Path.Combine(root, "lime.png"), 8m / 60m), new(Path.Combine(root, "blue.png"), 1m / 60m),
        }, output, false, 0.125m, 20, null, null, default);

        Assert.True(result.Success, result.Message);
        string raw = Path.Combine(root, "frames.rgb");
        FfmpegRunResult decoded = await process.RunAsync(installed, new[] { "-v", "error", "-y", "-i", output, "-vf", "scale=1:1:flags=area",
            "-f", "rawvideo", "-pix_fmt", "rgb24", raw }, TimeSpan.FromSeconds(30), default);
        Assert.True(decoded.Succeeded, decoded.StandardError);
        string shown = string.Concat(File.ReadAllBytes(raw).Chunk(3).Select(pixel =>
            pixel[0] > 150 && pixel[1] < 80 ? 'R' : pixel[1] > 150 && pixel[0] < 80 ? 'G' : pixel[2] > 150 && pixel[0] < 80 ? 'B' : '?'));
        Assert.Equal(string.Concat(Enumerable.Repeat("RRRRRRRGGGGGGGGB", 20)), shown);
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

    private RecotteToolService Service(FfmpegLocator? locator = null, HardwareEncoding hardware = HardwareEncoding.Off) =>
        new(new WorkspacePathPolicy(new(root)), new(), new(), ffmpeg: locator ?? new FfmpegLocator(Path.Combine(root, "ffmpeg.exe")),
            ffmpegRunner: runner, videoEncoders: new(hardware));

    private static string Codec(IReadOnlyList<string> arguments) => arguments[arguments.ToList().IndexOf("-c:v") + 1];
    private static bool IsProbe(IReadOnlyList<string> arguments) => arguments[^1] == "-";

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
        public List<TimeSpan> Timeouts { get; } = new();
        public string Version { get; set; } = "ffmpeg version 7.1-test Copyright (c) 2000-2024 the FFmpeg developers";
        public Func<IReadOnlyList<string>, FfmpegRunResult>? Encode { get; set; }
        public Func<IReadOnlyList<string>, Task<FfmpegRunResult>>? Intercept { get; set; }
        public HashSet<string> WorkingEncoders { get; } = new();
        public string? ConcatList { get; private set; }
        public string? WorkDirectory { get; private set; }

        public Task<FfmpegRunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            Timeouts.Add(timeout);
            if (arguments.Contains("-update")) WorkDirectory = Path.GetDirectoryName(arguments[^1]["file:".Length..]);
            if (arguments.Contains("concat")) ConcatList = File.ReadAllText(arguments[arguments.ToList().IndexOf("concat") + 4]["file:".Length..]);
            if (arguments.SequenceEqual(new[] { "-version" })) return Task.FromResult(new FfmpegRunResult(0, Version + "\n", "", false));
            if (Intercept is not null) return Intercept(arguments);
            if (IsProbe(arguments)) return Task.FromResult(new FfmpegRunResult(WorkingEncoders.Contains(Codec(arguments)) ? 0 : 1, "", "", false));
            if (Encode is not null) return Task.FromResult(Encode(arguments));
            WriteOutput(arguments);
            return Task.FromResult(new FfmpegRunResult(0, "", "", false));
        }
    }
}
