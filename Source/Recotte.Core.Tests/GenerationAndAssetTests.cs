using System.Text.Json.Nodes;

namespace Recotte.Core.Tests;

public sealed class GenerationAndAssetTests
{
    private static string Sample(string directory, string file) => Path.Combine(
        TestProjects.RepositoryRoot, "Samples", "RecotteProjects", directory, file);

    [Fact]
    public void Create_ProducesValidSanitizedEmptyProject()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            RecotteProjectDocument document = RecotteProject.Create(new("Created", directory));

            Assert.True(document.Validate().IsValid);
            Assert.True(document.ValidateCreatedProject().IsValid);
            Assert.Equal("Created", document.Settings?.ProjectName);
            Assert.True(Guid.TryParse(document.Settings?.ProjectGuid, out _));
            Assert.Equal(new[] { "Video", "Speaker", "Annot" }, document.Layers.Select(layer => layer.Type));
            Assert.Empty(document.Speakers);
            Assert.Empty(document.FileItems);
            string output = Path.Combine(directory, "Created.ccproj");
            document.SaveCopy(output);
            string text = File.ReadAllText(output);
            Assert.DoesNotContain("file://D:/Users/smoriya/Documents", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("001EmptyProject", text, StringComparison.Ordinal);
            byte[] bytes = File.ReadAllBytes(output);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty, StringComparison.Ordinal));
            Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
            Assert.True(RecotteProject.Load(output).Validate().IsValid);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Create_RepeatedlyProducesUniqueValidProjects()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            HashSet<string> guids = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < 100; index++)
            {
                string output = Path.Combine(directory, $"Created-{index}.ccproj");
                RecotteProjectDocument document = RecotteProject.Create(new($"Created-{index}", directory));
                Assert.True(document.ValidateCreatedProject().IsValid);
                Assert.True(guids.Add(document.Settings!.ProjectGuid!));
                document.SaveCopy(output);
                Assert.True(RecotteProject.Load(output).Validate().IsValid);
            }
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AddImageAsset_RegistersReferenceAndPlacesObject()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string output = Path.Combine(directory, "Image.ccproj");
            RecotteProjectDocument document = RecotteProject.Create(new("Image", directory));
            using ProjectEditSession session = document.BeginEdit(fileItemKeyAllocator: new FixedFileKeyAllocator('a'));

            AssetAddResult staged = session.Editor.AddImageAsset(new(2,
                Sample("004OneImage", "Sample.png"), output, "picture", new(0m), new(2m)));

            Assert.True(staged.Success);
            Assert.Equal(new string('a', 64), staged.FileItemKey);
            Assert.True(session.Commit().Success);
            document.SaveCopy(output);
            RecotteProjectDocument loaded = RecotteProject.Load(output);
            Assert.Single(loaded.FileItems);
            Assert.Equal("Image", loaded.Layers[2].Objects.Single().Type);
            Assert.Equal(2m, loaded.Layers[2].Objects.Single().EndTime);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AddImageAsset_UsesOriginalImageDimensionsForCropBounds()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string asset = Path.Combine(directory, "Original.png");
            byte[] header = new byte[24]
            {
                137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
                0, 0, 1, 64, 0, 0, 0, 240,
            };
            File.WriteAllBytes(asset, header);
            string output = Path.Combine(directory, "Image.ccproj");
            RecotteProjectDocument document = RecotteProject.Create(new("Image", directory));
            using ProjectEditSession session = document.BeginEdit(fileItemKeyAllocator: new FixedFileKeyAllocator('c'));

            AssetAddResult staged = session.Editor.AddImageAsset(new(2, asset, output, "picture", new(0m), new(2m)));

            Assert.True(staged.Success);
            Assert.True(session.Commit().Success);
            document.SaveCopy(output);
            JsonObject root = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
            JsonObject image = root["layers"]!.AsArray()[2]!["layer-objects"]!.AsArray().Single()!.AsObject();
            decimal[] cropBounds = image["properties"]!["CropBounds"]!["p-value"]!.AsArray()
                .Select(value => value!.GetValue<decimal>()).ToArray();
            Assert.Equal(new decimal[] { 0m, 0m, 320m, 240m }, cropBounds);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AddVideoAsset_RegistersReferenceAndPlacesObject()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string output = Path.Combine(directory, "Video.ccproj");
            RecotteProjectDocument document = RecotteProject.Create(new("Video", directory));
            using ProjectEditSession session = document.BeginEdit(fileItemKeyAllocator: new FixedFileKeyAllocator('b'));
            AssetAddResult staged = session.Editor.AddVideoAsset(new(0,
                Sample("005OneVideo", "Sample.mp4"), output, "movie", new(1m), new(4m)));

            Assert.True(staged.Success);
            Assert.True(session.Commit().Success);
            document.SaveCopy(output);
            TimelineObjectView video = RecotteProject.Load(output).Layers[0].Objects.Single();
            Assert.Equal("Video", video.Type);
            Assert.Equal(1m, video.StartTime);
            Assert.Equal(4m, video.EndTime);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("006OneCharacter", "006OneCharacter.ccproj", "Speaker Character")]
    [InlineData("009FullProject", "009FullProject.ccproj", "Figure")]
    public void RemoveTimelineObject_RemovesAnyUnlockedObjectKind(string directory, string file, string objectType)
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample(directory, file));
        TimelineObjectView target = document.Layers.SelectMany(layer => layer.Objects).First(item => item.Type == objectType);
        TimelineObjectId id = new(target.LayerIndex, target.ObjectKey!.Value);
        using ProjectEditSession session = document.BeginEdit();

        EditResult staged = session.Editor.RemoveTimelineObject(id);

        Assert.True(staged.Success);
        Assert.True(session.Commit().Success);
        Assert.DoesNotContain(document.Layers[target.LayerIndex].Objects, item => item.ObjectKey == target.ObjectKey);
        Assert.True(document.Validate().IsValid);
    }

    [Fact]
    public void RemoveTimelineObject_PrunesOnlyAnUnreferencedFileItem()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string source = Sample("009FullProject", "009FullProject.ccproj");
            string unlocked = Path.Combine(directory, "Unlocked.ccproj");
            string json = File.ReadAllText(source).Replace("\"st-locked\": true", "\"st-locked\": false", StringComparison.Ordinal)
                .Replace("\"pv-locked\": true", "\"pv-locked\": false", StringComparison.Ordinal);
            File.WriteAllText(unlocked, json);
            RecotteProjectDocument document = RecotteProject.Load(unlocked);
            TimelineObjectView[] videos = document.Layers.SelectMany(layer => layer.Objects).Where(item => item.Type == "Video").ToArray();
            Assert.Equal(2, videos.Length);
            string sharedKey = videos[0].FileItemKey!;
            Assert.All(videos, video => Assert.Equal(sharedKey, video.FileItemKey));

            using (ProjectEditSession first = document.BeginEdit())
            {
                Assert.True(first.Editor.RemoveTimelineObject(new(videos[0].LayerIndex, videos[0].ObjectKey!.Value)).Success);
                Assert.True(first.Commit().Success);
            }
            Assert.Contains(document.FileItems, item => item.Key == sharedKey);

            TimelineObjectView remaining = document.Layers.SelectMany(layer => layer.Objects).Single(item => item.Type == "Video");
            using (ProjectEditSession second = document.BeginEdit())
            {
                EditResult staged = second.Editor.RemoveTimelineObject(new(remaining.LayerIndex, remaining.ObjectKey!.Value));
                Assert.True(staged.Success);
                Assert.Contains(staged.Changes, change => change.Operation == "UnregisterFileItem" && change.Target == $"file:{sharedKey}");
                Assert.True(second.Commit().Success);
            }
            Assert.DoesNotContain(document.FileItems, item => item.Key == sharedKey);
            Assert.True(document.Validate().IsValid);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RemoveTimelineObject_DoesNotDeleteAssetFile()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string assetPath = Sample("004OneImage", "Sample.png");
            RecotteProjectDocument document = RecotteProject.Load(Sample("004OneImage", "004OneImage.ccproj"));
            TimelineObjectView image = document.Layers.SelectMany(layer => layer.Objects).Single(item => item.Type == "Image");
            using ProjectEditSession session = document.BeginEdit();

            Assert.True(session.Editor.RemoveTimelineObject(new(image.LayerIndex, image.ObjectKey!.Value)).Success);
            Assert.True(session.Commit().Success);

            Assert.Empty(document.FileItems);
            Assert.True(File.Exists(assetPath));
            string output = Path.Combine(directory, "Removed.ccproj");
            document.SaveCopy(output);
            RecotteProjectDocument loaded = RecotteProject.Load(output);
            Assert.DoesNotContain(loaded.Layers.SelectMany(layer => layer.Objects), item => item.Type == "Image");
            Assert.Empty(loaded.FileItems);
            Assert.True(loaded.Validate().IsValid);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RemoveTimelineObject_RejectsLockedObject()
    {
        RecotteProjectDocument document = RecotteProject.Load(Sample("005OneVideo", "005OneVideo.ccproj"));
        TimelineObjectView video = document.Layers.SelectMany(layer => layer.Objects).Single(item => item.Type == "Video");
        using ProjectEditSession session = document.BeginEdit();

        EditResult result = session.Editor.RemoveTimelineObject(new(video.LayerIndex, video.ObjectKey!.Value));

        Assert.False(result.Success);
        Assert.Equal("RC4207", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void ImportCharacter_ReusesEmptyLayerThenAddsSecondLayerWithoutVoices()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            RecotteProjectDocument target = RecotteProject.Create(new("Characters", directory));
            RecotteProjectDocument maleDonor = RecotteProject.Load(Sample("006OneCharacter", "006OneCharacter.ccproj"));
            using (ProjectEditSession first = target.BeginEdit())
            {
                CharacterImportResult result = first.Editor.ImportCharacter(new(maleDonor, "2D-スーツ男性", new(0m), new(10m)));
                Assert.True(result.Success);
                Assert.Equal(1, result.LayerId?.LayerIndex);
                Assert.True(first.Commit().Success);
            }
            RecotteProjectDocument twoDonor = RecotteProject.Load(Sample("008TwoCharacters", "008TwoCharacters.ccproj"));
            using (ProjectEditSession second = target.BeginEdit())
            {
                CharacterImportResult result = second.Editor.ImportCharacter(new(twoDonor, "2D-スーツ女性", new(0m), new(10m)));
                Assert.True(result.Success);
                Assert.Equal(2, result.LayerId?.LayerIndex);
                Assert.True(second.Commit().Success);
            }

            Assert.Equal(2, target.Speakers.Count);
            Assert.Equal(4, target.Layers.Count);
            Assert.Equal(2, target.Layers.SelectMany(layer => layer.Objects).Count(item => item.Type == "Speaker Character"));
            Assert.DoesNotContain(target.Layers.SelectMany(layer => layer.Objects), item => item.Type == "Speaker Voice");
            Assert.True(target.Validate().IsValid);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AssetFailures_DoNotMutateDocument()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            RecotteProjectDocument document = RecotteProject.Create(new("Failure", directory));
            using ProjectEditSession session = document.BeginEdit();
            AssetAddResult result = session.Editor.AddImageAsset(new(2, "missing.jpg",
                Path.Combine(directory, "Failure.ccproj"), "bad", new(0m), new(1m)));
            Assert.False(result.Success);
            Assert.Equal("RC4501", result.Diagnostics.Single().Code);
            Assert.Null(session.Editor.FindTimelineObject(new(2, 1)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DuplicateFileItemKey_IsRejected()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string output = Path.Combine(directory, "Assets.ccproj");
            RecotteProjectDocument document = RecotteProject.Create(new("Assets", directory));
            using ProjectEditSession session = document.BeginEdit(fileItemKeyAllocator: new FixedFileKeyAllocator('c'));
            Assert.True(session.Editor.AddImageAsset(new(2, Sample("004OneImage", "Sample.png"), output,
                "first", new(0m), new(1m))).Success);

            AssetAddResult duplicate = session.Editor.AddImageAsset(new(2, Sample("004OneImage", "Sample.png"), output,
                "second", new(1m), new(2m)));

            Assert.False(duplicate.Success);
            Assert.Equal("RC4505", duplicate.Diagnostics.Single().Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DuplicateCharacter_IsRejected()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            RecotteProjectDocument target = RecotteProject.Create(new("Characters", directory));
            RecotteProjectDocument donor = RecotteProject.Load(Sample("006OneCharacter", "006OneCharacter.ccproj"));
            using (ProjectEditSession first = target.BeginEdit())
            {
                Assert.True(first.Editor.ImportCharacter(new(donor, "2D-スーツ男性", new(0m), new(10m))).Success);
                Assert.True(first.Commit().Success);
            }
            using ProjectEditSession second = target.BeginEdit();
            CharacterImportResult duplicate = second.Editor.ImportCharacter(new(donor, "2D-スーツ男性", new(0m), new(10m)));
            Assert.False(duplicate.Success);
            Assert.Equal("RC4603", duplicate.Diagnostics.Single().Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Recotte.Core.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FixedFileKeyAllocator(char value) : IFileItemKeyAllocator
    {
        public string Allocate(IReadOnlyCollection<string> existingKeys) => new(value, 64);
    }
}
