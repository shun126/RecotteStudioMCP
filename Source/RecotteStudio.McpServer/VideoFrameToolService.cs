using System.Text.Json;

namespace RecotteStudio.McpServer;

public sealed partial class RecotteToolService
{
    public async Task<McpToolResult<object>> ExtractVideoFramesAsync(string videoPath, string outputPath,
        VideoFrameExtraction settings, CancellationToken cancellationToken)
    {
        try { settings.Validate(); }
        catch (ArgumentException exception) { return Fail("argument", "MCP_FRAME_ARGUMENT_INVALID", exception.Message); }
        PathPolicyResult input = paths.ValidateInput(videoPath, VideoFrameExtraction.VideoExtensions);
        if (!input.Success) return PathFailure(input);
        PathPolicyResult output = paths.ValidateOutput(outputPath, ".json");
        if (!output.Success) return PathFailure(output);
        FfmpegResolution resolution = ffmpeg.Resolve();
        if (!resolution.Found) return FfmpegUnavailable(resolution.Message!);
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        string? work = null;
        string? temporaryManifest = null;
        bool published = false;
        try
        {
            if (File.Exists(output.FullPath)) return Fail("path", "MCP_PATH_OUTPUT_EXISTS", "The manifest already exists; choose a new outputPath.");
            FfmpegProbeResult probe = await FfmpegLocator.ProbeAsync(ffmpegRunner, resolution.ExecutablePath!, cancellationToken);
            if (!probe.Success) return FfmpegUnavailable(probe.Message!);
            work = Path.Combine(Path.GetDirectoryName(output.FullPath!)!, $"{Path.GetFileNameWithoutExtension(output.FullPath)}.frames-{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);
            FfmpegRunResult run;
            await videoJobs.WaitAsync(cancellationToken);
            try { run = await ffmpegRunner.RunAsync(resolution.ExecutablePath!, settings.Arguments(input.FullPath!, work),
                TimeSpan.FromMinutes(10), cancellationToken); }
            finally { videoJobs.Release(); }
            McpToolResult<object>? failure = EncodingFailure(run, null);
            if (failure is not null) return failure;
            string[] images = Directory.GetFiles(work, "frame-*.jpg").Order(StringComparer.Ordinal).ToArray();
            if (images.Length == 0) return Fail("invalid", "MCP_FRAME_NO_FRAMES", "No video frames were found in the requested range.");
            IReadOnlyDictionary<int, decimal> timestamps = VideoFrameExtraction.ReadTimestamps(run.StandardError);
            List<object> frames = new();
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != Path.Combine(work, $"frame-{i + 1:0000}.jpg") || !timestamps.TryGetValue(i + 1, out decimal time) ||
                    time < settings.Start || time >= settings.Start + settings.Duration || new FileInfo(images[i]).Length == 0)
                    return Fail("ffmpeg", "MCP_FRAME_TIMESTAMPS_INVALID", "Extracted images could not be matched to source timestamps.");
                frames.Add(new { index = i + 1, sourceTime = time, imagePath = images[i] });
            }
            var manifest = new { videoPath = input.FullPath, outputPath = output.FullPath, settings,
                timestampBasis = "source video seconds, normalized to its first decoded frame; not project timeline time",
                frameCount = frames.Count, limitReached = frames.Count == settings.MaxFrames, frames };
            temporaryManifest = Path.Combine(Path.GetDirectoryName(output.FullPath!)!, $".{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporaryManifest, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryManifest, output.FullPath!);
            temporaryManifest = null;
            published = true;
            return Ok(manifest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ExceptionResult(exception); }
        finally
        {
            if (temporaryManifest is not null) File.Delete(temporaryManifest);
            if (!published && work is not null && Directory.Exists(work)) Directory.Delete(work, true);
        }
    }
}
