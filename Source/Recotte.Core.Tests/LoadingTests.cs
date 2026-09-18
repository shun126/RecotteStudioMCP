using System.Text;

namespace Recotte.Core.Tests;

public sealed class LoadingTests
{
    [Theory]
    [MemberData(nameof(TestProjects.ValidProjects), MemberType = typeof(TestProjects))]
    public void Load_ValidProject_ExposesKnownStructure(string projectPath)
    {
        RecotteProjectDocument document = RecotteProject.Load(projectPath);

        Assert.NotNull(document.ApplicationVersion);
        Assert.NotNull(document.Settings);
        Assert.NotEmpty(document.Layers);
    }

    [Fact]
    public void TryLoad_InvalidJson_ReturnsDiagnostic()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("{ invalid"));

        ProjectLoadResult result = RecotteProject.TryLoad(stream);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC1002" && item.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("1.8.5.0", true, 1, 8, 5, 0)]
    [InlineData("1.8.5", false, 0, 0, 0, 0)]
    [InlineData("unknown", false, 0, 0, 0, 0)]
    public void RecotteVersion_TryParse_RequiresFourNumericParts(string input, bool expected, int major, int minor, int build, int revision)
    {
        bool parsed = RecotteVersion.TryParse(input, out RecotteVersion version);

        Assert.Equal(expected, parsed);
        if (expected)
        {
            Assert.Equal(new RecotteVersion(major, minor, build, revision), version);
        }
    }
}
