namespace RecotteStudio.McpServer.Tests;

public sealed class VideoEncoderTests
{
    private const string Ffmpeg = @"C:\tools\ffmpeg.exe";
    private readonly ProbeRunner runner = new();

    [Theory]
    [InlineData(VideoEncoder.Software, "libx264")]
    [InlineData(VideoEncoder.Nvidia, "h264_nvenc")]
    [InlineData(VideoEncoder.Intel, "h264_qsv")]
    [InlineData(VideoEncoder.Amd, "h264_amf")]
    public void EachEncoderWritesBaselineH264(VideoEncoder encoder, string codec)
    {
        IReadOnlyList<string> arguments = VideoEncoders.Arguments(encoder);

        Assert.Equal(codec, VideoEncoders.CodecName(encoder));
        Assert.Equal(new[] { "-c:v", codec }, arguments.Take(2));
        Assert.Contains("baseline", arguments[arguments.ToList().IndexOf("-profile:v") + 1]);
    }

    [Theory]
    [InlineData(VideoEncoder.Nvidia)]
    [InlineData(VideoEncoder.Intel)]
    [InlineData(VideoEncoder.Amd)]
    public void TheTestEncodeUsesTheSameOptionsAsAClip(VideoEncoder encoder)
    {
        IReadOnlyList<string> codec = VideoEncoders.Arguments(encoder);
        IReadOnlyList<string> probe = VideoEncoders.ProbeArguments(encoder);
        IReadOnlyList<string> clip = VideoClip.BuildArguments("in.png", "out.tmp",
            VideoClip.Parse(5m, null, 1.2m, 0.5m, 0.5m, null, null, null, out _)!, new(1920, 1080, 30m), encoder);

        Assert.True(Contains(probe, codec));
        Assert.True(Contains(clip, codec));
        Assert.Equal(new[] { "-f", "null", "-" }, probe.TakeLast(3));
    }

    [Fact]
    public async Task OffNeverTestsAnEncoder()
    {
        Assert.Equal(VideoEncoder.Software, await new VideoEncoderSelector().ChooseAsync(runner, Ffmpeg, default));
        Assert.Empty(runner.Codecs);
    }

    [Fact]
    public async Task AutoUsesTheFirstGpuEncoderThatWorksAndRemembersIt()
    {
        runner.Working.Add("h264_qsv");
        VideoEncoderSelector selector = new(HardwareEncoding.Auto);

        Assert.Equal(VideoEncoder.Intel, await selector.ChooseAsync(runner, Ffmpeg, default));
        Assert.Equal(VideoEncoder.Intel, await selector.ChooseAsync(runner, Ffmpeg, default));
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv" }, runner.Codecs);
    }

    [Fact]
    public async Task AutoWithoutAWorkingGpuUsesLibx264()
    {
        Assert.Equal(VideoEncoder.Software, await new VideoEncoderSelector(HardwareEncoding.Auto).ChooseAsync(runner, Ffmpeg, default));
        Assert.Equal(new[] { "h264_nvenc", "h264_qsv", "h264_amf" }, runner.Codecs);
    }

    [Fact]
    public async Task AChosenGpuThatDoesNotWorkUsesLibx264WithoutTryingOthers()
    {
        runner.Working.Add("h264_qsv");

        Assert.Equal(VideoEncoder.Software, await new VideoEncoderSelector(HardwareEncoding.Nvidia).ChooseAsync(runner, Ffmpeg, default));
        Assert.Equal(new[] { "h264_nvenc" }, runner.Codecs);
    }

    [Fact]
    public async Task ChangingTheSettingOrCheckingAgainTestsAgain()
    {
        runner.Working.Add("h264_amf");
        VideoEncoderSelector selector = new(HardwareEncoding.Amd);
        await selector.ChooseAsync(runner, Ffmpeg, default);

        selector.Forget();
        await selector.ChooseAsync(runner, Ffmpeg, default);
        selector.Preference = HardwareEncoding.Amd;
        await selector.ChooseAsync(runner, Ffmpeg, default);

        Assert.Equal(3, runner.Codecs.Count);
    }

    [Fact]
    public async Task AnEncoderThatFailedOnAClipIsNotChosenAgain()
    {
        runner.Working.UnionWith(new[] { "h264_nvenc", "h264_qsv" });
        VideoEncoderSelector selector = new(HardwareEncoding.Auto);
        Assert.Equal(VideoEncoder.Nvidia, await selector.ChooseAsync(runner, Ffmpeg, default));

        selector.MarkFailed(Ffmpeg, VideoEncoder.Nvidia);

        Assert.Equal(VideoEncoder.Intel, await selector.ChooseAsync(runner, Ffmpeg, default));
    }

    [Fact]
    public async Task EachFfmpegIsTestedSeparately()
    {
        runner.Working.Add("h264_nvenc");
        VideoEncoderSelector selector = new(HardwareEncoding.Nvidia);

        await selector.ChooseAsync(runner, Ffmpeg, default);
        await selector.ChooseAsync(runner, @"C:\other\ffmpeg.exe", default);

        Assert.Equal(2, runner.Codecs.Count);
    }

    private static bool Contains(IReadOnlyList<string> arguments, IReadOnlyList<string> part) =>
        Enumerable.Range(0, arguments.Count - part.Count + 1).Any(start => arguments.Skip(start).Take(part.Count).SequenceEqual(part));

    private sealed class ProbeRunner : IFfmpegRunner
    {
        public HashSet<string> Working { get; } = new();
        public List<string> Codecs { get; } = new();

        public Task<FfmpegRunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            string codec = arguments[arguments.ToList().IndexOf("-c:v") + 1];
            Codecs.Add(codec);
            return Task.FromResult(Working.Contains(codec) ? new FfmpegRunResult(0, "", "", false)
                : new FfmpegRunResult(1, "", "Cannot load nvcuda.dll", false));
        }
    }
}
