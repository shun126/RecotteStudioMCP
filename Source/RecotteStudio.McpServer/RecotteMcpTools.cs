using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RecotteStudio.McpServer;

[McpServerToolType]
public sealed class RecotteMcpTools(RecotteToolService service, ActivityLog activity)
{
    private const string GenerationGuidance = " Before creating or editing a Recotte Studio project, call recotte_get_generation_rules and follow its guidance; the project and installed tools may have additional constraints.";
    private const string ReadOnly = "Reads one .ccproj through RecotteStudio.Core without changing or creating files.";
    private const string Removal = " removeTimelineObject removes any unlocked timeline object, prunes a file-item registration only when it becomes unused, and never deletes asset files from disk.";
    private const string Dialogue = " addTextOnlySpeakerVoice places a dialogue line as text only: Recotte Studio synthesizes the speech and draws the telop, so supply no audio and put no dialogue text into images or videos. updateSpeakerText also works on a clip that already has audio: the audio is kept, the clip is listed in audioRegenerationRequired with an RC4208 warning, and you must tell the user to regenerate the audio for those clips in Recotte Studio.";
    private const string Figure = " addFigureObject places an installed stamp on an Annot layer: figureKey comes from recotte_list_figures, x and y are the top-left corner in screen pixels, and width (default 128) and height (default: width) set its size.";
    private const string LayerOrder = " Layers draw in the order of the layers array: index 0 is at the back and each later layer draws over the earlier ones, whatever their types.";
    private const string VideoLayer = " addVideoLayer adds an empty Video layer, by default right after the last Video layer, so its clips draw above the existing videos; use it when two videos must play at the same time. layerName is optional (default 映像・音声N) and must not match another layer. insertIndex (0 to the layer count) inserts it at that index instead; out of range fails with RC4003. Every layer from the insertion point on moves up by one, so in the same batch refer to the new layer and to later layers by name; the result reports createdLayerIndex.";
    private const string AnnotationLayer = " addAnnotationLayer adds an empty Annot layer, by default right after the last Annot layer, so the images, editor notes, and stamps placed on it with addImageObject, addAnnotationText, and addFigureObject draw above every existing layer. insertIndex (0 to the layer count) inserts it at that index instead: insertIndex 0 makes it the backmost layer, so an image placed on it is a background behind every video, character, and other layer. layerName is optional (default 注釈N) and must not match another layer; refer to the new layer by name in the same batch, and the result reports createdLayerIndex.";
    private const string Character = " importCharacter copies one character and its Speaker layer from donorProjectPath, a project saved by Recotte Studio that already has that character: speakerKey comes from recotte_list_characters on the donor, and start and end set when the character is shown. Characters cannot be created from scratch, and no voices are copied; add dialogue afterwards with addTextOnlySpeakerVoice. The donor must hold every character already in the project and use the same text styles, because its telop frames, which Recotte Studio fits around the characters, replace the project's. The first import fills the empty Speaker layer of a new project; later imports insert a layer right after the last Speaker layer, so later layers' indexes grow by one. The layer keeps the donor layer's name unless layerName is given, and a name used by another layer fails with RC4607; refer to it by name in the same batch. The result reports createdLayerIndex.";
    private const string OperationTypes = " Supported operation types are updateSpeakerText, moveTimelineObject, addTextOnlySpeakerVoice, removeTimelineObject, addImageObject, addVideoObject, addAnnotationText, addFigureObject, addFadeEffect, updateFadeEffect, removeFadeEffect, addVideoLayer, addAnnotationLayer, and importCharacter. Fades support unlocked Image and Video clips: effectKey is Alpha Fade or Alpha Fade Out, duration is positive seconds within the clip; placement is at its start or end. updateFadeEffect and removeFadeEffect require effectIndex from recotte_list_object_effects (the index in the full sub-objects array; removal shifts subsequent indices). Curve editing is unsupported. addAnnotationText places an editor hint text box on an Annot layer." + Figure + LayerOrder + VideoLayer + AnnotationLayer + Character + Dialogue;
    private const string Editing = "Atomically applies supported RecotteStudio.Core operations and saves the result to a different .ccproj file. The source project is never modified. Recotte Studio is not launched." + OperationTypes + Removal + GenerationGuidance;
    private const string Overwriting = "Atomically applies supported RecotteStudio.Core operations and safely overwrites the loaded source .ccproj using RecotteStudio.Core Save(). A mandatory backup and external-change detection are used. Recotte Studio is not launched." + OperationTypes + Removal + GenerationGuidance;

