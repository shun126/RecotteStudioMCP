using System.Text;

namespace Recotte.Core.Tests;

public sealed class BatchSavingTests
{
    private static string OneText => Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "002OneText", "002OneText.ccproj");

    [Fact]
    public void ApplyOperationsAndSaveCopy_ReturnsBothResultsAndPreservesSource()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceCopy = Path.Combine(directory, "source.ccproj");
            string output = Path.Combine(directory, "output.ccproj");
            File.Copy(OneText, sourceCopy);
            string original = File.ReadAllText(sourceCopy);
            RecotteProjectDocument document = RecotteProject.Load(sourceCopy);

            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output, new ProjectOperation[]
            {
                new UpdateSpeakerTextOperation(new ObjectKeyReference(1000), "saved"),
            });

            Assert.True(result.Success);
            Assert.True(result.BatchResult.Committed);
            Assert.NotNull(result.SaveResult);
            Assert.True(File.Exists(output));
            Assert.Equal(original, File.ReadAllText(sourceCopy));
            RecotteProjectDocument loaded = RecotteProject.Load(output);
            Assert.Equal("saved", loaded.Layers[1].Objects.Single().Text);
            Assert.True(loaded.Validate().IsValid);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ApplyOperationsAndSaveCopy_DoesNotWriteOnEditFailureOrExistingDestination()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string failedOutput = Path.Combine(directory, "failed.ccproj");
            RecotteProjectDocument failedDocument = RecotteProject.Load(OneText);
            ProjectBatchSaveResult editFailure = failedDocument.ApplyOperationsAndSaveCopy(failedOutput, new ProjectOperation[]
            {
                new RemoveTimelineObjectOperation(new ObjectKeyReference(404)),
            });
            Assert.False(editFailure.Success);
            Assert.False(File.Exists(failedOutput));

            string existing = Path.Combine(directory, "existing.ccproj");
            File.WriteAllText(existing, "keep");
            RecotteProjectDocument blockedDocument = RecotteProject.Load(OneText);
            ProjectBatchSaveResult saveFailure = blockedDocument.ApplyOperationsAndSaveCopy(existing, Array.Empty<ProjectOperation>());
            Assert.False(saveFailure.Success);
            Assert.Equal(ProjectSaveFailureKind.InvalidPath, saveFailure.SaveFailureKind);
            Assert.Equal("keep", File.ReadAllText(existing));
            Assert.Equal(0, blockedDocument.Revision);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PreviewOperationsAndSaveCopy_DoesNotWriteOrRetainEditLock()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string output = Path.Combine(directory, "preview.ccproj");
            RecotteProjectDocument document = RecotteProject.Load(OneText);
            ProjectBatchSavePreview preview = document.PreviewOperationsAndSaveCopy(output, new ProjectOperation[]
            {
                new UpdateSpeakerTextOperation(new ObjectKeyReference(1000), "preview"),
            });

            Assert.True(preview.CanSave);
            Assert.NotNull(preview.SavePreview);
            Assert.False(File.Exists(output));
            Assert.Equal(0, document.Revision);
            Assert.Equal("sample text", document.Layers[1].Objects.Single().Text);
            using ProjectEditSession next = document.BeginEdit();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BatchSave_PreservesUnknownJsonFields()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string json = File.ReadAllText(OneText).TrimEnd();
            json = json[..^1] + ",\"future-batch-field\":{\"value\":42}}";
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
            RecotteProjectDocument document = RecotteProject.Load(stream);
            string output = Path.Combine(directory, "unknown.ccproj");

            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output, new ProjectOperation[]
            {
                new UpdateSpeakerTextOperation(new ObjectKeyReference(1000), "unknown-preserved"),
            });

            Assert.True(result.Success);
            Assert.Contains("\"future-batch-field\"", File.ReadAllText(output));
            Assert.Equal("unknown-preserved", RecotteProject.Load(output).Layers[1].Objects.Single().Text);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BatchSave_DoesNotWriteInvalidProject()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string json = File.ReadAllText(OneText).Replace("\"end-time\": 1.95", "\"end-time\": -1.0", StringComparison.Ordinal);
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
            RecotteProjectDocument document = RecotteProject.Load(stream);
            string output = Path.Combine(directory, "invalid.ccproj");

            ProjectBatchSaveResult result = document.ApplyOperationsAndSaveCopy(output, Array.Empty<ProjectOperation>());

            Assert.False(result.Success);
            Assert.Equal(ProjectSaveFailureKind.ValidationFailed, result.SaveFailureKind);
            Assert.False(File.Exists(output));
            Assert.Equal(0, document.Revision);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Recotte.Core.BatchTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
