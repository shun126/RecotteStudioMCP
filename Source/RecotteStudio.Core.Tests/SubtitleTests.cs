namespace RecotteStudio.Core.Tests;

public sealed class SubtitleTests
{
    private static string FullProject => Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects", "009FullProject", "009FullProject.ccproj");

    [Fact]
    public void GetSubtitleCues_ReturnsSpeakerVoicesInTimelineOrder()
    {
        IReadOnlyList<SubtitleCue> cues = RecotteProject.Load(FullProject).GetSubtitleCues();

        Assert.Equal(2, cues.Count);
        Assert.Equal(new TimelineObjectId(1, 1002), cues[0].ObjectId);
        Assert.Equal("こんにちは", cues[0].Text);
        Assert.Equal(1.65m, cues[0].End.TotalSeconds);
        Assert.Equal(new TimelineObjectId(2, 1002), cues[1].ObjectId);
        Assert.Equal("こんにちは！", cues[1].Text);
        Assert.Equal(1.6m, cues[1].Start.TotalSeconds);
    }

    [Fact]
    public void GetSubtitleCues_IsEmptyWithoutDialogue() =>
        Assert.Empty(RecotteProject.Load(TestProjects.EmptyProject).GetSubtitleCues());

    [Theory]
    [InlineData("0", "00:00:00,000")]
    [InlineData("0.6000000000000001", "00:00:00,600")]
    [InlineData("1.0005", "00:00:01,001")]
    [InlineData("59.9996", "00:01:00,000")]
    [InlineData("3723.25", "01:02:03,250")]
    public void FormatTime_PadsAndRoundsToMilliseconds(string seconds, string expected) =>
        Assert.Equal(expected, SubRipFormatter.FormatTime(new(decimal.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public void Format_NumbersFromOneAndDropsBlankLines()
    {
        SubtitleCue[] cues =
        {
            new(new(1, 1000), "A", new(0.5m), new(1.5m), "Hello"),
            new(new(2, 1000), "B", new(1.5m), new(3m), "first line\r\n\r\nsecond line  \n"),
        };

        Assert.Equal(
            "1\r\n00:00:00,500 --> 00:00:01,500\r\nHello\r\n\r\n" +
            "2\r\n00:00:01,500 --> 00:00:03,000\r\nfirst line\r\nsecond line\r\n\r\n",
            SubRipFormatter.Format(cues));
    }

    [Fact]
    public void Format_RejectsBlankTextAndEmptyDuration()
    {
        Assert.Throws<ArgumentException>(() => SubRipFormatter.Format(new SubtitleCue[] { new(new(1, 1), null, new(0m), new(1m), " \n ") }));
        Assert.Throws<ArgumentException>(() => SubRipFormatter.Format(new SubtitleCue[] { new(new(1, 1), null, new(1m), new(1m), "text") }));
    }
}
