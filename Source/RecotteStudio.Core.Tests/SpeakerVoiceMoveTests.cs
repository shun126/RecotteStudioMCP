using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class SpeakerVoiceMoveTests : IDisposable
{
    private const string Man = "2D-スーツ男性";
    private const string Woman = "2D-スーツ女性";
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteSpeakerVoiceMoveTests-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    private static string TwoVoices => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
        "011TwoVoices", "011TwoVoices.ccproj");

    // In 011TwoVoices both Speaker layers hold a Speaker Character 1000 and one audio-backed Speaker Voice 1001.
    private static readonly TimelineObjectId ManVoice = new(1, 1001);

    [Fact]
    public void Move_SplitsTextOnlyLinesBetweenSpeakersKeepingTextAndTime()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Split", directory));
        RecotteProjectDocument donor = RecotteProject.Load(TwoVoices);

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(donor, Man, new(0m), new(10m)),
            new ImportCharacterOperation(donor, Woman, new(0m), new(10m)),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者1"), "こんにちは", new(1m), new(2m)),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("話者1"), "よろしく", new(3m), new(4m)),
        });
        Assert.True(batch.Success);
        TimelineObjectId reply = Assert.IsType<TimelineObjectId>(batch.OperationResults[3].CreatedObjectId);

        ProjectBatchResult move = document.ApplyOperations(new[]
        {
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(reply), new LayerNameReference("話者2")),
        });

        Assert.True(move.Success);
        Assert.Empty(move.AudioRegenerationRequired);
        Assert.DoesNotContain(move.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
        TimelineObjectId moved = new(2, reply.ObjectKey);
        Assert.Equal(moved.ToString(), move.OperationResults[0].Target);
        JsonArray layers = SavedLayers(document);
        Assert.Equal(new[] { "こんにちは" }, Voices(layers[1]).Select(voice => (string?)voice["text"]!["text"]));
        JsonObject voice = Assert.Single(Voices(layers[2]));
        Assert.Equal(reply.ObjectKey, (int?)voice["objkey"]);
        Assert.Equal("よろしく", (string?)voice["text"]!["text"]);
        Assert.Equal((3m, 4m), ((decimal)voice["start-time"]!, (decimal)voice["end-time"]!));
        // The line now draws its telop with the woman's layer style rather than the man's.
        Assert.Equal("話者2", (string?)voice["text"]!["stext"]!.AsArray().OfType<JsonObject>().Single(item => (string?)item["c"] == "s")["style"]);
    }

    [Fact]
    public void Move_GivesALineANewKeyWhenTheTargetLayerUsesItsKey()
    {
        RecotteProjectDocument document = RecotteProject.Load(TwoVoices);

        ProjectBatchResult batch = document.ApplyOperations(new[]
        {
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(ManVoice), new LayerIndexReference(2)),
        });

        Assert.True(batch.Success);
        TimelineObjectId moved = new(2, 1002);
        Assert.Equal(moved.ToString(), batch.OperationResults[0].Target);
        ProjectChange change = batch.Changes[0];
        Assert.Equal(("MoveSpeakerVoiceToLayer", ManVoice.ToString(), moved.ToString()), (change.Operation, change.Before, change.After));
        JsonArray layers = SavedLayers(document);
        Assert.Empty(Voices(layers[1]));
        Assert.Equal(new int?[] { 1001, 1002 }, Voices(layers[2]).Select(voice => (int?)voice["objkey"]));
    }

    [Fact]
    public void Move_OfAnAudioBackedLineToAnotherSpeakerRequiresRegeneratingItsAudio()
    {
        RecotteProjectDocument document = RecotteProject.Load(TwoVoices);

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(ManVoice), new LayerIndexReference(2)),
            // A layer inserted later shifts the reported line along with the rest of the batch metadata.
            new AddVideoLayerOperation(),
        });

        Assert.True(batch.Success);
        TimelineObjectId moved = new(3, 1002);
        Assert.Equal(moved, Assert.Single(batch.AudioRegenerationRequired));
        ProjectDiagnostic warning = Assert.Single(batch.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
        Assert.Contains("'話者1' to layer '話者2'", warning.Message, StringComparison.Ordinal);
        Assert.Equal(moved.ToString(), batch.OperationResults[0].Target);
    }

    [Fact]
    public void Move_BackToTheOriginalSpeakerInTheSameBatchKeepsTheAudio()
    {
        RecotteProjectDocument document = RecotteProject.Load(TwoVoices);

        ProjectBatchResult batch = document.ApplyOperations(new[]
        {
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(ManVoice), new LayerIndexReference(2)),
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(new(2, 1002)), new LayerIndexReference(1)),
        });

        Assert.True(batch.Success);
        Assert.Empty(batch.AudioRegenerationRequired);
        Assert.DoesNotContain(batch.Diagnostics, diagnostic => diagnostic.Code == "RC4208");
    }

    [Fact]
    public void Move_ReleasesSnapsThatTheTargetLayerCannotHonor()
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(TwoVoices))!.AsObject();
        JsonArray manObjects = root["layers"]![1]!["layer-objects"]!.AsArray();
        manObjects.OfType<JsonObject>().Single(item => (int?)item["objkey"] == 1001)["start-snap"] = 1000;
        manObjects.OfType<JsonObject>().Single(item => (int?)item["objkey"] == 1000)["start-snap"] = 1001;
        string path = Path.Combine(directory, "Snapped.ccproj");
        File.WriteAllText(path, root.ToJsonString());
        RecotteProjectDocument document = RecotteProject.Load(path);

        Assert.True(document.ApplyOperations(new[]
        {
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(ManVoice), new LayerIndexReference(2)),
        }).Success);

        JsonArray layers = SavedLayers(document);
        Assert.Equal(0, (int?)Voices(layers[2]).Single(voice => (int?)voice["objkey"] == 1002)["start-snap"]);
        Assert.Equal(0, (int?)layers[1]!["layer-objects"]!.AsArray().Single()!["start-snap"]);
    }

    [Fact]
    public void Move_RejectsALineOverlappingOneOnTheTargetLayer()
    {
        RecotteProjectDocument document = RecotteProject.Load(TwoVoices);

        ProjectBatchResult batch = document.ApplyOperations(new ProjectOperation[]
        {
            new AddTextOnlySpeakerVoiceOperation(new LayerIndexReference(2), "かぶり", new(1.2m), new(1.4m)),
            new MoveSpeakerVoiceToLayerOperation(new TimelineObjectIdReference(ManVoice), new LayerIndexReference(2)),
        });

        Assert.False(batch.Success);
        Assert.Equal(1, batch.FailedOperationIndex);
        Assert.Equal("RC4211", Assert.Single(batch.Diagnostics, item => item.Severity == DiagnosticSeverity.Error).Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Move_RejectsTheSameLayerOrALayerThatIsNotASpeakerLayer(int layerIndex)
    {
        RecotteProjectDocument document = RecotteProject.Load(TwoVoices);
        using ProjectEditSession session = document.BeginEdit();

        SpeakerVoiceMoveResult result = session.Editor.MoveSpeakerVoiceToLayer(new(ManVoice, new(layerIndex)));

        Assert.False(result.Success);
        Assert.Equal("RC4209", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }

    [Fact]
    public void Move_RejectsAnObjectThatIsNotASpeakerVoice()
    {
        RecotteProjectDocument document = RecotteProject.Load(TwoVoices);
        using ProjectEditSession session = document.BeginEdit();

        SpeakerVoiceMoveResult result = session.Editor.MoveSpeakerVoiceToLayer(new(new(1, 1000), new(2)));

        Assert.False(result.Success);
        Assert.Equal("RC4202", Assert.Single(result.Diagnostics).Code);
    }

    private JsonArray SavedLayers(RecotteProjectDocument document)
    {
        string output = Path.Combine(directory, $"{Guid.NewGuid():N}.ccproj");
        document.SaveCopy(output);
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
        return JsonNode.Parse(File.ReadAllText(output))!["layers"]!.AsArray();
    }

    private static JsonObject[] Voices(JsonNode? layer) => layer!["layer-objects"]!.AsArray().OfType<JsonObject>()
        .Where(item => (string?)item["type"] == "Speaker Voice").ToArray();
}