    [McpServerTool(Name = "recotte_get_generation_rules"), Description("Reads the optional user-editable GenerationRules.md next to the MCP server executable and returns its current Markdown. Call before creating or editing a project. If the file is missing, the server logs a warning and returns available=false with empty content; continue normal work using the built-in tool constraints. No project path is needed.")]
    public McpToolResult<object> GetGenerationRules() =>
        activity.Track("recotte_get_generation_rules", null, () =>
        {
            string? markdown = GenerationRulesProvider.Read(activity);
            return new McpToolResult<object>(true, "success", null, null,
                new { available = markdown is not null, format = "markdown", fileName = GenerationRulesProvider.FileName,
                    content = markdown ?? string.Empty },
                Array.Empty<McpDiagnosticDto>());
        });

    [McpServerTool(Name = "recotte_list_effects"), Description("Lists installed effects, text effects, and transitions without executing Lua or modifying projects. kind is effects, text, or transitions. editable marks the two supported fades; other definitions are listed only. Filters by tag and Japanese/English name or internal key. Metadata includes literal defaultDuration and propertyKeys; complex Lua expressions are not evaluated.")]
    public McpToolResult<object> ListEffects(string? kind = null, string? tag = null, string? nameContains = null, int limit = 100) =>
        activity.Track("recotte_list_effects", null, () => service.ListEffects(kind, tag, nameContains, limit));

    [McpServerTool(Name = "recotte_list_object_effects"), Description(ReadOnly + " Lists supported fades on one uniquely identified object, returning effectIndex in the complete sub-objects array for updateFadeEffect and removeFadeEffect.")]
    public McpToolResult<object> ListObjectEffects(string projectPath, TimelineObjectTargetDto target) =>
        activity.Track("recotte_list_object_effects", projectPath, () => service.ListObjectEffects(projectPath, target));

    [McpServerTool(Name = "recotte_inspect_project"), Description(ReadOnly + " Returns its summary, capabilities, and validation diagnostics.")]
    public McpToolResult<object> InspectProject(string projectPath) => activity.Track("recotte_inspect_project", projectPath, () => service.InspectProject(projectPath));

    [McpServerTool(Name = "recotte_validate_project"), Description(ReadOnly + " Validation errors are returned as structured results, not protocol errors. An RC2401 warning marks an image or video that does not fill the screen and is on screen while a dialogue telop is drawn over it, and names the covered region.")]
    public McpToolResult<object> ValidateProject(string projectPath) => activity.Track("recotte_validate_project", projectPath, () => service.ValidateProject(projectPath));

    [McpServerTool(Name = "recotte_list_layers"), Description(ReadOnly + " Lists Core's safe layer views.")]
    public McpToolResult<object> ListLayers(string projectPath) => activity.Track("recotte_list_layers", projectPath, () => service.ListLayers(projectPath));

    [McpServerTool(Name = "recotte_list_characters"), Description(ReadOnly + " Lists the characters registered in the project with speakerKey, name, model file, and the Speaker layer (layerIndex, layerName, telopFrame) that shows each one. Call it on a donor project to find the speakerKey for importCharacter.")]
    public McpToolResult<object> ListCharacters(string projectPath) => activity.Track("recotte_list_characters", projectPath, () => service.ListCharacters(projectPath));

