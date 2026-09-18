using Recotte.McpServer;
using Recotte.Core;

namespace Recotte.McpServer.Tests;

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
    public async Task SameOutputPathIsSerialized()
    {
        OutputPathLockManager manager = new();
        await using IAsyncDisposable first = await manager.AcquireAsync("output.ccproj", default);
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await manager.AcquireAsync("output.ccproj", timeout.Token));
    }
}
