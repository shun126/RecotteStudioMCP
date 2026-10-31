using System.Globalization;
using System.Text;

namespace RecotteStudio.McpServer;

/// <summary>A validated request to show still images one after another like a flipbook.</summary>
public sealed record FlipbookSettings(decimal FrameDuration, int Loops, VideoFit Fit, string Background);

/// <summary>
/// Builds the two ffmpeg steps of a flipbook. Each distinct image is first drawn once onto a screen-sized background,
/// then a concat list shows those frames for whole numbers of project frames. Only file names this class creates
/// enter the list, so turning off the concat demuxer's safe mode cannot reach any other file.
/// </summary>
public static class FlipbookVideo
{
    public const int MaxFrames = 600, MaxImages = 200, MaxLoops = 100;
    public const decimal MinFrameDuration = 0.01m, MaxFrameDuration = 10m, DefaultFrameDuration = 0.125m;
    public const string DefaultBackground = "#FFFFFF";

    /// <summary>Parses and range-checks the options shared by every frame; returns null and a message when any is invalid.</summary>
    public static FlipbookSettings? Parse(decimal frameDuration, int loops, string? fit, string? background, out string? error)
    {
        bool fitValid = VideoClip.TryParse(fit, VideoFit.Contain, out VideoFit parsedFit);
        error = !IsFrameDuration(frameDuration) ? $"frameDuration must be between {MinFrameDuration} and {MaxFrameDuration} seconds."
            : loops is < 1 or > MaxLoops ? $"loops must be between 1 and {MaxLoops}."
            : !fitValid ? $"fit must be one of {VideoClip.Names<VideoFit>()}."
            : background is not null && !VideoClip.IsColor(background) ? "background must be a #RRGGBB color."
            : null;
        return error is null ? new(frameDuration, loops, parsedFit, (background ?? DefaultBackground).ToUpperInvariant()) : null;
    }

    public static bool IsFrameDuration(decimal seconds) => seconds is >= MinFrameDuration and <= MaxFrameDuration;

    /// <summary>Converts a frame's duration into whole project frames; every frame is shown for at least one.</summary>
    public static int Hold(decimal seconds, decimal fps) => Math.Max(1, (int)Math.Round(seconds * fps, MidpointRounding.AwayFromZero));

    /// <summary>The name of the prepared frame for the distinct image at <paramref name="index"/>.</summary>
    public static string FrameFileName(int index) => (index + 1).ToString("0000", CultureInfo.InvariantCulture) + ".png";

    /// <summary>Draws one image, scaled to fit, centred on the background; transparent parts show the background.</summary>
    public static IReadOnlyList<string> PrepareArguments(string imagePath, string framePath, FlipbookSettings settings, VideoFrameFormat format)
    {
        int width = format.Width, height = format.Height;
        string scale = settings.Fit == VideoFit.Cover
            ? $"scale={width}:{height}:force_original_aspect_ratio=increase:flags=lanczos,crop={width}:{height}"
            : $"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=lanczos";
        return new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-i", VideoClip.FileUrl(imagePath),
            "-filter_complex", $"color=c=0x{settings.Background[1..]}:s={width}x{height}[bg];" +
                $"[0:v]trim=end_frame=1,{scale},format=rgba[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2:shortest=1,format=rgb24,setsar=1[v]",
            "-map", "[v]", "-frames:v", "1", "-update", "1", "-f", "image2", VideoClip.FileUrl(framePath),
        };
    }

    /// <summary>
    /// Lists the prepared frames in showing order. framerate gives each image the project's time base, so every
    /// duration of whole project frames lands exactly on a frame boundary. The last entry is repeated without a
    /// duration because the concat demuxer otherwise ignores the final one.
    /// </summary>
    public static string ConcatList(IReadOnlyList<int> images, IReadOnlyList<int> holds, int loops, decimal fps)
    {
        string rate = VideoClip.Number(fps);
        StringBuilder list = new("ffconcat version 1.0\n");
        for (int loop = 0; loop < loops; loop++)
            for (int index = 0; index < images.Count; index++)
                list.Append($"file '{FrameFileName(images[index])}'\noption framerate {rate}\nduration ")
                    .Append((holds[index] / fps).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        list.Append($"file '{FrameFileName(images[^1])}'\noption framerate {rate}\n");
        return list.ToString();
    }

    /// <summary>Encodes the listed frames at the project frame rate into the fixed MP4 format.</summary>
    public static IReadOnlyList<string> EncodeArguments(string listPath, string outputPath, long frames, VideoFrameFormat format,
        VideoEncoder encoder = VideoEncoder.Software) =>
        VideoClip.EncodeArguments(new[] { "-f", "concat", "-safe", "0", "-i", VideoClip.FileUrl(listPath) },
            $"[0:v]fps={VideoClip.Number(format.FramesPerSecond)},format=yuv420p,setsar=1[v]", frames, format.FramesPerSecond, outputPath, encoder);
}