    [McpServerTool(Name = "recotte_list_timeline"), Description(ReadOnly + " Lists Core timeline entries in Core-defined order and overlap semantics.")]
    public McpToolResult<object> ListTimeline(string projectPath, string? layerName = null, string? objectType = null,
        decimal? start = null, decimal? end = null) => activity.Track("recotte_list_timeline", projectPath, () => service.ListTimeline(projectPath, layerName, objectType, start, end));

    [McpServerTool(Name = "recotte_find_timeline_objects"), Description(ReadOnly + " Finds timeline candidates and never silently selects an ambiguous edit target.")]
    public McpToolResult<object> FindTimelineObjects(string projectPath, string? layerName = null, string? objectType = null,
        string? textContains = null, decimal? start = null, decimal? end = null) =>
        activity.Track("recotte_find_timeline_objects", projectPath, () => service.FindTimelineObjects(projectPath, layerName, objectType, textContains, start, end));

    [McpServerTool(Name = "recotte_get_capabilities"), Description(ReadOnly + " Returns the exact Core capabilities for the loaded project version; call before editing. Also returns layout: the screen size, each telop frame's bounds in pixels, and the telop frame every Speaker layer uses, so images and videos can be composed around the dialogue.")]
    public McpToolResult<object> GetCapabilities(string projectPath) => activity.Track("recotte_get_capabilities", projectPath, () => service.GetCapabilities(projectPath));

    [McpServerTool(Name = "recotte_list_figures"), Description("Lists the stamps (figures) installed with Recotte Studio, such as emotion marks, arrows, symbols, and onomatopoeia, without reading or changing any project. Filter by tag or by nameContains (matches the key and the Japanese or English name); the result also returns every tag with its count. Pass a returned figureKey to addFigureObject; only figures with placeable true can be placed.")]
    public McpToolResult<object> ListFigures(string? tag = null, string? nameContains = null, int limit = 50) =>
        activity.Track("recotte_list_figures", null, () => service.ListFigures(tag, nameContains, limit));

    [McpServerTool(Name = "recotte_list_subtitle_cues"), Description(ReadOnly + " Lists the subtitle cues recotte_export_subtitles writes: every Speaker Voice with text, in timeline order, with layerIndex, objectKey, layerName, start, end, and text. To make subtitles in another language, translate each text yourself and pass them to recotte_export_subtitles.")]
    public McpToolResult<object> ListSubtitleCues(string projectPath) =>
        activity.Track("recotte_list_subtitle_cues", projectPath, () => service.ListSubtitleCues(projectPath));

    [McpServerTool(Name = "recotte_export_subtitles"), Description("Writes the dialogue of one .ccproj as a SubRip (.srt) subtitle file in UTF-8 with BOM and CRLF, timed to each Speaker Voice. The project is never modified. Without texts the project's own dialogue is written; an empty texts list is rejected, so omit it instead. For another language, call recotte_list_subtitle_cues, translate every cue, and pass texts with one entry per cue (layerIndex, objectKey, text); missing, unknown, or duplicate cues are rejected so a file never mixes languages. Keep each translation short enough to read in the cue's time and break long lines with \\n. Name files by language, such as Movie.en.srt or Movie.zh-Hans.srt. outputPath must end in .srt; set allowOverwrite to replace an existing file.")]
    public Task<McpToolResult<object>> ExportSubtitles(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<SubtitleTextDto>? texts = null, CancellationToken cancellationToken = default) =>
        activity.TrackAsync("recotte_export_subtitles", projectPath, () => service.ExportSubtitlesAsync(projectPath, outputPath, allowOverwrite, texts, cancellationToken));

