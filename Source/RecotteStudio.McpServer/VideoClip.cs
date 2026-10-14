using System.Globalization;
using System.Text.RegularExpressions;

namespace RecotteStudio.McpServer;

public enum VideoMotion { None, ZoomIn, ZoomOut, PanLeft, PanRight, PanUp, PanDown }
public enum VideoEasing { Linear, EaseInOut }
public enum VideoFit { Cover, Contain }

/// <summary>A validated request to turn one still image into a video clip.</summary>
public sealed record VideoClipSettings(decimal Duration, VideoMotion Motion, decimal Zoom, decimal FocusX, decimal FocusY,
    VideoEasing Easing, VideoFit Fit, string Background);

/// <summary>The output frame size and rate, taken from the project.</summary>
public sealed record VideoFrameFormat(int Width, int Height, decimal FramesPerSecond)
{
    public int FrameCount(decimal duration) => (int)Math.Round(duration * FramesPerSecond, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Validates clip settings and builds the fixed ffmpeg arguments. Only validated numbers and fixed keywords enter the
/// filter graph, always formatted with the invariant culture, so no caller text can change what ffmpeg runs.
/// </summary>
public static partial class VideoClip
{
    /// <summary>Image types Recotte Studio can import, which are also the accepted inputs.</summary>
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    /// <summary>Describes the output, which matches the video verified in Documents/RecotteProjects/005OneVideo.</summary>
    public const string OutputFormat = "H.264 Constrained Baseline, yuv420p, with a silent AAC-LC 44.1 kHz stereo track";

    public const decimal MinDuration = 0.5m, MaxDuration = 60m, MinZoom = 1.05m, MaxZoom = 1.5m;

    // Pan and zoom run on a frame twice the output size, so zoompan's whole-pixel crop does not make the motion shake.
    private const int Oversample = 2;

    /// <summary>Parses and range-checks the tool arguments; returns null and a message when any is invalid.</summary>
    public static VideoClipSettings? Parse(decimal duration, string? motion, decimal zoom, decimal focusX, decimal focusY,
        string? easing, string? fit, string? background, out string? error)
    {
        bool motionValid = TryParse(motion, VideoMotion.ZoomIn, out VideoMotion parsedMotion);
        bool easingValid = TryParse(easing, VideoEasing.EaseInOut, out VideoEasing parsedEasing);
        bool fitValid = TryParse(fit, VideoFit.Cover, out VideoFit parsedFit);
        error = duration is < MinDuration or > MaxDuration ? $"duration must be between {MinDuration} and {MaxDuration} seconds."
            : !motionValid ? $"motion must be one of {Names<VideoMotion>()}."
            : zoom is < MinZoom or > MaxZoom ? $"zoom must be between {MinZoom} and {MaxZoom}."
            : focusX is < 0m or > 1m || focusY is < 0m or > 1m ? "focusX and focusY must be between 0 and 1."
            : !easingValid ? $"easing must be one of {Names<VideoEasing>()}."
            : !fitValid ? $"fit must be one of {Names<VideoFit>()}."
            : background is not null && !IsColor(background) ? "background must be a #RRGGBB color."
            : null;
        return error is null
            ? new(duration, parsedMotion, zoom, focusX, focusY, parsedEasing, parsedFit, (background ?? "#000000").ToUpperInvariant())
            : null;
    }

    /// <summary>Builds the ffmpeg arguments that write the clip to <paramref name="outputPath"/> as MP4.</summary>
    public static IReadOnlyList<string> BuildArguments(string imagePath, string outputPath, VideoClipSettings settings, VideoFrameFormat format)
    {
        int frames = format.FrameCount(settings.Duration);
        int wide = format.Width * Oversample, high = format.Height * Oversample;
        string fit = settings.Fit == VideoFit.Cover
            ? $"scale={wide}:{high}:force_original_aspect_ratio=increase:flags=lanczos,crop={wide}:{high}"
            : $"scale={wide}:{high}:force_original_aspect_ratio=decrease:flags=lanczos,pad={wide}:{high}:(ow-iw)/2:(oh-ih)/2:color=0x{settings.Background[1..]}";
        (string zoom, string x, string y) = Motion(settings, frames);
        string fps = Number(format.FramesPerSecond);
        // trim keeps only the first frame, so an animated GIF yields exactly the requested number of frames.
        string filter = $"[0:v]trim=end_frame=1,{fit},zoompan=z='{zoom}':x='{x}':y='{y}':d={frames}:s={format.Width}x{format.Height}:fps={fps}," +
            "format=yuv420p,setsar=1[v]";
        return EncodeArguments(new[] { "-i", FileUrl(imagePath) }, filter, frames, format.FramesPerSecond, outputPath);
    }

    /// <summary>
    /// Wraps one video input and a filter graph labelling its result [v] into the fixed encoding: the format in
    /// <see cref="OutputFormat"/>, exactly <paramref name="frames"/> frames, and a silent track of the same length.
    /// </summary>
    internal static IReadOnlyList<string> EncodeArguments(IEnumerable<string> videoInput, string filter, int frames, decimal fps, string outputPath) =>
        new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" }
            .Concat(videoInput)
            .Concat(new[]
            {
                "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
                "-filter_complex", filter,
                "-map", "[v]", "-map", "1:a",
                "-frames:v", Number(frames), "-t", Number(Math.Round(frames / fps, 6)),
                "-c:v", "libx264", "-profile:v", "baseline", "-preset", "medium", "-crf", "18",
                "-c:a", "aac", "-b:a", "128k", "-ar", "44100", "-ac", "2",
                "-f", "mp4", "-movflags", "+faststart", FileUrl(outputPath),
            }).ToArray();

    // file: keeps a path that looks like a URL or protocol from being opened as one.
    internal static string FileUrl(string path) => "file:" + Path.GetFullPath(path);

    // zoompan's x and y are the top-left of the visible part of the oversampled frame and are clamped to the frame.
    private static (string Zoom, string X, string Y) Motion(VideoClipSettings settings, int frames)
    {
        string last = Number(Math.Max(frames - 1, 1));
        string progress = settings.Easing == VideoEasing.Linear ? $"on/{last}" : $"(1-cos(PI*on/{last}))/2";
        string zoom = Number(settings.Zoom), rise = Number(settings.Zoom - 1m);
        string focusX = $"(iw-iw/zoom)*{Number(settings.FocusX)}", focusY = $"(ih-ih/zoom)*{Number(settings.FocusY)}";
        return settings.Motion switch
        {
            VideoMotion.None => ("1", "0", "0"),
            VideoMotion.ZoomIn => ($"1+{rise}*{progress}", focusX, focusY),
            VideoMotion.ZoomOut => ($"{zoom}-{rise}*{progress}", focusX, focusY),
            // A pan names the direction the view moves, so panRight slides the image to the left.
            VideoMotion.PanRight => (zoom, $"(iw-iw/zoom)*{progress}", focusY),
            VideoMotion.PanLeft => (zoom, $"(iw-iw/zoom)*(1-{progress})", focusY),
            VideoMotion.PanDown => (zoom, focusX, $"(ih-ih/zoom)*{progress}"),
            VideoMotion.PanUp => (zoom, focusX, $"(ih-ih/zoom)*(1-{progress})"),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    internal static bool TryParse<T>(string? value, T fallback, out T result) where T : struct, Enum
    {
        result = fallback;
        if (string.IsNullOrWhiteSpace(value)) return true;
        // Matches whole names only; Enum.TryParse would also accept numbers and comma-joined names.
        string? name = Enum.GetNames<T>().FirstOrDefault(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null) return false;
        result = Enum.Parse<T>(name);
        return true;
    }

    internal static string Names<T>() where T : struct, Enum =>
        string.Join(", ", Enum.GetNames<T>().Select(name => char.ToLowerInvariant(name[0]) + name[1..]));

    internal static string Number(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    internal static bool IsColor(string value) => ColorPattern().IsMatch(value);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
