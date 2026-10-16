using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class LayerMoveTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteLayerMoveTests-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    private static string Sample(string folder) => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
        folder, folder + ".ccproj");

    [Fact]
    public void MoveLayer_SwapsTwoSpeakerTracksAsRecotteStudioDoes()
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample("008TwoCharacters"));

        ProjectBatchResult batch = document.ApplyOperations(new[]
        {
            new MoveLayerOperation(new LayerNameReference("話者2"), 1),
        });

        Assert.True(batch.Success);
        Assert.Equal("layer:1", batch.OperationResults[0].Target);
        string output = Path.Combine(directory, "swapped.ccproj");
        document.SaveCopy(output);
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
        // 014SwapTwoCharacters is 008TwoCharacters after swapping its Speaker tracks in Recotte Studio.
        JsonObject expected = JsonNode.Parse(File.ReadAllText(Sample("014SwapTwoCharacters")))!.AsObject();
        JsonObject actual = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        JsonArray expectedLayers = expected["layers"]!.AsArray();
        JsonArray actualLayers = actual["layers"]!.AsArray();
        Assert.Equal(expectedLayers.Count, actualLayers.Count);
        for (int index = 0; index < expectedLayers.Count; index++)
        {
            JsonObject left = (JsonObject)expectedLayers[index]!.DeepClone();
            JsonObject right = (JsonObject)actualLayers[index]!.DeepClone();
            left.Remove("selected");
            right.Remove("selected");
            Assert.True(JsonNode.DeepEquals(left, right), $"layer {index}: {right.ToJsonString()}");
        }
        Assert.Equal(Keys(expected["speakers"]), Keys(actual["speakers"]));
        Assert.Contains(batch.Changes, change => change.Operation == "OrderSpeakers");
    }

    [Fact]
    public void MoveLayer_SendsABackgroundAnnotLayerBehindEveryLayer()
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample("004OneImage"));
        string[] speakers = Keys(JsonNode.Parse(File.ReadAllText(Sample("004OneImage")))!["speakers"]);

        ProjectBatchResult batch = document.ApplyOperations(new[] { new MoveLayerOperation(new LayerIndexReference(2), 0) });

        Assert.True(batch.Success);
        Assert.Equal(new[] { "Annot", "Video", "Speaker" }, document.Layers.Select(layer => layer.Type));
        Assert.Equal("Image", Assert.Single(document.Layers[0].Objects).Type);
        Assert.DoesNotContain(batch.Changes, change => change.Operation == "OrderSpeakers");
        string output = Path.Combine(directory, "background.ccproj");
        document.SaveCopy(output);
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
        Assert.Equal(speakers, Keys(JsonNode.Parse(File.ReadAllText(output))!["speakers"]));
    }

    [Fact]
    public void MoveLayer_RebasesEarlierResultsAndChangesToTheFinalLayout()
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample("011TwoVoices"));

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者2"), "追加", new(5m), new(6m)),
            new MoveLayerOperation(new LayerNameReference("話者2"), 1),
        });

        Assert.True(batch.Success);
        TimelineObjectId created = Assert.IsType<TimelineObjectId>(batch.OperationResults[0].CreatedObjectId);
        Assert.Equal(1, created.LayerIndex);
        Assert.Equal("layer:1", batch.OperationResults[0].Target);
        Assert.Contains(document.Layers[1].Objects, item => item.ObjectKey == created.ObjectKey && item.Text == "追加");
        Assert.All(batch.OperationResults[0].Changes, change => Assert.StartsWith("$.layers[1]", change.JsonPath));
        Assert.Equal(batch.OperationResults.SelectMany(operation => operation.Changes), batch.Changes);
    }

    [Fact]
    public void MoveLayer_KeepsTrackOfAudioThatNeedsRegenerating()
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample("011TwoVoices"));

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new UpdateSpeakerTextOperation(new TimelineObjectIdReference(new(1, 1001)), "変更"),
            new MoveLayerOperation(new LayerNameReference("話者1"), 2),
        });

        Assert.True(batch.Success);
        Assert.Equal(new TimelineObjectId(2, 1001), Assert.Single(batch.AudioRegenerationRequired));
        ProjectDiagnostic warning = Assert.Single(batch.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
        Assert.Contains("on layer 2 was changed", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveLayer_ToItsOwnIndexChangesNothing()
    {
        using ProjectEditSession session = RecotteProject.Load(Sample("008TwoCharacters")).BeginEdit();

        LayerMoveResult result = session.Editor.MoveLayer(new(1, 1));

        Assert.True(result.Success);
        Assert.Equal(1, result.LayerIndex);
        Assert.Empty(session.Changes);
    }

    [Theory]
    [InlineData(1, 4, "RC4003")]
    [InlineData(1, -1, "RC4003")]
    [InlineData(4, 0, "RC4005")]
    public void MoveLayer_RejectsAMissingLayerOrAnIndexOutOfRange(int layerIndex, int targetIndex, string code)
    {
        using ProjectEditSession session = RecotteProject.Load(Sample("008TwoCharacters")).BeginEdit();

        LayerMoveResult result = session.Editor.MoveLayer(new(layerIndex, targetIndex));

        Assert.False(result.Success);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }

    [Fact]
    public void MoveLayer_RejectsALockedLayer()
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(Sample("008TwoCharacters")))!.AsObject();
        root["layers"]![2]!["locked"] = true;
        string path = Path.Combine(directory, "Locked.ccproj");
        File.WriteAllText(path, root.ToJsonString());
        using ProjectEditSession session = RecotteProject.Load(path).BeginEdit();

        LayerMoveResult result = session.Editor.MoveLayer(new(2, 1));

        Assert.False(result.Success);
        Assert.Equal("RC4005", Assert.Single(result.Diagnostics).Code);
    }

    private static string[] Keys(JsonNode? speakers) => speakers!.AsArray().Select(item => (string)item!["key"]!).ToArray();
}
