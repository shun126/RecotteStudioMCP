namespace Recotte.Core.Tests;

public sealed class AnnotationTextTests
{
    private static string AnnotationSample => Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects",
        "010OneAnnotationText", "010OneAnnotationText.ccproj");

    [Fact]
    public void AddAnnotationText_ClonesVerifiedFigureAndRegistersStyle()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        using ProjectEditSession session = document.BeginEdit();

        EditResult staged = session.Editor.AddAnnotationText(new(2, "編集メモ：ここからBGM",
            new(1m), new(6m)));

        Assert.True(staged.Success);
        Assert.Equal(new[] { "RegisterAnnotationTextStyle", "AddAnnotationText" },
            staged.Changes.Select(change => change.Operation));
        Assert.True(session.Commit().Success);
        TimelineObjectView note = Assert.Single(document.Layers[2].Objects);
        Assert.Equal("Figure", note.Type);
        Assert.Equal("編集メモ：ここからBGM", note.Text);
        Assert.Equal(1m, note.StartTime);
        Assert.Equal(6m, note.EndTime);
        Assert.True(document.Validate().IsValid);
    }

    [Fact]
    public void AddAnnotationText_BatchReturnsCreatedObjectAndReusesStyle()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        ProjectBatchResult result = document.ApplyOperations(new ProjectOperation[]
        {
            new AddAnnotationTextOperation(new LayerIndexReference(2), "画像を切り替える", new(0m), new(2m)),
            new AddAnnotationTextOperation(new LayerIndexReference(2), "効果音候補", new(2m), new(4m)),
        });

        Assert.True(result.Success);
        Assert.All(result.OperationResults, item => Assert.NotNull(item.CreatedObjectId));
        Assert.Equal(2, document.Layers[2].Objects.Count);
        Assert.Equal(2, result.OperationResults[0].Changes.Count);
        Assert.Single(result.OperationResults[1].Changes);
        Assert.Equal("AddAnnotationText", result.OperationResults[1].Changes[0].Operation);
    }

    [Fact]
    public void AddAnnotationText_AcceptsAndSaves1855Sample()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteAnnotationTests-").FullName;
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(AnnotationSample);
            Assert.Equal(CompatibilityLevel.Supported, document.Compatibility);
            ProjectBatchResult batch = document.ApplyOperations(new[]
            {
                new AddAnnotationTextOperation(new LayerIndexReference(2), "追加メモ", new(1m), new(3m)),
            });
            Assert.True(batch.Success);

            string output = Path.Combine(directory, "annotation.ccproj");
            document.SaveCopy(output);
            RecotteProjectDocument loaded = RecotteProject.Load(output);
            Assert.Equal(2, loaded.Layers[2].Objects.Count);
            Assert.Contains(loaded.Layers[2].Objects, item => item.Text == "追加メモ");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1, "note", 0, 1, "RC4703")]
    [InlineData(2, " ", 0, 1, "RC4701")]
    [InlineData(2, "note", 1, 1, "RC4702")]
    public void AddAnnotationText_RejectsInvalidInput(int layer, string text, decimal start, decimal end, string code)
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
        using ProjectEditSession session = document.BeginEdit();

        EditResult result = session.Editor.AddAnnotationText(new(layer, text, new(start), new(end)));

        Assert.False(result.Success);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(session.Changes);
    }
}
