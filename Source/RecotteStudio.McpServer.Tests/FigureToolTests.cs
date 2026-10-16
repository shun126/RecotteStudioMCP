using RecotteStudio.Core;
using RecotteStudio.McpServer;
using System.Text.Json;

namespace RecotteStudio.McpServer.Tests;

public sealed class FigureToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-figures-{Guid.NewGuid():N}");
    private readonly RecotteToolService service;

    public FigureToolTests()
    {
        string figures = Path.Combine(root, "figures");
        WriteFigure(figures, "emotion", "emotion_0019", "Emotion_0019", "ひらめき", "Inspiration", "\"svg\": \"emotion_0019.svg\"", "figure");
        WriteFigure(figures, "arrow", "arrow_0001", "Arrow_0001", "矢印", "Arrow", "\"svg\": \"arrow_0001.svg\"", "figure");
        WriteFigure(figures, "balloon", "balloon_0001", "Balloon_0001", "吹き出し", "Balloon", "\"lua\": \"balloon_0001.lua\"", "figure-with-border");
        FigureCatalogProvider catalog = new(figures);
        service = new(new(new(root)), new(catalog), new(), catalog);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ListFiguresFiltersByTagAndName()
    {
        JsonElement all = JsonSerializer.SerializeToElement(service.ListFigures().Data);
        Assert.Equal(3, all.GetProperty("matched").GetInt32());
        Assert.Equal(new[] { "arrow", "balloon", "emotion" },
            all.GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("tag").GetString()));

        JsonElement byName = JsonSerializer.SerializeToElement(service.ListFigures(nameContains: "ひらめき").Data);
        JsonElement figure = Assert.Single(byName.GetProperty("figures").EnumerateArray());
        Assert.Equal("Emotion_0019", figure.GetProperty("figureKey").GetString());
        Assert.True(figure.GetProperty("placeable").GetBoolean());

        JsonElement byTag = JsonSerializer.SerializeToElement(service.ListFigures(tag: "balloon").Data);
        Assert.False(Assert.Single(byTag.GetProperty("figures").EnumerateArray()).GetProperty("placeable").GetBoolean());
        Assert.Empty(JsonSerializer.SerializeToElement(service.ListFigures(limit: 0).Data).GetProperty("figures").EnumerateArray());
    }

    [Fact]
    public void ListFiguresReportsMissingInstallation()
    {
        FigureCatalogProvider missing = new(Path.Combine(root, "absent"));
        McpToolResult<object> result = new RecotteToolService(new(new(root)), new(missing), new(), missing).ListFigures();

        Assert.False(result.Success);
        Assert.Equal("MCP_FIGURES_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task AddFigureObjectPlacesStampWithDefaultSquareSize()
    {
        string output = Path.Combine(root, "スタンプ.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("スタンプ", output, false, new[]
        {
            new ProjectOperationDto("addFigureObject", Layer: new(LayerIndex: 2), Start: 0m, End: 2m,
                FigureKey: "Emotion_0019", X: 100m, Y: 50m),
        }, default);

        Assert.True(result.Success, result.Message);
        RecotteProjectDocument created = RecotteProject.Load(output);
        Assert.Equal("Figure", Assert.Single(created.Layers[2].Objects).Type);
        Assert.Equal("emotion/emotion_0019.svg", Assert.Single(created.FileItems).RelativePath);
        Assert.Contains("[100,50,128,128]", File.ReadAllText(output).Replace(" ", "").Replace("\r\n", "").Replace("\t", ""));
    }

    [Fact]
    public async Task InspectAssetsResolvesPlacedFigureAgainstFiguresDirectory()
    {
        string output = Path.Combine(root, "assets.ccproj");
        Assert.True((await service.CreateProjectAsync("assets", output, false, new[]
        {
            new ProjectOperationDto("addFigureObject", Layer: new(LayerIndex: 2), Start: 0m, End: 2m,
                FigureKey: "Emotion_0019", X: 100m, Y: 50m),
        }, default)).Success);

        McpToolResult<object> result = service.InspectAssets(output);

        Assert.True(result.Success, result.Message);
        JsonElement data = JsonSerializer.SerializeToElement(result.Data);
        Assert.Equal((1, 1, 0, 0), (data.GetProperty("assetCount").GetInt32(), data.GetProperty("foundCount").GetInt32(),
            data.GetProperty("missingCount").GetInt32(), data.GetProperty("unresolvedCount").GetInt32()));
        JsonElement asset = Assert.Single(data.GetProperty("assets").EnumerateArray());
        Assert.Equal("Figures", asset.GetProperty("locationKind").GetString());
        Assert.Equal("Found", asset.GetProperty("status").GetString());
        Assert.Equal(Path.Combine(root, "figures", "emotion", "emotion_0019.svg"), asset.GetProperty("ResolvedRelativePath").GetString());
        Assert.Equal("Figure", Assert.Single(asset.GetProperty("referencedBy").EnumerateArray()).GetProperty("objectType").GetString());
        Assert.Empty(data.GetProperty("danglingReferences").EnumerateArray());
    }

    [Theory]
    [InlineData("Unknown_0001", 100, "recotte_list_figures")]
    [InlineData("Emotion_0019", null, "x and y are required")]
    public async Task AddFigureObjectRejectsInvalidArguments(string figureKey, int? x, string expected)
    {
        string output = Path.Combine(root, "invalid.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("invalid", output, false, new[]
        {
            new ProjectOperationDto("addFigureObject", Layer: new(LayerIndex: 2), Start: 0m, End: 2m,
                FigureKey: figureKey, X: x, Y: 50m),
        }, default);

        Assert.False(result.Success);
        Assert.Equal("MCP_OPERATION_INVALID", result.ErrorCode);
        Assert.Contains(expected, result.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task AddFigureObjectFailsForFigureThatCannotBePlaced()
    {
        string output = Path.Combine(root, "balloon.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("balloon", output, false, new[]
        {
            new ProjectOperationDto("addFigureObject", Layer: new(LayerIndex: 2), Start: 0m, End: 2m,
                FigureKey: "Balloon_0001", X: 0m, Y: 0m),
        }, default);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC4801");
        Assert.False(File.Exists(output));
    }

    private static void WriteFigure(string figures, string tag, string file, string key, string ja, string en, string figure, string style)
    {
        string folder = Path.Combine(figures, tag);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file + ".json"), $$"""
            { "name": "{{key}}", "tag": "{{tag}}", "display-text": false,
              "display-name": { "en": "{{en}}", "ja": "{{ja}}" },
              "figure": { {{figure}} }, "figure-style": { "style-name": "{{style}}" } }
            """);
        if (figure.Contains("svg")) File.WriteAllText(Path.Combine(folder, file + ".svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 500"/>""");
    }
}
