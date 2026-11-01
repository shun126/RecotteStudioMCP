using System.Text;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class SpeakerActionTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteSpeakerAction-").FullName;
    public void Dispose() => Directory.Delete(directory, true);

    // Each case removes an action Recotte Studio saved and adds it back: 3D with and without Loop, 2D, and a 1.8.5.0 project.
    [Theory]
    [InlineData("015SpeakerActions", 1, 1004)]
    [InlineData("015SpeakerActions", 1, 1001)]
    [InlineData("015SpeakerActions", 2, 1001)]
    [InlineData("009FullProject", 2, 1001)]
    public void AddSpeakerAction_ReproducesTheActionRecotteStudioSaved(string sample, int layer, int objectKey)
    {
        JsonObject root = Sample(sample);
        JsonArray objects = root["layers"]![layer]!["layer-objects"]!.AsArray();
        JsonObject original = objects.OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == objectKey);
        objects.Remove(original);
        RecotteProjectDocument document = Load(root);
        string action = original["properties"]!["Emotion"]!["p-value"]!.GetValue<string>();
        bool loop = original["properties"]!["Loop"]!["p-value"]!.GetValue<bool>();

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new AddSpeakerActionOperation(new LayerIndexReference(layer), action,
                new(original["start-time"]!.GetValue<decimal>()), new(original["end-time"]!.GetValue<decimal>()), loop),
        });

        Assert.True(result.Success, Messages(result));
        TimelineObjectId created = Assert.IsType<TimelineObjectId>(Assert.Single(result.OperationResults).CreatedObjectId);
        Assert.Equal(layer, created.LayerIndex);
        string output = Path.Combine(directory, "action.ccproj");
        document.SaveCopy(output);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(output))!["layers"]![layer]!["layer-objects"]!.AsArray()
            .OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == created.ObjectKey);
        Assert.Equal(Number(original, "start-time"), Number(saved, "start-time"), 9);
        Assert.Equal(Number(original, "end-time"), Number(saved, "end-time"), 9);
        Assert.Equal(Number(OutWeight(original), "start-time"), Number(OutWeight(saved), "start-time"), 9);
        Assert.True(JsonNode.DeepEquals(WithoutTimes(original), WithoutTimes(saved)), saved.ToJsonString());
        TimelineEntryView entry = Assert.Single(RecotteProject.Load(output).GetTimelineEntries(), item => item.ObjectId == created);
        Assert.Equal(action, entry.Action);
    }

    [Fact]
    public void AddSpeakerAction_AcceptsTheActionNameWithoutItsPack()
    {
        RecotteProjectDocument document = Load(Sample("015SpeakerActions"));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new AddSpeakerActionOperation(new LayerNameReference("話者1"), "Surprise.vmd", new(30m), new(32m)),
            new AddSpeakerActionOperation(new LayerNameReference("話者2"), "smile", new(30m), new(32m)),
        });

        Assert.True(result.Success, Messages(result));
        Assert.Equal(new[] { "tsurumaki_maki:Surprise.vmd", "2D-Maki:smile" }, result.Changes.Select(change => change.After));
    }

    [Fact]
    public void Capabilities_AndLayersReportTheCharactersActions()
    {
        RecotteProjectDocument document = Load(Sample("015SpeakerActions"));

        Assert.True(document.Capabilities.CanAddSpeakerAction);
        SpeakerLayerView[] layers = document.Layers.OfType<SpeakerLayerView>().ToArray();
        Assert.Equal(30, layers[0].Actions.Count);
        Assert.Contains("tsurumaki_maki:JITO.vmd", layers[0].Actions);
        Assert.Equal(new[] { "2D-Maki:normal", "2D-Maki:smile" }, layers[1].Actions);
        Assert.Empty(Assert.Single(RecotteProject.Load(TestProjects.EmptyProject).Layers.OfType<SpeakerLayerView>()).Actions);
    }

    [Theory]
    [InlineData(0, "smile", 1, 2, "RC4921")]   // a Video layer
    [InlineData(2, "angry", 1, 2, "RC4922")]   // not one of 2D-Maki's actions
    [InlineData(2, "", 1, 2, "RC4922")]
    [InlineData(2, "smile", 2, 2, "RC4923")]
    [InlineData(2, "smile", 2, 2.3, "RC4923")] // shorter than the 0.2-second blend-in and blend-out
    public void AddSpeakerAction_RejectsInvalidRequests(int layer, string action, decimal start, decimal end, string code) =>
        AssertRejected(Sample("015SpeakerActions"), new AddSpeakerActionOperation(new LayerIndexReference(layer), action, new(start), new(end)), code);

    [Fact]
    public void AddSpeakerAction_RejectsLayersWithoutAnUnlockedCharacter()
    {
        AssertRejected(RootOf(TestProjects.EmptyProject), new AddSpeakerActionOperation(new LayerIndexReference(1), "smile", new(0m), new(1m)), "RC4921");

        JsonObject locked = Sample("015SpeakerActions");
        locked["layers"]![2]!["locked"] = true;
        AssertRejected(locked, new AddSpeakerActionOperation(new LayerIndexReference(2), "smile", new(0m), new(1m)), "RC4921");
    }

    [Fact]
    public void AddSpeakerAction_ListsTheAvailableActionsWhenTheActionIsUnknown()
    {
        ProjectBatchResult result = Load(Sample("015SpeakerActions")).ApplyOperations(new ProjectOperation[]
            { new AddSpeakerActionOperation(new LayerIndexReference(2), "angry", new(0m), new(1m)) });

        Assert.Contains("2D-Maki:normal, 2D-Maki:smile", Assert.Single(result.Diagnostics).Message);
    }

    // Action 1004 on layer 1 spans 15-20 with its OutWeight blend at 4.8; the blend follows the new end.
    [Theory]
    [InlineData(16, 19, 2.8)]   // shorter
    [InlineData(10, 22, 11.8)]  // earlier and longer
    [InlineData(25, 30, 4.8)]   // later, same length
    public void MoveTimelineObject_MovesAndResizesASpeakerAction(decimal start, decimal end, decimal outWeightStart)
    {
        RecotteProjectDocument document = Load(Sample("015SpeakerActions"));
        TimelineObjectId id = new(1, 1004);
        Assert.True(Assert.Single(document.GetTimelineEntries(), item => item.ObjectId == id).Capabilities.CanMove);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(id), new(start), new(end)) });

        Assert.True(result.Success, Messages(result));
        JsonObject saved = Saved(document, id);
        Assert.Equal((double)start, Number(saved, "start-time"), 9);
        Assert.Equal((double)end, Number(saved, "end-time"), 9);
        Assert.Equal(0d, Number(Weight(saved, "InWeight"), "start-time"), 9);
        Assert.Equal((double)outWeightStart, Number(OutWeight(saved), "start-time"), 9);
        Assert.Equal("tsurumaki_maki:JITO.vmd", saved["properties"]!["Emotion"]!["p-value"]!.GetValue<string>());
    }

    [Fact]
    public void MoveTimelineObject_RejectsASpeakerActionShorterThanItsBlends()
    {
        AssertRejected(Sample("015SpeakerActions"),
            new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1004)), new(15m), new(15.3m)), "RC4923");
        AssertRejected(Sample("015SpeakerActions"),
            new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1004)), new(15m), new(15m)), "RC4301");
    }

    [Fact]
    public void MoveTimelineObject_RejectsALockedSpeakerAction()
    {
        JsonObject root = Sample("015SpeakerActions");
        Object(root, 1, 1004)["tl-locked"] = true;
        AssertRejected(root, new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1004)), new(16m), new(19m)), "RC4207");
        Assert.False(Assert.Single(Load(root).GetTimelineEntries(), item => item.ObjectId == new TimelineObjectId(1, 1004)).Capabilities.CanMove);
    }

    [Fact]
    public void UpdateSpeakerAction_ReplacesTheActionAndLoopAndKeepsTheRest()
    {
        RecotteProjectDocument document = Load(Sample("015SpeakerActions"));
        TimelineObjectId id = new(1, 1004);
        JsonObject before = (JsonObject)Object(Sample("015SpeakerActions"), 1, 1004).DeepClone();
        Assert.True(Assert.Single(document.GetTimelineEntries(), item => item.ObjectId == id).Capabilities.CanUpdateAction);
        Assert.True(document.Capabilities.CanUpdateSpeakerAction);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new UpdateSpeakerActionOperation(new TimelineObjectIdReference(id), "Smile_1.vmd", false),
            new UpdateSpeakerActionOperation(new TimelineObjectIdReference(new(2, 1001)), "normal"),
        });

        Assert.True(result.Success, Messages(result));
        JsonObject saved = Saved(document, id);
        Assert.Equal("tsurumaki_maki:Smile_1.vmd", saved["properties"]!["Emotion"]!["p-value"]!.GetValue<string>());
        Assert.False(saved["properties"]!["Loop"]!["p-value"]!.GetValue<bool>());
        before["properties"]!["Emotion"]!["p-value"] = "tsurumaki_maki:Smile_1.vmd";
        before["properties"]!["Loop"]!["p-value"] = false;
        Assert.True(JsonNode.DeepEquals(before, saved), saved.ToJsonString());
        Assert.Equal("2D-Maki:normal", Saved(document, new(2, 1001))["properties"]!["Emotion"]!["p-value"]!.GetValue<string>());
        Assert.Equal("tsurumaki_maki:Smile_1.vmd",
            Assert.Single(document.GetTimelineEntries(), item => item.ObjectId == id).Action);
    }

    [Theory]
    [InlineData(1, 1000, "smile", null, "RC4926")]          // the Speaker Character, not an action
    [InlineData(1, 1004, null, null, "RC4927")]
    [InlineData(1, 1004, "angry", null, "RC4922")]
    [InlineData(1, 1004, " ", null, "RC4922")]
    public void UpdateSpeakerAction_RejectsInvalidRequests(int layer, int objectKey, string? action, bool? loop, string code) =>
        AssertRejected(Sample("015SpeakerActions"),
            new UpdateSpeakerActionOperation(new TimelineObjectIdReference(new(layer, objectKey)), action, loop), code);

    [Fact]
    public void UpdateSpeakerAction_RejectsALockedAction()
    {
        JsonObject root = Sample("015SpeakerActions");
        Object(root, 2, 1001)["tl-locked"] = true;
        AssertRejected(root, new UpdateSpeakerActionOperation(new TimelineObjectIdReference(new(2, 1001)), "normal"), "RC4207");
        Assert.False(Assert.Single(Load(root).GetTimelineEntries(), item => item.ObjectId == new TimelineObjectId(2, 1001)).Capabilities.CanUpdateAction);
    }

    // 009FullProject's 話者1 (layer 1) with line 1002 at 1-5 and three actions: 1001 placed with the line, 1003 in part
    // of it at 2-3, and 1004 held from before the line to after it at 0.5-6.
    private static JsonObject LineWithActions()
    {
        JsonObject root = Sample("009FullProject");
        JsonArray objects = root["layers"]![1]!["layer-objects"]!.AsArray();
        JsonObject action = Object(root, 1, 1001);
        SetRange(Object(root, 1, 1002), 1m, 5m);
        SetRange(action, 0.9999999999999998m, 5m);
        foreach ((int key, decimal start, decimal end) in new[] { (1003, 2m, 3m), (1004, 0.5m, 6m) })
        {
            JsonObject copy = (JsonObject)action.DeepClone();
            copy["objkey"] = key;
            SetRange(copy, start, end);
            objects.Add(copy);
        }
        return root;
    }

    private static void SetRange(JsonObject item, decimal start, decimal end)
    {
        item["start-time"] = start;
        item["end-time"] = end;
        if (item["type"]!.GetValue<string>() == "Speaker Action") OutWeight(item)["start-time"] = end - start - 0.2m;
    }

    [Fact]
    public void MoveTimelineObject_MovesTheActionsWithinALineWithIt()
    {
        RecotteProjectDocument document = Load(LineWithActions());

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1002)), new(2m), new(8m)) });

        Assert.True(result.Success, Messages(result));
        Assert.Equal((2m, 8m), Range(document, 1001));    // placed with the line: takes its new range
        Assert.Equal((3.5m, 5m), Range(document, 1003));  // 1-2 seconds into a 4-second line: the same share of 6 seconds
        Assert.Equal((0.5m, 6m), Range(document, 1004));  // reaches outside the line: stays
        Assert.Equal(5.8, Number(OutWeight(Saved(document, new(1, 1001))), "start-time"), 9);
        Assert.Equal(1.3, Number(OutWeight(Saved(document, new(1, 1003))), "start-time"), 9);
    }

    [Fact]
    public void MoveTimelineObject_MovesALineAloneWithoutMoveActions()
    {
        RecotteProjectDocument document = Load(LineWithActions());

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1002)), new(2m), new(8m), MoveActions: false) });

        Assert.True(result.Success, Messages(result));
        Assert.Equal((2m, 8m), Range(document, 1002));
        Assert.Equal((2m, 3m), Range(document, 1003));
        Assert.Single(result.Changes.Select(change => change.Target).Distinct());
    }

    [Fact]
    public void MoveTimelineObject_RejectsALineWhoseActionsCannotFollow()
    {
        JsonObject locked = LineWithActions();
        Object(locked, 1, 1003)["tl-locked"] = true;
        AssertRejected(locked, new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1002)), new(2m), new(8m)), "RC4207");

        // Shrinking the line to 0.5 seconds would leave action 1003 at an eighth of that, shorter than its blends.
        AssertRejected(LineWithActions(), new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1002)), new(1m), new(1.5m)), "RC4923");
    }

    private static (decimal, decimal) Range(RecotteProjectDocument document, int objectKey)
    {
        TimelineEntryView entry = Assert.Single(document.GetTimelineEntries(), item => item.ObjectId == new TimelineObjectId(1, objectKey));
        return (entry.StartTime!.Value.TotalSeconds, entry.EndTime!.Value.TotalSeconds);
    }

    private JsonObject Saved(RecotteProjectDocument document, TimelineObjectId id)
    {
        string output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ccproj");
        document.SaveCopy(output);
        return Object(JsonNode.Parse(File.ReadAllText(output))!.AsObject(), id.LayerIndex, id.ObjectKey);
    }

    private static JsonObject Object(JsonObject root, int layer, int objectKey) => root["layers"]![layer]!["layer-objects"]!.AsArray()
        .OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == objectKey);

    private static void AssertRejected(JsonObject root, ProjectOperation operation, string code)
    {
        RecotteProjectDocument document = Load(root);
        long revision = document.Revision;
        ProjectBatchResult result = document.ApplyOperations(new[] { operation });
        Assert.False(result.Success);
        Assert.Equal(code, result.Diagnostics.First(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Code);
        Assert.Equal(revision, document.Revision);
    }

    // Recotte Studio saves the clip that was last clicked as selected; an added action is never selected.
    private static JsonObject WithoutTimes(JsonObject action)
    {
        JsonObject copy = (JsonObject)action.DeepClone();
        copy.Remove("objkey");
        copy.Remove("start-time");
        copy.Remove("end-time");
        copy["selected"] = false;
        OutWeight(copy).Remove("start-time");
        return copy;
    }

    private static JsonObject OutWeight(JsonObject action) => Weight(action, "OutWeight");
    private static JsonObject Weight(JsonObject action, string category) => action["sub-objects"]!.AsArray().OfType<JsonObject>()
        .Single(item => item["category"]!.GetValue<string>() == category);
    private static double Number(JsonObject item, string name) => item[name]!.GetValue<double>();

    private static JsonObject Sample(string name) => RootOf(Path.Combine(TestProjects.RepositoryRoot,
        "Documents", "RecotteProjects", name, name + ".ccproj"));
    private static JsonObject RootOf(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static RecotteProjectDocument Load(JsonObject root) => RecotteProject.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));
    private static string Messages(ProjectBatchResult result) => string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
}
