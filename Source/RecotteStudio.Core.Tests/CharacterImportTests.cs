using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class CharacterImportTests : IDisposable
{
    private const string Man = "2D-スーツ男性";
    private const string Woman = "2D-スーツ女性";
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteCharacterImportTests-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    private static string SampleImage => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
        "004OneImage", "Sample.png");

    private static string TwoVoices => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
        "011TwoVoices", "011TwoVoices.ccproj");

    [Fact]
    public void ImportCharacter_BatchRebuildsTheDonorsTelopTargetLinks()
    {
        RecotteProjectDocument donor = RecotteProject.Load(TwoVoices);
        RecotteProjectDocument target = RecotteProject.Create(new("Pair", directory));

        ProjectBatchResult batch = target.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(donor, Man, new(0m), new(10m)),
            new ImportCharacterOperation(donor, Woman, new(0m), new(10m)),
        });

        Assert.True(batch.Success);
        Assert.Equal(new int?[] { 1, 2 }, batch.OperationResults.Select(result => result.CreatedLayerIndex));
        Assert.Equal(new TimelineObjectId(2, 1000), batch.OperationResults[1].CreatedObjectId);
        // The man's layer pointed at 話者2 before it existed, so the woman's import links it back.
        Assert.Contains(batch.OperationResults[1].Changes, change => change.Operation == "LinkTelopTargetLayer" &&
            change.Target == "layer:1" && (string?)change.After == "話者2");
        JsonArray layers = SavedLayers(target);
        Assert.Equal(new[] { "Video", "Speaker", "Speaker", "Annot" }, layers.Select(layer => (string?)layer!["type"]));
        Assert.Equal(new[] { "話者1", "話者2" }, new[] { Name(layers[1]), Name(layers[2]) });
        Assert.Equal(new[] { "話者2", "話者2" }, new[] { Property(layers[1], "TelopTargetLayer"), Property(layers[2], "TelopTargetLayer") });
        Assert.All(new[] { layers[1], layers[2] }, layer => Assert.Equal(string.Empty, Property(layer, "LinkFolder")));
        Assert.All(new[] { layers[1], layers[2] }, layer => Assert.Equal("Speaker Character",
            (string?)Assert.Single(layer!["layer-objects"]!.AsArray())!["type"]));
    }

    [Fact]
    public void ImportCharacter_ResolvesTheTelopTargetWhenImportedInReverseOrder()
    {
        RecotteProjectDocument donor = RecotteProject.Load(TwoVoices);
        RecotteProjectDocument target = RecotteProject.Create(new("Pair", directory));

        Assert.True(target.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(donor, Woman, new(0m), new(10m)),
            new ImportCharacterOperation(donor, Man, new(0m), new(10m)),
        }).Success);

        JsonArray layers = SavedLayers(target);
        Assert.Equal(new[] { "話者2", "話者1" }, new[] { Name(layers[1]), Name(layers[2]) });
        Assert.Equal(new[] { "話者2", "話者2" }, new[] { Property(layers[1], "TelopTargetLayer"), Property(layers[2], "TelopTargetLayer") });
    }

    [Fact]
    public void ImportCharacter_ClearsATelopTargetThatNamesAMissingLayer()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Single", directory));

        Assert.True(target.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Man, new(0m), new(10m)),
        }).Success);

        JsonArray layers = SavedLayers(target);
        Assert.Equal("話者1", Name(layers[1]));
        Assert.Equal(string.Empty, Property(layers[1], "TelopTargetLayer"));
        Assert.Equal("下枠", Property(layers[1], "TelopFrame"));
        Assert.Contains(target.TelopFrames, frame => frame.Name == "下枠");
    }

    [Fact]
    public void ImportCharacter_RenamesTheLayerAndItsSelfReference()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Renamed", directory));

        ProjectBatchResult batch = target.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Woman, new(0m), new(10m), "聞き手"),
            new AddTextOnlySpeakerVoiceOperation(new LayerNameReference("聞き手"), "こんにちは", new(1m), new(2m)),
        });

        Assert.True(batch.Success);
        JsonArray layers = SavedLayers(target);
        Assert.Equal("聞き手", Name(layers[1]));
        Assert.Equal("聞き手", Property(layers[1], "TelopTargetLayer"));
        Assert.Equal(new[] { "Speaker Character", "Speaker Voice" },
            layers[1]!["layer-objects"]!.AsArray().Select(item => (string?)item!["type"]));
    }

    [Theory]
    [InlineData("注釈1")]
    [InlineData(" ")]
    public void ImportCharacter_RejectsAUsedOrBlankLayerName(string name)
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Duplicate", directory));
        using ProjectEditSession session = target.BeginEdit();

        CharacterImportResult result = session.Editor.ImportCharacter(new(RecotteProject.Load(TwoVoices), Man,
            new(0m), new(10m), name));

        Assert.False(result.Success);
        Assert.Equal("RC4607", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }

    [Fact]
    public void ImportCharacter_RejectsADonorWithoutATelopFrameTheProjectUses()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Frames", directory));
        Assert.True(target.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Man, new(0m), new(10m)),
        }).Success);
        // The same two characters, but without the 下枠 frame the man's layer now draws its telop in.
        JsonObject root = JsonNode.Parse(File.ReadAllText(TwoVoices))!.AsObject();
        JsonArray frames = root["telop-frames"]!.AsArray();
        int lower = frames.Select((item, index) => (item, index)).Single(pair => pair.item is JsonObject frame &&
            (string?)frame["FrameName"]!["p-value"] == "下枠").index;
        frames.RemoveAt(lower);
        // Recotte Studio writes the string "properties" before every frame object.
        if (frames[lower - 1] is JsonValue marker && (string?)marker == "properties") frames.RemoveAt(lower - 1);
        string donorPath = Path.Combine(directory, "Donor.ccproj");
        File.WriteAllText(donorPath, root.ToJsonString());

        ProjectBatchResult batch = target.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(donorPath), Woman, new(0m), new(10m)),
        });

        Assert.False(batch.Success);
        ProjectDiagnostic diagnostic = Assert.Single(batch.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Equal("RC4606", diagnostic.Code);
        Assert.Contains("下枠", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportCharacter_RejectsADonorThatPlacesAnExistingCharacterElsewhere()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Layout", directory));
        Assert.True(target.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Man, new(0m), new(10m)),
        }).Success);
        // The same two characters and frames, but the man stands where the donor's frames were not fitted around him.
        JsonObject root = JsonNode.Parse(File.ReadAllText(TwoVoices))!.AsObject();
        JsonObject manCharacter = root["layers"]!.AsArray().OfType<JsonObject>()
            .Single(layer => (string?)layer["type"] == "Speaker" && Property(layer, "Speaker") == Man)["layer-objects"]!.AsArray().OfType<JsonObject>()
            .Single(item => (string?)item["type"] == "Speaker Character");
        manCharacter["properties"]!["CharBounds"]!["p-value"]![0] = 400.0;
        string donorPath = Path.Combine(directory, "Donor.ccproj");
        File.WriteAllText(donorPath, root.ToJsonString());

        ProjectBatchResult batch = target.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(donorPath), Woman, new(0m), new(10m)),
        });

        Assert.False(batch.Success);
        ProjectDiagnostic diagnostic = Assert.Single(batch.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Equal("RC4606", diagnostic.Code);
        Assert.Contains(Man, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportCharacter_FillingTheEmptySpeakerLayerLeavesEarlierResultsInPlace()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Reuse", directory));

        ProjectBatchResult batch = target.ApplyOperations(new ProjectOperation[]
        {
            new AddImageAssetOperation(new LayerIndexReference(2), SampleImage, Path.Combine(directory, "Out.ccproj"), "image", new(0m), new(1m)),
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Man, new(0m), new(10m)),
        });

        Assert.True(batch.Success);
        Assert.Equal(1, batch.OperationResults[1].CreatedLayerIndex);
        AssertLocatesSavedObjects(target, batch.OperationResults[0], 2);
        Assert.Equal(batch.OperationResults.SelectMany(operation => operation.Changes), batch.Changes);
    }

    [Fact]
    public void ImportCharacter_InsertingASpeakerLayerRebasesEarlierResultsAndChanges()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Insert", directory));

        ProjectBatchResult batch = target.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Man, new(0m), new(10m)),
            new AddImageAssetOperation(new LayerIndexReference(2), SampleImage, Path.Combine(directory, "Out.ccproj"), "image", new(0m), new(1m)),
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Woman, new(0m), new(10m)),
        });

        Assert.True(batch.Success);
        Assert.Equal(new int?[] { 1, null, 2 }, batch.OperationResults.Select(result => result.CreatedLayerIndex));
        AssertLocatesSavedObjects(target, batch.OperationResults[1], 3);
        Assert.Equal(batch.OperationResults.SelectMany(operation => operation.Changes), batch.Changes);
    }

    [Fact]
    public void ImportCharacter_InsertsAfterTheLastSpeakerLayerAndKeepsABackgroundLayerBehind()
    {
        RecotteProjectDocument target = RecotteProject.Create(new("Background", directory));

        ProjectBatchResult batch = target.ApplyOperations(new ProjectOperation[]
        {
            new AddAnnotationLayerOperation("下敷き", 0),
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Man, new(0m), new(10m)),
            new ImportCharacterOperation(RecotteProject.Load(TwoVoices), Woman, new(0m), new(10m)),
        });

        Assert.True(batch.Success);
        Assert.Equal(new int?[] { 0, 2, 3 }, batch.OperationResults.Select(result => result.CreatedLayerIndex));
        JsonArray layers = SavedLayers(target);
        Assert.Equal(new[] { "Annot", "Video", "Speaker", "Speaker", "Annot" }, layers.Select(layer => (string?)layer!["type"]));
        Assert.Equal(new[] { "下敷き", "映像・音声1", "話者1", "話者2", "注釈1" }, layers.Select(Name));
    }

    private void AssertLocatesSavedObjects(RecotteProjectDocument document, ProjectOperationResult operation, int layerIndex)
    {
        TimelineObjectId id = Assert.IsType<TimelineObjectId>(operation.CreatedObjectId);
        Assert.Equal(layerIndex, id.LayerIndex);
        Assert.Contains(SavedLayers(document)[layerIndex]!["layer-objects"]!.AsArray(), item => (int?)item!["objkey"] == id.ObjectKey);
        Assert.All(operation.Changes.Where(change => change.JsonPath.StartsWith("$.layers[", StringComparison.Ordinal)),
            change => Assert.StartsWith($"$.layers[{layerIndex}]", change.JsonPath));
    }

    private JsonArray SavedLayers(RecotteProjectDocument document)
    {
        string output = Path.Combine(directory, $"{Guid.NewGuid():N}.ccproj");
        document.SaveCopy(output);
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
        return JsonNode.Parse(File.ReadAllText(output))!["layers"]!.AsArray();
    }

    private static string? Name(JsonNode? layer) => (string?)layer!["name"];
    private static string? Property(JsonNode? layer, string name) => (string?)layer!["properties"]![name]!["p-value"];
}
