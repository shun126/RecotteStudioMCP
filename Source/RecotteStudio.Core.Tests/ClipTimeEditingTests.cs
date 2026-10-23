using System.Text;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class ClipTimeEditingTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteClipTime-").FullName;
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("004OneImage", "Image")]
    [InlineData("010OneAnnotationText", "Figure")]
    [InlineData("009FullProject", "Figure")]
    [InlineData("005OneVideo", "Video")]
    public void Move_ShiftsTheClipAndKeepsEverythingElse(string sample, string type)
    {
        JsonObject root = Sample(sample);
        (TimelineObjectId id, int objectIndex, JsonObject original) = First(root, type);
        decimal start = original["start-time"]!.GetValue<decimal>(), end = original["end-time"]!.GetValue<decimal>();
        RecotteProjectDocument document = Load(root);
        Assert.True(Entry(document, id).Capabilities.CanMove);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(start + 2m), new(end + 2m)) });

        Assert.True(result.Success, Messages(result));
        Assert.Equal(new[] { "start-time", "end-time" }, result.Changes.Select(change => change.JsonPath.Split('.')[^1]));
        Assert.All(result.Changes, change => Assert.StartsWith($"$.layers[{id.LayerIndex}].layer-objects[{objectIndex}].", change.JsonPath));
        JsonObject saved = Saved(document, id);
        JsonObject expected = (JsonObject)original.DeepClone();
        expected["start-time"] = start + 2m;
        expected["end-time"] = end + 2m;
        Assert.True(JsonNode.DeepEquals(expected, saved), saved.ToJsonString());
    }

    [Fact]
    public void Resize_ScalesTheVideoSourceRangeAndExtendsTheProject()
    {
        JsonObject root = Sample("005OneVideo");
        (TimelineObjectId id, _, _) = First(root, "Video");
        RecotteProjectDocument document = Load(root);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(1m), new(1.5m)) });

        Assert.True(result.Success, Messages(result));
        JsonObject saved = Saved(document, id);
        Assert.Equal(0m, saved["properties"]!["StartTime"]!["p-value"]!.GetValue<decimal>());
        // The clip was 0.76665 seconds of a 0.76665-second range at normal speed, so 0.5 seconds plays 0.5 seconds of the file.
        Assert.Equal(0.5m, Math.Round(saved["properties"]!["EndTime"]!["p-value"]!.GetValue<decimal>(), 6));
        Assert.Equal(1.5m, document.Settings!.Duration);
        Assert.False(saved["st-locked"]!.GetValue<bool>());
        Assert.False(saved["pv-locked"]!.GetValue<bool>());
    }

    [Fact]
    public void Resize_KeepsAFadeInAtTheStartAndAFadeOutAtTheEnd()
    {
        JsonObject root = Sample("009FullProject");
        JsonArray objects = root["layers"]![0]!["layer-objects"]!.AsArray();
        objects.Remove(objects.OfType<JsonObject>().Single(item => item["type"]!.GetValue<string>() == "Transition"));
        RecotteProjectDocument document = Load(root);
        TimelineObjectId fadeIn = new(0, 1000), fadeOut = new(0, 1001);
        Assert.True(Entry(document, fadeOut).Capabilities.CanMove);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new MoveTimelineObjectOperation(new TimelineObjectIdReference(fadeIn), new(0m), new(2m)),
            new MoveTimelineObjectOperation(new TimelineObjectIdReference(fadeOut), new(3m), new(5m)),
        });

        Assert.True(result.Success, Messages(result));
        Assert.Equal(new FadeEffectView(0, "Alpha Fade", 0m, 0.5m), Assert.Single(document.GetFadeEffects(fadeIn)));
        Assert.Equal(new FadeEffectView(0, "Alpha Fade Out", 1.5m, 0.5m), Assert.Single(document.GetFadeEffects(fadeOut)));
    }

    [Fact]
    public void Move_ReleasesTheClipsOwnSnap()
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, _, _) = First(root, "Image");
        Object(root, id)["start-snap"] = 77;
        RecotteProjectDocument document = Load(root);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(3m), new(4m)) });

        Assert.True(result.Success, Messages(result));
        Assert.Equal(0, Saved(document, id)["start-snap"]!.GetValue<int>());
    }

    [Fact]
    public void Move_KeepsUnknownEffectsWhenTheLengthStaysTheSame()
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, _, _) = First(root, "Image");
        Object(root, id)["sub-objects"]!.AsArray().Add(new JsonObject { ["category"] = "Effect", ["start-time"] = 0.2, ["duration"] = 0.3 });
        decimal length = Object(root, id)["end-time"]!.GetValue<decimal>() - Object(root, id)["start-time"]!.GetValue<decimal>();

        RecotteProjectDocument document = Load(root);
        Assert.True(document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(5m), new(5m + length)) }).Success);
        AssertRejected(root, new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(5m), new(9m)), "RC4302");
    }

    [Fact]
    public void Resize_RejectsAFadeLongerThanTheClip()
    {
        JsonObject root = Sample("009FullProject");
        JsonArray objects = root["layers"]![0]!["layer-objects"]!.AsArray();
        objects.Remove(objects.OfType<JsonObject>().Single(item => item["type"]!.GetValue<string>() == "Transition"));
        AssertRejected(root, new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(0, 1000)), new(0m), new(0.3m)), "RC4302");
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1001)]
    public void Move_RejectsClipsJoinedByATransition(int key)
    {
        JsonObject root = Sample("009FullProject");
        TimelineObjectId id = new(0, key);
        Assert.False(Entry(Load(root), id).Capabilities.CanMove);
        AssertRejected(root, new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(4m), new(5m)), "RC4303");
    }

    [Fact]
    public void Move_IgnoresPreviewLocksOfARecotteSavedVideo()
    {
        JsonObject root = Sample("013TwoVideos");
        JsonObject video = root["layers"]![1]!["layer-objects"]!.AsArray().OfType<JsonObject>().Single(item => (string?)item["type"] == "Video");
        video["st-locked"] = true;
        video["pv-locked"] = true;
        RecotteProjectDocument document = Load(root);
        TimelineEntryView wipe = Assert.Single(document.GetTimelineEntries(), item => item.LayerIndex == 1);
        Assert.True(wipe.IsLocked);
        Assert.True(wipe.Capabilities.CanMove);

        Assert.True(document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(wipe.ObjectId!.Value), new(0m), new(0.7m)) }).Success);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "tl-locked")]
    [InlineData(false, "locked")]
    public void Move_RejectsLockedLayersAndTimelineLockedClips(bool lockLayer, string? objectLock)
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, _, _) = First(root, "Image");
        if (lockLayer) root["layers"]![id.LayerIndex]!["locked"] = true;
        if (objectLock is not null) Object(root, id)[objectLock] = true;

        Assert.False(Entry(Load(root), id).Capabilities.CanMove);
        AssertRejected(root, new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(1m), new(2m)), "RC4207");
    }

    [Fact]
    public void Move_RejectsAnEmptyRangeAndUnsupportedObjects()
    {
        JsonObject image = Sample("004OneImage");
        (TimelineObjectId imageId, _, _) = First(image, "Image");
        AssertRejected(image, new MoveTimelineObjectOperation(new TimelineObjectIdReference(imageId), new(1m), new(1m)), "RC4301");

        JsonObject character = Sample("006OneCharacter");
        (TimelineObjectId characterId, _, _) = First(character, "Speaker Character");
        Assert.False(Entry(Load(character), characterId).Capabilities.CanMove);
        AssertRejected(character, new MoveTimelineObjectOperation(new TimelineObjectIdReference(characterId), new(0m), new(1m)), "RC4202");
    }

    [Fact]
    public void Capabilities_ReportClipMoving()
    {
        Assert.True(RecotteProject.Load(TestProjects.EmptyProject).Capabilities.CanMoveClips);
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

    private JsonObject Saved(RecotteProjectDocument document, TimelineObjectId id)
    {
        string output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ccproj");
        document.SaveCopy(output);
        return Object(JsonNode.Parse(File.ReadAllText(output))!.AsObject(), id);
    }

    private static TimelineEntryView Entry(RecotteProjectDocument document, TimelineObjectId id) =>
        Assert.Single(document.GetTimelineEntries(), item => item.ObjectId == id);

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

    private static string Messages(ProjectBatchResult result) => string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
}
