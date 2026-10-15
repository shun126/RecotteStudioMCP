using System.Text.Json;
using RecotteStudio.Core;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class EffectToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-effect-tools-{Guid.NewGuid():N}");
    private readonly RecotteToolService service;
    public EffectToolTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "effects", "effects"));
        File.WriteAllText(Path.Combine(root, "effects", "effects", "fade.lua"), """
            function GetInfo()
                local info = {name="Alpha Fade", displayname={ja="フェード",en="Fade"},tag="in"}
                return info
            end
            """);
        service = new(new(new(root)), new(), new(), effectCatalog: new(Path.Combine(root, "effects")));
    }
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ListsAndFiltersInstalledEffectsAndReportsMissingInstallation()
    {
        var listed = JsonSerializer.SerializeToElement(service.ListEffects(nameContains: "フェード").Data);
        Assert.True(Assert.Single(listed.GetProperty("effects").EnumerateArray()).GetProperty("editable").GetBoolean());
        Assert.Equal(0, JsonSerializer.SerializeToElement(service.ListEffects(kind: "text").Data).GetProperty("matched").GetInt32());
        Assert.Empty(JsonSerializer.SerializeToElement(service.ListEffects(limit: 0).Data).GetProperty("effects").EnumerateArray());
        var missing = new RecotteToolService(new(new(root)), new(), new(), effectCatalog: new(Path.Combine(root, "missing"))).ListEffects();
        Assert.Equal("MCP_EFFECTS_UNAVAILABLE", missing.ErrorCode);
    }

    [Fact]
    public async Task CreatesImageAndFadeThenUpdatesAndRemovesThroughMcp()
    {
        string png = Path.Combine(root, "image.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1cAAAAASUVORK5CYII="));
        string input = Path.Combine(root, "input.ccproj");
        var created = await service.CreateProjectAsync("Fade", input, false, new[] {
            new ProjectOperationDto("addImageObject", Layer: new(LayerIndex: 2), AssetPath: png, ProjectPath: input,
                DisplayName: "Image", Start: 0, End: 2) }, default);
        Assert.True(created.Success, created.Message);
        int key = RecotteProject.Load(input).Layers[2].Objects.Single().ObjectKey!.Value;
        var target = new TimelineObjectTargetDto(LayerIndex: 2, ObjectKey: key);
        var add = new ProjectOperationDto("addFadeEffect", Target: target, EffectKey: "Alpha Fade", Duration: 0.5m);
        Assert.True(service.PreviewOperations(input, new[] { add }).Success);
        Assert.Empty(RecotteProject.Load(input).GetFadeEffects(new(2, key)));
        string faded = Path.Combine(root, "faded.ccproj");
        Assert.True((await service.ApplyOperationsAndSaveCopyAsync(input, faded, false, new[] { add }, default)).Success);
        var listed = service.ListObjectEffects(faded, target);
        Assert.True(listed.Success, listed.Message);
        int index = Assert.Single(JsonSerializer.SerializeToElement(listed.Data).GetProperty("effects").EnumerateArray()).GetProperty("EffectIndex").GetInt32();
        var update = new ProjectOperationDto("updateFadeEffect", Target: target, EffectIndex: index, EffectKey: "Alpha Fade Out", Duration: 1);
        Assert.True((await service.ApplyOperationsAndOverwriteAsync(faded, new[] { update }, default)).Success);
        Assert.Equal(1m, Assert.Single(RecotteProject.Load(faded).GetFadeEffects(new(2, key))).Start);
        var remove = new ProjectOperationDto("removeFadeEffect", Target: target, EffectIndex: index);
        string removed = Path.Combine(root, "removed.ccproj");
        Assert.True((await service.ApplyOperationsAndSaveCopyAsync(faded, removed, false, new[] { remove }, default)).Success);
        Assert.Empty(RecotteProject.Load(removed).GetFadeEffects(new(2, key)));
        Assert.False(service.ListObjectEffects(input, new(ObjectKey: -1)).Success);
    }

    [Fact]
    public void MissingFadeArgumentsAreRejected()
    {
        var mapper = new ProjectOperationMapper();
        Assert.False(mapper.Map(new[] { new ProjectOperationDto("addFadeEffect", Target: new(ObjectKey: 1), EffectKey: "Alpha Fade") }).Success);
        Assert.False(mapper.Map(new[] { new ProjectOperationDto("removeFadeEffect", Target: new(ObjectKey: 1)) }).Success);
    }
}
