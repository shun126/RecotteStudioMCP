using RecotteStudio.Core;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecotteStudio.McpServer.Tests;

/// <summary>Covers adding a text-only Speaker Voice to a Speaker layer that holds no clonable voice yet.</summary>
public sealed class TextVoiceTemplateTests : IDisposable
{
    private const string TextTemplateResource = "RecotteStudio.Core.Templates.Text.ccproj";

    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-text-{Guid.NewGuid():N}");
    private readonly RecotteToolService service;

    public TextVoiceTemplateTests()
    {
        Directory.CreateDirectory(root);
        WorkspacePathPolicy policy = new(new(root));
        service = new(policy, new(), new());
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void EmbeddedTextTemplateResolvesToOneVerifiedTextOnlyVoice()
    {
        Assembly core = typeof(RecotteProject).Assembly;
        Assert.Contains(TextTemplateResource, core.GetManifestResourceNames());

        using Stream stream = core.GetManifestResourceStream(TextTemplateResource)!;
        RecotteProjectDocument template = RecotteProject.Load(stream);
        Assert.True(template.Validate().IsValid);
        TimelineEntryView voice = Assert.Single(template.GetTimelineEntries(), entry => entry.ObjectType == "Speaker Voice");
        Assert.True(voice.Capabilities.CanUpdateText);
    }

    [Fact]
    public async Task CreateProjectAcceptsTextOperationsOnTheEmptySpeakerLayer()
    {
        string output = Path.Combine(root, "narration.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("narration", output, false,
            new[] { AddText("春はあけぼの。", 0m, 5.5m) }, default);

        Assert.True(result.Success);
        RecotteProjectDocument created = RecotteProject.Load(output);
        ProjectSummary summary = created.GetSummary();
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.WarningCount);
        Assert.Equal(0, summary.MissingAssetCount);

        JsonObject voice = SpeakerVoices(output).Single();
        Assert.Equal("春はあけぼの。", (string?)voice["name"]);
        Assert.Equal("春はあけぼの。", (string?)voice["text"]!["text"]);
        Assert.False(voice.ContainsKey("audio"));
        Assert.Equal(0, (int?)voice["voice-hash"]);
        Assert.Equal(string.Empty, (string?)voice["properties"]!["File"]!["p-value"]);
    }

    [Fact]
    public async Task ConsecutiveTextOperationsAllocateUniqueObjectKeys()
    {
        string output = Path.Combine(root, "sequence.ccproj");
        McpToolResult<object> result = await service.CreateProjectAsync("sequence", output, false,
            new[] { AddText("一", 0m, 1m), AddText("二", 1m, 2m), AddText("三", 2m, 3m) }, default);

        Assert.True(result.Success);
        int[] keys = RecotteProject.Load(output).GetTimelineEntries()
            .Where(entry => entry.ObjectType == "Speaker Voice" && entry.ObjectId is not null)
            .Select(entry => entry.ObjectId!.Value.ObjectKey).ToArray();
        Assert.Equal(3, keys.Length);
        Assert.Equal(3, keys.Distinct().Count());
    }

    [Fact]
    public async Task ExistingLayerVoiceIsPreferredOverTheEmbeddedTemplate()
    {
        string seeded = Path.Combine(root, "seeded.ccproj");
        Assert.True((await service.CreateProjectAsync("seeded", seeded, false,
            new[] { AddText("最初", 0m, 1m) }, default)).Success);

        // Mark the existing voice with a field the embedded template leaves false, so the clone source is identifiable.
        Mutate(seeded, project => SpeakerVoices(project).Single()["snap-to-end"] = true);

        string output = Path.Combine(root, "cloned.ccproj");
        Assert.True((await service.ApplyOperationsAndSaveCopyAsync(seeded, output, false,
            new[] { AddText("二番目", 1m, 2m) }, default)).Success);

        JsonObject added = SpeakerVoices(output).Single(voice => (string?)voice["name"] == "二番目");
        Assert.True((bool?)added["snap-to-end"]);
    }

