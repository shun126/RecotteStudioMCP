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
    public async Task FullScreenImageUnderDialogueReportsNoTelopCoverageWarning()
    {
        string image = Path.Combine(root, "Background.png");
        // Signature plus an IHDR chunk header declaring 1920x1080; Core reads nothing beyond it.
        File.WriteAllBytes(image, new byte[]
        {
            137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 7, 128, 0, 0, 4, 56,
        });
        string output = Path.Combine(root, "背景付き.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("背景付き", output, false, new[]
        {
            new ProjectOperationDto("addTextOnlySpeakerVoice", Layer: new(LayerIndex: 1), Text: "こんにちは", Start: 0m, End: 2m),
            new ProjectOperationDto("addImageObject", Layer: new(LayerIndex: 2), Start: 0m, End: 2m, AssetPath: image,
                ProjectPath: output, DisplayName: "Background.png"),
        }, default);

        Assert.True(result.Success);
        Assert.Equal(2, RecotteProject.Load(output).Layers.Sum(layer => layer.Objects.Count));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "RC2401");
        Assert.DoesNotContain(service.ValidateProject(output).Diagnostics, diagnostic => diagnostic.Code == "RC2401");
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
