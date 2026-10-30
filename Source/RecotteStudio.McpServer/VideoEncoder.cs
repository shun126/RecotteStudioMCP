using System.Collections.Concurrent;

namespace RecotteStudio.McpServer;

/// <summary>The H.264 encoders a clip can be made with. Software is libx264; the others use the GPU.</summary>
public enum VideoEncoder { Software, Nvidia, Intel, Amd }

/// <summary>The setting in the settings window. Auto uses the first GPU encoder that works, otherwise libx264.</summary>
public enum HardwareEncoding { Auto, Off, Nvidia, Intel, Amd }

/// <summary>The fixed ffmpeg options of each encoder.</summary>
public static class VideoEncoders
{
    /// <summary>The GPU encoders in the order Auto tries them.</summary>
    public static readonly VideoEncoder[] Hardware = { VideoEncoder.Nvidia, VideoEncoder.Intel, VideoEncoder.Amd };

    public static string CodecName(VideoEncoder encoder) => encoder switch
    {
        VideoEncoder.Nvidia => "h264_nvenc",
        VideoEncoder.Intel => "h264_qsv",
        VideoEncoder.Amd => "h264_amf",
        _ => "libx264",
    };

    /// <summary>
    /// The video codec options. Every encoder writes the baseline profile, which has no B-frames, as the clips verified in
    /// Recotte Studio do, at a quality close to libx264 CRF 18.
    /// </summary>
    public static IReadOnlyList<string> Arguments(VideoEncoder encoder) => encoder switch
    {
        VideoEncoder.Nvidia => new[] { "-c:v", "h264_nvenc", "-profile:v", "baseline", "-preset", "p5", "-rc", "vbr", "-cq", "18", "-b:v", "0" },
        VideoEncoder.Intel => new[] { "-c:v", "h264_qsv", "-profile:v", "baseline", "-preset", "medium", "-global_quality", "18" },
        VideoEncoder.Amd => new[] { "-c:v", "h264_amf", "-profile:v", "constrained_baseline", "-quality", "balanced", "-rc", "cqp", "-qp_i", "18", "-qp_p", "18" },
        _ => new[] { "-c:v", "libx264", "-profile:v", "baseline", "-preset", "medium", "-crf", "18" },
    };

    /// <summary>Encodes a few black frames to nowhere with the same options as a real clip, to test that the encoder works.</summary>
    public static IReadOnlyList<string> ProbeArguments(VideoEncoder encoder) =>
        new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=256x256:r=30", "-frames:v", "5",
            "-pix_fmt", "yuv420p" }
            .Concat(Arguments(encoder)).Concat(new[] { "-f", "null", "-" }).ToArray();
}

/// <summary>
/// Chooses the encoder for a clip from the setting. A GPU encoder is tested once by encoding a few frames, because
/// ffmpeg lists the encoders it was built with even when the GPU or its driver is missing. Results are kept for each
/// ffmpeg executable until the setting changes, the user checks again, or the encoder fails while making a clip.
/// </summary>
public sealed class VideoEncoderSelector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, bool> works = new(StringComparer.OrdinalIgnoreCase);
    private volatile HardwareEncoding preference;

    /// <summary>Without a setting from the settings window, clips are made with libx264.</summary>
    public VideoEncoderSelector(HardwareEncoding preference = HardwareEncoding.Off) => this.preference = preference;

    /// <summary>Gets or sets the setting; changing it tests the encoders again.</summary>
    public HardwareEncoding Preference
    {
        get => preference;
        set { preference = value; works.Clear(); }
    }

    /// <summary>Forgets which encoders work, so the next clip tests them again.</summary>
    public void Forget() => works.Clear();

    /// <summary>Returns the first encoder the setting allows that works, or libx264.</summary>
    public async Task<VideoEncoder> ChooseAsync(IFfmpegRunner runner, string executablePath, CancellationToken cancellationToken)
    {
        IEnumerable<VideoEncoder> candidates = preference switch
        {
            HardwareEncoding.Auto => VideoEncoders.Hardware,
            HardwareEncoding.Nvidia => new[] { VideoEncoder.Nvidia },
            HardwareEncoding.Intel => new[] { VideoEncoder.Intel },
            HardwareEncoding.Amd => new[] { VideoEncoder.Amd },
            _ => Array.Empty<VideoEncoder>(),
        };
        foreach (VideoEncoder encoder in candidates)
            if (await WorksAsync(runner, executablePath, encoder, cancellationToken)) return encoder;
        return VideoEncoder.Software;
    }

    /// <summary>Records that a GPU encoder failed on a clip that libx264 then made, so it is not chosen again.</summary>
    public void MarkFailed(string executablePath, VideoEncoder encoder) => works[Key(executablePath, encoder)] = false;

    private async Task<bool> WorksAsync(IFfmpegRunner runner, string executablePath, VideoEncoder encoder, CancellationToken cancellationToken)
    {
        string key = Key(executablePath, encoder);
        if (works.TryGetValue(key, out bool known)) return known;
        FfmpegRunResult run = await runner.RunAsync(executablePath, VideoEncoders.ProbeArguments(encoder), ProbeTimeout, cancellationToken);
        return works[key] = run.Succeeded;
    }

    // The modified time is part of the key, so replacing ffmpeg in place tests the encoders again.
    private static string Key(string executablePath, VideoEncoder encoder)
    {
        string full = Path.GetFullPath(executablePath);
        return $"{full}|{File.GetLastWriteTimeUtc(full).Ticks}|{encoder}";
    }
}
