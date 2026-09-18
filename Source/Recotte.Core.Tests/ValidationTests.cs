using System.Text;

namespace Recotte.Core.Tests;

public sealed class ValidationTests
{
    [Theory]
    [MemberData(nameof(TestProjects.ValidProjects), MemberType = typeof(TestProjects))]
    public void Validate_ValidProject_HasNoErrors(string projectPath)
    {
        RecotteProjectDocument document = RecotteProject.Load(projectPath);

        ProjectValidationResult result = document.Validate();

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_MissingFileItem_ReturnsReferenceError()
    {
        const string json = """
            {
              "app_version": "1.8.5.0",
              "setting": {},
              "speakers": [],
              "file-items": [],
              "layers": [{
                "type": "Annot",
                "layer-objects": [{
                  "type": "Image",
                  "objkey": 1000,
                  "start-time": 0,
                  "end-time": 1,
                  "properties": { "File": { "p-value": "missing" } }
                }]
              }]
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
        RecotteProjectDocument document = RecotteProject.Load(stream);

        ProjectValidationResult result = document.Validate();

        Assert.Contains(result.Diagnostics, item => item.Code == "RC2101" && item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_ReversedTimeRange_ReturnsError()
    {
        const string json = """
            {
              "app_version": "1.8.5.0",
              "setting": {},
              "speakers": [],
              "file-items": [],
              "layers": [{
                "type": "Speaker",
                "layer-objects": [{
                  "type": "Speaker Voice",
                  "objkey": 1000,
                  "start-time": 2,
                  "end-time": 1,
                  "properties": {}
                }]
              }]
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        ProjectValidationResult result = RecotteProject.Load(stream).Validate();

        Assert.Contains(result.Diagnostics, item => item.Code == "RC2201");
    }
}
