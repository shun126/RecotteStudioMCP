using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class AnnotationLayerTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteAnnotationLayerTests-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    private static string Sample(string folder, string file) => Path.Combine(
        TestProjects.RepositoryRoot, "Documents", "RecotteProjects", folder, file);

    [Fact]
    public void AddAnnotationLayer_ReproducesTheLayerRecotteStudioAdds()
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample("004OneImage", "004OneImage.ccproj"));
        ProjectBatchResult batch = document.ApplyOperations(new[] { new AddAnnotationLayerOperation() });

        Assert.True(batch.Success);
        Assert.Equal(3, batch.OperationResults[0].CreatedLayerIndex);
        Assert.Equal("AddAnnotationLayer", Assert.Single(batch.Changes).Operation);
        Assert.Equal(new[] { "映像・音声1", "話者1", "注釈1", "注釈2" }, document.Layers.Select(layer => layer.Name));
        Assert.Single(document.Layers[2].Objects);
        Assert.Empty(document.Layers[3].Objects);

        string output = Path.Combine(directory, "added.ccproj");
        document.SaveCopy(output);
        // 012TwoImages is 004OneImage after adding a second Annot layer in Recotte Studio and placing an image on it.
        JsonObject expected = Layer(Sample("012TwoImages", "012TwoImages.ccproj"), 3);
        JsonObject actual = Layer(output, 3);
        foreach (JsonObject layer in new[] { expected, actual })
        {
            layer.Remove("layer-objects");
            layer.Remove("selected");
        }
        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
        Assert.Equal(false, (bool?)Layer(output, 3)["selected"]);
    }

    [Fact]
    public void AddAnnotationLayer_AcceptsImagesNotesAndStampsOnANewProject()
    {
        string output = Path.Combine(directory, "Notes.ccproj");
        RecotteProjectDocument document = RecotteProject.Create(new("Notes", directory));
        Assert.Equal("1.8.5.0", document.ApplicationVersionText);
        FigureDefinition stamp = WriteStamp();

        ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output, new ProjectOperation[]
        {
            new AddAnnotationLayerOperation("前景"),
            new AddImageAssetOperation(new LayerNameReference("前景"), Sample("004OneImage", "Sample.png"), output,
                "image", new(0m), new(2m)),
            new AddAnnotationTextOperation(new LayerNameReference("前景"), "編集メモ", new(0m), new(2m)),
            new AddFigureOperation(new LayerNameReference("前景"), stamp, new(1m), new(2m), 100m, 100m, 128m, 128m),
        });

        Assert.True(result.Success);
        Assert.Equal(3, result.BatchResult.OperationResults[0].CreatedLayerIndex);
        RecotteProjectDocument saved = RecotteProject.Load(output);
        Assert.True(saved.Validate().IsValid);
        Assert.Equal(new[] { "Video", "Speaker", "Annot", "Annot" }, saved.Layers.Select(layer => layer.Type));
        Assert.Equal(new[] { "注釈1", "前景" }, saved.Layers.Skip(2).Select(layer => layer.Name));
        Assert.Empty(saved.Layers[2].Objects);
        Assert.Equal(new[] { "Image", "Figure", "Figure" }, saved.Layers[3].Objects.Select(item => item.Type));
    }

    [Fact]
    public void AddAnnotationLayer_NumbersDefaultNamesAfterTheExistingLayers()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new AddAnnotationLayerOperation(), new AddVideoLayerOperation(), new AddAnnotationLayerOperation(),
        });

        Assert.True(batch.Success);
        Assert.Equal(new int?[] { 4, 1, 5 }, batch.OperationResults.Select(result => result.CreatedLayerIndex));
        Assert.Equal(new[] { "映像・音声1", "映像・音声2", "話者1", "注釈1", "注釈2", "注釈3" },
            document.Layers.Select(layer => layer.Name));
    }

    [Fact]
    public void AddAnnotationLayer_AtIndexZeroHoldsABackgroundBehindEveryLayer()
    {
        string output = Path.Combine(directory, "Background.ccproj");
        RecotteProjectDocument document = RecotteProject.Create(new("Background", directory));

        // Layers draw in array order, so the Annot layer at index 0 is behind the video, the character, and the notes.
        ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output, new ProjectOperation[]
        {
            new AddAnnotationLayerOperation("下敷き", 0),
            new AddImageAssetOperation(new LayerNameReference("下敷き"), Sample("004OneImage", "Sample.png"), output,
                "background", new(0m), new(2m)),
        });

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => $"{item.Code} {item.Message}")));
        Assert.Equal(0, result.BatchResult.OperationResults[0].CreatedLayerIndex);
        Assert.Equal(new TimelineObjectId(0, Assert.IsType<TimelineObjectId>(result.BatchResult.OperationResults[1].CreatedObjectId).ObjectKey),
            result.BatchResult.OperationResults[1].CreatedObjectId);
        RecotteProjectDocument saved = RecotteProject.Load(output);
        Assert.True(saved.Validate().IsValid);
        Assert.Equal(new[] { "Annot", "Video", "Speaker", "Annot" }, saved.Layers.Select(layer => layer.Type));
        Assert.Equal(new[] { "下敷き", "映像・音声1", "話者1", "注釈1" }, saved.Layers.Select(layer => layer.Name));
        Assert.Equal("Image", Assert.Single(saved.Layers[0].Objects).Type);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void AddAnnotationLayer_RejectsAnIndexOutsideTheLayers(int index)
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        using ProjectEditSession session = document.BeginEdit();

        LayerAddResult result = session.Editor.AddAnnotationLayer(new(null, index));

        Assert.False(result.Success);
        Assert.Equal("RC4003", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }

    [Theory]
    [InlineData("注釈1")]
    [InlineData("話者1")]
    [InlineData(" ")]
    public void AddAnnotationLayer_RejectsABlankOrDuplicateName(string name)
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        using ProjectEditSession session = document.BeginEdit();

        LayerAddResult result = session.Editor.AddAnnotationLayer(new(name));

        Assert.False(result.Success);
        Assert.Equal("RC4002", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }

    [Fact]
    public void AddAnnotationLayer_FailureLeavesTheExistingLayersUntouched()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new AddAnnotationLayerOperation("前景"),
            new AddAnnotationTextOperation(new LayerNameReference("前景"), "メモ", new(2m), new(1m)),
        });

        Assert.False(batch.Success);
        Assert.Equal(1, batch.FailedOperationIndex);
        Assert.Equal(new[] { "映像・音声1", "話者1", "注釈1" }, document.Layers.Select(layer => layer.Name));
    }

    [Fact]
    public void Capabilities_ReportAnnotationLayerAddition()
    {
        Assert.True(RecotteProject.Load(TestProjects.EmptyProject).Capabilities.CanAddAnnotationLayer);
    }

    private FigureDefinition WriteStamp()
    {
        string folder = Path.Combine(directory, "figures", "emotion");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "emotion_0019.json"), """
            { "name": "Emotion_0019", "tag": "emotion", "display-text": false,
              "display-name": { "en": "Inspiration", "ja": "ひらめき" },
              "figure": { "svg": "emotion_0019.svg" }, "figure-style": { "style-name": "figure" } }
            """);
        File.WriteAllText(Path.Combine(folder, "emotion_0019.svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 500"/>""");
        return FigureCatalog.Load(Path.Combine(directory, "figures")).Find("Emotion_0019")!;
    }

    private static JsonObject Layer(string path, int index) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(path))!["layers"]![index]!.DeepClone();
}
