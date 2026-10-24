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
    public void FadeSaveMatchesSampleAndPreservesOtherData(string type, string key)
    {
        var (document, id, original) = Target(type);
        decimal clipLength = original["end-time"]!.GetValue<decimal>() - original["start-time"]!.GetValue<decimal>();
        decimal duration = Math.Min(0.5m, clipLength);
        var result = document.ApplyOperations(new ProjectOperation[] { new AddFadeEffectOperation(new TimelineObjectIdReference(id), key, new(duration)) });
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
        Assert.Equal(1, Assert.Single(RecotteProject.Load(output).GetFadeEffects(id)).EffectIndex);
    }

    [Fact]
    public void UpdateRemoveAndFailureAreAtomic()
    {
        var (document, id, _) = Target("Image");
        var target = new TimelineObjectIdReference(id);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new AddFadeEffectOperation(target, "Alpha Fade", new(0.1m)) }).Success);
        long revision = document.Revision;
        var failed = document.ApplyOperations(new ProjectOperation[] {
            new UpdateFadeEffectOperation(target, 1, "Alpha Fade Out", new(0.1m)), new RemoveFadeEffectOperation(target, 0) });
        Assert.False(failed.Success);
        Assert.True(failed.RolledBack);
        Assert.Equal(revision, document.Revision);
        Assert.Equal("Alpha Fade", Assert.Single(document.GetFadeEffects(id)).EffectKey);
        Assert.True(document.PreviewOperations(new ProjectOperation[] { new RemoveFadeEffectOperation(target, 1) }).BatchResult.Success);
        Assert.Single(document.GetFadeEffects(id));
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new UpdateFadeEffectOperation(target, 1, "Alpha Fade Out", new(0.1m)) }).Success);
        Assert.Equal("Alpha Fade Out", Assert.Single(document.GetFadeEffects(id)).EffectKey);
        Assert.True(document.ApplyOperations(new ProjectOperation[] { new RemoveFadeEffectOperation(target, 1) }).Success);
        Assert.Empty(document.GetFadeEffects(id));
    }

    [Theory]
    [InlineData("Alpha Fade", 0)]
    [InlineData("Alpha Fade", 10000)]
    [InlineData("Spin", 0.1)]
    public void RejectsUnsupportedOrInvalidFade(string key, double seconds)
    {
        var (document, id, _) = Target("Image");
        Assert.False(document.ApplyOperations(new ProjectOperation[] { new AddFadeEffectOperation(new TimelineObjectIdReference(id), key, new((decimal)seconds)) }).Success);
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public void LockedVideoIsRejected()
    {
        var (document, id, _) = Target("Video", false);
        Assert.False(document.ApplyOperations(new ProjectOperation[] { new AddFadeEffectOperation(new TimelineObjectIdReference(id), "Alpha Fade", new(0.1m)) }).Success);
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
        Assert.Equal(2, catalog.Effects.Count(effect => effect.CanEdit));
    }
}
