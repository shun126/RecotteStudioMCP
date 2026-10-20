using RecotteStudio.McpServer;
using RecotteStudio.Core;
using System.Text.Json;

namespace RecotteStudio.McpServer.Tests;

public sealed class ToolServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-tools-{Guid.NewGuid():N}");
    private readonly RecotteToolService service;
    private readonly string project;

    public ToolServiceTests()
    {
        Directory.CreateDirectory(root);
        project = Path.Combine(root, "input.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Empty.ccproj"), project);
        WorkspacePathPolicy policy = new(new(root));
        service = new(policy, new(), new());
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ReadingToolsUseCoreViews()
    {
        Assert.True(service.InspectProject(project).Success);
        Assert.True(service.ValidateProject(project).Success);
        Assert.True(service.ListLayers(project).Success);
        Assert.True(service.ListTimeline(project).Success);
        Assert.True(service.GetCapabilities(project).Success);
    }

    [Fact]
    public void PreviewDoesNotCreateOrModifyFiles()
    {
        byte[] before = File.ReadAllBytes(project);
        string output = Path.Combine(root, "preview.ccproj");
        McpToolResult<object> result = service.PreviewOperationsAndSave(project, output, false,
            Array.Empty<ProjectOperationDto>());
        Assert.True(result.Success);
        Assert.Equal(before, File.ReadAllBytes(project));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task SaveCopyNeverChangesSource()
    {
        byte[] before = File.ReadAllBytes(project);
        string output = Path.Combine(root, "copy.ccproj");
        McpToolResult<object> result = await service.ApplyOperationsAndSaveCopyAsync(project, output, false,
            Array.Empty<ProjectOperationDto>(), default);
        Assert.True(result.Success);
        Assert.True(File.Exists(output));
        Assert.Equal(before, File.ReadAllBytes(project));
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
    }

    [Fact]
    public async Task CreateProjectUsesValidatedBuiltInTemplate()
    {
        string output = Path.Combine(root, "新規プロジェクト.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("新規プロジェクト", output, false,
            Array.Empty<ProjectOperationDto>(), default);

        Assert.True(result.Success);
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.Equal("新規プロジェクト", created.Settings?.ProjectName);
        Assert.True(created.Validate().IsValid);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task CreateProjectCanAddAnnotationEditingHint()
    {
        string output = Path.Combine(root, "注釈付き.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("注釈付き", output, false,
            new[] { new ProjectOperationDto("addAnnotationText", Layer: new(LayerIndex: 2),
                Text: "編集メモ：ここからBGM", Start: 1m, End: 5m) }, default);

        Assert.True(result.Success);
        TimelineObjectView note = Assert.Single(RecotteProject.Load(output).Layers[2].Objects);
        Assert.Equal("Figure", note.Type);
        Assert.Equal("編集メモ：ここからBGM", note.Text);
    }

    [Fact]
    public async Task CreateProjectCanAddAnnotationLayerAndPlaceNotesOnIt()
    {
        string image = Path.Combine(root, "Sample.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sample.png"), image);
        string output = Path.Combine(root, "前景付き.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("前景付き", output, false, new[]
        {
            new ProjectOperationDto("addAnnotationLayer", LayerName: "前景"),
            new ProjectOperationDto("addImageObject", Layer: new(Name: "前景"), AssetPath: image, ProjectPath: output,
                DisplayName: "Sample.png", Start: 0m, End: 2m),
            new ProjectOperationDto("addAnnotationText", Layer: new(Name: "前景"), Text: "編集メモ", Start: 0m, End: 2m),
        }, default);

        Assert.True(result.Success, result.Message);
        JsonElement operations = JsonSerializer.SerializeToElement(result.Data).GetProperty("batch").GetProperty("OperationResults");
        Assert.Equal(3, operations[0].GetProperty("CreatedLayerIndex").GetInt32());
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.Equal(new[] { "注釈1", "前景" }, created.Layers.OfType<AnnotationLayerView>().Select(layer => layer.Name));
        Assert.Equal(new[] { "Image", "Figure" }, created.Layers[3].Objects.Select(item => item.Type));
        JsonElement layers = JsonSerializer.SerializeToElement(service.ListLayers(output).Data).GetProperty("layers");
        Assert.Equal("前景", layers[3].GetProperty("Name").GetString());
        Assert.True(JsonSerializer.SerializeToElement(service.GetCapabilities(output).Data)
            .GetProperty("capabilities").GetProperty("CanAddAnnotationLayer").GetBoolean());
    }

    [Fact]
    public async Task CreateProjectCanPutABackgroundImageBehindEveryLayer()
    {
        string image = Path.Combine(root, "Sample.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sample.png"), image);
        string output = Path.Combine(root, "下敷き付き.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("下敷き付き", output, false, new[]
        {
            new ProjectOperationDto("addAnnotationLayer", LayerName: "下敷き", InsertIndex: 0),
            new ProjectOperationDto("addImageObject", Layer: new(Name: "下敷き"), AssetPath: image, ProjectPath: output,
                DisplayName: "Sample.png", Start: 0m, End: 2m),
        }, default);

        Assert.True(result.Success, result.Message);
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.Equal(new[] { "下敷き", "映像・音声1", "話者1", "注釈1" }, created.Layers.Select(layer => layer.Name));
        Assert.Equal("Image", Assert.Single(created.Layers[0].Objects).Type);
    }

    [Fact]
    public void ListCharactersReportsSpeakerKeysAndLayers()
    {
        string donor = Path.Combine(root, "donor.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), donor);

        McpToolResult<object> result = service.ListCharacters(donor);

        Assert.True(result.Success);
        JsonElement characters = JsonSerializer.SerializeToElement(result.Data).GetProperty("characters");
        Assert.Equal(new[] { "2D-スーツ男性", "2D-スーツ女性" },
            characters.EnumerateArray().Select(item => item.GetProperty("speakerKey").GetString()));
        Assert.Equal(new[] { 1, 2 }, characters.EnumerateArray().Select(item => item.GetProperty("layerIndex").GetInt32()));
        Assert.Equal("下枠", characters[0].GetProperty("telopFrame").GetString());
        Assert.Empty(JsonSerializer.SerializeToElement(service.ListCharacters(project).Data).GetProperty("characters").EnumerateArray());
    }

    [Fact]
    public async Task CreateProjectCanImportCharactersAndGiveThemDialogue()
    {
        string donor = Path.Combine(root, "donor.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), donor);
        string output = Path.Combine(root, "対談.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("対談", output, false, new[]
        {
            new ProjectOperationDto("importCharacter", DonorProjectPath: donor, SpeakerKey: "2D-スーツ男性", Start: 0m, End: 6m),
            new ProjectOperationDto("importCharacter", DonorProjectPath: donor, SpeakerKey: "2D-スーツ女性", Start: 0m, End: 6m,
                LayerName: "聞き手"),
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(Name: "話者1"), Text: "こんにちは", Start: 1m, End: 3m),
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(Name: "聞き手"), Text: "よろしく", Start: 3m, End: 5m),
        }, default);

        Assert.True(result.Success, result.Message);
        JsonElement operations = JsonSerializer.SerializeToElement(result.Data).GetProperty("batch").GetProperty("OperationResults");
        Assert.Equal(new[] { 1, 2 }, new[] { operations[0], operations[1] }.Select(item => item.GetProperty("CreatedLayerIndex").GetInt32()));
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.True(created.Validate().IsValid);
        Assert.Equal(new[] { "2D-スーツ男性", "2D-スーツ女性" }, created.Speakers.Select(speaker => speaker.Key));
        Assert.Equal(new[] { "話者1", "聞き手" }, created.Layers.OfType<SpeakerLayerView>().Select(layer => layer.Name));
        Assert.All(created.Layers.OfType<SpeakerLayerView>(), layer =>
            Assert.Equal(new[] { "Speaker Character", "Speaker Voice" }, layer.Objects.Select(item => item.Type)));
    }

    [Fact]
    public async Task CreateProjectCanGiveALineTheCharactersExpression()
    {
        string donor = Path.Combine(root, "donor.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), donor);
        JsonElement character = JsonSerializer.SerializeToElement(service.ListCharacters(donor).Data).GetProperty("characters")[0];
        Assert.Contains("2D-スーツ男性:直立(笑顔1)", character.GetProperty("actions").EnumerateArray().Select(item => item.GetString()));
        string output = Path.Combine(root, "笑顔.ccproj");

        McpToolResult<object> result = await service.CreateProjectAsync("笑顔", output, false, new[]
        {
            new ProjectOperationDto("importCharacter", DonorProjectPath: donor, SpeakerKey: "2D-スーツ男性", Start: 0m, End: 6m),
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(Name: "話者1"), Text: "うれしいです", Start: 1m, End: 3m),
            new ProjectOperationDto("addSpeakerAction", Layer: new(Name: "話者1"), Action: "直立(笑顔1)", Start: 1m, End: 3m),
            new ProjectOperationDto("addSpeakerAction", Layer: new(Name: "話者1"), Action: "2D-スーツ男性:頭を抱える", Start: 3m, End: 6m, Loop: true),
        }, default);

        Assert.True(result.Success, result.Message);
        JsonElement entries = JsonSerializer.SerializeToElement(service.ListTimeline(output, objectType: "Speaker Action").Data).GetProperty("entries");
        Assert.Equal(new[] { "2D-スーツ男性:直立(笑顔1)", "2D-スーツ男性:頭を抱える" },
            entries.EnumerateArray().Select(item => item.GetProperty("Action").GetString()));
        Assert.True(JsonSerializer.SerializeToElement(service.GetCapabilities(output).Data)
            .GetProperty("capabilities").GetProperty("CanAddSpeakerAction").GetBoolean());
    }

    [Fact]
    public async Task MoveLayerSwapsTheSpeakerTracks()
    {
        string source = Path.Combine(root, "二人.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), source);
        string output = Path.Combine(root, "入れ替え.ccproj");

        McpToolResult<object> result = await service.ApplyOperationsAndSaveCopyAsync(source, output, false, new[]
        {
            new ProjectOperationDto("moveLayer", Layer: new(Name: "話者2"), InsertIndex: 1),
        }, default);

        Assert.True(result.Success, result.Message);
        RecotteProjectDocument saved = RecotteProject.Load(output);
        Assert.Equal(new[] { "映像・音声1", "話者2", "話者1", "注釈1" }, saved.Layers.Select(layer => layer.Name));
        Assert.Equal(new[] { "2D-スーツ女性", "2D-スーツ男性" }, saved.Speakers.Select(speaker => speaker.Key));
        McpToolResult<object> missing = service.PreviewOperations(source, new[] { new ProjectOperationDto("moveLayer", Layer: new(Name: "話者2")) });
        Assert.Equal("MCP_OPERATION_INVALID", missing.ErrorCode);
    }

    [Fact]
    public async Task CreateProjectCanGiveEachSpeakerItsOwnLayer()
    {
        string output = Path.Combine(root, "対話.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("対話", output, false, new[]
        {
            new ProjectOperationDto("addSpeakerLayer", LayerName: "聞き手"),
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(Name: "話者1"), Text: "こんにちは", Start: 0m, End: 2m),
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(Name: "聞き手"), Text: "どうも", Start: 2m, End: 4m),
        }, default);

        Assert.True(result.Success, result.Message);
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.Equal(new[] { "映像・音声1", "話者1", "聞き手", "注釈1" }, created.Layers.Select(layer => layer.Name));
        Assert.All(created.Layers.OfType<SpeakerLayerView>(), layer => Assert.Single(layer.Objects));
        Assert.True(JsonSerializer.SerializeToElement(service.GetCapabilities(output).Data)
            .GetProperty("capabilities").GetProperty("CanAddSpeakerLayer").GetBoolean());
    }

    [Fact]
    public async Task MoveSpeakerVoiceToLayerHandsALineToTheOtherSpeaker()
    {
        string source = Path.Combine(root, "二人.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), source);
        string output = Path.Combine(root, "振り分け.ccproj");

        McpToolResult<object> result = await service.ApplyOperationsAndSaveCopyAsync(source, output, false, new[]
        {
            new ProjectOperationDto("moveSpeakerVoiceToLayer", Target: new(LayerIndex: 1, ObjectKey: 1001), Layer: new(Name: "話者2")),
        }, default);

        Assert.True(result.Success, result.Message);
        JsonElement batch = JsonSerializer.SerializeToElement(result.Data).GetProperty("batch");
        // Both layers number their voice 1001, so the moved line takes a new key on the woman's layer.
        Assert.Equal(new TimelineObjectId(2, 1002).ToString(), Assert.Single(batch.GetProperty("audioRegenerationRequired").EnumerateArray()).GetString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
        RecotteProjectDocument saved = RecotteProject.Load(output);
        Assert.Equal(new[] { 0, 2 }, saved.Layers.OfType<SpeakerLayerView>()
            .Select(layer => layer.Objects.Count(item => item.Type == "Speaker Voice")));
        Assert.True(JsonSerializer.SerializeToElement(service.GetCapabilities(output).Data)
            .GetProperty("capabilities").GetProperty("CanMoveSpeakerVoiceToLayer").GetBoolean());
    }

    [Fact]
    public void ImportCharacterRejectsADonorOutsideTheWorkspace()
    {
        string outside = Directory.CreateTempSubdirectory("recotte-donor-").FullName;
        try
        {
            string donor = Path.Combine(outside, "donor.ccproj");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), donor);
            WorkspacePathPolicy policy = new(new(root));
            RecotteToolService guarded = new(policy, new ProjectOperationMapper(paths: policy), new());

            McpToolResult<object> result = guarded.PreviewOperations(project, new[]
            {
                new ProjectOperationDto("importCharacter", DonorProjectPath: donor, SpeakerKey: "2D-スーツ男性", Start: 0m, End: 6m),
            });

            Assert.False(result.Success);
            Assert.Equal("MCP_PATH_OUTSIDE_WORKSPACE", result.ErrorCode);
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public void ImportCharacterReportsAnUnreadableDonorAsALoadFailure()
    {
        string donor = Path.Combine(root, "locked.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TwoVoices.ccproj"), donor);
        using FileStream locked = new(donor, FileMode.Open, FileAccess.Read, FileShare.None);

        McpToolResult<object> result = service.PreviewOperations(project, new[]
        {
            new ProjectOperationDto("importCharacter", DonorProjectPath: donor, SpeakerKey: "2D-スーツ男性", Start: 0m, End: 6m),
        });

        Assert.False(result.Success);
        Assert.Equal("MCP_PROJECT_LOAD_FAILED", result.ErrorCode);
    }

    [Fact]
    public async Task CreateProjectCanAddVideoLayerAndPlaceVideoOnIt()
    {
        string video = Path.Combine(root, "Sample.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sample.mp4"), video);
        string output = Path.Combine(root, "二画面.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("二画面", output, false, new[]
        {
            new ProjectOperationDto("addVideoLayer", LayerName: "ワイプ"),
            new ProjectOperationDto("addVideoObject", Layer: new(Name: "ワイプ"), AssetPath: video, ProjectPath: output,
                DisplayName: "wipe", Start: 0m, End: 2m),
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(Name: "話者1"), Text: "こんにちは", Start: 0m, End: 2m),
        }, default);

        Assert.True(result.Success, result.Message);
        JsonElement operations = JsonSerializer.SerializeToElement(result.Data).GetProperty("batch").GetProperty("OperationResults");
        Assert.Equal(1, operations[0].GetProperty("CreatedLayerIndex").GetInt32());
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.Equal(new[] { "Video", "Video", "Speaker", "Annot" }, created.Layers.Select(layer => layer.Type));
        Assert.Equal("ワイプ", created.Layers[1].Name);
        Assert.Equal("Video", Assert.Single(created.Layers[1].Objects).Type);
        Assert.Equal("Speaker Voice", Assert.Single(created.Layers[2].Objects).Type);
    }

    [Fact]
    public async Task SetObjectBoundsShrinksAVideoIntoAWipe()
    {
        string video = Path.Combine(root, "Sample.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sample.mp4"), video);
        string output = Path.Combine(root, "ワイプ.ccproj");
        McpToolResult<object> created = await service.CreateProjectAsync("ワイプ", output, false, new[]
        {
            new ProjectOperationDto("addVideoLayer", LayerName: "ワイプ"),
            new ProjectOperationDto("addVideoObject", Layer: new(Name: "ワイプ"), AssetPath: video, ProjectPath: output,
                DisplayName: "wipe", Start: 0m, End: 2m),
        }, default);
        Assert.True(created.Success, created.Message);
        JsonElement entry = Assert.Single(JsonSerializer.SerializeToElement(service.ListTimeline(output, layerName: "ワイプ").Data)
            .GetProperty("entries").EnumerateArray());
        Assert.Equal(1920m, entry.GetProperty("Bounds").GetProperty("Width").GetDecimal());
        Assert.True(entry.GetProperty("Capabilities").GetProperty("CanSetBounds").GetBoolean());
        int key = entry.GetProperty("objectKey").GetInt32();

        McpToolResult<object> moved = await service.ApplyOperationsAndOverwriteAsync(output, new[]
        {
            new ProjectOperationDto("setObjectBounds", Target: new(LayerIndex: 1, ObjectKey: key), X: 1380m, Y: 60m, Width: 480m, Height: 270m),
        }, default);

        Assert.True(moved.Success, moved.Message);
        TimelineEntryView wipe = Assert.Single(RecotteProject.Load(output).GetTimelineEntries(), item => item.LayerIndex == 1);
        Assert.Equal(new ObjectBounds(1380m, 60m, 480m, 270m), wipe.Bounds);
        Assert.True(JsonSerializer.SerializeToElement(service.GetCapabilities(output).Data)
            .GetProperty("capabilities").GetProperty("CanSetObjectBounds").GetBoolean());
    }

    [Fact]
    public void FailedPreviewReportsTheFirstErrorDiagnosticAsItsErrorCode()
    {
        string image = Path.Combine(root, "Sample.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sample.png"), image);

        // Images go on annotation layers only, so placing one on the Video layer behind everything is rejected.
        McpToolResult<object> result = service.PreviewOperations(project, new[]
        {
            new ProjectOperationDto("addImageObject", Layer: new(LayerIndex: 0), AssetPath: image, ProjectPath: project,
                DisplayName: "Background.png", Start: 0m, End: 2m),
        });

        Assert.False(result.Success);
        Assert.Equal("edit", result.Category);
        Assert.Equal("RC4503", result.ErrorCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC4503");
    }

    [Fact]
    public void CapabilitiesDescribeTelopLayout()
    {
        JsonElement layout = JsonSerializer.SerializeToElement(service.GetCapabilities(project).Data).GetProperty("layout");

        Assert.Equal(1080, layout.GetProperty("screenHeight").GetInt32());
        JsonElement lower = layout.GetProperty("telopFrames").EnumerateArray()
            .Single(frame => frame.GetProperty("Name").GetString() == "LowerFrame");
        Assert.Equal(800m, lower.GetProperty("y").GetDecimal());
        Assert.Equal(200m, lower.GetProperty("height").GetDecimal());
        Assert.Equal("LowerFrame", layout.GetProperty("speakerLayers")[0].GetProperty("telopFrame").GetString());
    }

    [Fact]
    public async Task CreateProjectRejectsBlankNameWithoutCreatingOutput()
    {
        string output = Path.Combine(root, "invalid.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync(" ", output, false,
            Array.Empty<ProjectOperationDto>(), default);

        Assert.False(result.Success);
        Assert.Equal("argument", result.Category);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task OverwriteUsesCoreSafeSaveAndCreatesBackup()
    {
        McpToolResult<object> result = await service.ApplyOperationsAndOverwriteAsync(project,
            Array.Empty<ProjectOperationDto>(), default);

        Assert.True(result.Success);
        JsonElement data = JsonSerializer.SerializeToElement(result.Data);
        Assert.Equal("overwrite", data.GetProperty("saveMode").GetString());
        Assert.Equal(Path.GetFullPath(project), data.GetProperty("destinationPath").GetString());
        Assert.True(File.Exists(data.GetProperty("backupPath").GetString()));
        Assert.Equal(0, data.GetProperty("revision").GetInt64());
        Assert.True(RecotteProject.Load(project).Validate().IsValid);
    }

    [Fact]
    public async Task EverySaveModeListsAudioBackedClipsWhoseTextChanged()
    {
        string voiced = Path.Combine(root, "voiced.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OneVoice.ccproj"), voiced);
        ProjectOperationDto[] operations = { new("updateSpeakerText", Target: new TimelineObjectTargetDto(1, 1000), Text: "書き換え") };

        McpToolResult<object>[] results =
        {
            service.PreviewOperations(voiced, operations),
            service.PreviewOperationsAndSave(voiced, Path.Combine(root, "voiced-preview.ccproj"), false, operations),
            service.PreviewOperationsAndOverwrite(voiced, operations),
            await service.ApplyOperationsAndSaveCopyAsync(voiced, Path.Combine(root, "voiced-copy.ccproj"), false, operations, default),
            await service.ApplyOperationsAndOverwriteAsync(voiced, operations, default),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
            System.Text.Json.JsonElement targets = System.Text.Json.JsonSerializer.SerializeToElement(result.Data)
                .GetProperty("batch").GetProperty("audioRegenerationRequired");
            Assert.Equal(1, targets.GetArrayLength());
        });
    }

    [Fact]
    public async Task RolledBackBatchesDoNotAskForAudioRegeneration()
    {
        string voiced = Path.Combine(root, "rolled-back.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OneVoice.ccproj"), voiced);
        byte[] before = File.ReadAllBytes(voiced);
        // The text update succeeds, then the missing target fails the batch and rolls the update back.
        ProjectOperationDto[] operations =
        {
            new("updateSpeakerText", Target: new TimelineObjectTargetDto(1, 1000), Text: "書き換え"),
            new("removeTimelineObject", Target: new TimelineObjectTargetDto(1, 999999)),
        };
        string copy = Path.Combine(root, "rolled-back-copy.ccproj");

        McpToolResult<object>[] results =
        {
            service.PreviewOperations(voiced, operations),
            service.PreviewOperationsAndSave(voiced, copy, false, operations),
            service.PreviewOperationsAndOverwrite(voiced, operations),
            await service.ApplyOperationsAndSaveCopyAsync(voiced, copy, false, operations, default),
            await service.ApplyOperationsAndOverwriteAsync(voiced, operations, default),
        };

        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
            System.Text.Json.JsonElement targets = System.Text.Json.JsonSerializer.SerializeToElement(result.Data)
                .GetProperty("batch").GetProperty("audioRegenerationRequired");
            Assert.Equal(0, targets.GetArrayLength());
        });
        Assert.False(File.Exists(copy));
        Assert.Equal(before, File.ReadAllBytes(voiced));
    }

    [Fact]
    public void OverwritePreviewDoesNotChangeFiles()
    {
        byte[] before = File.ReadAllBytes(project);
        McpToolResult<object> result = service.PreviewOperationsAndOverwrite(project,
            Array.Empty<ProjectOperationDto>());

        Assert.True(result.Success);
        Assert.Equal(before, File.ReadAllBytes(project));
        Assert.Empty(Directory.GetFiles(root, "*.bak"));
    }

    [Fact]
    public void SavingProjectsHaveNoExternalProcessDependency()
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (!Directory.Exists(Path.Combine(sourceRoot, "RecotteStudio.McpServer")))
            sourceRoot = Path.GetFullPath(Path.Combine(sourceRoot, ".."));
        string[] files = Directory.GetFiles(Path.Combine(sourceRoot, "RecotteStudio.McpServer"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(sourceRoot, "RecotteStudio.Core"), "*.cs")).ToArray();
        string source = string.Join('\n', files.Select(File.ReadAllText));
        // Only the optional ffmpeg integration may start a process, and never as part of loading or saving a project.
        string saving = string.Join('\n', files.Where(file => Path.GetFileName(file) != "FfmpegProcess.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("System.Diagnostics.Process", saving, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", saving, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute", saving, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellExecute", saving, StringComparison.Ordinal);
        Assert.DoesNotContain("Recotte Studio.exe", source, StringComparison.OrdinalIgnoreCase);
    }
}