    [McpServerTool(Name = "recotte_create_video_from_image"), Description("Makes an .mp4 video clip from one still image (PNG, JPEG, BMP, or GIF; an animated GIF uses its first frame) with ffmpeg, adding a slow zoom or pan. The image is not modified, and the project is only read for its frame size and rate; only 1920x1080 projects are supported. Requires ffmpeg, which the user installs and sets in the Recotte Studio MCP settings window; if it is missing, MCP_FFMPEG_UNAVAILABLE is returned, so tell the user instead of running ffmpeg yourself. motion is none, zoomIn, zoomOut, panLeft, panRight, panUp, or panDown (the direction the view moves); zoom (1.05-1.5, default 1.2) is the largest magnification; focusX and focusY (0-1, default 0.5) are the point zoom moves toward and the fixed position across a pan, so keep faces out of the dialogue telop band; easing is easeInOut or linear; fit is cover (crop to fill) or contain (letterbox with background #RRGGBB). duration is 0.5-60 seconds. outputPath must end in .mp4; set allowOverwrite to replace a file. The clip is not placed: pass the returned suggestedOperation, with start and end set, to recotte_apply_operations_and_save_copy, and set its projectPath to the project that will be saved. Never draw dialogue into the image.")]
    public Task<McpToolResult<object>> CreateVideoFromImage(string projectPath, string imagePath, string outputPath, decimal duration,
        bool allowOverwrite = false, string? motion = null, decimal zoom = 1.2m, decimal focusX = 0.5m, decimal focusY = 0.5m,
        string? easing = null, string? fit = null, string? background = null, CancellationToken cancellationToken = default) =>
        activity.TrackAsync("recotte_create_video_from_image", projectPath, () => service.CreateVideoFromImageAsync(projectPath, imagePath,
            outputPath, allowOverwrite, duration, motion, zoom, focusX, focusY, easing, fit, background, cancellationToken));

    [McpServerTool(Name = "recotte_create_flipbook_video"), Description("Makes an .mp4 flipbook (frame-by-frame animation) from still images shown one after another, with ffmpeg. Use it to animate a sequence of drawings or to combine images into one clip. frames lists the images in showing order (PNG, JPEG, BMP, or GIF; an animated GIF uses its first frame); the same imagePath may appear more than once, for example to go back and forth. Each frame is shown for its duration, or frameDuration (default 0.125 s, 8 drawings per second), rounded to whole project frames and at least one. loops repeats the whole sequence (1-100). Up to 600 frames, 200 different images, and 60 seconds in total. Every image is scaled to the screen by fit, contain (default, whole image with margins) or cover (crop to fill), and drawn over background (#RRGGBB, default #FFFFFF), which also shows through transparent parts. The images and project are not modified; only 1920x1080 projects are supported. Requires ffmpeg set in the Recotte Studio MCP settings window; if it is missing, MCP_FFMPEG_UNAVAILABLE is returned, so tell the user instead of running ffmpeg yourself. The result lists when each frame starts within the first loop. The clip is not placed: pass the returned suggestedOperation, with start and end set, to recotte_apply_operations_and_save_copy, and set its projectPath to the project that will be saved. Never draw dialogue into the images.")]
    public Task<McpToolResult<object>> CreateFlipbookVideo(string projectPath, IReadOnlyList<FlipbookFrameDto> frames, string outputPath,
        bool allowOverwrite = false, decimal frameDuration = FlipbookVideo.DefaultFrameDuration, int loops = 1, string? fit = null,
        string? background = null, CancellationToken cancellationToken = default) =>
        activity.TrackAsync("recotte_create_flipbook_video", projectPath, () => service.CreateFlipbookVideoAsync(projectPath, frames,
            outputPath, allowOverwrite, frameDuration, loops, fit, background, cancellationToken));

    [McpServerTool(Name = "recotte_inspect_assets"), Description(ReadOnly + " Lists every registered external file (images, videos, voices, figure SVGs) with the timeline objects that use it, and checks whether its stored relative and absolute paths exist. Status is Found, Missing, or Unresolved; a loc whose base directory is unverified is reported Unresolved, never guessed. danglingReferences lists objects whose File key is not registered.")]
    public McpToolResult<object> InspectAssets(string projectPath) => activity.Track("recotte_inspect_assets", projectPath, () => service.InspectAssets(projectPath));

