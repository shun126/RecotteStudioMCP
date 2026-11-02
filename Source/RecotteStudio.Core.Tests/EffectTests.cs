using System.Text;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class EffectTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"recotte-effects-{Guid.NewGuid():N}");
    public EffectTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static JsonObject Sample() => JsonNode.Parse(File.ReadAllText(Path.Combine(TestProjects.RepositoryRoot,
        "Documents", "RecotteProjects", "009FullProject", "009FullProject.ccproj")))!.AsObject();
    private static IEnumerable<JsonObject> Objects(JsonObject root) => root["layers"]!.AsArray().OfType<JsonObject>()
        .SelectMany(layer => layer["layer-objects"]!.AsArray().OfType<JsonObject>());
    private static RecotteProjectDocument Load(JsonObject root) => RecotteProject.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));
    private static (RecotteProjectDocument Document, TimelineObjectId Id, JsonObject Original) Target(string type, bool unlock = true)
    {
        JsonObject root = type == "Image" ? JsonNode.Parse(File.ReadAllText(Path.Combine(TestProjects.RepositoryRoot,
            "Documents", "RecotteProjects", "004OneImage", "004OneImage.ccproj")))!.AsObject() : Sample();
        var pair = root["layers"]!.AsArray().Select((layer, index) => (layer, index))
            .SelectMany(pair => pair.layer!["layer-objects"]!.AsArray().OfType<JsonObject>().Select(obj => (pair.index, obj)))
            .First(pair => pair.obj["type"]!.GetValue<string>() == type);
        if (unlock) foreach (string field in new[] { "tl-locked", "st-locked", "pv-locked" }) pair.obj[field] = false;
        else pair.obj["tl-locked"] = true;
        pair.obj["sub-objects"] = new JsonArray(new JsonObject { ["future"] = "preserve" });
        return (Load(root), new(pair.index, pair.obj["objkey"]!.GetValue<int>()), (JsonObject)pair.obj.DeepClone());
    }

    [Theory]
    [InlineData("Video", "Alpha Fade")]
    [InlineData("Video", "Alpha Fade Out")]
    [InlineData("Image", "Alpha Fade")]
    [InlineData("Image", "Alpha Fade Out")]
    [InlineData("Speaker Character", "Alpha Fade")]
    [InlineData("Speaker Character", "Alpha Fade Out")]
    [InlineData("Figure", "Alpha Fade")]
    public void FadeSaveMatchesSampleAndPreservesOtherData(string type, string key)
    {
        var (document, id, original) = Target(type);
        decimal clipLength = original["end-time"]!.GetValue<decimal>() - original["start-time"]!.GetValue<decimal>();
        decimal duration = Math.Min(0.5m, clipLength);
        var result = document.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(new TimelineObjectIdReference(id), key, new(duration)) });
        Assert.True(result.Success, string.Join(";", result.Diagnostics.Select(item => item.Message)));
        string output = Path.Combine(directory, "fade.ccproj");
        document.SaveCopy(output);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(output))!["layers"]![id.LayerIndex]!["layer-objects"]!.AsArray()
            .OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == id.ObjectKey);
        JsonObject expectedFade = Objects(Sample()).SelectMany(item => item["sub-objects"]!.AsArray().OfType<JsonObject>())
            .First(item => item["type-name"]?.GetValue<string>() == key);
        expectedFade["start-time"] = key == "Alpha Fade" ? 0m : clipLength - duration;
        expectedFade["duration"] = duration;
        Assert.True(JsonNode.DeepEquals(expectedFade, saved["sub-objects"]![1]));
        saved["sub-objects"]!.AsArray().RemoveAt(1);
        Assert.True(JsonNode.DeepEquals(original, saved));
        Assert.Equal(1, Assert.Single(RecotteProject.Load(output).GetClipEffects(id)).EffectIndex);
    }

    [Fact]
    public void UpdateRemoveAndFailureAreAtomic()
    {
        var (document, id, _) = Target("Image");
        var target = new TimelineObjectIdReference(id);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(target, "Alpha Fade", new(0.1m)) }).Success);
        long revision = document.Revision;
        var failed = document.ApplyOperations(new ProjectOperation[] {
            new UpdateClipEffectOperation(target, 1, Start: new(0.1m)), new RemoveClipEffectOperation(target, 0) });
        Assert.False(failed.Success);
        Assert.True(failed.RolledBack);
        Assert.Equal(revision, document.Revision);
        Assert.Equal("Alpha Fade", Assert.Single(document.GetClipEffects(id)).EffectKey);
        Assert.True(document.PreviewOperations(new ProjectOperation[] { new RemoveClipEffectOperation(target, 1) }).BatchResult.Success);
        Assert.Single(document.GetClipEffects(id));
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new UpdateClipEffectOperation(target, 1, Start: new(0.1m)) }).Success);
        Assert.Equal(0.1m, Assert.Single(document.GetClipEffects(id)).Start);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new RemoveClipEffectOperation(target, 1) }).Success);
        Assert.Empty(document.GetClipEffects(id));
    }

    [Theory]
    [InlineData("Alpha Fade", 0)]
    [InlineData("Alpha Fade", 10000)]
    [InlineData("Spin", 0.1)]
    public void RejectsUnsupportedOrInvalidFade(string key, double seconds)
    {
        var (document, id, _) = Target("Image");
        Assert.False(document.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(new TimelineObjectIdReference(id), key, new((decimal)seconds)) }).Success);
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public void LockedVideoIsRejected()
    {
        var (document, id, _) = Target("Video", false);
        Assert.False(document.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(new TimelineObjectIdReference(id), "Alpha Fade", new(0.1m)) }).Success);
    }

    [Theory]
    [InlineData("Video")]
    [InlineData("Image")]
    [InlineData("Figure")]
    [InlineData("Speaker Character")]
    public void VibrationCanBeAddedMovedConfiguredAndRemovedWithoutChangingTheClip(string type)
    {
        var (document, id, original) = Target(type);
        var target = new TimelineObjectIdReference(id);
        Assert.True(document.ApplyOperations(new ProjectOperation[] {
            new AddClipEffectOperation(target, "Vibration", new(0.1m), EffectProperties: new Dictionary<string, decimal>
                { ["SwingWidth"] = 12, ["cycle"] = 0.05m }) }).Success);
        var added = Assert.Single(document.GetClipEffects(id));
        Assert.Equal(1, added.EffectIndex);
        Assert.True(added.CanEdit);
        Assert.Equal(12m, added.Properties["SwingWidth"]!.GetValue<decimal>());
        Assert.True(document.ApplyOperations(new ProjectOperation[] {
            new UpdateClipEffectOperation(target, added.EffectIndex, Start: new(0.05m), Duration: new(0.15m),
                EffectProperties: new Dictionary<string, decimal> { ["SwingWidth"] = 6 }) }).Success);
        string output = Path.Combine(directory, "vibration.ccproj");
        document.SaveCopy(output);
        var saved = RecotteProject.Load(output);
        var moved = Assert.Single(saved.GetClipEffects(id));
        Assert.Equal(0.05m, moved.Start);
        Assert.Equal(0.15m, moved.Duration);
        Assert.Equal(6m, moved.Properties["SwingWidth"]!.GetValue<decimal>());
        Assert.Equal(0.05m, moved.Properties["cycle"]!.GetValue<decimal>());
        Assert.True(saved.ApplyOperations(new ProjectOperation[] { new RemoveClipEffectOperation(target, moved.EffectIndex) }).Success);
        string removed = Path.Combine(directory, "removed.ccproj");
        saved.SaveCopy(removed);
        JsonObject clip = JsonNode.Parse(File.ReadAllText(removed))!["layers"]![id.LayerIndex]!["layer-objects"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["objkey"]!.GetValue<int>() == id.ObjectKey && item["type"]!.GetValue<string>() == type);
        Assert.True(JsonNode.DeepEquals(original, clip));
    }

    [Theory]
    [InlineData("SwingWidth", -1)]
    [InlineData("cycle", 0)]
    [InlineData("cycle", -1)]
    [InlineData("unknown", 1)]
    public void InvalidSettingsRollBackTheWholeBatch(string name, int value)
    {
        var (document, id, _) = Target("Image");
        var target = new TimelineObjectIdReference(id);
        var result = document.ApplyOperations(new ProjectOperation[] {
            new AddClipEffectOperation(target, "Alpha Fade", new(0.1m)),
            new AddClipEffectOperation(target, "Vibration", new(0.1m), EffectProperties: new Dictionary<string, decimal> { [name] = value }) });
        Assert.False(result.Success);
        Assert.True(result.RolledBack);
        Assert.Empty(document.GetClipEffects(id));
        Assert.Equal(0, document.Revision);
    }

    [Theory]
    [InlineData("Video")]
    [InlineData("Image")]
    [InlineData("Speaker Character")]
    public void ClipAndLayerLocksPreventAllEffectMutations(string type)
    {
        var (document, id, _) = Target(type);
        var target = new TimelineObjectIdReference(id);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(target, "Vibration", new(0.1m)) }).Success);
        string output = Path.Combine(directory, "locked.ccproj");
        document.SaveCopy(output);
        JsonObject root = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        foreach (bool layerLock in new[] { false, true })
        {
            JsonObject layer = root["layers"]![id.LayerIndex]!.AsObject();
            layer["locked"] = layerLock;
            layer["layer-objects"]!.AsArray().OfType<JsonObject>().Single(item => item["objkey"]!.GetValue<int>() == id.ObjectKey)["tl-locked"] = !layerLock;
            var locked = Load(root);
            foreach (ProjectOperation op in new ProjectOperation[] {
                new AddClipEffectOperation(target, "Alpha Fade", new(0.1m)),
                new UpdateClipEffectOperation(target, 1, Start: new(0.01m)), new RemoveClipEffectOperation(target, 1) })
            {
                var result = locked.ApplyOperations(new[] { op });
                Assert.Contains(result.Diagnostics, item => item.Code == "RC4207");
                Assert.Single(locked.GetClipEffects(id));
            }
        }
    }

    [Fact]
    public void TimingChangesKeepCurvesAndUnknownFieldsAndRejectOverflow()
    {
        JsonObject root = Sample();
        JsonObject video = Objects(root).First(item => item["type"]!.GetValue<string>() == "Video");
        video["tl-locked"] = false;
        JsonObject fade = video["sub-objects"]![0]!.AsObject();
        fade["future"] = new JsonObject { ["keep"] = true };
        JsonObject before = (JsonObject)fade.DeepClone();
        var document = Load(root);
        var target = new TimelineObjectIdReference(new(0, video["objkey"]!.GetValue<int>()));
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new UpdateClipEffectOperation(target, 0, Start: new(0.1m), Duration: new(0.2m)) }).Success);
        string output = Path.Combine(directory, "preserved.ccproj");
        document.SaveCopy(output);
        JsonObject after = Objects(JsonNode.Parse(File.ReadAllText(output))!.AsObject()).First(item => item["type"]!.GetValue<string>() == "Video")["sub-objects"]![0]!.AsObject();
        before["start-time"] = 0.1m;
        before["duration"] = 0.2m;
        Assert.True(JsonNode.DeepEquals(before, after));
        long revision = document.Revision;
        Assert.False(document.ApplyOperations(new ProjectOperation[] { new UpdateClipEffectOperation(target, 0, Start: new(100m)) }).Success);
        Assert.Equal(revision, document.Revision);
    }

    [Fact]
    public void AnimatedVibrationPropertiesArePreservedByMovesAndRejectedByPropertyEdits()
    {
        var (document, id, _) = Target("Image");
        var target = new TimelineObjectIdReference(id);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(target, "Vibration", new(0.1m)) }).Success);
        string output = Path.Combine(directory, "animated.ccproj");
        document.SaveCopy(output);
        JsonObject root = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        JsonObject effect = root["layers"]![id.LayerIndex]!["layer-objects"]![0]!["sub-objects"]![1]!.AsObject();
        effect["properties"]!["SwingWidth"]!["keyframes"] = new JsonArray(new JsonObject { ["time"] = 0m, ["future"] = true });
        var animated = Load(root);
        Assert.True(animated.ApplyOperations(new ProjectOperation[] { new UpdateClipEffectOperation(target, 1, Start: new(0.1m)) }).Success);
        var failed = animated.ApplyOperations(new ProjectOperation[] { new UpdateClipEffectOperation(target, 1,
            EffectProperties: new Dictionary<string, decimal> { ["cycle"] = 0.2m, ["SwingWidth"] = 5 }) });
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Code == "RC4907");
        var listed = Assert.Single(animated.GetClipEffects(id));
        Assert.Equal(0.1m, listed.Start);
        Assert.Equal(0.1m, listed.Properties["cycle"]!.GetValue<decimal>());
    }

    [Fact]
    public void UnsupportedEffectsAreListedButCannotBeMutatedAndVoicesAreNotTargets()
    {
        JsonObject root = Sample();
        JsonObject video = Objects(root).First(item => item["type"]!.GetValue<string>() == "Video");
        video["tl-locked"] = false;
        JsonObject effect = video["sub-objects"]![0]!.AsObject();
        effect["type-name"] = "Future Effect";
        var document = Load(root);
        var id = new TimelineObjectId(0, video["objkey"]!.GetValue<int>());
        Assert.False(Assert.Single(document.GetClipEffects(id)).CanEdit);
        Assert.False(document.ApplyOperations(new ProjectOperation[] { new RemoveClipEffectOperation(new TimelineObjectIdReference(id), 0) }).Success);
        var (voiceDocument, voiceId, _) = Target("Speaker Voice");
        var result = voiceDocument.ApplyOperations(new ProjectOperation[] { new AddClipEffectOperation(new TimelineObjectIdReference(voiceId), "Alpha Fade", new(0.1m)) });
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC4901");
    }

    [Fact]
    public void CatalogReadsLiteralMetadataWithoutExecutingLuaAndReportsUnsupportedFiles()
    {
        Directory.CreateDirectory(Path.Combine(directory, "effects"));
        File.WriteAllText(Path.Combine(directory, "effects", "fade.lua"), """
            -- name = "Ignore this comment"
            function GetInfo()
                local info = { name = "Alpha Fade", displayname = { ja = "フェード", en = "Fade" }, tag = "in" }
                return info
            end
            function InitEffect()
                SetDuration(0.5)
                AddProperty(NewProperty("curve", {ja="時間曲線"}, "curve"))
            end
            error("must never execute")
            """);
        File.WriteAllText(Path.Combine(directory, "effects", "unknown.lua"), "function GetInfo() return calculateInfo() end");
        EffectCatalog catalog = EffectCatalog.Load(directory);
        var fade = Assert.Single(catalog.Effects);
        Assert.Equal("フェード", fade.NameJa);
        Assert.Equal(0.5m, fade.DefaultDuration);
        Assert.Equal(new[] { "curve" }, fade.PropertyKeys);
        Assert.True(fade.CanEdit);
        Assert.Single(catalog.Diagnostics);
    }

    [Fact]
    public void CatalogReportsInaccessibleKindFolderAndReadsTheOthers()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(directory, "text"));
        File.WriteAllText(Path.Combine(directory, "text", "pop.lua"), """
            function GetInfo()
                local info = { name = "Pop" }
                return info
            end
            """);
        using InaccessibleDirectory locked = new(Path.Combine(directory, "effects"));
        if (!locked.IsDenied) return;

        EffectCatalog catalog = EffectCatalog.Load(directory);

        Assert.Equal("Pop", Assert.Single(catalog.Effects).Key);
        Assert.StartsWith($"Cannot read {Path.Combine(directory, "effects")}", Assert.Single(catalog.Diagnostics));
    }

    [Fact]
    public void InstalledCatalogReadsEveryDefinition()
    {
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RecotteStudio", "effects");
        if (!Directory.Exists(installed)) return;
        EffectCatalog catalog = EffectCatalog.Load(installed);
        Assert.Empty(catalog.Diagnostics);
        Assert.Equal(Directory.EnumerateFiles(installed, "*.lua", SearchOption.AllDirectories).Count(), catalog.Effects.Count);
        Assert.Equal(3, catalog.Effects.Count(effect => effect.CanEdit));
    }
}
