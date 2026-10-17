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

    private static JsonObject OutWeight(JsonObject action) => action["sub-objects"]!.AsArray().OfType<JsonObject>()
        .Single(item => item["category"]!.GetValue<string>() == "OutWeight");
    private static double Number(JsonObject item, string name) => item[name]!.GetValue<double>();

    private static JsonObject Sample(string name) => RootOf(Path.Combine(TestProjects.RepositoryRoot,
        "Documents", "RecotteProjects", name, name + ".ccproj"));
    private static JsonObject RootOf(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static RecotteProjectDocument Load(JsonObject root) => RecotteProject.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));
    private static string Messages(ProjectBatchResult result) => string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
}