    [Fact]
    public async Task IncompatibleTextStylesFailExplicitlyAndAreReportedAsUnavailable()
    {
        string project = Path.Combine(root, "custom-styles.ccproj");
        Assert.True((await service.CreateProjectAsync("custom-styles", project, false,
            Array.Empty<ProjectOperationDto>(), default)).Success);
        Mutate(project, root => ((JsonArray)root["text-styles"]!).RemoveAt(0));

        Assert.False(RecotteProject.Load(project).Capabilities.CanAddTextOnlySpeakerVoice);

        string output = Path.Combine(root, "custom-styles-edited.ccproj");
        McpToolResult<object> result = await service.ApplyOperationsAndSaveCopyAsync(project, output, false,
            new[] { AddText("追加できない", 0m, 1m) }, default);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC4405");
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task EmbeddedTemplateIsBoundToTheTargetLayerSettings()
    {
        string project = Path.Combine(root, "customized.ccproj");
        Assert.True((await service.CreateProjectAsync("customized", project, false,
            Array.Empty<ProjectOperationDto>(), default)).Success);

        // Diverge from the embedded template: a resized telop frame and a layer using its own style and volume.
        Mutate(project, root =>
        {
            JsonObject frame = ((JsonArray)root["telop-frames"]!).OfType<JsonObject>().First();
            frame["Bounds"]!["p-value"] = new JsonArray(51.0, 858.0, 1632.0, 200.0);
            JsonObject layer = (JsonObject)root["layers"]![1]!["properties"]!;
            layer["DefaultTextStyle"]!["p-value"] = "話者1";
            layer["InitialAudioVolume"]!["p-value"] = 1.0;
        });
        string framesBefore = Read(project)["telop-frames"]!.ToJsonString();

        Assert.True(RecotteProject.Load(project).Capabilities.CanAddTextOnlySpeakerVoice);
        string output = Path.Combine(root, "customized-edited.ccproj");
        Assert.True((await service.ApplyOperationsAndSaveCopyAsync(project, output, false,
            new[] { AddText("レイヤーの設定を使う", 200m, 218m) }, default)).Success);

        JsonObject voice = SpeakerVoices(output).Single();
        JsonObject style = ((JsonArray)voice["text"]!["stext"]!).OfType<JsonObject>().Single(run => (string?)run["c"] == "s");
        Assert.Equal("話者1", (string?)style["style"]);
        Assert.Equal("LowerFrame", (string?)voice["properties"]!["TelopFrame"]!["p-value"]);
        Assert.Equal(1.0, (double?)voice["properties"]!["AudioVolume"]!["p-value"]);
        Assert.Equal(200m, (decimal?)voice["start-time"]);
        Assert.Equal(218m, (decimal?)voice["end-time"]);
        Assert.False(voice.ContainsKey("audio"));
        Assert.Equal(0, (int?)voice["voice-hash"]);
        Assert.Equal(string.Empty, (string?)voice["properties"]!["File"]!["p-value"]);
        Assert.Equal(framesBefore, Read(output)["telop-frames"]!.ToJsonString());
    }

    [Fact]
    public async Task InvalidLayerDefaultsDoNotCorruptTheVerifiedVoiceTemplate()
    {
        string project = Path.Combine(root, "invalid-defaults.ccproj");
        Assert.True((await service.CreateProjectAsync("invalid-defaults", project, false,
            Array.Empty<ProjectOperationDto>(), default)).Success);

        Mutate(project, root =>
        {
            JsonObject layer = (JsonObject)root["layers"]![1]!["properties"]!;
            layer["InitialAudioVolume"]!["p-value"] = "invalid";
            layer["BaseLipMorphLevel"]!["p-value"] = true;
            layer["ShowTelopDefault"]!["p-value"] = 1;
        });

        Assert.True(RecotteProject.Load(project).Capabilities.CanAddTextOnlySpeakerVoice);
        string output = Path.Combine(root, "invalid-defaults-edited.ccproj");
        Assert.True((await service.ApplyOperationsAndSaveCopyAsync(project, output, false,
            new[] { AddText("安全な既定値を使う", 0m, 1m) }, default)).Success);

        JsonObject voice = SpeakerVoices(output).Single();
        JsonObject properties = (JsonObject)voice["properties"]!;
        JsonValue audioVolume = Assert.IsAssignableFrom<JsonValue>(properties["AudioVolume"]!["p-value"]);
        JsonValue lipMorphLevel = Assert.IsAssignableFrom<JsonValue>(properties["LipMorphLevel"]!["p-value"]);
        JsonValue telopOn = Assert.IsAssignableFrom<JsonValue>(properties["TelopOn"]!["p-value"]);
        Assert.True(audioVolume.TryGetValue<double>(out _));
        Assert.True(lipMorphLevel.TryGetValue<double>(out _));
        Assert.True(telopOn.TryGetValue<bool>(out _));
    }

    [Fact]
    public async Task CapabilityMatchesTheActualOutcomeForACreatedProject()
    {
        string project = Path.Combine(root, "capability.ccproj");
        Assert.True((await service.CreateProjectAsync("capability", project, false,
            Array.Empty<ProjectOperationDto>(), default)).Success);

        Assert.True(RecotteProject.Load(project).Capabilities.CanAddTextOnlySpeakerVoice);
        string output = Path.Combine(root, "capability-edited.ccproj");
        Assert.True((await service.ApplyOperationsAndSaveCopyAsync(project, output, false,
            new[] { AddText("能力どおり追加できる", 0m, 1m) }, default)).Success);
    }

    private static ProjectOperationDto AddText(string text, decimal start, decimal end) =>
        new("addTextOnlySpeakerVoice", Layer: new LayerTargetDto(LayerIndex: 1), Text: text, Start: start, End: end);

    private static IEnumerable<JsonObject> SpeakerVoices(string path) => SpeakerVoices(Read(path));

    private static IEnumerable<JsonObject> SpeakerVoices(JsonObject root) =>
        ((JsonArray)root["layers"]!).OfType<JsonObject>()
            .SelectMany(layer => ((JsonArray)layer["layer-objects"]!).OfType<JsonObject>())
            .Where(item => (string?)item["type"] == "Speaker Voice");

    private static JsonObject Read(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    private static void Mutate(string path, Action<JsonObject> change)
    {
        JsonObject root = Read(path);
        change(root);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
