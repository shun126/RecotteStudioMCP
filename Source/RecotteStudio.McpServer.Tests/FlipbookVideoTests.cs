namespace RecotteStudio.McpServer.Tests;

public sealed class FlipbookVideoTests
{
    private static readonly VideoFrameFormat FullHd60 = new(1920, 1080, 60m);

    [Fact]
    public void DefaultsAreEightDrawingsPerSecondContainedOnWhite()
    {
        FlipbookSettings settings = FlipbookVideo.Parse(FlipbookVideo.DefaultFrameDuration, 1, null, null, out _)!;

        Assert.Equal(new FlipbookSettings(0.125m, 1, VideoFit.Contain, "#FFFFFF"), settings);
    }

    [Theory]
    [InlineData(0.009, 1, null, null, "frameDuration")]
    [InlineData(10.1, 1, null, null, "frameDuration")]
    [InlineData(0.125, 0, null, null, "loops")]
    [InlineData(0.125, 101, null, null, "loops")]
    [InlineData(0.125, 1, "stretch", null, "fit")]
    [InlineData(0.125, 1, null, "white", "background")]
    public void OutOfRangeOptionsAreRejected(double frameDuration, int loops, string? fit, string? background, string named)
    {
        Assert.Null(FlipbookVideo.Parse((decimal)frameDuration, loops, fit, background, out string? error));
        Assert.Contains(named, error);
    }

    [Theory]
    [InlineData(0.125, 60, 8)]
    [InlineData(0.1, 60, 6)]
    [InlineData(0.01, 60, 1)]
    [InlineData(0.04, 29.97, 1)]
    [InlineData(1, 29.97, 30)]
    public void HoldsAreWholeProjectFramesAndAtLeastOne(double seconds, double fps, int expected) =>
        Assert.Equal(expected, FlipbookVideo.Hold((decimal)seconds, (decimal)fps));

    [Fact]
    public void ConcatListShowsEachFrameForItsHoldAndRepeatsTheLastEntry()
    {
        string list = FlipbookVideo.ConcatList(new[] { 0, 1, 0 }, new[] { 7, 8, 1 }, 2, 60m);

        Assert.Equal(
            "ffconcat version 1.0\n" +
            "file '0001.png'\noption framerate 60\nduration 0.116666667\n" +
            "file '0002.png'\noption framerate 60\nduration 0.133333333\n" +
            "file '0001.png'\noption framerate 60\nduration 0.016666667\n" +
            "file '0001.png'\noption framerate 60\nduration 0.116666667\n" +
            "file '0002.png'\noption framerate 60\nduration 0.133333333\n" +
            "file '0001.png'\noption framerate 60\nduration 0.016666667\n" +
            "file '0001.png'\noption framerate 60\n", list);
    }

    [Fact]
    public void PreparingDrawsTheImageCentredOverTheBackground()
    {
        FlipbookSettings settings = FlipbookVideo.Parse(0.125m, 1, "cover", "#102030", out _)!;

        IReadOnlyList<string> arguments = FlipbookVideo.PrepareArguments("in.png", "0001.png", settings, FullHd60);

        Assert.Equal("file:" + Path.GetFullPath("in.png"), arguments[arguments.ToList().IndexOf("-i") + 1]);
        Assert.Equal("color=c=0x102030:s=1920x1080[bg];[0:v]trim=end_frame=1,scale=1920:1080:force_original_aspect_ratio=increase:flags=lanczos," +
            "crop=1920:1080,format=rgba[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2:shortest=1,format=rgb24,setsar=1[v]",
            arguments[arguments.ToList().IndexOf("-filter_complex") + 1]);
        Assert.Equal("file:" + Path.GetFullPath("0001.png"), arguments[^1]);
    }

    [Fact]
    public void EncodingReadsTheListAtTheProjectRate()
    {
        IReadOnlyList<string> arguments = FlipbookVideo.EncodeArguments("frames.ffconcat", "out.tmp", 32, new(1920, 1080, 29.97m));
        List<string> list = arguments.ToList();

        Assert.Equal(new[] { "-f", "concat", "-safe", "0", "-i", "file:" + Path.GetFullPath("frames.ffconcat") }, list.GetRange(list.IndexOf("concat") - 1, 6));
        Assert.Equal("[0:v]fps=29.97,format=yuv420p,setsar=1[v]", list[list.IndexOf("-filter_complex") + 1]);
        Assert.Equal("32", list[list.IndexOf("-frames:v") + 1]);
        Assert.Equal("baseline", list[list.IndexOf("-profile:v") + 1]);
    }
}
