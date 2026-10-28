using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class ProjectSizeTests : IDisposable
{
    private const string Man = "2D-スーツ男性";
    private const string Woman = "2D-スーツ女性";
    private readonly string directory = Directory.CreateTempSubdirectory("RecotteProjectSize-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Create_MakesAVerticalProjectWithTelopFramesFittedToIt()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Short", directory, 1080, 1920));

        Assert.True(document.Validate().IsValid);
        Assert.True(document.ValidateCreatedProject().IsValid);
        Assert.Equal((1080, 1920), (document.Settings?.Width, document.Settings?.Height));
        // The frames keep their 200-pixel height, scale with the width, and keep their share of the free space above and below.
        TelopFrameView lower = document.TelopFrames.Single(frame => frame.Name == "LowerFrame");
        TelopFrameView upper = document.TelopFrames.Single(frame => frame.Name == "UpperFrame");
        Assert.Equal((45m, 1563.64m, 990m, 200m), (lower.X, lower.Y, lower.Width, lower.Height));
        Assert.Equal((45m, 156.36m, 990m, 200m), (upper.X, upper.Y, upper.Width, upper.Height));
        Assert.All(document.TelopFrames, frame => Assert.True(frame.X >= 0 && frame.Y >= 0 && frame.X + frame.Width <= 1080 && frame.Y + frame.Height <= 1920));

        string output = Path.Combine(directory, "Short.ccproj");
        document.SaveCopy(output);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        Assert.Equal("[1080,1920]", saved["setting"]!["size"]!.ToJsonString());
        Assert.True(RecotteProject.Load(output).Validate().IsValid);
    }

    [Fact]
    public void Create_KeepsTheTemplateWhenTheDefaultSizeIsRequested()
    {
        JsonObject template = Sample("001EmptyProject");
        RecotteProjectDocument document = RecotteProject.Create(new("Wide", directory));

        Assert.Equal((1920, 1080), (document.Settings?.Width, document.Settings?.Height));
        string output = Path.Combine(directory, "Wide.ccproj");
        document.SaveCopy(output);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        Assert.True(JsonNode.DeepEquals(template["telop-frames"], saved["telop-frames"]));
        template["layers"]![1]!["properties"]!["LinkFolder"]!["p-value"] =
            new Uri(Path.Combine(directory, "voice", "話者1")).AbsoluteUri;
        Assert.True(JsonNode.DeepEquals(template["layers"], saved["layers"]));
    }

    [Theory]
    [InlineData(255, 1920)]
    [InlineData(1080, 7681)]
    [InlineData(0, 0)]
    public void Create_RejectsSizesOutOfRange(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => RecotteProject.Create(new("Bad", directory, width, height)));
    }

    [Fact]
    public void SetProjectSize_CropsAFullScreenImageToTheNewScreenByDefault()
    {
        RecotteProjectDocument document = Load(Sample("004OneImage"));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1080, 1920) });

        Assert.True(result.Success, Messages(result));
        Assert.Contains(result.Changes, change => change.Operation == "SetProjectSize" && (string?)change.After == "[1080,1920]");
        JsonObject image = Saved(document, "Image");
        Assert.Equal(new[] { 0m, 0m, 1080m, 1920m }, Rect(image, "Bounds"));
        // The 16:9 picture is shown over the whole 9:16 screen, so only its centered 607.5-pixel-wide strip is kept.
        Assert.Equal(new[] { 656.25m, 0m, 607.5m, 1080m }, Rect(image, "CropBounds"));
    }

    [Fact]
    public void SetProjectSize_ContainKeepsTheWholePictureCentered()
    {
        RecotteProjectDocument document = Load(Sample("005OneVideo"));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1080, 1920, ScreenFit.Contain) });

        Assert.True(result.Success, Messages(result));
        JsonObject video = Saved(document, "Video");
        Assert.Equal(new[] { 0m, 656.25m, 1080m, 607.5m }, Rect(video, "Bounds"));
        Assert.Equal(new[] { 0m, 0m, 1920m, 1080m }, Rect(video, "CropBounds"));
    }

    [Fact]
    public void SetProjectSize_KeepsCharactersInTheirCornerAtTheirSize()
    {
        RecotteProjectDocument document = Load(Sample("006OneCharacter"));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1080, 1920) });

        Assert.True(result.Success, Messages(result));
        JsonObject root = SavedRoot(document);
        JsonObject character = Objects(root, "Speaker Character").Single();
        Assert.Equal(new[] { 720m, 1560m, 360m, 360m }, Rect(character, "CharBounds"));
        Assert.Equal(new[] { 720m, 1560m, 360m, 360m }, Values(root["layers"]![1]!["properties"]!["CharBounds"]!));
        TelopFrameView lower = document.TelopFrames.Single(frame => frame.Name == "LowerFrame");
        Assert.Equal((45m, 787.5m, 200m), (lower.X, lower.Width, lower.Height));
    }

    [Fact]
    public void SetProjectSize_BackToTheOldSizeRestoresWhatWasMovedOrScaled()
    {
        JsonObject original = Sample("009FullProject");
        RecotteProjectDocument document = Load(original);

        Assert.True(document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1080, 1920) }).Success);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1920, 1080) }).Success);

        JsonObject root = SavedRoot(document);
        Assert.Equal(original["telop-frames"]!.AsArray().OfType<JsonObject>().Select(frame => Values(frame["Bounds"]!)),
            root["telop-frames"]!.AsArray().OfType<JsonObject>().Select(frame => Values(frame["Bounds"]!)), new RectComparer());
        Assert.Equal(Objects(original, "Speaker Character").Select(item => Rect(item, "CharBounds")),
            Objects(root, "Speaker Character").Select(item => Rect(item, "CharBounds")), new RectComparer());
        Assert.Equal(Objects(original, "Figure").Select(item => Rect(item, "Bounds")),
            Objects(root, "Figure").Select(item => Rect(item, "Bounds")), new RectComparer());
    }

    [Fact]
    public void SetProjectSize_LeavesLockedAndAnimatedRectanglesWithAWarning()
    {
        JsonObject root = Sample("012TwoImages");
        JsonObject[] images = Objects(root, "Image").ToArray();
        images[0]["tl-locked"] = true;
        images[1]["properties"]!["Bounds"]!["keyframes"] = new JsonArray(new JsonObject { ["time"] = 0.0 });
        RecotteProjectDocument document = Load(root);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1080, 1920) });

        Assert.True(result.Success, Messages(result));
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "RC4932" && diagnostic.Severity == DiagnosticSeverity.Warning));
        Assert.All(Objects(SavedRoot(document), "Image"), image => Assert.Equal(new[] { 0m, 0m, 1920m, 1080m }, Rect(image, "Bounds")));
    }

    [Fact]
    public void SetProjectSize_RejectsSizesOutOfRange()
    {
        RecotteProjectDocument document = Load(Sample("001EmptyProject"));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(100, 1920) });

        Assert.False(result.Success);
        Assert.Equal("RC4931", result.Diagnostics.First(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Code);
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public void SetProjectSize_ToTheCurrentSizeChangesNothing()
    {
        RecotteProjectDocument document = Load(Sample("006OneCharacter"));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1920, 1080) });

        Assert.True(result.Success, Messages(result));
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void SetProjectSize_UpdatesAnExportSizeChosenForTheOldScreen()
    {
        JsonObject root = Sample("001EmptyProject");
        root["setting"]!["export-video-size"] = new JsonArray(1920, 1080);
        RecotteProjectDocument document = Load(root);

        Assert.True(document.ApplyOperations(new ProjectOperation[] { new SetProjectSizeOperation(1080, 1920) }).Success);

        Assert.Equal("[1080,1920]", SavedRoot(document)["setting"]!["export-video-size"]!.ToJsonString());
    }

    [Fact]
    public void Capabilities_ReportThatTheSizeCanBeChanged()
    {
        Assert.True(Load(Sample("001EmptyProject")).Capabilities.CanSetProjectSize);
    }

    [Fact]
    public void AddedImagesAndVideosFillAVerticalScreen()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Short", directory, 1080, 1920));
        string output = Path.Combine(directory, "Short.ccproj");

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new AddImageAssetOperation(new LayerIndexReference(2), Asset("004OneImage", "Sample.png"), output, "image", new(0m), new(1m)),
            new AddVideoAssetOperation(new LayerIndexReference(0), Asset("005OneVideo", "Sample.mp4"), output, "video", new(0m), new(1m)),
            new AddAnnotationTextOperation(new LayerIndexReference(2), "メモ", new(0m), new(1m)),
        });

        Assert.True(result.Success, Messages(result));
        JsonObject root = SavedRoot(document);
        Assert.Equal(new[] { 0m, 0m, 1080m, 1920m }, Rect(Objects(root, "Image").Single(), "Bounds"));
        JsonObject video = Objects(root, "Video").Single();
        Assert.Equal(new[] { 0m, 0m, 1080m, 1920m }, Rect(video, "Bounds"));
        Assert.Equal(new[] { 0m, 0m, 1920m, 1080m }, Rect(video, "CropBounds"));
        // The template text box sits at [710, 176, 500, 128] on 1920x1080; on 1080 wide it would run off the right edge.
        Assert.Equal(new[] { 290m, 331.29m, 500m, 128m }, Rect(Objects(root, "Figure").Single(), "Bounds"));
    }

    [Fact]
    public void AddVideo_UsesTheVideoSizeForCropBoundsAndSkipsRotatedVideos()
    {
        RecotteProjectDocument document = RecotteProject.Create(new("Short", directory, 1080, 1920));
        string output = Path.Combine(directory, "Short.ccproj");
        string upright = Path.Combine(directory, "upright.mp4"), rotated = Path.Combine(directory, "rotated.mp4");
        File.WriteAllBytes(upright, Mp4(1080, 1920, rotated: false, version: 1));
        File.WriteAllBytes(rotated, Mp4(1920, 1080, rotated: true, version: 0));

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new AddVideoAssetOperation(new LayerIndexReference(0), upright, output, "upright", new(0m), new(1m)),
            new AddVideoAssetOperation(new LayerIndexReference(0), rotated, output, "rotated", new(1m), new(2m)),
        });

        Assert.True(result.Success, Messages(result));
        JsonObject[] videos = Objects(SavedRoot(document), "Video").ToArray();
        Assert.Equal(new[] { 0m, 0m, 1080m, 1920m }, Rect(videos[0], "CropBounds"));
        // A rotated track's stored size is not the shown size, so the template's crop is kept.
        Assert.Equal(new[] { 0m, 0m, 1920m, 1080m }, Rect(videos[1], "CropBounds"));
    }

    [Fact]
    public void ImportCharacter_FromALandscapeDonorFitsAVerticalProject()
    {
        RecotteProjectDocument donor = RecotteProject.Load(Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects",
            "011TwoVoices", "011TwoVoices.ccproj"));
        RecotteProjectDocument target = RecotteProject.Create(new("Short", directory, 1080, 1920));

        // The second import checks that the first character sits where the relaid-out donor puts it.
        ProjectBatchResult result = target.ApplyOperations(new ProjectOperation[]
        {
            new ImportCharacterOperation(donor, Man, new(0m), new(10m)),
            new ImportCharacterOperation(donor, Woman, new(0m), new(10m)),
        });

        Assert.True(result.Success, Messages(result));
        JsonObject root = SavedRoot(target);
        Assert.Equal("[1080,1920]", root["setting"]!["size"]!.ToJsonString());
        Assert.Equal(new[] { new[] { 0m, 1560m, 360m, 360m }, new[] { 720m, 1560m, 360m, 360m } },
            Objects(root, "Speaker Character").Select(item => Rect(item, "CharBounds")));
        Assert.All(target.TelopFrames, frame => Assert.True(frame.X >= 0 && frame.X + frame.Width <= 1080 && frame.Y + frame.Height <= 1920));
    }

    // A minimal MP4: ftyp, then moov holding one video trak with tkhd and mdia/hdlr.
    private static byte[] Mp4(int width, int height, bool rotated, int version)
    {
        int[] matrix = rotated ? new[] { 0, 0x00010000, 0, -0x00010000, 0, 0, 0, 0, 0x40000000 }
            : new[] { 0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000 };
        byte[] tkhd = new byte[(version == 1 ? 52 : 40) + 36 + 8];
        tkhd[0] = (byte)version;
        int offset = version == 1 ? 52 : 40;
        for (int index = 0; index < 9; index++) BinaryPrimitives.WriteInt32BigEndian(tkhd.AsSpan(offset + index * 4), matrix[index]);
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(offset + 36), (uint)width << 16);
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(offset + 40), (uint)height << 16);
        byte[] hdlr = new byte[24];
        Encoding.ASCII.GetBytes("vide").CopyTo(hdlr, 8);
        byte[] trak = Box("trak", Box("tkhd", tkhd), Box("mdia", Box("hdlr", hdlr)));
        return Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\0\0")).Concat(Box("moov", trak)).ToArray();
    }

    private static byte[] Box(string type, params byte[][] payloads)
    {
        byte[] payload = payloads.SelectMany(item => item).ToArray();
        byte[] box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private JsonObject SavedRoot(RecotteProjectDocument document)
    {
        string output = Path.Combine(directory, $"{Guid.NewGuid():N}.ccproj");
        document.SaveCopy(output);
        return JsonNode.Parse(File.ReadAllText(output))!.AsObject();
    }

    private JsonObject Saved(RecotteProjectDocument document, string type) => Objects(SavedRoot(document), type).Single();

    private static IEnumerable<JsonObject> Objects(JsonObject root, string type) => root["layers"]!.AsArray().OfType<JsonObject>()
        .SelectMany(layer => layer["layer-objects"]!.AsArray().OfType<JsonObject>())
        .Where(item => (string?)item["type"] == type);

    private static decimal[] Rect(JsonObject item, string property) => Values(item["properties"]![property]!);

    private static decimal[] Values(JsonNode entry) => entry["p-value"]!.AsArray().Select(value => value!.GetValue<decimal>()).ToArray();

    private static JsonObject Sample(string name) => JsonNode.Parse(File.ReadAllText(Asset(name, name + ".ccproj")))!.AsObject();

    private static string Asset(string sample, string file) => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects", sample, file);

    private static RecotteProjectDocument Load(JsonObject root) => RecotteProject.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));

    private static string Messages(ProjectBatchResult result) => string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

    // Rounding to hundredths can leave a round trip a hundredth or two off.
    private sealed class RectComparer : IEqualityComparer<decimal[]>
    {
        public bool Equals(decimal[]? x, decimal[]? y) => x is not null && y is not null && x.Length == y.Length &&
            x.Zip(y).All(pair => Math.Abs(pair.First - pair.Second) <= 0.02m);
        public int GetHashCode(decimal[] obj) => obj.Length;
    }
}
