using System.Text;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class BoundsEditingTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteBounds-").FullName;
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("004OneImage", "Image")]
    [InlineData("009FullProject", "Figure")]
    [InlineData("010OneAnnotationText", "Figure")]
    [InlineData("005OneVideo", "Video")]
    public void SetObjectBounds_SavesTheRectangleAndKeepsEverythingElse(string sample, string type)
    {
        JsonObject root = Sample(sample);
        (TimelineObjectId id, int objectIndex, JsonObject original) = First(root, type);

        RecotteProjectDocument document = Load(root);
        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new SetObjectBoundsOperation(new TimelineObjectIdReference(id), 100m, -20m, 640m, 360m) });

        Assert.True(result.Success, Messages(result));
        ProjectChange change = Assert.Single(result.Changes);
        Assert.Equal("SetObjectBounds", change.Operation);
        Assert.Equal(id.ToString(), change.Target);
        Assert.Equal($"$.layers[{id.LayerIndex}].layer-objects[{objectIndex}].properties.Bounds.p-value", change.JsonPath);
        string output = Path.Combine(directory, "moved.ccproj");
        document.SaveCopy(output);
        JsonObject saved = Object(JsonNode.Parse(File.ReadAllText(output))!.AsObject(), id);
        Assert.Equal(new[] { 100m, -20m, 640m, 360m }, Bounds(saved));
        JsonObject expected = (JsonObject)original.DeepClone();
        expected["properties"]!["Bounds"]!["p-value"] = saved["properties"]!["Bounds"]!["p-value"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(expected, saved), saved.ToJsonString());
    }

    [Fact]
    public void SetObjectBounds_KeepsOmittedValues()
    {
        JsonObject root = Sample("009FullProject");
        (TimelineObjectId id, _, _) = First(root, "Figure");
        RecotteProjectDocument document = Load(root);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new SetObjectBoundsOperation(new TimelineObjectIdReference(id), X: 50m),
            new SetObjectBoundsOperation(new TimelineObjectIdReference(id), Height: 64m),
        });

        Assert.True(result.Success, Messages(result));
        Assert.Equal("[211.0,629.0,128.0,128.0]", result.Changes[0].Before);
        TimelineEntryView entry = Assert.Single(document.GetTimelineEntries(), item => item.ObjectId == id);
        Assert.Equal(new ObjectBounds(50m, 629m, 128m, 64m), entry.Bounds);
    }

    [Fact]
    public void SetObjectBounds_MovesARecotteSavedVideoDespiteItsPreviewLocks()
    {
        JsonObject root = Sample("013TwoVideos");
        JsonObject video = root["layers"]![1]!["layer-objects"]!.AsArray().OfType<JsonObject>().Single(item => (string?)item["type"] == "Video");
        video["st-locked"] = true;
        video["pv-locked"] = true;
        RecotteProjectDocument document = Load(root);
        TimelineEntryView wipe = Assert.Single(document.GetTimelineEntries(), item => item.LayerIndex == 1);
        Assert.True(wipe.IsLocked);
        Assert.True(wipe.Capabilities.CanSetBounds);
        Assert.Equal(new ObjectBounds(0m, 0m, 1920m, 1080m), wipe.Bounds);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new SetObjectBoundsOperation(new TimelineObjectIdReference(wipe.ObjectId!.Value), 1380m, 60m, 480m, 270m) });

        Assert.True(result.Success, Messages(result));
        string output = Path.Combine(directory, "wipe.ccproj");
        document.SaveCopy(output);
        JsonObject saved = Object(JsonNode.Parse(File.ReadAllText(output))!.AsObject(), wipe.ObjectId!.Value);
        Assert.Equal(new[] { 1380m, 60m, 480m, 270m }, Bounds(saved));
        Assert.True(saved["st-locked"]!.GetValue<bool>());
        Assert.True(saved["pv-locked"]!.GetValue<bool>());
        Assert.Equal(new ObjectBounds(0m, 0m, 1920m, 1080m),
            Assert.Single(document.GetTimelineEntries(), item => item.LayerIndex == 0).Bounds);
    }

    [Theory]
    [InlineData(null, null, null, null, "RC4913")]
    [InlineData(null, null, 0, null, "RC4913")]
    [InlineData(null, null, null, -1, "RC4913")]
    public void SetObjectBounds_RejectsInvalidValues(int? x, int? y, int? width, int? height, string code)
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, _, _) = First(root, "Image");
        AssertRejected(root, new SetObjectBoundsOperation(new TimelineObjectIdReference(id), x, y, width, height), code);
    }

    [Fact]
    public void SetObjectBounds_RejectsObjectsWithoutEditableBounds()
    {
        JsonObject voice = Sample("002OneText");
        (TimelineObjectId voiceId, _, _) = First(voice, "Speaker Voice");
        AssertRejected(voice, new SetObjectBoundsOperation(new TimelineObjectIdReference(voiceId), 0m), "RC4911");

        JsonObject character = Sample("006OneCharacter");
        (TimelineObjectId characterId, _, _) = First(character, "Speaker Character");
        AssertRejected(character, new SetObjectBoundsOperation(new TimelineObjectIdReference(characterId), 0m), "RC4911");

        JsonObject animated = Sample("004OneImage");
        (TimelineObjectId imageId, _, _) = First(animated, "Image");
        Object(animated, imageId)["properties"]!["Bounds"]!["keyframes"] = new JsonArray(new JsonObject { ["time"] = 0.0 });
        AssertRejected(animated, new SetObjectBoundsOperation(new TimelineObjectIdReference(imageId), 0m), "RC4912");
        Assert.False(Assert.Single(Load(animated).GetTimelineEntries()).Capabilities.CanSetBounds);

        AssertRejected(Sample("004OneImage"), new SetObjectBoundsOperation(new ObjectKeyReference(9999), 0m), "RC6001");
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "tl-locked")]
    [InlineData(false, "locked")]
    public void SetObjectBounds_RejectsLockedLayersAndTimelineLockedObjects(bool lockLayer, string? objectLock)
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, _, _) = First(root, "Image");
        if (lockLayer) root["layers"]![id.LayerIndex]!["locked"] = true;
        if (objectLock is not null) Object(root, id)[objectLock] = true;

        Assert.False(Assert.Single(Load(root).GetTimelineEntries()).Capabilities.CanSetBounds);
        AssertRejected(root, new SetObjectBoundsOperation(new TimelineObjectIdReference(id), 10m), "RC4207");
    }

    [Fact]
    public void Capabilities_ReportBoundsEditing()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        Assert.True(document.Capabilities.CanSetObjectBounds);
    }

    private static void AssertRejected(JsonObject root, ProjectOperation operation, string code)
    {
        RecotteProjectDocument document = Load(root);
        long revision = document.Revision;
        ProjectBatchResult result = document.ApplyOperations(new[] { operation });
        Assert.False(result.Success);
        Assert.Equal(code, result.Diagnostics.First(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Code);
        Assert.Equal(revision, document.Revision);
    }

    private static JsonObject Sample(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(TestProjects.RepositoryRoot,
        "Documents", "RecotteProjects", name, name + ".ccproj")))!.AsObject();
    private static RecotteProjectDocument Load(JsonObject root) => RecotteProject.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));

    private static (TimelineObjectId Id, int ObjectIndex, JsonObject Object) First(JsonObject root, string type)
    {
        JsonArray layers = root["layers"]!.AsArray();
        for (int layer = 0; layer < layers.Count; layer++)
        {
            JsonArray objects = layers[layer]!["layer-objects"]!.AsArray();
            for (int index = 0; index < objects.Count; index++)
                if (objects[index]!["type"]!.GetValue<string>() == type)
                    return (new(layer, objects[index]!["objkey"]!.GetValue<int>()), index, (JsonObject)objects[index]!.DeepClone());
        }
        throw new InvalidOperationException($"The sample has no {type} object.");
    }

    private static JsonObject Object(JsonObject root, TimelineObjectId id) => root["layers"]![id.LayerIndex]!["layer-objects"]!
        .AsArray().OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == id.ObjectKey);

    private static decimal[] Bounds(JsonObject item) => item["properties"]!["Bounds"]!["p-value"]!.AsArray()
        .Select(value => value!.GetValue<decimal>()).ToArray();

    private static string Messages(ProjectBatchResult result) => string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
}
