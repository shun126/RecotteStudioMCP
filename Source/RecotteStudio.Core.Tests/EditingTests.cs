namespace RecotteStudio.Core.Tests;

public sealed class EditingTests
{
    private static string OneText => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects", "002OneText", "002OneText.ccproj");

    [Fact]
    public void Commit_PublishesTextAndIncrementsRevision()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        using ProjectEditSession session = document.BeginEdit();

        EditResult staged = session.Editor.UpdateSpeakerText(new(new TimelineObjectId(1, 1000), "changed"));
        EditResult committed = session.Commit();

        Assert.True(staged.Success);
        Assert.True(committed.Success);
        Assert.Equal(1, document.Revision);
        SpeakerVoiceView voice = Assert.IsType<SpeakerVoiceView>(document.Layers[1].Objects.Single());
        Assert.Equal("changed", voice.Name);
        Assert.Equal("changed", voice.Text);
        Assert.Contains(session.Changes, change => change.Before as string == "sample text" && change.After as string == "changed" && change.JsonPath.EndsWith(".text.text"));
    }

    [Fact]
    public void DisposeWithoutCommit_RollsBackAndReleasesSessionLock()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        using (ProjectEditSession session = document.BeginEdit())
        {
            Assert.True(session.Editor.UpdateSpeakerText(new(new TimelineObjectId(1, 1000), "discarded")).Success);
            Assert.Throws<InvalidOperationException>(() => document.BeginEdit());
        }

        Assert.Equal("sample text", document.Layers[1].Objects.Single().Text);
        using ProjectEditSession next = document.BeginEdit();
    }

    [Fact]
    public void AddAndRemove_TextOnlyVoice_PreserveTransactionIsolation()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        using ProjectEditSession session = document.BeginEdit();

        EditResult added = session.Editor.AddTextOnlySpeakerVoice(new(new SpeakerLayerId(1), "second", new ProjectTime(2m), new ProjectTime(3m)));

        Assert.True(added.Success);
        Assert.Single(document.Layers[1].Objects);
        Assert.Equal(2, session.Editor.SpeakerVoices.Count);
        Assert.True(session.Commit().Success);
        Assert.Equal(2, document.Layers[1].Objects.Count);
        Assert.Equal(2, document.Layers[1].Objects.Select(item => item.ObjectKey).Distinct().Count());
    }

    [Fact]
    public void VoiceWithoutHashOrAudioMarkers_IsNotReportedAsAudioBacked()
    {
        string json = File.ReadAllText(OneText);
        int at = json.IndexOf("\"voice-hash\"", StringComparison.Ordinal);
        Assert.True(at >= 0);
        // Rename the member so the voice has no voice-hash at all; the validator does not require it.
        RecotteProjectDocument document = RecotteProject.Load(new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(json.Remove(at, "\"voice-hash\"".Length).Insert(at, "\"x-voice-hash\""))));

        Assert.True(document.Validate().IsValid);
        Assert.False(document.GetTimelineEntries().Single(item => item.ObjectType == "Speaker Voice").HasAudio);
        using ProjectEditSession session = document.BeginEdit();
        EditResult result = session.Editor.UpdateSpeakerText(new(new TimelineObjectId(1, 1000), "changed"));
        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AudioBackedVoice_TextUpdateKeepsAudioAndReportsRegeneration()
    {
        string path = Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects", "003OneVoice", "003OneVoice.ccproj");
        RecotteProjectDocument document = RecotteProject.Load(path);
        using ProjectEditSession session = document.BeginEdit();

        SpeakerVoiceView before = Assert.IsType<SpeakerVoiceView>(document.Layers[1].Objects.Single());
        string? fileBefore = before.FileItemKey;

        EditResult result = session.Editor.UpdateSpeakerText(new(new TimelineObjectId(1, 1000), "changed"));

        Assert.True(result.Success);
        ProjectDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal("RC4208", warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.True(session.Commit().Success);
        SpeakerVoiceView after = Assert.IsType<SpeakerVoiceView>(document.Layers[1].Objects.Single());
        Assert.Equal("changed", after.Name);
        Assert.Equal("changed", after.Text);
        Assert.Equal(fileBefore, after.FileItemKey);
        Assert.NotEmpty(after.FileItemKey!);
        TimelineEntryView entry = document.GetTimelineEntries().Single(item => item.ObjectType == "Speaker Voice");
        Assert.True(entry.HasAudio);
        Assert.True(entry.Capabilities.CanUpdateText);
    }
}