    [McpServerTool(Name = "recotte_preview_operations"), Description("Atomically previews supported RecotteStudio.Core operations without changing the source, revision, or filesystem." + OperationTypes + Removal + GenerationGuidance)]
    public McpToolResult<object> PreviewOperations(string projectPath, IReadOnlyList<ProjectOperationDto> operations) =>
        activity.Track("recotte_preview_operations", projectPath, () => service.PreviewOperations(projectPath, operations));

    [McpServerTool(Name = "recotte_preview_operations_and_save"), Description("Atomically previews supported operations and Core's SaveCopy plan without creating or changing files." + OperationTypes + Removal + GenerationGuidance)]
    public McpToolResult<object> PreviewOperationsAndSave(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations) => activity.Track("recotte_preview_operations_and_save", projectPath, () => service.PreviewOperationsAndSave(projectPath, outputPath, allowOverwrite, operations));

    [McpServerTool(Name = "recotte_apply_operations_and_save_copy"), Description(Editing)]
    public Task<McpToolResult<object>> ApplyOperationsAndSaveCopy(string projectPath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        activity.TrackAsync("recotte_apply_operations_and_save_copy", projectPath, () => service.ApplyOperationsAndSaveCopyAsync(projectPath, outputPath, allowOverwrite, operations, cancellationToken));

    [McpServerTool(Name = "recotte_preview_operations_and_overwrite"), Description("Previews supported operations and RecotteStudio.Core's safe overwrite plan without changing any file. Mandatory backup, validation, and external-change checks are reported. Recotte Studio is not launched." + OperationTypes + Removal + GenerationGuidance)]
    public McpToolResult<object> PreviewOperationsAndOverwrite(string projectPath,
        IReadOnlyList<ProjectOperationDto> operations) => activity.Track("recotte_preview_operations_and_overwrite", projectPath, () => service.PreviewOperationsAndOverwrite(projectPath, operations));

    [McpServerTool(Name = "recotte_apply_operations_and_overwrite"), Description(Overwriting)]
    public Task<McpToolResult<object>> ApplyOperationsAndOverwrite(string projectPath,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        activity.TrackAsync("recotte_apply_operations_and_overwrite", projectPath, () => service.ApplyOperationsAndOverwriteAsync(projectPath, operations, cancellationToken));

    [McpServerTool(Name = "recotte_add_text_sequence_and_save_copy"), Description(Editing + " Core calculates all sequence times from start, durations, and gap.")]
    public Task<McpToolResult<object>> AddTextSequenceAndSaveCopy(string projectPath, string outputPath, LayerTargetDto layer,
        decimal start, decimal defaultDuration, decimal gap, bool allowOverwrite, IReadOnlyList<TextSequenceItemDto> items,
        CancellationToken cancellationToken) => activity.TrackAsync("recotte_add_text_sequence_and_save_copy", projectPath, () => service.AddTextSequenceAndSaveCopyAsync(projectPath, outputPath, layer,
            start, defaultDuration, gap, allowOverwrite, items, cancellationToken));

    [McpServerTool(Name = "recotte_create_project_from_template"), Description(Editing + " Treats the source as a verified template and preserves unknown fields through Core SaveCopy.")]
    public Task<McpToolResult<object>> CreateProjectFromTemplate(string templatePath, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        activity.TrackAsync("recotte_create_project_from_template", templatePath, () => service.CreateProjectFromTemplateAsync(templatePath, outputPath, allowOverwrite, operations, cancellationToken));

    [McpServerTool(Name = "recotte_create_project"), Description("Creates a validated Recotte Studio 1.8.5.0 project from Core's built-in template, atomically applies supported operations, and publishes it only after a verified SaveCopy." + OperationTypes + Removal + GenerationGuidance)]
    public Task<McpToolResult<object>> CreateProject(string projectName, string outputPath, bool allowOverwrite,
        IReadOnlyList<ProjectOperationDto> operations, CancellationToken cancellationToken) =>
        activity.TrackAsync("recotte_create_project", outputPath, () => service.CreateProjectAsync(projectName, outputPath, allowOverwrite, operations, cancellationToken));
}
