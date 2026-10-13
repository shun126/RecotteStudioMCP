using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class FigureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"RecotteStudio.Core.Tests-{Guid.NewGuid():N}");
    private readonly string figures;

    public FigureTests()
    {
        figures = Path.Combine(directory, "figures");
        WriteFigure("emotion", "emotion_0019", """
            { "name": "Emotion_0019", "tag": "emotion", "display-text": false,
              "display-name": { "en": "Inspiration", "ja": "ひらめき" },
              "figure": { "svg": "emotion_0019.svg" }, "figure-style": { "style-name": "figure" } }
            """, withSvg: true);
        WriteFigure("balloon", "balloon_0001", """
            { "name": "Balloon_0001", "tag": "balloon", "display-text": false,
              "display-name": { "en": "Round Rect Balloon", "ja": "吹き出し(丸角矩形)" },
              "figure": { "lua": "balloon_0001.lua" }, "figure-style": { "style-name": "figure-with-border" } }
            """, withSvg: false);
        File.WriteAllText(Path.Combine(figures, "emotion", "broken.json"), "{ not json");
    }

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Load_ReadsDefinitionsAndSkipsMalformedFiles()
    {
        FigureCatalog catalog = FigureCatalog.Load(figures);

        Assert.Equal(new[] { "Balloon_0001", "Emotion_0019" }, catalog.Figures.Select(figure => figure.Key));
        FigureDefinition emotion = catalog.Find("Emotion_0019")!;
        Assert.Equal("emotion", emotion.Tag);
        Assert.Equal("ひらめき", emotion.DisplayNameJa);
        Assert.Equal("emotion/emotion_0019.svg", emotion.SvgRelativePath);
        Assert.True(emotion.CanPlace);
        Assert.False(catalog.Find("Balloon_0001")!.CanPlace);
        Assert.Null(catalog.Find("emotion_0019"));
    }

    [Fact]
    public void Load_MissingDirectory_Throws() =>
        Assert.Throws<DirectoryNotFoundException>(() => FigureCatalog.Load(Path.Combine(directory, "absent")));

    [Fact]
    public void AddFigure_ReproducesTheFigureSavedByRecotteStudio()
    {
        string samplePath = Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects", "009FullProject",
            "009FullProject.ccproj");
        JsonObject expected = Figures(JsonNode.Parse(File.ReadAllText(samplePath))!.AsObject()).Single();
        decimal[] bounds = expected["properties"]!["Bounds"]!["p-value"]!.AsArray().Select(value => value!.GetValue<decimal>()).ToArray();
        RecotteProjectDocument document = RecotteProject.Create(new("Figure", directory));
        using ProjectEditSession session = document.BeginEdit(fileItemKeyAllocator: new FixedFileKeyAllocator('f'));

        AssetAddResult staged = session.Editor.AddFigure(new(2, FigureCatalog.Load(figures).Find("Emotion_0019")!,
            new(1m), new(2m), bounds[0], bounds[1], bounds[2], bounds[3]));

        Assert.True(staged.Success);
        Assert.Equal(new[] { "RegisterFileItem", "AddFigure" }, staged.Changes.Select(change => change.Operation));
        Assert.True(session.Commit().Success);
        string output = Path.Combine(directory, "Figure.ccproj");
        document.SaveCopy(output);
        JsonObject root = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        JsonObject added = Figures(root).Single();
        JsonObject fileItem = root["file-items"]!.AsArray().Single()!.AsObject();
        Assert.Equal(1, (int)fileItem["loc"]!);
        Assert.Equal("emotion/emotion_0019.svg", (string?)fileItem["rpath"]);
        Assert.Equal("file://" + Path.Combine(figures, "emotion", "emotion_0019.svg").Replace('\\', '/'), (string?)fileItem["apath"]);
        Assert.Equal(new string('f', 64), (string?)added["properties"]!["File"]!["p-value"]);

        // Everything except identity, timing, and the project-specific file key must match the Recotte Studio sample.
        foreach (JsonObject figure in new[] { expected, added })
        {
            figure.Remove("objkey");
            figure.Remove("start-time");
            figure.Remove("end-time");
            figure.Remove("selected");
            figure["properties"]!["File"]!["p-value"] = "";
        }
        Assert.True(JsonNode.DeepEquals(expected, added), added.ToJsonString());
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
    }

    [Fact]
    public void AddFigure_ReusesTheFileItemAndRemovalPrunesItOnlyWhenUnused()
    {
        FigureDefinition figure = FigureCatalog.Load(figures).Find("Emotion_0019")!;
        RecotteProjectDocument document = RecotteProject.Create(new("Figure", directory));
        using ProjectEditSession session = document.BeginEdit();

        AssetAddResult first = session.Editor.AddFigure(new(2, figure, new(0m), new(1m), 0m, 0m, 128m, 128m));
        AssetAddResult second = session.Editor.AddFigure(new(2, figure, new(1m), new(2m), 200m, 0m, 64m, 64m));

        Assert.True(first.Success && second.Success);
        Assert.Equal(first.FileItemKey, second.FileItemKey);
        Assert.Equal("AddFigure", Assert.Single(second.Changes).Operation);
        Assert.DoesNotContain(session.Editor.RemoveTimelineObject(first.ObjectId!.Value).Changes,
            change => change.Operation == "UnregisterFileItem");
        Assert.Contains(session.Editor.RemoveTimelineObject(second.ObjectId!.Value).Changes,
            change => change.Operation == "UnregisterFileItem");
    }

    [Theory]
    [InlineData("Balloon_0001", 2, 0, 1, 128, "RC4801")] // script-drawn figures are not verified
    [InlineData("Emotion_0019", 2, 1, 1, 128, "RC4802")] // empty time range
    [InlineData("Emotion_0019", 2, 0, 1, 0, "RC4802")] // no size
    [InlineData("Emotion_0019", 1, 0, 1, 128, "RC4803")] // a Speaker layer, not an Annot layer
    public void AddFigure_RejectsUnsupportedRequests(string key, int layerIndex, int start, int end, int size, string code)
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Figure", directory));
        using ProjectEditSession session = document.BeginEdit();

        AssetAddResult result = session.Editor.AddFigure(new(layerIndex, FigureCatalog.Load(figures).Find(key)!,
            new(start), new(end), 0m, 0m, size, size));

        Assert.False(result.Success);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    private static IEnumerable<JsonObject> Figures(JsonObject root) => root["layers"]!.AsArray()
        .SelectMany(layer => layer!["layer-objects"]!.AsArray()).Select(item => item!.AsObject())
        .Where(item => (string?)item["type"] == "Figure").ToArray();

    private void WriteFigure(string tag, string name, string definition, bool withSvg)
    {
        string folder = Path.Combine(figures, tag);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".json"), definition);
        if (withSvg) File.WriteAllText(Path.Combine(folder, name + ".svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 500"/>""");
    }

    private sealed class FixedFileKeyAllocator(char value) : IFileItemKeyAllocator
    {
        public string Allocate(IReadOnlyCollection<string> existingKeys) => new(value, 64);
    }
}
