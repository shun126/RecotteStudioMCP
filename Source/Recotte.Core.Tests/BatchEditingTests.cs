namespace Recotte.Core.Tests;

public sealed class BatchEditingTests
{
    private static string OneText => Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "002OneText", "002OneText.ccproj");

    [Fact]
    public void RemoveTimelineObject_RejectsAmbiguousProjectWideObjectKey()
    {
        string fullProject = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects",
            "009FullProject", "009FullProject.ccproj");
        RecotteProjectDocument document = RecotteProject.Load(fullProject);

        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new RemoveTimelineObjectOperation(new ObjectKeyReference(1000)),
        });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC6002");
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public void ApplyOperations_CommitsAllOperationsInOrder()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        ProjectOperation[] operations =
        {
            new UpdateSpeakerTextOperation(new TimelineObjectIdReference(new(1, 1000)), "changed"),
            new MoveTimelineObjectOperation(new ObjectKeyReference(1000), new(2m), new(4m)),
        };

        ProjectBatchResult result = document.ApplyOperations(operations);

        Assert.True(result.Success);
        Assert.True(result.Committed);
        Assert.False(result.CanCommit);
        Assert.False(result.RolledBack);
        Assert.Equal(2, result.AppliedOperationCount);
        Assert.Equal(new[] { ProjectOperationKind.UpdateSpeakerText, ProjectOperationKind.MoveTimelineObject },
            result.OperationResults.Select(item => item.OperationType));
        Assert.Equal(5, result.Changes.Count);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(1, document.Revision);
        Assert.Equal("changed", document.Layers[1].Objects.Single().Text);
        Assert.Equal(2m, document.Layers[1].Objects.Single().StartTime);
    }

    [Fact]
    public void ApplyOperations_FailureStopsAndRollsBackEverything()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        ProjectOperation[] operations =
        {
            new UpdateSpeakerTextOperation(new ObjectKeyReference(1000), "discarded"),
            new MoveTimelineObjectOperation(new ObjectKeyReference(999999), new(0m), new(1m)),
            new RemoveTimelineObjectOperation(new ObjectKeyReference(1000)),
        };

        ProjectBatchResult result = document.ApplyOperations(operations);

        Assert.False(result.Success);
        Assert.Equal(1, result.AppliedOperationCount);
        Assert.Equal(1, result.FailedOperationIndex);
        Assert.Equal(2, result.OperationResults.Count);
        Assert.True(result.RolledBack);
        Assert.Empty(result.Changes);
        Assert.Equal(0, document.Revision);
        Assert.Equal("sample text", document.Layers[1].Objects.Single().Text);
    }

    [Fact]
    public void SessionBatch_FailureMakesCommitUnavailableAndReleasesLockOnDispose()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        using (ProjectEditSession edit = document.BeginEdit())
        {
            ProjectBatchResult result = edit.ApplyOperations(new ProjectOperation[]
            {
                new RemoveTimelineObjectOperation(new ObjectKeyReference(404)),
            });
            Assert.False(result.Success);
            Assert.False(edit.Commit().Success);
        }

        using ProjectEditSession next = document.BeginEdit();
    }

    [Fact]
    public void PreviewOperations_IsNonMutatingAndMatchesExecutionShape()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        ProjectOperation[] operations =
        {
            new UpdateSpeakerTextOperation(new ObjectKeyReference(1000), "previewed"),
            new MoveTimelineObjectOperation(new ObjectKeyReference(1000), new(1m), new(3m)),
        };

        ProjectBatchPreview preview = document.PreviewOperations(operations);

        Assert.True(preview.BatchResult.Success);
        Assert.True(preview.BatchResult.RolledBack);
        Assert.Equal(0, document.Revision);
        Assert.Equal("sample text", document.Layers[1].Objects.Single().Text);
        ProjectBatchResult applied = document.ApplyOperations(operations);
        Assert.Equal(preview.BatchResult.OperationResults.Select(item => item.OperationType),
            applied.OperationResults.Select(item => item.OperationType));
        Assert.Equal(preview.BatchResult.Changes.Select(item => item.Operation), applied.Changes.Select(item => item.Operation));
    }

    [Fact]
    public void AssetOperations_ReuseVerifiedEditorsInsideOneBatch()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteCoreBatchAssets-").FullName;
        try
        {
            string output = Path.Combine(directory, "assets.ccproj");
            string image = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "004OneImage", "Sample.png");
            string video = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "005OneVideo", "Sample.mp4");
            RecotteProjectDocument document = RecotteProject.Create(new("Assets", directory));

            ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
            {
                new AddImageAssetOperation(new LayerIndexReference(2), image, output, "image", new(0m), new(1m)),
                new AddVideoAssetOperation(new LayerIndexReference(0), video, output, "video", new(1m), new(3m)),
            }, new() { FileItemKeyAllocator = new CountingFileKeyAllocator() });

            Assert.True(result.Success);
            Assert.Equal(2, document.FileItems.Count);
            Assert.Equal(new[] { "Image", "Video" }, result.OperationResults.Select(item => item.CreatedObjectId)
                .Select(id => document.Layers[id!.Value.LayerIndex].Objects.Single(item => item.ObjectKey == id.Value.ObjectKey).Type));
            Assert.All(result.OperationResults, item => Assert.NotNull(item.CreatedFileItemKey));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class CountingFileKeyAllocator : IFileItemKeyAllocator
    {
        public string Allocate(IReadOnlyCollection<string> existingKeys) => new((char)('a' + existingKeys.Count), 64);
    }
}
