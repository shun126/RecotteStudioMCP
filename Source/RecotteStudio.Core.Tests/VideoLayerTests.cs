using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class VideoLayerTests
{
    private static string Sample(string directory, string file) => Path.Combine(
        TestProjects.RepositoryRoot, "Documents", "RecotteProjects", directory, file);

    [Fact]
    public void AddVideoLayer_ReproducesTheLayerRecotteStudioAdds()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteVideoLayerTests-").FullName;
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(Sample("005OneVideo", "005OneVideo.ccproj"));
            ProjectBatchResult batch = document.ApplyOperations(new[] { new AddVideoLayerOperation() });

            Assert.True(batch.Success);
            Assert.Equal(1, batch.OperationResults[0].CreatedLayerIndex);
            Assert.Equal("layer:1", batch.OperationResults[0].Target);
            Assert.Equal("AddVideoLayer", Assert.Single(batch.Changes).Operation);
            Assert.Equal(new[] { "Video", "Video", "Speaker", "Annot" }, document.Layers.Select(layer => layer.Type));
            Assert.Equal(new[] { "映像・音声1", "映像・音声2", "話者1", "注釈1" }, document.Layers.Select(layer => layer.Name));
            Assert.Empty(document.Layers[1].Objects);

            string output = Path.Combine(directory, "added.ccproj");
            document.SaveCopy(output);
            // 013TwoVideos is 005OneVideo after adding a second Video layer in Recotte Studio and placing a clip on it.
            JsonObject expected = Layer(Sample("013TwoVideos", "013TwoVideos.ccproj"), 1);
            JsonObject actual = Layer(output, 1);
            foreach (JsonObject layer in new[] { expected, actual })
            {
                layer.Remove("layer-objects");
                layer.Remove("selected");
            }
            Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
            Assert.Equal(false, (bool?)Layer(output, 1)["selected"]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AddVideoLayer_LetsLaterOperationsPlaceAVideoOnItByName()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteVideoLayerTests-").FullName;
        try
        {
            string output = Path.Combine(directory, "TwoVideos.ccproj");
            RecotteProjectDocument document = RecotteProject.Create(new("TwoVideos", directory));
            string video = Sample("005OneVideo", "Sample.mp4");

            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output, new ProjectOperation[]
            {
                new AddVideoAssetOperation(new LayerIndexReference(0), video, output, "base", new(0m), new(4m)),
                new AddVideoLayerOperation("ワイプ"),
                new AddVideoAssetOperation(new LayerNameReference("ワイプ"), video, output, "wipe", new(1m), new(3m)),
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.BatchResult.OperationResults[1].CreatedLayerIndex);
            Assert.Equal(new TimelineObjectId(1, 2), result.BatchResult.OperationResults[2].CreatedObjectId);
            RecotteProjectDocument saved = RecotteProject.Load(output);
            Assert.True(saved.Validate().IsValid);
            Assert.Equal(new[] { "Video", "Video", "Speaker", "Annot" }, saved.Layers.Select(layer => layer.Type));
            Assert.Equal("base", Assert.Single(saved.Layers[0].Objects).Name);
            TimelineObjectView wipe = Assert.Single(saved.Layers[1].Objects);
            Assert.Equal("wipe", wipe.Name);
            Assert.Equal(1m, wipe.StartTime);
            Assert.Equal(3m, wipe.EndTime);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AddVideoLayer_NumbersDefaultNamesAfterTheExistingLayers()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        ProjectBatchResult batch = document.ApplyOperations(new[]
        {
            new AddVideoLayerOperation(), new AddVideoLayerOperation(),
        });

        Assert.True(batch.Success);
        Assert.Equal(new int?[] { 1, 2 }, batch.OperationResults.Select(result => result.CreatedLayerIndex));
        Assert.Equal(new[] { "映像・音声1", "映像・音声2", "映像・音声3", "話者1", "注釈1" },
            document.Layers.Select(layer => layer.Name));
    }

    [Theory]
    [InlineData("話者1")]
    [InlineData("映像・音声1")]
    [InlineData(" ")]
    public void AddVideoLayer_RejectsABlankOrDuplicateName(string name)
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        using ProjectEditSession session = document.BeginEdit();

        LayerAddResult result = session.Editor.AddVideoLayer(new(name));

        Assert.False(result.Success);
        Assert.Null(result.LayerIndex);
        Assert.Equal("RC4002", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }

    [Fact]
    public void AddVideoLayer_FailureRollsBackTheWholeBatch()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        ProjectBatchResult batch = document.ApplyOperations(new[]
        {
            new AddVideoLayerOperation("B"), new AddVideoLayerOperation("B"),
        });

        Assert.False(batch.Success);
        Assert.Equal(1, batch.FailedOperationIndex);
        Assert.Equal(3, document.Layers.Count);
    }

    [Fact]
    public void Capabilities_ReportVideoLayerAdditionButNotGeneralLayerAddition()
    {
        ProjectCapabilities capabilities = RecotteProject.Load(TestProjects.EmptyProject).Capabilities;

        Assert.True(capabilities.CanAddVideoLayer);
        Assert.False(capabilities.CanAddLayer);
    }

    private static JsonObject Layer(string path, int index) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(path))!["layers"]![index]!.DeepClone();
}
