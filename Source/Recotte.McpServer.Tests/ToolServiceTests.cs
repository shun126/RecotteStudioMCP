using Recotte.McpServer;
using Recotte.Core;
using System.Text.Json;

namespace Recotte.McpServer.Tests;

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
        string[] files = Directory.GetFiles(Path.Combine(sourceRoot, "Recotte.McpServer"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(sourceRoot, "Recotte.Core"), "*.cs")).ToArray();
        string source = string.Join('\n', files.Select(File.ReadAllText));

        Assert.DoesNotContain("System.Diagnostics.Process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellExecute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Recotte Studio.exe", source, StringComparison.OrdinalIgnoreCase);
    }
}
