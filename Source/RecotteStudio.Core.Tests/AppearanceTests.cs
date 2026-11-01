using System.Text;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class AppearanceTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteAppearance-").FullName;
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("004OneImage", "Image")]
    [InlineData("005OneVideo", "Video")]
    public void SetObjectAppearance_SavesEachValueAndKeepsEverythingElse(string sample, string type)
    {
        JsonObject root = Sample(sample);
        (TimelineObjectId id, JsonObject original) = First(root, type);
        RecotteProjectDocument document = Load(root);
        Assert.True(document.Capabilities.CanSetObjectAppearance);
        TimelineEntryView before = Entry(document, id);
        Assert.True(before.Capabilities.CanSetAppearance);
        Assert.Equal(new ObjectAppearance(1m, 0m, "none", 0, 0, 1m, null), before.Appearance);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new SetObjectAppearanceOperation(new TimelineObjectIdReference(id), Opacity: 0.5m, Angle: -15m, Flip: ObjectFlip.Horizontal,
                Brightness: -30, Contrast: 20, Gamma: 1.5m),
        });

        Assert.True(result.Success, Messages(result));
        Assert.Equal(6, result.Changes.Count);
        JsonObject saved = Saved(document, id);
        JsonObject expected = (JsonObject)original.DeepClone();
        foreach ((string name, JsonNode value) in new (string, JsonNode)[]
            { ("Opacity", 0.5m), ("Angle", -15m), ("Flip", "x"), ("Brightness", -30), ("Contrast", 20), ("Gamma", 1.5m) })
            expected["properties"]![name]!["p-value"] = value;
        Assert.True(JsonNode.DeepEquals(expected, saved), saved.ToJsonString());
        Assert.Equal(new ObjectAppearance(0.5m, -15m, "horizontal", -30, 20, 1.5m, null), Entry(document, id).Appearance);
    }

    [Fact]
    public void SetObjectAppearance_KeepsOmittedValues()
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, JsonObject original) = First(root, "Image");
        RecotteProjectDocument document = Load(root);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            { new SetObjectAppearanceOperation(new TimelineObjectIdReference(id), Opacity: 0m) });

        Assert.True(result.Success, Messages(result));
        JsonObject expected = (JsonObject)original.DeepClone();
        expected["properties"]!["Opacity"]!["p-value"] = 0m;
        Assert.True(JsonNode.DeepEquals(expected, Saved(document, id)));
    }

    // Recotte Studio saves 左右反転 as x; the default action is one of the layer's actions, by its full pack:name.
    [Fact]
    public void SetObjectAppearance_TurnsACharacterAndSetsItsDefaultAction()
    {
        JsonObject root = Sample("015SpeakerActions");
        TimelineObjectId id = new(1, 1000);
        JsonObject original = (JsonObject)Object(root, id).DeepClone();
        RecotteProjectDocument document = Load(root);
        Assert.Equal(new ObjectAppearance(null, 0m, "none", null, null, null, ""), Entry(document, id).Appearance);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new SetObjectAppearanceOperation(new TimelineObjectIdReference(id), Flip: ObjectFlip.Horizontal, DefaultAction: "Smile_1.vmd"),
            new SetObjectAppearanceOperation(new TimelineObjectIdReference(new(2, 1000)), Flip: ObjectFlip.Both, DefaultAction: "smile"),
        });

        Assert.True(result.Success, Messages(result));
        JsonObject saved = Saved(document, id);
        JsonObject expected = (JsonObject)original.DeepClone();
        expected["properties"]!["Flip"]!["p-value"] = "x";
        expected["properties"]!["DefaultAction"]!["p-value"] = "tsurumaki_maki:Smile_1.vmd";
        Assert.True(JsonNode.DeepEquals(expected, saved), saved.ToJsonString());
        Assert.Equal(new ObjectAppearance(null, 0m, "both", null, null, null, "2D-Maki:smile"), Entry(document, new(2, 1000)).Appearance);

        Assert.True(document.ApplyOperations(new ProjectOperation[]
            { new SetObjectAppearanceOperation(new TimelineObjectIdReference(id), Flip: ObjectFlip.None, DefaultAction: "") }).Success);
        Assert.True(JsonNode.DeepEquals(original, Saved(document, id)));
    }

    [Theory]
    [InlineData(-0.1, null, null, null)]
    [InlineData(1.1, null, null, null)]
    [InlineData(null, 361.0, null, null)]
    [InlineData(null, null, 101, null)]
    [InlineData(null, null, null, 0.1)]
    [InlineData(null, null, null, 5.5)]
    [InlineData(null, null, null, null)]
    public void SetObjectAppearance_RejectsValuesOutOfRange(double? opacity, double? angle, int? brightness, double? gamma)
    {
        JsonObject root = Sample("004OneImage");
        (TimelineObjectId id, _) = First(root, "Image");
        AssertRejected(root, new SetObjectAppearanceOperation(new TimelineObjectIdReference(id), (decimal?)opacity, (decimal?)angle,
            Brightness: brightness, Gamma: (decimal?)gamma), "RC4943");
    }

    [Fact]
    public void SetObjectAppearance_RejectsWhatTheTargetCannotChange()
    {
        JsonObject voice = Sample("002OneText");
        (TimelineObjectId voiceId, _) = First(voice, "Speaker Voice");
        AssertRejected(voice, new SetObjectAppearanceOperation(new TimelineObjectIdReference(voiceId), Opacity: 0.5m), "RC4941");
        Assert.False(Assert.Single(Load(voice).GetTimelineEntries(), item => item.ObjectId == voiceId).Capabilities.CanSetAppearance);

        // A figure (stamp) has an Angle but no Opacity, and the message names what it can change.
        JsonObject figure = Sample("009FullProject");
        TimelineObjectId figureId = new(3, 1000);
        ProjectBatchResult rejected = Load(figure).ApplyOperations(new ProjectOperation[]
            { new SetObjectAppearanceOperation(new TimelineObjectIdReference(figureId), Angle: 10m, Opacity: 0.5m) });
        Assert.Equal("RC4941", rejected.Diagnostics[0].Code);
        Assert.Contains("It can change: Angle", rejected.Diagnostics[0].Message);
        Assert.True(Load(figure).ApplyOperations(new ProjectOperation[]
            { new SetObjectAppearanceOperation(new TimelineObjectIdReference(figureId), Angle: 10m) }).Success);

        AssertRejected(Sample("015SpeakerActions"),
            new SetObjectAppearanceOperation(new TimelineObjectIdReference(new(2, 1000)), DefaultAction: "angry"), "RC4944");

        JsonObject animated = Sample("004OneImage");
        (TimelineObjectId imageId, JsonObject image) = First(animated, "Image");
        image["properties"]!["Opacity"]!["keyframes"] = new JsonArray(new JsonObject { ["time"] = 0.0 });
        AssertRejected(animated, new SetObjectAppearanceOperation(new TimelineObjectIdReference(imageId), Opacity: 0.5m), "RC4942");

        JsonObject locked = Sample("004OneImage");
        (TimelineObjectId lockedId, JsonObject lockedImage) = First(locked, "Image");
        lockedImage["tl-locked"] = true;
        AssertRejected(locked, new SetObjectAppearanceOperation(new TimelineObjectIdReference(lockedId), Opacity: 0.5m), "RC4207");
        Assert.False(Entry(Load(locked), lockedId).Capabilities.CanSetAppearance);
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

    private static (TimelineObjectId Id, JsonObject Object) First(JsonObject root, string type)
    {
        JsonArray layers = root["layers"]!.AsArray();
        for (int layer = 0; layer < layers.Count; layer++)
            foreach (JsonObject item in layers[layer]!["layer-objects"]!.AsArray().OfType<JsonObject>())
                if (item["type"]!.GetValue<string>() == type) return (new(layer, item["objkey"]!.GetValue<int>()), item);
        throw new InvalidOperationException($"No {type} in the sample.");
    }

    private static JsonObject Object(JsonObject root, TimelineObjectId id) => root["layers"]![id.LayerIndex]!["layer-objects"]!
        .AsArray().OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == id.ObjectKey);

    private static JsonObject Sample(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(TestProjects.RepositoryRoot,
        "Documents", "RecotteProjects", name, name + ".ccproj")))!.AsObject();
    private static RecotteProjectDocument Load(JsonObject root) => RecotteProject.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));
    private static string Messages(ProjectBatchResult result) => string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
}
