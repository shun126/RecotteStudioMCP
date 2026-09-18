using Recotte.Core;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recotte.McpServer.Tests;

/// <summary>Covers adding a text-only Speaker Voice to a Speaker layer that holds no clonable voice yet.</summary>
public sealed class TextVoiceTemplateTests : IDisposable
{
    private const string TextTemplateResource = "Recotte.Core.Templates.Text.ccproj";

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
