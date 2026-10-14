using System.Text;

namespace RecotteStudio.Core.Tests;

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

    [Theory]
    [InlineData("LowerFrame", true, 0, 540, 540, 0, 2, true)]
    [InlineData("UpperFrame", true, 0, 0, 540, 0, 2, true)]
    [InlineData("LowerFrame", true, 0, 0, 1080, 0, 2, false)] // a full-screen background is never reported
    [InlineData("LowerFrame", true, -10, -10, 1100, 0, 2, false)] // still fills the screen
    [InlineData("LowerFrame", true, 0, 0, 800, 0, 2, false)] // ends exactly where the lower frame begins
    [InlineData("UpperFrame", true, 0, 280, 800, 0, 2, false)] // starts exactly where the upper frame ends
    [InlineData("LowerFrame", true, 0, 540, 540, 1, 2, false)] // on screen only after the line has ended
    [InlineData("LowerFrame", false, 0, 540, 540, 0, 2, false)] // the telop is switched off
    [InlineData("Missing", true, 0, 540, 540, 0, 2, false)] // the frame is not defined in the project
    [InlineData("LowerFrame", true, 0, 540, 540, 0, 2, false, false)] // the Speaker layer is hidden
    [InlineData("LowerFrame", true, 0, 540, 540, 0, 2, false, true, false)] // the image layer is hidden
    public void Validate_ImageUnderDialogueTelop_WarnsOnlyWhenCovered(string telopFrame, bool telopOn, int imageX,
        int imageY, int imageHeight, int imageStart, int imageEnd, bool expected, bool speakerVisible = true, bool imageVisible = true)
    {
        string json = $$"""
            {
              "app_version": "1.8.5.0",
              "setting": { "size": [1920, 1080] },
              "speakers": [],
              "file-items": [],
              "telop-frames": [
                "properties",
                { "FrameName": { "p-value": "LowerFrame" }, "Bounds": { "p-value": [80.0, 800.0, 1760.0, 200.0] } },
                "properties",
                { "FrameName": { "p-value": "UpperFrame" }, "Bounds": { "p-value": [80.0, 80.0, 1760.0, 200.0] } }
              ],
              "layers": [{
                "type": "Speaker",{{(speakerVisible ? "" : " \"visible\": false,")}}
                "layer-objects": [{
                  "type": "Speaker Voice",
                  "objkey": 1000,
                  "start-time": 0,
                  "end-time": 1,
                  "properties": {
                    "TelopOn": { "p-value": {{(telopOn ? "true" : "false")}} },
                    "TelopFrame": { "p-value": "{{telopFrame}}" }
                  }
                }]
              }, {
                "type": "Annot",{{(imageVisible ? "" : " \"visible\": false,")}}
                "layer-objects": [{
                  "type": "Image",
                  "objkey": 1001,
                  "name": "Background.png",
                  "start-time": {{imageStart}},
                  "end-time": {{imageEnd}},
                  "properties": { "Bounds": { "p-value": [{{imageX}}, {{imageY}}, 1940, {{imageHeight}}] } }
                }]
              }]
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        ProjectValidationResult result = RecotteProject.Load(stream).Validate();

        Assert.True(result.IsValid);
        ProjectDiagnostic[] warnings = result.Diagnostics.Where(item => item.Code == "RC2401").ToArray();
        if (!expected)
        {
            Assert.Empty(warnings);
            return;
        }
        ProjectDiagnostic warning = Assert.Single(warnings);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("$.layers[1].layer-objects[0]", warning.JsonPath);
        Assert.Contains(telopFrame, warning.Message);
    }

    [Fact]
    public void TelopFrames_SampleProject_ExposesFrameBoundsAndSpeakerLayerFrame()
    {
        RecotteProjectDocument document = RecotteProject.Load(TestProjects.EmptyProject);

        Assert.Equal(new TelopFrameView("LowerFrame", "下枠", 80m, 800m, 1760m, 200m),
            document.TelopFrames.Single(frame => frame.Name == "LowerFrame"));
        Assert.Equal(new TelopFrameView("UpperFrame", "上枠", 80m, 80m, 1760m, 200m),
            document.TelopFrames.Single(frame => frame.Name == "UpperFrame"));
        Assert.Equal("LowerFrame", document.Layers.OfType<SpeakerLayerView>().First().TelopFrame);
    }
}
