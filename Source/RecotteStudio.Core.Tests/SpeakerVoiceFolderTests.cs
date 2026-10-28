using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class SpeakerVoiceFolderTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteVoiceFolder-").FullName;
    private static string DonorPath => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
        "011TwoVoices", "011TwoVoices.ccproj");

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Create_SetsTheInitialSpeakersLinkAndCreatesTheDirectoryOnSave()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Example", directory));
        Assert.False(Directory.Exists(Path.Combine(directory, "voice")));

        AssertLink(document, 1, "話者1");
        Assert.True(Directory.Exists(Path.Combine(directory, "voice", "話者1")));
    }

    [Fact]
    public void Save_CreatesMissingVoiceFoldersAndKeepsExistingOnes()
    {
        RecotteProjectDocument created = RecotteProject.Create(new("Example", directory));
        string path = Path.Combine(directory, "Example.ccproj");
        created.SaveCopy(path);
        string existing = Path.Combine(directory, "voice", "話者1", "voice.wav");
        File.WriteAllText(existing, "keep");

        RecotteProjectDocument document = RecotteProject.Load(path);
        Assert.True(document.ApplyOperations(new[] { new AddSpeakerLayerOperation("話者 #1%") }).Success);
        Assert.False(Directory.Exists(Path.Combine(directory, "voice", "話者 #1%")));
        ProjectSaveResult result = document.Save();

        Assert.Empty(result.Warnings);
        Assert.True(Directory.Exists(Path.Combine(directory, "voice", "話者 #1%")));
        Assert.Equal("keep", File.ReadAllText(existing));
    }

    [Fact]
    public void Save_WarnsWhenTheVoiceFolderCannotBeCreated()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Example", directory));
        File.WriteAllText(Path.Combine(directory, "voice"), "a file, not a directory");

        ProjectSaveResult result = document.SaveCopy(Path.Combine(directory, "Example.ccproj"));

        Assert.True(File.Exists(result.DestinationPath));
        ProjectDiagnostic warning = Assert.Single(result.Warnings);
        Assert.Equal("RC5402", warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Save_CreatesOnlyFoldersInTheProjectsVoiceDirectory()
    {
        // The donor's layers link to its Voice folder on another machine; only the new layer's folder is created.
        string path = Path.Combine(directory, "TwoVoices.ccproj");
        File.Copy(DonorPath, path);
        RecotteProjectDocument document = RecotteProject.Load(path);
        Assert.True(document.ApplyOperations(new[] { new AddSpeakerLayerOperation("追加") }).Success);
        Assert.Empty(document.Save().Warnings);

        string created = Assert.Single(Directory.GetDirectories(directory));
        Assert.Equal(Path.Combine(directory, "voice"), created);
        Assert.Equal(Path.Combine(directory, "voice", "追加"), Assert.Single(Directory.GetDirectories(created)));
    }

    [Theory]
    [InlineData("聞き手", "聞き手")]
    [InlineData("../案内\\役:*?\"<>|", ".._案内_役_______")]
    [InlineData("...", "_")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("LPT1", "_LPT1")]
    [InlineData("話者. ", "話者")]
    [InlineData("話者 #1%", "話者 #1%")]
    public void AddSpeakerLayer_UsesASafeFolderName(string name, string folder)
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Example", directory));
        Assert.True(document.ApplyOperations(new[] { new AddSpeakerLayerOperation(name) }).Success);

        AssertLink(document, 2, folder);
    }

    [Fact]
    public void ImportCharacter_UsesTheTargetDirectoryAndRequestedLayerName()
    {
        RecotteProjectDocument created = RecotteProject.Create(new("Example", directory));
        string path = Path.Combine(directory, "Example.ccproj");
        created.SaveCopy(path);
        RecotteProjectDocument document = RecotteProject.Load(path);
        Assert.True(document.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(DonorPath), "2D-スーツ女性", new(0m), new(10m), "聞き手"),
        }).Success);

        AssertLink(document, 1, "聞き手");
    }

    [Fact]
    public void StreamWithoutAProjectLocation_ClearsTheDonorsLink()
    {
        using FileStream stream = File.OpenRead(DonorPath);
        RecotteProjectDocument document = RecotteProject.Load(stream);
        Assert.True(document.ApplyOperations(new[] { new AddSpeakerLayerOperation("追加") }).Success);
        // The stream's file-explorer-path may still point to another machine; it is not a project location.
        Assert.Equal(string.Empty, Link(Saved(document), 3));

        using FileStream empty = File.OpenRead(TestProjects.EmptyProject);
        RecotteProjectDocument target = RecotteProject.Load(empty);
        Assert.True(target.ApplyOperations(new[]
        {
            new ImportCharacterOperation(RecotteProject.Load(DonorPath), "2D-スーツ女性", new(0m), new(10m)),
        }).Success);
        Assert.Equal(string.Empty, Link(Saved(target), 1));
    }

    [Fact]
    public void PreviewAndFailedBatch_DoNotChangeExistingLinks()
    {
        RecotteProjectDocument document = RecotteProject.Load(DonorPath);
        string before = Link(Saved(document), 1);
        Assert.True(document.PreviewOperations(new[] { new AddSpeakerLayerOperation("追加") }).BatchResult.Success);
        Assert.False(document.ApplyOperations(new[]
        {
            new AddSpeakerLayerOperation("追加"), new AddSpeakerLayerOperation("追加"),
        }).Success);

        JsonObject saved = Saved(document);
        Assert.Equal(before, Link(saved, 1));
        Assert.Equal(4, saved["layers"]!.AsArray().Count);
        Assert.False(Directory.Exists(Path.Combine(directory, "voice")));
    }

    private void AssertLink(RecotteProjectDocument document, int layer, string folder)
    {
        Uri uri = new(Link(Saved(document), layer));
        Assert.True(uri.IsFile);
        Assert.Equal(Path.Combine(directory, "voice", folder), uri.LocalPath);
    }

    private JsonObject Saved(RecotteProjectDocument document)
    {
        string output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ccproj");
        document.SaveCopy(output);
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
        return JsonNode.Parse(File.ReadAllText(output))!.AsObject();
    }

    private static string Link(JsonObject root, int layer) =>
        (string)root["layers"]![layer]!["properties"]!["LinkFolder"]!["p-value"]!;
}
