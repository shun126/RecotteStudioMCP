namespace Recotte.Core.Tests;

public sealed class InspectionAndSequenceTests
{
    private static string OneText => Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "002OneText", "002OneText.ccproj");

    [Fact]
    public void Lookup_DistinguishesFoundNotFoundAndAmbiguous()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);

        LookupResult<LayerView> found = document.FindLayer(new LayerNameReference("話者1"));
        LookupResult<LayerView> missing = document.FindLayer(new LayerNameReference("missing"));
        LookupResult<LayerView> ambiguous = document.FindLayer(new LayerQuery());
        LookupResult<TimelineObjectView> voice = document.FindTimelineObject(new TimelineObjectIdReference(new(1, 1000)));
        string fullProject = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects", "009FullProject", "009FullProject.ccproj");
        LookupResult<TimelineObjectView> duplicateKey = RecotteProject.Load(fullProject).FindTimelineObject(new ObjectKeyReference(1000));

        Assert.Equal(LookupStatus.Found, found.Status);
        Assert.Equal(1, found.Value?.Index);
        Assert.Equal(LookupStatus.NotFound, missing.Status);
        Assert.Equal(LookupStatus.Ambiguous, ambiguous.Status);
        Assert.Null(ambiguous.Value);
        Assert.Equal(3, ambiguous.Candidates.Count);
        Assert.Equal(LookupStatus.Found, voice.Status);
        Assert.Equal(LookupStatus.Ambiguous, duplicateKey.Status);
        Assert.Null(duplicateKey.Value);
    }

    [Fact]
    public void SpeakerLookupAndSummary_AreStructured()
    {
        string characterVoice = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects",
            "007OneCharacterWithVoice", "007OneCharacterWithVoice.ccproj");
        RecotteProjectDocument speakerDocument = RecotteProject.Load(characterVoice);
        SpeakerView speaker = Assert.Single(speakerDocument.Speakers);
        RecotteProjectDocument document = RecotteProject.Load(OneText);

        LookupResult<SpeakerView> found = speakerDocument.FindSpeaker(new() { Name = speaker.Name });
        ProjectSummary summary = document.GetSummary();

        Assert.Equal(LookupStatus.Found, found.Status);
        Assert.Equal(3, summary.LayerCount);
        Assert.Equal(1, summary.ObjectCount);
        Assert.Equal(0, summary.SpeakerCount);
        Assert.Equal(1, summary.ObjectCountsByType["Speaker Voice"]);
        Assert.True(summary.Validation.IsValid);
        Assert.True(summary.Capabilities.CanAddTextOnlySpeakerVoice);
        Assert.False(summary.Capabilities.CanAddAudioObject);
        Assert.False(summary.Capabilities.CanAddCharacter);
    }

    [Fact]
    public void TimelineQueries_UseHalfOpenRangesAndStableOrder()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        TimelineEntryView entry = Assert.Single(document.GetTimelineEntries());
        decimal start = entry.StartTime!.Value.TotalSeconds;
        decimal end = entry.EndTime!.Value.TotalSeconds;

        Assert.Single(document.FindObjectsAt(new(start)));
        Assert.Empty(document.FindObjectsAt(new(end)));
        Assert.Single(document.FindObjectsOverlapping(new(start), new(end)));
        Assert.Empty(document.FindObjectsOverlapping(new(end), new(end + 1m)));
        Assert.True(entry.Capabilities.CanUpdateText);
        Assert.Throws<ArgumentException>(() => document.FindObjectsOverlapping(new(2m), new(2m)));
    }

    [Fact]
    public void TimelineCapabilities_AllowRemovingEveryUnlockedObjectKind()
    {
        string fullProject = Path.Combine(TestProjects.RepositoryRoot, "Samples", "RecotteProjects",
            "009FullProject", "009FullProject.ccproj");
        RecotteProjectDocument document = RecotteProject.Load(fullProject);

        TimelineEntryView[] entries = document.GetTimelineEntries().ToArray();

        Assert.NotEmpty(entries);
        Assert.True(document.Capabilities.CanRemoveTimelineObject);
        Assert.All(entries, entry => Assert.Equal(!entry.IsLocked, entry.Capabilities.CanRemove));
        Assert.Contains(entries, entry => entry.ObjectType == "Figure" && entry.Capabilities.CanRemove);
        Assert.Contains(entries, entry => entry.ObjectType == "Video" && !entry.Capabilities.CanRemove);
    }

    [Fact]
    public void FindEmptyRanges_ReturnsGapsInsideRequestedRange()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        TimelineObjectView voice = document.Layers[1].Objects.Single();
        decimal objectStart = voice.StartTime!.Value;
        decimal objectEnd = voice.EndTime!.Value;
        decimal queryEnd = objectEnd + 2m;

        IReadOnlyList<ProjectTimeRange> ranges = document.FindEmptyRanges(new LayerIndexReference(1), new(0m), new(queryEnd));

        Assert.DoesNotContain(ranges, range => range.Start.TotalSeconds < objectEnd && objectStart < range.End.TotalSeconds);
        Assert.Equal(queryEnd, ranges.Last().End.TotalSeconds);
    }

    [Fact]
    public void AddTextSequence_AppliesDurationsGapAndReturnsIds()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        AddTextSequenceResult result = document.AddTextSequence(new(
            new LayerNameReference("話者1"),
            new[] { new TextSequenceItem("first", new(1m)), new TextSequenceItem("second") },
            new(2m), new(0.5m), new(2m)));

        Assert.True(result.Success);
        Assert.Equal(2, result.ObjectIds.Count);
        Assert.Equal(5.5m, result.FinalEndTime.TotalSeconds);
        TimelineObjectView[] added = document.Layers[1].Objects.Where(item => item.ObjectKey != 1000).OrderBy(item => item.StartTime).ToArray();
        Assert.Equal(new decimal?[] { 2m, 3.5m }, added.Select(item => item.StartTime));
        Assert.Equal(new decimal?[] { 3m, 5.5m }, added.Select(item => item.EndTime));
    }

    [Fact]
    public void AddTextSequence_RollsBackWhenLayerCannotBeResolved()
    {
        RecotteProjectDocument document = RecotteProject.Load(OneText);
        AddTextSequenceResult result = document.AddTextSequence(new(
            new LayerNameReference("missing"), new[] { new TextSequenceItem("first"), new TextSequenceItem("second") },
            new(0m), new(0m), new(1m)));

        Assert.False(result.Success);
        Assert.Empty(result.ObjectIds);
        Assert.Single(document.Layers[1].Objects);
        Assert.Equal(0, document.Revision);
    }
}
