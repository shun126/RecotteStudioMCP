using RecotteStudio.Core;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class CharacterEditingToolTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("recotte-character-tools-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task CharacterTimePositionAndSizeCanBePreviewedAndSavedTogether()
    {
        string input = Path.Combine(root, "input.ccproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OneCharacter.ccproj"), input);
        byte[] original = File.ReadAllBytes(input);
        RecotteToolService service = new(new(new(root)), new(), new());
        TimelineEntryView character = Assert.Single(RecotteProject.Load(input).GetTimelineEntries(),
            entry => entry.ObjectType == "Speaker Character");
        Assert.True(character.Capabilities.CanMove);
        Assert.True(character.Capabilities.CanSetBounds);
        TimelineObjectId id = character.ObjectId!.Value;
        var target = new TimelineObjectTargetDto(LayerIndex: id.LayerIndex, ObjectKey: id.ObjectKey);
        ProjectOperationDto[] operations = {
            new("moveTimelineObject", Target: target, Start: 2m, End: 8m),
            new("setObjectBounds", Target: target, X: 100m, Y: 50m, Width: 400m, Height: 800m),
        };

        Assert.True(service.PreviewOperations(input, operations).Success);
        Assert.Equal(original, File.ReadAllBytes(input));
        string output = Path.Combine(root, "edited.ccproj");
        var result = await service.ApplyOperationsAndSaveCopyAsync(input, output, false, operations, default);

        Assert.True(result.Success, result.Message);
        RecotteProjectDocument saved = RecotteProject.Load(output);
        TimelineEntryView edited = Assert.Single(saved.GetTimelineEntries(), entry => entry.ObjectId == id);
        Assert.Equal(2m, edited.StartTime!.Value.TotalSeconds);
        Assert.Equal(8m, edited.EndTime!.Value.TotalSeconds);
        Assert.Equal(new ObjectBounds(100m, 50m, 400m, 800m), edited.Bounds);
        Assert.True(saved.Validate().IsValid);
        Assert.Equal(original, File.ReadAllBytes(input));
    }
}
