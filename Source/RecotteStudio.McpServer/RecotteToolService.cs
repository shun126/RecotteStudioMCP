using RecotteStudio.Core;

namespace RecotteStudio.McpServer;

public sealed partial class RecotteToolService(WorkspacePathPolicy paths, ProjectOperationMapper operationMapper,
    OutputPathLockManager outputLocks, FigureCatalogProvider? figureCatalog = null, EffectCatalogProvider? effectCatalog = null,
    FfmpegLocator? ffmpeg = null, IFfmpegRunner? ffmpegRunner = null)
{
    private readonly FigureCatalogProvider figureCatalog = figureCatalog ?? new();
    private readonly EffectCatalogProvider effectCatalog = effectCatalog ?? new();
    private readonly FfmpegLocator ffmpeg = ffmpeg ?? new();
    private readonly IFfmpegRunner ffmpegRunner = ffmpegRunner ?? new FfmpegProcess();
    // Encoding uses every core, so clips are made one at a time.
    private readonly SemaphoreSlim videoJobs = new(1, 1);

    public McpToolResult<object> ListEffects(string? kind = null, string? tag = null, string? nameContains = null, int limit = 100)
    {
        EffectCatalog catalog;
        try { catalog = effectCatalog.Load(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return Fail("unsupported", "MCP_EFFECTS_UNAVAILABLE", exception.Message); }
        var matches = catalog.Effects.Where(effect =>
            (kind is null || string.Equals(effect.Kind, kind, StringComparison.OrdinalIgnoreCase)) &&
            (tag is null || string.Equals(effect.Tag, tag, StringComparison.OrdinalIgnoreCase)) &&
            (nameContains is null || new[] { effect.Key, effect.NameJa, effect.NameEn }.Any(name =>
                name?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) == true))).ToArray();
        return Ok(new { effectsDirectory = catalog.Directory, matched = matches.Length,
            kinds = catalog.Effects.GroupBy(effect => effect.Kind).Select(group => new { kind = group.Key, count = group.Count() }).ToArray(),
            effects = matches.Take(Math.Max(0, limit)).Select(effect => new {
                effectKey = effect.Key, kind = effect.Kind, tag = effect.Tag, nameJa = effect.NameJa, nameEn = effect.NameEn,
                editable = effect.CanEdit, defaultDuration = effect.DefaultDuration, propertyKeys = effect.PropertyKeys,
                relativePath = effect.RelativePath }), catalog.Diagnostics });
    }

    public McpToolResult<object> ListObjectEffects(string projectPath, TimelineObjectTargetDto target) =>
        WithDocument(projectPath, (_, document) =>
        {
            TimelineObjectReference reference;
            try { reference = operationMapper.MapObject(target); }
            catch (ArgumentException exception) { return Fail("invalid", "MCP_EFFECT_TARGET_INVALID", exception.Message); }
            var found = document.FindTimelineObject(reference);
            if (found.Value?.ObjectKey is not int key) return Fail("invalid", "MCP_EFFECT_TARGET_INVALID", "The target was not found uniquely.");
            return Ok(new { effects = document.GetFadeEffects(new(found.Value.LayerIndex, key)) });
        });

    public McpToolResult<object> ListFigures(string? tag = null, string? nameContains = null, int limit = 50)
    {
        FigureCatalog catalog;
        try { catalog = figureCatalog.Load(); }
        catch (DirectoryNotFoundException exception) { return Fail("unsupported", "MCP_FIGURES_UNAVAILABLE", exception.Message); }
        FigureDefinition[] matches = catalog.Figures.Where(figure =>
            (tag is null || string.Equals(figure.Tag, tag, StringComparison.OrdinalIgnoreCase)) &&
            (nameContains is null || new[] { figure.Key, figure.DisplayNameJa, figure.DisplayNameEn }.Any(name =>
                name?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) == true))).ToArray();
        return Ok(new
        {
            figuresDirectory = catalog.Directory,
            tags = catalog.Figures.GroupBy(figure => figure.Tag)
                .Select(group => new { tag = group.Key, count = group.Count(), placeable = group.Count(figure => figure.CanPlace) }).ToArray(),
            matched = matches.Length,
            figures = matches.Take(Math.Max(limit, 0)).Select(figure => new
            {
                figureKey = figure.Key, tag = figure.Tag, nameJa = figure.DisplayNameJa, nameEn = figure.DisplayNameEn,
                placeable = figure.CanPlace,
            }).ToArray(),
        });
    }

    public McpToolResult<object> InspectProject(string projectPath) => WithDocument(projectPath, (path, document) =>
    {
        ProjectSummary summary = document.GetSummary();
        return Ok(new { projectPath = path, summary = Summary(summary), capabilities = summary.Capabilities });
    });

    public McpToolResult<object> ValidateProject(string projectPath) => WithDocument(projectPath, (_, document) =>
    {
        ProjectValidationResult validation = document.Validate();
        return new(validation.IsValid, validation.IsValid ? "success" : "validation", null, null,
            new { isValid = validation.IsValid, compatibilityLevel = document.Compatibility.ToString() }, Diagnostics(validation.Diagnostics));
    });

    public McpToolResult<object> ListLayers(string projectPath) => WithDocument(projectPath, (_, document) => Ok(new
    {
        layers = document.Layers.Select(layer => new
        {
            layerIndex = layer.Index, layer.Name, layer.Type, objectCount = layer.Objects.Count,
        }).ToArray(),
    }));

    public McpToolResult<object> ListCharacters(string projectPath) => WithDocument(projectPath, (_, document) => Ok(new
    {
        characters = document.Speakers.Select(speaker =>
        {
            SpeakerLayerView? layer = document.Layers.OfType<SpeakerLayerView>().FirstOrDefault(item => item.SpeakerKey == speaker.Key);
            return new
            {
                speakerKey = speaker.Key, speaker.Name, model = speaker.File,
                layerIndex = layer?.Index, layerName = layer?.Name, telopFrame = layer?.TelopFrame,
                actions = layer?.Actions ?? Array.Empty<string>(),
            };
        }).ToArray(),
    }));

    public McpToolResult<object> ListTimeline(string projectPath, string? layerName = null, string? objectType = null,
        decimal? start = null, decimal? end = null) => WithDocument(projectPath, (_, document) =>
    {
        TimelineQuery query = new()
        {
            Layer = layerName is null ? null : new LayerNameReference(layerName),
            ObjectType = objectType,
            OverlappingRange = Range(start, end),
        };
        return Ok(new { entries = document.GetTimelineEntries(query).Select(Timeline).ToArray() });
    });

    public McpToolResult<object> FindTimelineObjects(string projectPath, string? layerName = null,
        string? objectType = null, string? textContains = null, decimal? start = null, decimal? end = null) =>
        WithDocument(projectPath, (_, document) =>
        {
            TimelineQuery query = new()
            {
                Layer = layerName is null ? null : new LayerNameReference(layerName), ObjectType = objectType,
                OverlappingRange = Range(start, end),
            };
            IEnumerable<TimelineEntryView> entries = document.GetTimelineEntries(query);
            if (textContains is not null) entries = entries.Where(item => item.Text?.Contains(textContains, StringComparison.OrdinalIgnoreCase) == true);
            return Ok(new { entries = entries.Select(Timeline).ToArray() });
        });

    public McpToolResult<object> GetCapabilities(string projectPath) => WithDocument(projectPath, (_, document) => Ok(new
    {
        applicationVersion = document.ApplicationVersionText,
        compatibilityLevel = document.Compatibility.ToString(),
        capabilities = document.Capabilities,
        layout = new
        {
            screenWidth = document.Settings?.Width, screenHeight = document.Settings?.Height,
            telopFrames = document.TelopFrames.Select(frame => new
            {
                frame.Name, frame.DisplayName, x = frame.X, y = frame.Y, width = frame.Width, height = frame.Height,
            }).ToArray(),
            speakerLayers = document.Layers.OfType<SpeakerLayerView>().Select(layer => new
            {
                layerIndex = layer.Index, layer.Name, telopFrame = layer.TelopFrame,
            }).ToArray(),
        },
    }));

    public McpToolResult<object> InspectAssets(string projectPath) => WithDocument(projectPath, (path, document) =>
    {
        AssetInspection inspection = document.InspectAssets(figureCatalog.Directory);
        return Ok(new
        {
            projectPath = path,
            figuresDirectory = figureCatalog.Directory,
            assetCount = inspection.Assets.Count,
            foundCount = inspection.Assets.Count(asset => asset.Status == AssetReferenceStatus.Found),
            missingCount = inspection.Assets.Count(asset => asset.Status == AssetReferenceStatus.Missing),
            unresolvedCount = inspection.Assets.Count(asset => asset.Status == AssetReferenceStatus.Unresolved),
            unreferencedCount = inspection.Assets.Count(asset => asset.ReferencedBy.Count == 0),
            assets = inspection.Assets.Select(asset => new
            {
                index = asset.Index, fileItemKey = asset.Key, location = asset.Location,
                locationKind = asset.LocationKind.ToString(), status = asset.Status.ToString(),
                asset.RelativePath, asset.ResolvedRelativePath, asset.RelativePathExists,
                asset.AbsolutePath, asset.ResolvedAbsolutePath, asset.AbsolutePathExists,
                referencedBy = asset.ReferencedBy.Select(AssetObject).ToArray(),
            }).ToArray(),
            danglingReferences = inspection.DanglingReferences.Select(reference => new
            {
                reference.FileItemKey, reference.LayerIndex, reference.ObjectIndex, reference.ObjectKey,
                objectType = reference.ObjectType, reference.Name,
            }).ToArray(),
        }, document.LoadDiagnostics);
    });

    public McpToolResult<object> ListSubtitleCues(string projectPath) => WithDocument(projectPath, (_, document) => Ok(new
    {
        cues = document.GetSubtitleCues().Select(cue => new
        {
            layerIndex = cue.ObjectId.LayerIndex, objectKey = cue.ObjectId.ObjectKey, cue.LayerName,
            start = cue.Start.TotalSeconds, end = cue.End.TotalSeconds, text = cue.Text,
        }).ToArray(),
    }));

    public async Task<McpToolResult<object>> ExportSubtitlesAsync(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<SubtitleTextDto>? texts, CancellationToken cancellationToken)
    {
        PathPolicyResult input = paths.ValidateInput(projectPath);
        if (!input.Success) return PathFailure(input);
        PathPolicyResult output = paths.ValidateOutput(outputPath, ".srt");
        if (!output.Success) return PathFailure(output);
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        string? temporary = null;
        try
        {
            IReadOnlyList<SubtitleCue> cues = RecotteProject.Load(input.FullPath!).GetSubtitleCues();
            if (cues.Count == 0) return Fail("invalid", "MCP_SUBTITLE_NO_CUES", "The project has no Speaker Voice with text; no file was written.");
            if (texts is not null)
            {
                McpToolResult<object>? mismatch = ReplaceTexts(ref cues, texts);
                if (mismatch is not null) return mismatch;
            }
            if (!allowOverwrite && File.Exists(output.FullPath!))
                return Fail("path", "MCP_PATH_OUTPUT_EXISTS", "The output file exists; set allowOverwrite to replace it.");
            string content = SubRipFormatter.Format(cues);
            cancellationToken.ThrowIfCancellationRequested();
            // Write beside the destination and move it into place, so a failed write never leaves a partial subtitle file.
            temporary = Path.Combine(Path.GetDirectoryName(output.FullPath!)!, $".{Path.GetFileName(output.FullPath!)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporary, content, new System.Text.UTF8Encoding(true), cancellationToken);
            File.Move(temporary, output.FullPath!, allowOverwrite);
            temporary = null;
            return Ok(new { projectPath = input.FullPath, outputPath = output.FullPath, cueCount = cues.Count,
                translated = texts is not null });
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ExceptionResult(exception); }
        finally { if (temporary is not null) File.Delete(temporary); }
    }

    public Task<McpToolResult<object>> CreateVideoFromImageAsync(string projectPath, string imagePath, string outputPath,
        bool allowOverwrite, decimal duration, string? motion, decimal zoom, decimal focusX, decimal focusY, string? easing,
        string? fit, string? background, CancellationToken cancellationToken)
    {
        VideoClipSettings? settings = VideoClip.Parse(duration, motion, zoom, focusX, focusY, easing, fit, background, out string? invalid);
        if (settings is null) return Task.FromResult(Fail("argument", "MCP_VIDEO_ARGUMENT_INVALID", invalid!));
        PathPolicyResult image = paths.ValidateInput(imagePath, VideoClip.ImageExtensions);
        if (ImageFailure(image, null) is McpToolResult<object> rejected) return Task.FromResult(rejected);
        return MakeVideoAsync(projectPath, outputPath, allowOverwrite, Path.GetFileNameWithoutExtension(image.FullPath!),
            async (executable, format, temporary, token) =>
            {
                FfmpegRunResult run = await ffmpegRunner.RunAsync(executable, VideoClip.BuildArguments(image.FullPath!, temporary, settings, format),
                    VideoTimeout(settings.Duration), token);
                return EncodingFailure(run, null) is McpToolResult<object> failed ? new(failed) : new(null, format.FrameCount(settings.Duration),
                    new() { ["imagePath"] = image.FullPath, ["motion"] = settings.Motion.ToString() });
            }, cancellationToken);
    }

    public Task<McpToolResult<object>> CreateFlipbookVideoAsync(string projectPath, IReadOnlyList<FlipbookFrameDto>? frames,
        string outputPath, bool allowOverwrite, decimal frameDuration, int loops, string? fit, string? background,
        CancellationToken cancellationToken)
    {
        FlipbookSettings? settings = FlipbookVideo.Parse(frameDuration, loops, fit, background, out string? invalid);
        if (settings is null) return Task.FromResult(Fail("argument", "MCP_VIDEO_ARGUMENT_INVALID", invalid!));
        if (frames is not { Count: > 0 } || frames.Count > FlipbookVideo.MaxFrames)
            return Task.FromResult(Fail("argument", "MCP_VIDEO_ARGUMENT_INVALID", $"frames must contain 1 to {FlipbookVideo.MaxFrames} entries."));
        string[] imagePaths = new string[frames.Count];
        for (int index = 0; index < frames.Count; index++)
        {
            if (frames[index].Duration is decimal seconds && !FlipbookVideo.IsFrameDuration(seconds))
                return Task.FromResult(Fail("argument", "MCP_VIDEO_ARGUMENT_INVALID",
                    $"frames[{index}].duration must be between {FlipbookVideo.MinFrameDuration} and {FlipbookVideo.MaxFrameDuration} seconds."));
            PathPolicyResult image = paths.ValidateInput(frames[index].ImagePath, VideoClip.ImageExtensions);
            if (ImageFailure(image, index) is McpToolResult<object> rejected) return Task.FromResult(rejected);
            imagePaths[index] = image.FullPath!;
        }
        // Each distinct image is prepared once, however often it is shown.
        string[] distinct = imagePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length > FlipbookVideo.MaxImages)
            return Task.FromResult(Fail("argument", "MCP_VIDEO_ARGUMENT_INVALID", $"frames may use at most {FlipbookVideo.MaxImages} different images."));
        int[] imageIndexes = imagePaths.Select(path => Array.FindIndex(distinct, other => string.Equals(other, path, StringComparison.OrdinalIgnoreCase))).ToArray();

        return MakeVideoAsync(projectPath, outputPath, allowOverwrite, Path.GetFileNameWithoutExtension(outputPath),
            async (executable, format, temporary, token) =>
            {
                decimal fps = format.FramesPerSecond;
                int[] holds = frames.Select(frame => FlipbookVideo.Hold(frame.Duration ?? settings.FrameDuration, fps)).ToArray();
                long total = (long)holds.Sum() * settings.Loops;
                if (total < format.FrameCount(VideoClip.MinDuration))
                    return new(Fail("argument", "MCP_VIDEO_ARGUMENT_INVALID",
                        $"The flipbook lasts {Math.Round(total / fps, 3)} seconds including loops; it must be at least {VideoClip.MinDuration} seconds."));
                string work = Path.Combine(Path.GetTempPath(), "RecotteStudioMCP", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    for (int index = 0; index < distinct.Length; index++)
                    {
                        FfmpegRunResult prepared = await ffmpegRunner.RunAsync(executable, FlipbookVideo.PrepareArguments(distinct[index],
                            Path.Combine(work, FlipbookVideo.FrameFileName(index)), settings, format), PrepareTimeout, token);
                        if (EncodingFailure(prepared, distinct[index]) is McpToolResult<object> failed) return new(failed);
                    }
                    string list = Path.Combine(work, "frames.ffconcat");
                    await File.WriteAllTextAsync(list, FlipbookVideo.ConcatList(imageIndexes, holds, settings.Loops, fps),
                        new System.Text.UTF8Encoding(false), token);
                    FfmpegRunResult run = await ffmpegRunner.RunAsync(executable, FlipbookVideo.EncodeArguments(list, temporary, total, format),
                        VideoTimeout(total / fps), token);
                    if (EncodingFailure(run, null) is McpToolResult<object> encodeFailed) return new(encodeFailed);
                }
                finally
                {
                    try { Directory.Delete(work, true); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
                int start = 0;
                return new(null, total, new()
                {
                    ["loops"] = settings.Loops, ["frameCountPerLoop"] = holds.Sum(), ["imageCount"] = frames.Count,
                    ["distinctImageCount"] = distinct.Length,
                    // Where each image appears in the first loop, so the flipbook can be timed against dialogue.
                    ["frames"] = holds.Select((hold, index) =>
                    {
                        var item = new { index, imagePath = imagePaths[index], start = Math.Round(start / fps, 6),
                            duration = Math.Round(hold / fps, 6), frameCount = hold };
                        start += hold;
                        return item;
                    }).ToArray(),
                });
            }, cancellationToken);
    }

    private sealed record VideoJob(McpToolResult<object>? Failure, long FrameCount = 0, Dictionary<string, object?>? Details = null);

    // Shared by the video tools: checks the project and output, finds ffmpeg, lets encode write the clip to a temporary
    // file beside the output, and publishes that file only when encoding succeeded.
    private async Task<McpToolResult<object>> MakeVideoAsync(string projectPath, string outputPath, bool allowOverwrite, string displayName,
        Func<string, VideoFrameFormat, string, CancellationToken, Task<VideoJob>> encode, CancellationToken cancellationToken)
    {
        PathPolicyResult project = paths.ValidateInput(projectPath);
        if (!project.Success) return PathFailure(project);
        PathPolicyResult output = paths.ValidateOutput(outputPath, ".mp4");
        if (!output.Success) return PathFailure(output);
        FfmpegResolution resolution = ffmpeg.Resolve();
        if (!resolution.Found) return FfmpegUnavailable(resolution.Message!);
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        string? temporary = null;
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(project.FullPath!);
            // The clip is made at the project's screen size, so addVideoObject fills the screen with it. H.264 in yuv420p needs
            // even sides, and the size is capped at 1920x1080 worth of pixels, such as 1080x1920 for a vertical short.
            if (document.Settings is not { Width: int width, Height: int height, FramesPerSecond: decimal fps } ||
                !VideoClip.SupportsScreen(width, height) || fps is < 1m or > 120m)
                return Fail("unsupported", "MCP_VIDEO_PROJECT_SIZE_UNSUPPORTED",
                    "Only projects with even width and height, at most 1920x1080 pixels in all (such as 1920x1080 or 1080x1920), and a frame rate of 1 to 120 are supported.");
            if (!allowOverwrite && File.Exists(output.FullPath!))
                return Fail("path", "MCP_PATH_OUTPUT_EXISTS", "The output file exists; set allowOverwrite to replace it.");
            FfmpegProbeResult probe = await FfmpegLocator.ProbeAsync(ffmpegRunner, resolution.ExecutablePath!, cancellationToken);
            if (!probe.Success) return FfmpegUnavailable(probe.Message!);

            VideoFrameFormat format = new(width, height, fps);
            temporary = Path.Combine(Path.GetDirectoryName(output.FullPath!)!, $".{Path.GetFileName(output.FullPath!)}.{Guid.NewGuid():N}.tmp");
            long started = Environment.TickCount64;
            VideoJob job;
            await videoJobs.WaitAsync(cancellationToken);
            try { job = await encode(resolution.ExecutablePath!, format, temporary, cancellationToken); }
            finally { videoJobs.Release(); }
            if (job.Failure is not null) return job.Failure;
            if (!File.Exists(temporary)) return Fail("ffmpeg", "MCP_FFMPEG_FAILED", "ffmpeg reported success but wrote no file.");
            File.Move(temporary, output.FullPath!, allowOverwrite);
            temporary = null;

            decimal actual = Math.Round(job.FrameCount / fps, 6);
            int? videoLayer = document.Layers.OfType<VideoLayerView>().Select(layer => (int?)layer.Index).FirstOrDefault();
            Dictionary<string, object?> data = new()
            {
                ["projectPath"] = project.FullPath, ["outputPath"] = output.FullPath,
                ["width"] = format.Width, ["height"] = format.Height, ["fps"] = fps, ["duration"] = actual, ["frameCount"] = job.FrameCount,
                ["videoFormat"] = VideoClip.OutputFormat, ["ffmpegPath"] = resolution.ExecutablePath, ["ffmpegVersion"] = probe.Version,
                ["elapsedSeconds"] = Math.Round((Environment.TickCount64 - started) / 1000m, 1),
            };
            foreach ((string key, object? value) in job.Details ?? new()) data[key] = value;
            // Ready for recotte_apply_operations_and_save_copy: set start and end to where the clip belongs.
            data["suggestedOperation"] = new
            {
                type = "addVideoObject", layer = videoLayer is int index ? new { layerIndex = index } : null,
                assetPath = output.FullPath, projectPath = project.FullPath, displayName, start = 0m, end = actual,
            };
            return Ok(data);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ExceptionResult(exception); }
        finally { if (temporary is not null) File.Delete(temporary); }
    }

    private static McpToolResult<object>? ImageFailure(PathPolicyResult image, int? frame)
    {
        string where = frame is int index ? $"frames[{index}]: " : string.Empty;
        if (image.ErrorCode == "MCP_PATH_EXTENSION")
            return Fail("argument", "MCP_VIDEO_IMAGE_UNSUPPORTED", $"{where}The image must be one of {string.Join(", ", VideoClip.ImageExtensions)}.");
        return image.Success ? null : Fail("path", image.ErrorCode!, where + image.Message);
    }

    private static McpToolResult<object>? EncodingFailure(FfmpegRunResult run, string? imagePath)
    {
        string subject = imagePath is null ? "ffmpeg" : $"ffmpeg could not read {imagePath}:";
        if (run.TimedOut) return Fail("ffmpeg", "MCP_FFMPEG_TIMEOUT", $"{subject} did not finish in time; try a shorter duration.");
        return run.Succeeded ? null
            : Fail("ffmpeg", "MCP_FFMPEG_FAILED", $"{subject} failed (exit code {run.ExitCode}): {Excerpt(run.StartError ?? run.StandardError)}");
    }

    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromSeconds(30);

    private static McpToolResult<object> FfmpegUnavailable(string reason) => Fail("unsupported", "MCP_FFMPEG_UNAVAILABLE",
        $"{reason} Ask the user to install ffmpeg and set it in the ffmpeg section of the Recotte Studio MCP settings window.");
    // Allow ten seconds of encoding per second of video, plus startup time, even for long clips.
    private static TimeSpan VideoTimeout(decimal duration) =>
        TimeSpan.FromSeconds((double)Math.Min(60m + duration * 10m, TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerSecond - 1m));
    private static string Excerpt(string text)
    {
        text = text.Trim();
        return text.Length <= 2000 ? text : "..." + text[^2000..];
    }

    // Replacement texts must match the cues one to one, so a file never mixes translated and original lines.
    private static McpToolResult<object>? ReplaceTexts(ref IReadOnlyList<SubtitleCue> cues, IReadOnlyList<SubtitleTextDto> texts)
    {
        Dictionary<TimelineObjectId, string> byId = new();
        foreach (SubtitleTextDto text in texts)
        {
            TimelineObjectId id = new(text.LayerIndex, text.ObjectKey);
            if (string.IsNullOrWhiteSpace(text.Text))
                return Fail("argument", "MCP_SUBTITLE_TEXT_EMPTY", $"The text for layerIndex {id.LayerIndex}, objectKey {id.ObjectKey} is empty.");
            if (!byId.TryAdd(id, text.Text))
                return Fail("argument", "MCP_SUBTITLE_TEXT_DUPLICATE", $"layerIndex {id.LayerIndex}, objectKey {id.ObjectKey} is given more than once.");
        }
        HashSet<TimelineObjectId> known = cues.Select(cue => cue.ObjectId).ToHashSet();
        string[] unknown = byId.Keys.Where(id => !known.Contains(id)).Select(Describe).ToArray();
        if (unknown.Length > 0)
            return Fail("argument", "MCP_SUBTITLE_TEXT_UNKNOWN", $"No subtitle cue matches {string.Join(", ", unknown)}; use recotte_list_subtitle_cues.");
        string[] missing = cues.Where(cue => !byId.ContainsKey(cue.ObjectId)).Select(cue => Describe(cue.ObjectId)).ToArray();
        if (missing.Length > 0)
            return Fail("argument", "MCP_SUBTITLE_TEXT_MISSING", $"Every cue needs a text; missing {string.Join(", ", missing)}.");
        cues = cues.Select(cue => cue with { Text = byId[cue.ObjectId] }).ToArray();
        return null;

        static string Describe(TimelineObjectId id) => $"(layerIndex {id.LayerIndex}, objectKey {id.ObjectKey})";
    }

    public McpToolResult<object> PreviewOperations(string projectPath, IReadOnlyList<ProjectOperationDto>? operations) =>
        WithMappedDocument(projectPath, operations, (_, document, mapped) =>
        {
            ProjectBatchPreview preview = document.PreviewOperations(mapped);
            return Batch(preview.BatchResult, new { estimatedDuration = preview.EstimatedDuration?.TotalSeconds, preview.CanSaveCopy });
        });

    public McpToolResult<object> PreviewOperationsAndSave(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto>? operations)
    {
        PathPolicyResult output = paths.ValidateOutput(outputPath);
        if (!output.Success) return PathFailure(output);
        return WithMappedDocument(projectPath, operations, (_, document, mapped) =>
        {
            ProjectBatchSavePreview preview = document.PreviewOperationsAndSaveCopy(output.FullPath!, mapped,
                new() { SaveOptions = new() { AllowOverwrite = allowOverwrite } });
            return Batch(preview.Operations.BatchResult, new
            {
                canSave = preview.CanSave,
                savePreview = preview.SavePreview is null ? null : new
                {
                    preview.SavePreview.DestinationPath, preview.SavePreview.DestinationExists,
                    preview.SavePreview.OverwriteRequired, preview.SavePreview.CanSave,
                },
            }, preview.Diagnostics);
        });
    }

    public async Task<McpToolResult<object>> ApplyOperationsAndSaveCopyAsync(string projectPath, string outputPath,
        bool allowOverwrite, IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken)
    {
        PathPolicyResult input = paths.ValidateInput(projectPath);
        if (!input.Success) return PathFailure(input);
        PathPolicyResult output = paths.ValidateOutput(outputPath);
        if (!output.Success) return PathFailure(output);
        if (PathsEqual(input.FullPath!, output.FullPath!)) return Fail("path", "MCP_PATH_SOURCE_DESTINATION_SAME", "The source and output paths must differ.");
        OperationMapResult map = operationMapper.Map(operations);
        if (!map.Success) return Fail("argument", map.ErrorCode!, map.Message!);
        cancellationToken.ThrowIfCancellationRequested();
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(input.FullPath!);
            cancellationToken.ThrowIfCancellationRequested();
            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output.FullPath!, map.Operations,
                new() { SaveOptions = new() { AllowOverwrite = allowOverwrite } });
            return BatchSave(input.FullPath!, output.FullPath!, result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ExceptionResult(exception); }
    }

    public McpToolResult<object> PreviewOperationsAndOverwrite(string projectPath,
        IReadOnlyList<ProjectOperationDto>? operations) => WithMappedDocument(projectPath, operations, (path, document, mapped) =>
    {
        ProjectBatchSavePreview preview = document.PreviewOperationsAndOverwrite(mapped);
        return Batch(preview.Operations.BatchResult, new
        {
            canSave = preview.CanSave, saveMode = "overwrite", projectPath = path,
            sourceChanged = preview.SavePreview?.ExternalChangeDetected,
            backupPath = preview.SavePreview?.BackupPath,
            blockingDiagnostics = Diagnostics(preview.SavePreview?.BlockingDiagnostics),
        }, preview.Diagnostics);
    });

    public async Task<McpToolResult<object>> ApplyOperationsAndOverwriteAsync(string projectPath,
        IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken)
    {
        PathPolicyResult input = paths.ValidateInput(projectPath);
        if (!input.Success) return PathFailure(input);
        OperationMapResult map = operationMapper.Map(operations);
        if (!map.Success) return Fail("argument", map.ErrorCode!, map.Message!);
        cancellationToken.ThrowIfCancellationRequested();
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(input.FullPath!, cancellationToken);
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(input.FullPath!);
            ProjectBatchSaveResult result = document.ApplyOperationsAndOverwrite(map.Operations);
            return OverwriteResult(input.FullPath!, result);
        }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    public async Task<McpToolResult<object>> AddTextSequenceAndSaveCopyAsync(string projectPath, string outputPath,
        LayerTargetDto layer, decimal start, decimal defaultDuration, decimal gap, bool allowOverwrite,
        IReadOnlyList<TextSequenceItemDto> items, CancellationToken cancellationToken)
    {
        List<ProjectOperationDto> operations = new(items.Count);
        // Placement itself is deliberately delegated to Core below; this list is only used for shared path/map validation.
        PathPolicyResult input = paths.ValidateInput(projectPath); if (!input.Success) return PathFailure(input);
        PathPolicyResult output = paths.ValidateOutput(outputPath); if (!output.Success) return PathFailure(output);
        if (PathsEqual(input.FullPath!, output.FullPath!)) return Fail("path", "MCP_PATH_SOURCE_DESTINATION_SAME", "The source and output paths must differ.");
        try
        {
            LayerReference mappedLayer = operationMapper.MapLayer(layer);
            AddTextSequenceRequest request = new(mappedLayer, items.Select(item => new TextSequenceItem(item.Text,
                item.Duration is decimal duration ? new ProjectTime(duration) : null)).ToArray(), new(start), new(gap), new(defaultDuration));
            await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
            RecotteProjectDocument document = RecotteProject.Load(input.FullPath!);
            cancellationToken.ThrowIfCancellationRequested();
            AddTextSequenceResult sequence = document.AddTextSequence(request);
            if (!sequence.Success) return Batch(sequence.BatchResult, new { finalEndTime = sequence.FinalEndTime.TotalSeconds, objectIds = sequence.ObjectIds });
            ProjectSaveResult saved = document.SaveCopy(output.FullPath!, new() { AllowOverwrite = allowOverwrite });
            return Ok(new { projectPath = input.FullPath, outputPath = output.FullPath, finalEndTime = sequence.FinalEndTime.TotalSeconds,
                objectIds = sequence.ObjectIds, batch = BatchData(sequence.BatchResult, true), save = Save(saved) }, sequence.BatchResult.Diagnostics.Concat(saved.Warnings));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ExceptionResult(exception); }
    }

    public Task<McpToolResult<object>> CreateProjectFromTemplateAsync(string templatePath, string outputPath,
        bool allowOverwrite, IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken) =>
        ApplyOperationsAndSaveCopyAsync(templatePath, outputPath, allowOverwrite, operations, cancellationToken);

    public async Task<McpToolResult<object>> CreateProjectAsync(string projectName, string outputPath,
        bool allowOverwrite, IReadOnlyList<ProjectOperationDto>? operations, CancellationToken cancellationToken,
        int width = ProjectScreenSize.DefaultWidth, int height = ProjectScreenSize.DefaultHeight)
    {
        if (!ProjectScreenSize.IsValid(width, height))
            return Fail("argument", "MCP_PROJECT_SIZE_INVALID", $"width and height must be whole numbers from {ProjectScreenSize.Minimum} to {ProjectScreenSize.Maximum}.");
        PathPolicyResult output = paths.ValidateOutput(outputPath);
        if (!output.Success) return PathFailure(output);
        OperationMapResult map = operationMapper.Map(operations);
        if (!map.Success) return Fail("argument", map.ErrorCode!, map.Message!);
        string? directory = Path.GetDirectoryName(output.FullPath!);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return Fail("path", "MCP_PATH_DIRECTORY_MISSING", "The output directory does not exist.");
        cancellationToken.ThrowIfCancellationRequested();
        await using IAsyncDisposable held = await outputLocks.AcquireAsync(output.FullPath!, cancellationToken);
        try
        {
            RecotteProjectDocument document = RecotteProject.Create(new(projectName, directory, width, height));
            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output.FullPath!, map.Operations,
                new() { SaveOptions = new() { AllowOverwrite = allowOverwrite } });
            return BatchSave(string.Empty, output.FullPath!, result);
        }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    private McpToolResult<object> WithMappedDocument(string path, IReadOnlyList<ProjectOperationDto>? operations,
        Func<string, RecotteProjectDocument, IReadOnlyList<ProjectOperation>, McpToolResult<object>> action)
    {
        OperationMapResult map = operationMapper.Map(operations);
        return !map.Success ? Fail("argument", map.ErrorCode!, map.Message!) :
            WithDocument(path, (full, document) => action(full, document, map.Operations));
    }

    private McpToolResult<object> WithDocument(string path, Func<string, RecotteProjectDocument, McpToolResult<object>> action)
    {
        PathPolicyResult checkedPath = paths.ValidateInput(path);
        if (!checkedPath.Success) return PathFailure(checkedPath);
        try { return action(checkedPath.FullPath!, RecotteProject.Load(checkedPath.FullPath!)); }
        catch (Exception exception) { return ExceptionResult(exception); }
    }

    private static McpToolResult<object> BatchSave(string source, string output, ProjectBatchSaveResult result) =>
        new(result.Success, result.Success ? "success" : result.SaveFailureKind is null ? "edit" : "save",
            result.Success ? null : result.SaveFailureKind?.ToString() ?? FirstErrorCode(result.Diagnostics),
            result.Success ? null : "The atomic edit or SaveCopy operation failed.",
            new { projectPath = source, outputPath = output, batch = BatchData(result.BatchResult, result.Success), save = result.SaveResult is null ? null : Save(result.SaveResult) },
            Diagnostics(Kept(result.Diagnostics, result.Success)));
    private static McpToolResult<object> OverwriteResult(string path, ProjectBatchSaveResult result)
    {
        ProjectSaveResult? save = result.SaveResult;
        string? errorCode = result.Success ? null : result.SaveFailureKind?.ToString() ?? FirstErrorCode(result.Diagnostics);
        string category = result.Success ? "success" : result.SaveFailureKind == ProjectSaveFailureKind.SourceChanged ? "conflict" :
            result.SaveFailureKind is null ? "edit" : "save";
        return new(result.Success, category, errorCode,
            result.Success ? null : result.SaveFailureKind == ProjectSaveFailureKind.SourceChanged
                ? "The source project changed after it was loaded." : "The atomic edit or overwrite operation failed.",
            new { saveMode = "overwrite", projectPath = path, destinationPath = save?.DestinationPath,
                backupPath = save?.BackupPath, revision = save?.Revision, batch = BatchData(result.BatchResult, result.Success),
                validation = save is null ? null : new { isValid = save.ValidationAfterSave.IsValid,
                    diagnostics = Diagnostics(save.ValidationAfterSave.Diagnostics) } }, Diagnostics(Kept(result.Diagnostics, result.Success)));
    }
    private static McpToolResult<object> Batch(ProjectBatchResult result, object? extra = null, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(result.Success, result.Success ? "success" : "edit",
            result.Success ? null : FirstErrorCode(diagnostics ?? result.Diagnostics), result.Success ? null : "The atomic operation preview failed.",
            new { batch = BatchData(result, result.Success), extra }, Diagnostics(Kept(diagnostics ?? result.Diagnostics, result.Success)));
    // A failed batch carries its reason only in the diagnostics; surfacing the first error code lets callers and the
    // activity log tell, say, a wrong layer type (RC4503) from a missing object without reading every diagnostic.
    private static string? FirstErrorCode(IEnumerable<ProjectDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)?.Code;
    // changesKept is false when the batch failed or its save failed: every operation was rolled back or never reached the
    // file, so no clip's text changed even though earlier operations in the batch report success.
    private static object BatchData(ProjectBatchResult value, bool changesKept) => new { value.Success, value.AppliedOperationCount, value.FailedOperationIndex,
        // Core derives this from the final atomic batch state, so removed or reverted intermediate edits are excluded.
        audioRegenerationRequired = changesKept
            ? value.AudioRegenerationRequired.Select(item => item.ToString()).ToArray()
            : Array.Empty<string>(),
        value.OperationResults, value.Changes, validation = new { value.Validation.IsValid, diagnostics = Diagnostics(value.Validation.Diagnostics) },
        value.CanCommit, value.Committed, value.RolledBack };
    private const string StaleAudio = "RC4208";
    private static IEnumerable<ProjectDiagnostic> Kept(IEnumerable<ProjectDiagnostic> diagnostics, bool changesKept) =>
        changesKept ? diagnostics : diagnostics.Where(diagnostic => diagnostic.Code != StaleAudio);
    private static object Save(ProjectSaveResult value) => new { value.DestinationPath, value.BackupPath, value.Revision, value.SavedAtUtc,
        warnings = Diagnostics(value.Warnings), validation = new { value.ValidationAfterSave.IsValid, diagnostics = Diagnostics(value.ValidationAfterSave.Diagnostics) } };
    private static object Summary(ProjectSummary value) => new { value.ApplicationVersion, compatibilityLevel = value.Compatibility.ToString(),
        value.CompatibilityReason, value.Revision, duration = value.Duration?.TotalSeconds, value.AutoDuration, value.Width, value.Height,
        frameRate = value.FramesPerSecond, value.LayerCount, value.ObjectCount, value.SpeakerCount, value.FileItemCount,
        value.ObjectCountsByType, value.ErrorCount, value.WarningCount, value.MissingAssetCount };
    private static object Timeline(TimelineEntryView value) => new { value.LayerIndex, value.LayerName, value.LayerType,
        objectKey = value.ObjectId?.ObjectKey, value.ObjectIndex, value.ObjectType, value.Name, value.Text,
        start = value.StartTime?.TotalSeconds, end = value.EndTime?.TotalSeconds, duration = value.Duration?.TotalSeconds,
        locked = value.IsLocked, hasAudio = value.HasAudio, value.Bounds, value.Action, value.Capabilities };
    private static object AssetObject(AssetReferencingObject value) => new { value.LayerIndex, value.ObjectIndex, value.ObjectKey,
        objectType = value.ObjectType, value.Name };
    private static ProjectTimeRange? Range(decimal? start, decimal? end) => start is null && end is null ? null :
        start is decimal a && end is decimal b ? new(new(a), new(b)) : throw new ArgumentException("start and end must be supplied together.");
    private static McpToolResult<object> Ok(object data, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(true, "success", null, null, data, Diagnostics(diagnostics));
    private static McpToolResult<object> Fail(string category, string code, string message, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(false, category, code, message, null, Diagnostics(diagnostics));
    private static McpToolResult<object> PathFailure(PathPolicyResult result) => Fail("path", result.ErrorCode!, result.Message!);
    private static IReadOnlyList<McpDiagnosticDto> Diagnostics(IEnumerable<ProjectDiagnostic>? values) => (values ?? Array.Empty<ProjectDiagnostic>())
        .Select(value => new McpDiagnosticDto(value.Code, value.Severity.ToString(), value.Message, value.JsonPath)).ToArray();
    private static bool PathsEqual(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static McpToolResult<object> ExceptionResult(Exception exception) => exception switch
    {
        RecotteProjectSaveException save => Fail("save", save.Kind.ToString(), save.Message, save.Warnings),
        RecotteProjectLoadException load => Fail("load", "MCP_PROJECT_LOAD_FAILED", load.Message),
        RecotteProjectCreationException creation => Fail("creation", "MCP_PROJECT_CREATION_FAILED", creation.Message, creation.Diagnostics),
        ArgumentException argument => Fail("argument", "MCP_ARGUMENT_INVALID", argument.Message),
        IOException io => Fail("save", "MCP_IO_FAILED", io.Message),
        _ => Fail("unexpected", "MCP_UNEXPECTED", $"{exception.GetType().Name}: {exception.Message} CorrelationId: {Guid.NewGuid():N}"),
    };
}
