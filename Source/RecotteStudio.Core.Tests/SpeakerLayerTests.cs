using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class SpeakerLayerTests : IDisposable
{
    private const string Man = "2D-スーツ男性";
    private const string Woman = "2D-スーツ女性";
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteSpeakerLayerTests-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    private static string Sample(string folder) => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
        folder, folder + ".ccproj");

    [Fact]
    public void AddSpeakerLayer_AddsTheCharacterlessLayerOfANewProjectAfterTheLastSpeakerLayer()
    {
        // Saving creates the new layer's voice folder beside the project, so work on a copy of the sample.
        string path = Path.Combine(directory, "006OneCharacter.ccproj");
        File.Copy(Sample("006OneCharacter"), path);
        RecotteProjectDocument document = RecotteProject.Load(path);

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new AddSpeakerLayerOperation(),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者2"), "はじめまして", new(1m), new(2m)),
        });

        Assert.True(batch.Success);
        Assert.Equal(2, batch.OperationResults[0].CreatedLayerIndex);
        JsonArray layers = SavedLayers(document);
        Assert.Equal(new[] { "映像・音声1", "話者1", "話者2", "注釈1" }, layers.Select(layer => (string?)layer!["name"]));
        // Apart from its name, audio folder link, and the line placed on it, this is the new-project template layer.
        JsonObject expected = Layer(Sample("001EmptyProject"), 1);
        JsonObject actual = (JsonObject)layers[2]!.DeepClone();
        string projectDirectory = Path.GetDirectoryName(document.SourcePath!)!;
        Assert.Equal(Path.Combine(projectDirectory, "voice", "話者2"),
            new Uri((string)actual["properties"]!["LinkFolder"]!["p-value"]!).LocalPath);
        expected["properties"]!["LinkFolder"]!["p-value"] = actual["properties"]!["LinkFolder"]!["p-value"]!.DeepClone();
        Assert.Equal("Speaker Voice", (string?)Assert.Single(actual["layer-objects"]!.AsArray())!["type"]);
        foreach (JsonObject layer in new[] { expected, actual })
        {
            layer.Remove("name");
            layer.Remove("layer-objects");
        }
        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
    }

    [Fact]
    public void AddSpeakerLayer_SplitsOneLayersLinesBetweenTwoSpeakers()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Split", directory));
        Assert.True(document.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(RecotteProject.Load(Sample("011TwoVoices")), Man, new(0m), new(10m)),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者1"), "こんにちは", new(1m), new(2m)),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者1"), "どうも", new(3m), new(4m)),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者1"), "では始めましょう", new(5m), new(6m)),
        }).Success);
        TimelineObjectId reply = new(1, document.Layers[1].Objects.OfType<SpeakerVoiceView>().Single(voice => voice.Text == "どうも").ObjectKey!.Value);

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new AddSpeakerLayerOperation("聞き手"),
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(reply), new LayerNameReference("聞き手")),
        });

        Assert.True(batch.Success);
        JsonArray layers = SavedLayers(document);
        Assert.Equal(new[] { "話者1", "聞き手" }, layers.Where(layer => (string?)layer!["type"] == "Speaker").Select(layer => (string?)layer!["name"]));
        Assert.Equal(new[] { "こんにちは", "では始めましょう" }, Texts(layers[1]));
        Assert.Equal(new[] { "どうも" }, Texts(layers[2]));
        Assert.Equal(new TimelineObjectId(2, reply.ObjectKey).ToString(), batch.OperationResults[1].Target);
    }

    [Fact]
    public void AddSpeakerLayer_LeavesALayerThatImportCharacterCanFill()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Pair", directory));
        RecotteProjectDocument donor = RecotteProject.Load(Sample("011TwoVoices"));

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(donor, Man, new(0m), new(10m)),
            new AddSpeakerLayerOperation(),
            new ImportCharacterOperation(donor, Woman, new(0m), new(10m)),
        });

        Assert.True(batch.Success);
        Assert.Equal(new int?[] { 1, 2, 2 }, batch.OperationResults.Select(result => result.CreatedLayerIndex));
        Assert.Equal(new[] { Man, Woman }, document.Layers.OfType<SpeakerLayerView>().Select(layer => layer.SpeakerKey));
    }

    [Fact]
    public void AddSpeakerLayer_RejectsAProjectWithoutTheTelopFrameTheLayerUses()
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(Sample("006OneCharacter")))!.AsObject();
        RemoveFrame(root, "LowerFrame");
        root["layers"]![1]!["properties"]!["TelopFrame"]!["p-value"] = "UpperFrame";
        string path = Path.Combine(directory, "NoLowerFrame.ccproj");
        File.WriteAllText(path, root.ToJsonString());
        RecotteProjectDocument document = RecotteProject.Load(path);
        using ProjectEditSession session = document.BeginEdit();

        LayerAddResult result = session.Editor.AddSpeakerLayer(new());

        Assert.False(result.Success);
        Assert.Equal("RC4004", Assert.Single(result.Diagnostics).Code);
        Assert.Contains("LowerFrame", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
        Assert.Empty(session.Changes);
        // The capability says so up front rather than offering an operation that cannot succeed.
        Assert.False(document.Capabilities.CanAddSpeakerLayer);
        Assert.True(RecotteProject.Load(Sample("006OneCharacter")).Capabilities.CanAddSpeakerLayer);
    }

    [Fact]
    public void AddSpeakerLayer_IsNoLongerOfferedOnceImportedFramesLackTheOneItUses()
    {
        JsonObject donorRoot = JsonNode.Parse(File.ReadAllText(Sample("011TwoVoices")))!.AsObject();
        RemoveFrame(donorRoot, "LowerFrame");
        string donorPath = Path.Combine(directory, "Donor.ccproj");
        File.WriteAllText(donorPath, donorRoot.ToJsonString());
        RecotteProjectDocument document = RecotteProject.Create(new("Frames", directory));
        Assert.True(document.Capabilities.CanAddSpeakerLayer);

        // importCharacter replaces the project's telop frames with the donor's, which have no LowerFrame.
        Assert.True(document.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(donorPath), Man, new(0m), new(10m)),
        }).Success);

        Assert.False(document.Capabilities.CanAddSpeakerLayer);
        Assert.Equal("RC4004", Assert.Single(document.ApplyOperations(new[] { new AddSpeakerLayerOperation() }).Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Code);
    }

    [Fact]
    public void AddSpeakerLayer_RejectsANameAnotherLayerUses()
    {
        using ProjectEditSession session = RecotteProject.Load(Sample("006OneCharacter")).BeginEdit();

        LayerAddResult result = session.Editor.AddSpeakerLayer(new("話者1"));

        Assert.False(result.Success);
        Assert.Equal("RC4002", Assert.Single(result.Diagnostics).Code);
    }

    private JsonArray SavedLayers(RecotteProjectDocument document)
    {
        string output = Path.Combine(directory, $"{Guid.NewGuid():N}.ccproj");
        document.SaveCopy(output);
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
        return JsonNode.Parse(File.ReadAllText(output))!["layers"]!.AsArray();
    }

    private static void RemoveFrame(JsonObject root, string name)
    {
        JsonArray frames = root["telop-frames"]!.AsArray();
        int removed = frames.Select((item, index) => (item, index)).Single(pair => pair.item is JsonObject frame &&
            (string?)frame["FrameName"]!["p-value"] == name).index;
        frames.RemoveAt(removed);
        // Recotte Studio writes the string "properties" before every frame object.
        if (frames[removed - 1] is JsonValue marker && (string?)marker == "properties") frames.RemoveAt(removed - 1);
    }

    private static JsonObject Layer(string path, int index) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(path))!["layers"]![index]!.DeepClone();

    private static string?[] Texts(JsonNode? layer) => layer!["layer-objects"]!.AsArray().OfType<JsonObject>()
        .Where(item => (string?)item["type"] == "Speaker Voice").Select(item => (string?)item["text"]!["text"]).ToArray();
}
