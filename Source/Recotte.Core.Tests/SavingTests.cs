using System.Text.Json.Nodes;

namespace Recotte.Core.Tests;

[Collection("CurrentDirectory")]
public sealed class SavingTests
{
    [Theory]
    [MemberData(nameof(TestProjects.SupportedProjects), MemberType = typeof(TestProjects))]
    public void SaveCopy_ValidProject_PreservesJsonMeaning(string projectPath)
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string outputPath = Path.Combine(temporaryDirectory, Path.GetFileName(projectPath));
        try
        {
            RecotteProjectDocument document = RecotteProject.Load(projectPath);

            document.SaveCopy(outputPath);

            JsonNode? expected = JsonNode.Parse(File.ReadAllText(projectPath));
            JsonNode? actual = JsonNode.Parse(File.ReadAllText(outputPath));
            Assert.True(JsonNode.DeepEquals(expected, actual));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public void SaveCopy_WritesUtf8WithoutBomAndCrLf()
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string outputPath = Path.Combine(temporaryDirectory, "output.ccproj");
        try
        {
            RecotteProject.Load(TestProjects.EmptyProject).SaveCopy(outputPath);

            byte[] bytes = File.ReadAllBytes(outputPath);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            string text = File.ReadAllText(outputPath);
            Assert.Contains("\r\n", text);
            Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public void SaveCopy_ExistingDestination_RequiresExplicitOverwrite()
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string outputPath = Path.Combine(temporaryDirectory, "output.ccproj");
        try
        {
            File.WriteAllText(outputPath, "original");
            RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);

            Assert.ThrowsAny<IOException>(() => document.SaveCopy(outputPath));
            Assert.Equal("original", File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public void SaveCopy_UnknownProperties_ArePreserved()
    {
        const string json = """
            {
              "app_version": "1.8.5.0",
              "setting": { "future-setting": { "enabled": true } },
              "speakers": [],
              "file-items": [],
              "layers": [],
              "future-root": [1, "two", false]
            }
            """;
        string temporaryDirectory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string outputPath = Path.Combine(temporaryDirectory, "output.ccproj");
        try
        {
            using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
            RecotteProject.Load(stream).SaveCopy(outputPath);

            JsonNode? saved = JsonNode.Parse(File.ReadAllText(outputPath));
            Assert.True(saved?["setting"]?["future-setting"]?["enabled"]?.GetValue<bool>() ?? false);
            Assert.Equal("two", saved?["future-root"]?[1]?.GetValue<string>());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Fact]
    public void SaveCopy_RelativeSource_RemainsProtectedAfterCurrentDirectoryChanges()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Directory.CreateTempSubdirectory("RecotteCoreTests-").FullName;
        string sourcePath = Path.Combine(temporaryDirectory, "source.ccproj");
        try
        {
            File.Copy(TestProjects.EmptyProject, sourcePath);
            byte[] originalContents = File.ReadAllBytes(sourcePath);
            Environment.CurrentDirectory = temporaryDirectory;
            RecotteProjectDocument document = RecotteProject.Load("source.ccproj");

            Environment.CurrentDirectory = TestProjects.RepositoryRoot;

            Assert.Equal(sourcePath, document.SourcePath);
            Assert.ThrowsAny<IOException>(() => document.SaveCopy(sourcePath, new ProjectSaveOptions
            {
                AllowOverwrite = true,
            }));
            Assert.Equal(originalContents, File.ReadAllBytes(sourcePath));
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
