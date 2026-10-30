using System.Globalization;

namespace RecotteStudio.McpServer.Tests;

public sealed class VideoClipTests
{
    private static readonly VideoFrameFormat FullHd60 = new(1920, 1080, 60m);

    [Fact]
    public void DefaultsAreAnEasedZoomInThatCoversTheScreen()
    {
        VideoClipSettings settings = Parse(duration: 5m)!;

        Assert.Equal(new VideoClipSettings(5m, VideoMotion.ZoomIn, 1.2m, 0.5m, 0.5m, VideoEasing.EaseInOut, VideoFit.Cover, "#000000"), settings);
    }

    [Theory]
    [InlineData(0.4, null, 1.2, 0.5, null, null, null, "duration")]
    [InlineData(0.999, null, 1.2, 0.5, null, null, null, "duration")]
    [InlineData(5, "spin", 1.2, 0.5, null, null, null, "motion")]
    [InlineData(5, "1", 1.2, 0.5, null, null, null, "motion")]
    [InlineData(5, "none,zoomIn", 1.2, 0.5, null, null, null, "motion")]
    [InlineData(5, null, 1.0, 0.5, null, null, null, "zoom")]
    [InlineData(5, null, 1.6, 0.5, null, null, null, "zoom")]
    [InlineData(5, null, 1.2, 1.1, null, null, null, "focusX")]
    [InlineData(5, null, 1.2, 0.5, "bounce", null, null, "easing")]
    [InlineData(5, null, 1.2, 0.5, null, "stretch", null, "fit")]
    [InlineData(5, null, 1.2, 0.5, null, null, "red", "background")]
    [InlineData(5, null, 1.2, 0.5, null, null, "#FFF", "background")]
    [InlineData(5, null, 1.2, 0.5, null, null, "#000000:x=1", "background")]
    public void OutOfRangeArgumentsAreRejected(double duration, string? motion, double zoom, double focus, string? easing,
        string? fit, string? background, string named)
    {
        VideoClipSettings? settings = VideoClip.Parse((decimal)duration, motion, (decimal)zoom, (decimal)focus, 0.5m, easing, fit,
            background, out string? error);

        Assert.Null(settings);
        Assert.Contains(named, error);
    }

    [Fact]
    public void NamesAreMatchedIgnoringCase()
    {
        VideoClipSettings settings = Parse(motion: "PANLEFT", easing: "linear", fit: "Contain", background: "#a0b1c2")!;

        Assert.Equal(VideoMotion.PanLeft, settings.Motion);
        Assert.Equal(VideoEasing.Linear, settings.Easing);
        Assert.Equal(VideoFit.Contain, settings.Fit);
        Assert.Equal("#A0B1C2", settings.Background);
    }

    [Fact]
    public void ArgumentsUseFixedFormatAndQuoteNoCallerText()
    {
        IReadOnlyList<string> arguments = Build(Parse(duration: 5m, focusY: 0.4m)!);

        Assert.Equal("file:" + Path.GetFullPath("in.png"), After(arguments, "-i"));
        Assert.Equal("file:" + Path.GetFullPath("out.tmp"), arguments[^1]);
        Assert.Equal("[0:v]trim=end_frame=1,scale=3840:2160:force_original_aspect_ratio=increase:flags=lanczos,crop=3840:2160," +
            "zoompan=z='1+0.2*(1-cos(PI*on/299))/2':x='(iw-iw/zoom)*0.5':y='(ih-ih/zoom)*0.4':d=300:s=1920x1080:fps=60," +
            "format=yuv420p,setsar=1[v]", After(arguments, "-filter_complex"));
        Assert.Equal("300", After(arguments, "-frames:v"));
        Assert.Equal("5", After(arguments, "-t"));
        Assert.Equal("libx264", After(arguments, "-c:v"));
        Assert.Equal("baseline", After(arguments, "-profile:v"));
        Assert.Equal(new[] { "-f", "mp4", "-movflags", "+faststart" }, arguments.Skip(arguments.Count - 5).Take(4));
        Assert.Contains("-nostdin", arguments);
    }

    [Theory]
    [InlineData("none", "1", "0", "0")]
    [InlineData("zoomOut", "1.2-0.2*on/299", "(iw-iw/zoom)*0.5", "(ih-ih/zoom)*0.5")]
    [InlineData("panRight", "1.2", "(iw-iw/zoom)*on/299", "(ih-ih/zoom)*0.5")]
    [InlineData("panLeft", "1.2", "(iw-iw/zoom)*(1-on/299)", "(ih-ih/zoom)*0.5")]
    [InlineData("panDown", "1.2", "(iw-iw/zoom)*0.5", "(ih-ih/zoom)*on/299")]
    [InlineData("panUp", "1.2", "(iw-iw/zoom)*0.5", "(ih-ih/zoom)*(1-on/299)")]
    public void EachMotionMovesTheVisibleWindow(string motion, string zoom, string x, string y)
    {
        string filter = After(Build(Parse(motion: motion, easing: "linear")!), "-filter_complex");

        Assert.Contains($"zoompan=z='{zoom}':x='{x}':y='{y}':d=300:", filter);
    }

    [Fact]
    public void ContainPadsWithTheBackgroundColor()
    {
        string filter = After(Build(Parse(fit: "contain", background: "#102030")!), "-filter_complex");

        Assert.Contains("scale=3840:2160:force_original_aspect_ratio=decrease:flags=lanczos,pad=3840:2160:(ow-iw)/2:(oh-ih)/2:color=0x102030,", filter);
    }

    [Fact]
    public void FrameCountRoundsDurationTimesRate()
    {
        IReadOnlyList<string> arguments = VideoClip.BuildArguments("in.png", "out.tmp", Parse(duration: 5.01m)!, new(1920, 1080, 29.97m));

        Assert.Equal("150", After(arguments, "-frames:v"));
        Assert.Equal("5.005005", After(arguments, "-t"));
        Assert.Contains(":fps=29.97,", After(arguments, "-filter_complex"));
    }

    [Fact]
    public void LongClipFrameCountDoesNotOverflowThirtyTwoBits()
    {
        IReadOnlyList<string> arguments = Build(Parse(duration: 40000000m)!);

        Assert.Equal("2400000000", After(arguments, "-frames:v"));
        Assert.Equal("40000000", After(arguments, "-t"));
    }

    [Fact]
    public void NumbersIgnoreTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string filter = After(Build(Parse(zoom: 1.25m, focusX: 0.3m)!), "-filter_complex");

            Assert.Contains("1+0.25*", filter);
            Assert.Contains("(iw-iw/zoom)*0.3", filter);
            Assert.DoesNotContain("0,25", filter);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static VideoClipSettings? Parse(decimal duration = 5m, string? motion = null, decimal zoom = 1.2m, decimal focusX = 0.5m,
        decimal focusY = 0.5m, string? easing = null, string? fit = null, string? background = null) =>
        VideoClip.Parse(duration, motion, zoom, focusX, focusY, easing, fit, background, out _);

    private static IReadOnlyList<string> Build(VideoClipSettings settings) =>
        VideoClip.BuildArguments("in.png", "out.tmp", settings, FullHd60);

    private static string After(IReadOnlyList<string> arguments, string option) =>
        arguments[arguments.ToList().IndexOf(option) + 1];
}
