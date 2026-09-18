using System.Text;

namespace Recotte.Core.Tests;

public sealed class CompatibilityAndOverwriteSavingTests
{
    [Fact]
    public void SupportedProfile_ExposesEveryImplementedCapability()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);

        Assert.Equal(CompatibilityLevel.Supported, document.ProjectCompatibility.Level);
        Assert.True(document.ProjectCompatibility.CanRead);
        Assert.True(document.ProjectCompatibility.CanValidate);
        Assert.True(document.ProjectCompatibility.CanEdit);
        Assert.True(document.ProjectCompatibility.CanAddObjects);
        Assert.True(document.ProjectCompatibility.CanSaveCopy);
        Assert.True(document.ProjectCompatibility.CanOverwrite);
    }

    [Theory]
    [InlineData("1.7.1.2", CompatibilityLevel.ReadOnly)]
    [InlineData("9.9.9.9", CompatibilityLevel.Unsupported)]
    public void UnverifiedVersion_DoesNotBorrowSupportedProfile(string version, CompatibilityLevel level)
    {
        using MemoryStream stream = ProjectStream(version);
        RecotteProjectDocument document = RecotteProject.Load(stream);

        Assert.Equal(level, document.Compatibility);
        Assert.False(document.ProjectCompatibility.CanEdit);
        Assert.False(document.ProjectCompatibility.CanSaveCopy);
        Assert.Throws<InvalidOperationException>(() => document.BeginEdit());
    }

    [Fact]
    public void Registry_RequiresAnExactVersionMatch()
    {
        Assert.True(ProjectFormatProfileRegistry.Default.TryGetProfile(RecotteVersion.SupportedVersion, out _));
        Assert.True(ProjectFormatProfileRegistry.Default.TryGetProfile(new RecotteVersion(1, 8, 5, 1), out _));
        Assert.True(ProjectFormatProfileRegistry.Default.TryGetProfile(new RecotteVersion(1, 8, 5, 5), out _));
        Assert.False(ProjectFormatProfileRegistry.Default.TryGetProfile(new RecotteVersion(1, 9, 0, 0), out _));
    }

    [Theory]
    [InlineData("1.7.1.2")]
    [InlineData("9.9.9.9")]
    public void UnverifiedVersion_BatchReturnsStructuredRejection(string version)
    {
        using MemoryStream stream = ProjectStream(version);
        RecotteProjectDocument document = RecotteProject.Load(stream);

        ProjectBatchResult result = document.ApplyOperations(Array.Empty<ProjectOperation>());

        Assert.False(result.Success);
        Assert.False(result.Committed);
        Assert.True(result.RolledBack);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RC6101");
        Assert.False(document.Capabilities.CanEditSpeakerText);
        Assert.False(document.Capabilities.CanSaveCopy);
    }

    [Fact]
    public void Save_CreatesBackupUpdatesStateAndCanRunTwice()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string source = Path.Combine(directory, "source.ccproj");
        try
        {
            File.Copy(TestProjects.EmptyProject, source);
            byte[] original = File.ReadAllBytes(source);
            RecotteProjectDocument document = RecotteProject.Load(source);

            ProjectSaveResult first = document.Save();
            SourceFileState firstState = document.SourceFileState!;
            ProjectSaveResult second = document.Save();

            Assert.NotNull(first.BackupPath);
            Assert.Equal(original, File.ReadAllBytes(first.BackupPath!));
            Assert.NotEqual(first.BackupPath, second.BackupPath);
            Assert.Equal(source, document.SourceFileState!.AbsolutePath);
            Assert.Equal(source, firstState.AbsolutePath);
            Assert.True(File.Exists(second.BackupPath));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Save_RejectsStreamAndExternallyChangedSource()
    {
        using MemoryStream stream = ProjectStream("1.8.5.0");
        Assert.Throws<InvalidOperationException>(() => RecotteProject.Load(stream).Save());

        string directory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string source = Path.Combine(directory, "source.ccproj");
        try
        {
            File.Copy(TestProjects.EmptyProject, source);
            RecotteProjectDocument document = RecotteProject.Load(source);
            DateTime timestamp = File.GetLastWriteTimeUtc(source);
            byte[] changed = File.ReadAllBytes(source);
            changed[^2] = changed[^2] == (byte)' ' ? (byte)'\t' : (byte)' ';
            File.WriteAllBytes(source, changed);
            File.SetLastWriteTimeUtc(source, timestamp);

            SavePreview preview = document.PreviewSave();
            RecotteProjectSaveException exception = Assert.Throws<RecotteProjectSaveException>(() => document.Save());

            Assert.True(preview.ExternalChangeDetected);
            Assert.False(preview.CanSave);
            Assert.Equal(ProjectSaveFailureKind.SourceChanged, exception.Kind);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PreviewSave_DoesNotWriteAndUsesSaveConditions()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string source = Path.Combine(directory, "source.ccproj");
        try
        {
            File.Copy(TestProjects.EmptyProject, source);
            byte[] before = File.ReadAllBytes(source);
            RecotteProjectDocument document = RecotteProject.Load(source);

            SavePreview preview = document.PreviewSave();

            Assert.True(preview.CanSave);
            Assert.NotNull(preview.BackupPath);
            Assert.False(File.Exists(preview.BackupPath));
            Assert.Equal(before, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ActiveEditSession_BlocksEverySave()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string copy = Path.Combine(directory, "copy.ccproj");
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);
            using ProjectEditSession session = document.BeginEdit();
            Assert.False(document.PreviewSave(copy).CanSave);
            Assert.Throws<RecotteProjectSaveException>(() => document.SaveCopy(copy));
            Assert.Throws<RecotteProjectSaveException>(() => document.Save());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CommittedEdit_SavePersistsWhileBackupRetainsOriginal()
    {
        string directory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string source = Path.Combine(directory, "source.ccproj");
        string oneText = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "002OneText", "002OneText.ccproj");
        try
        {
            File.Copy(oneText, source);
            RecotteProjectDocument document = RecotteProject.Load(source);
            using (ProjectEditSession session = document.BeginEdit())
            {
                Assert.True(session.Editor.UpdateSpeakerText(new(new TimelineObjectId(1, 1000), "saved text")).Success);
                Assert.True(session.Commit().Success);
            }

            ProjectSaveResult result = document.Save();

            Assert.Equal("saved text", RecotteProject.Load(source).Layers[1].Objects.Single().Text);
            Assert.Equal("sample text", RecotteProject.Load(result.BackupPath!).Layers[1].Objects.Single().Text);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static MemoryStream ProjectStream(string version) => new(Encoding.UTF8.GetBytes($$"""
        { "app_version": "{{version}}", "setting": {}, "speakers": [], "file-items": [], "layers": [] }
        """));
}
