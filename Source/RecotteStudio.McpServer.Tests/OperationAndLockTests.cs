using RecotteStudio.McpServer;
using RecotteStudio.Core;

namespace RecotteStudio.McpServer.Tests;

public sealed class OperationAndLockTests
{
    [Fact]
    public void MapperRejectsUnknownAndConflictingReferences()
    {
        ProjectOperationMapper mapper = new();
        Assert.False(mapper.Map(new[] { new ProjectOperationDto("invented") }).Success);
        Assert.False(mapper.Map(new[] { new ProjectOperationDto("removeTimelineObject",
            Target: new(1, 2, "other")) }).Success);
    }

    [Fact]
    public void MapperMapsAnnotationText()
    {
        OperationMapResult result = new ProjectOperationMapper().Map(new[]
        {
            new ProjectOperationDto("addAnnotationText", Layer: new(LayerIndex: 2),
                Text: "編集メモ：BGM候補", Start: 3m, End: 8m),
        });

        Assert.True(result.Success);
        AddAnnotationTextOperation operation = Assert.IsType<AddAnnotationTextOperation>(Assert.Single(result.Operations));
        Assert.Equal("編集メモ：BGM候補", operation.Text);
        Assert.Equal(3m, operation.StartTime.TotalSeconds);
        Assert.Equal(8m, operation.EndTime.TotalSeconds);
    }

    [Fact]
    public void MapperMapsVideoLayerWithOptionalName()
    {
        OperationMapResult result = new ProjectOperationMapper().Map(new[]
        {
            new ProjectOperationDto("addVideoLayer"),
            new ProjectOperationDto("addVideoLayer", LayerName: "ワイプ"),
        });

        Assert.True(result.Success);
        Assert.Equal(new string?[] { null, "ワイプ" },
            result.Operations.Select(operation => Assert.IsType<AddVideoLayerOperation>(operation).Name));
    }

    [Fact]
    public void MapperMapsAnnotationLayerWithOptionalName()
    {
        OperationMapResult result = new ProjectOperationMapper().Map(new[]
        {
            new ProjectOperationDto("addAnnotationLayer"),
            new ProjectOperationDto("addAnnotationLayer", LayerName: "前景"),
        });

        Assert.True(result.Success);
        Assert.Equal(new string?[] { null, "前景" },
            result.Operations.Select(operation => Assert.IsType<AddAnnotationLayerOperation>(operation).Name));
    }

    [Fact]
    public void MapperMapsObjectBoundsWithOmittedValues()
    {
        OperationMapResult result = new ProjectOperationMapper().Map(new[]
        {
            new ProjectOperationDto("setObjectBounds", Target: new(LayerIndex: 2, ObjectKey: 1000), X: 10m, Width: 320m),
        });

        Assert.True(result.Success);
        Assert.Equal(new SetObjectBoundsOperation(new TimelineObjectIdReference(new(2, 1000)), 10m, null, 320m, null),
            Assert.Single(result.Operations));
    }

    [Fact]
    public void MapperMapsSpeakerActionAndRequiresTheAction()
    {
        ProjectOperationMapper mapper = new();
        OperationMapResult result = mapper.Map(new[]
        {
            new ProjectOperationDto("addSpeakerAction", Layer: new(Name: "話者1"), Action: "smile", Start: 1m, End: 2m),
        });

        Assert.True(result.Success);
        Assert.Equal(new AddSpeakerActionOperation(new LayerNameReference("話者1"), "smile", new(1m), new(2m)),
            Assert.Single(result.Operations));
        Assert.Equal("MCP_OPERATION_INVALID", mapper.Map(new[]
            { new ProjectOperationDto("addSpeakerAction", Layer: new(Name: "話者1"), Start: 1m, End: 2m) }).ErrorCode);
    }

    [Fact]
    public void MapperMapsAppearanceAndMoveActions()
    {
        ProjectOperationMapper mapper = new();
        OperationMapResult result = mapper.Map(new[]
        {
            new ProjectOperationDto("setObjectAppearance", Target: new(LayerIndex: 1, ObjectKey: 1000), Opacity: 0.5m, Flip: "Horizontal",
                Brightness: -30m, DefaultAction: "smile"),
            new ProjectOperationDto("moveTimelineObject", Target: new(LayerIndex: 1, ObjectKey: 1002), Start: 1m, End: 2m, MoveActions: false),
            new ProjectOperationDto("moveTimelineObject", Target: new(LayerIndex: 1, ObjectKey: 1002), Start: 1m, End: 2m),
        });

        Assert.True(result.Success, result.Message);
        Assert.Equal(new ProjectOperation[]
        {
            new SetObjectAppearanceOperation(new TimelineObjectIdReference(new(1, 1000)), Opacity: 0.5m, Flip: ObjectFlip.Horizontal,
                Brightness: -30, DefaultAction: "smile"),
            new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1002)), new(1m), new(2m), MoveActions: false),
            new MoveTimelineObjectOperation(new TimelineObjectIdReference(new(1, 1002)), new(1m), new(2m)),
        }, result.Operations);
        Assert.Equal("MCP_OPERATION_INVALID", mapper.Map(new[]
            { new ProjectOperationDto("setObjectAppearance", Target: new(LayerIndex: 1, ObjectKey: 1000), Flip: "left") }).ErrorCode);
        Assert.Equal("MCP_OPERATION_INVALID", mapper.Map(new[]
            { new ProjectOperationDto("setObjectAppearance", Target: new(LayerIndex: 1, ObjectKey: 1000), Brightness: 1.5m) }).ErrorCode);
    }

    [Fact]
    public void MapperMapsSpeakerActionUpdatesAndRequiresAChange()
    {
        ProjectOperationMapper mapper = new();
        OperationMapResult result = mapper.Map(new[]
        {
            new ProjectOperationDto("updateSpeakerAction", Target: new(LayerIndex: 1, ObjectKey: 1004), Action: "smile"),
            new ProjectOperationDto("updateSpeakerAction", Target: new(LayerIndex: 1, ObjectKey: 1004), Loop: true),
        });

        Assert.True(result.Success);
        Assert.Equal(new ProjectOperation[]
        {
            new UpdateSpeakerActionOperation(new TimelineObjectIdReference(new(1, 1004)), "smile"),
            new UpdateSpeakerActionOperation(new TimelineObjectIdReference(new(1, 1004)), Loop: true),
        }, result.Operations);
        Assert.Equal("MCP_OPERATION_INVALID", mapper.Map(new[]
            { new ProjectOperationDto("updateSpeakerAction", Target: new(LayerIndex: 1, ObjectKey: 1004)) }).ErrorCode);
    }

    [Fact]
    public async Task SameOutputPathIsSerialized()
    {
        OutputPathLockManager manager = new();
        await using IAsyncDisposable first = await manager.AcquireAsync("output.ccproj", default);
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await manager.AcquireAsync("output.ccproj", timeout.Token));
    }
}
