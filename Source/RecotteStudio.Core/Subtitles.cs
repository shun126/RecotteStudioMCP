using System.Globalization;
using System.Text;

namespace RecotteStudio.Core;

/// <summary>Describes one spoken line that can be written as a subtitle cue.</summary>
/// <param name="ObjectId">The Speaker Voice that the cue comes from; object keys repeat across layers, so both parts identify it.</param>
/// <param name="LayerName">The Speaker layer name, when present.</param>
/// <param name="Start">The time the line starts.</param>
/// <param name="End">The time the line ends.</param>
/// <param name="Text">The dialogue text as stored in the project.</param>
public sealed record SubtitleCue(TimelineObjectId ObjectId, string? LayerName, ProjectTime Start, ProjectTime End, string Text);

public sealed partial class RecotteProjectDocument
{
    /// <summary>
    /// Returns every Speaker Voice that has text and a positive duration, in timeline order. Lines of different speakers
    /// may overlap, as they do on screen.
    /// </summary>
    public IReadOnlyList<SubtitleCue> GetSubtitleCues() => GetTimelineEntries(new() { ObjectType = "Speaker Voice" })
        .Where(entry => entry.ObjectId is not null && entry.StartTime is not null && entry.EndTime is not null &&
            entry.EndTime.Value.TotalSeconds > entry.StartTime.Value.TotalSeconds && !string.IsNullOrWhiteSpace(entry.Text))
        .Select(entry => new SubtitleCue(entry.ObjectId!.Value, entry.LayerName, entry.StartTime!.Value, entry.EndTime!.Value, entry.Text!))
        .ToArray();
}

/// <summary>Formats subtitle cues as SubRip (.srt) text.</summary>
public static class SubRipFormatter
{
    /// <summary>
    /// Formats cues numbered from 1 with HH:MM:SS,mmm times and CRLF line breaks. Blank lines inside a text would end the
    /// cue early in SubRip, so they are removed; a cue whose text is blank is rejected.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a cue has no visible text or does not end after it starts.</exception>
    public static string Format(IEnumerable<SubtitleCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        StringBuilder builder = new();
        int number = 0;
        foreach (SubtitleCue cue in cues)
        {
            string[] lines = cue.Text.ReplaceLineEndings("\n").Split('\n')
                .Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToArray();
            if (lines.Length == 0) throw new ArgumentException($"The cue for {cue.ObjectId} has no visible text.", nameof(cues));
            if (cue.End.TotalSeconds <= cue.Start.TotalSeconds)
                throw new ArgumentException($"The cue for {cue.ObjectId} must end after it starts.", nameof(cues));
            builder.Append(++number).Append("\r\n")
                .Append(FormatTime(cue.Start)).Append(" --> ").Append(FormatTime(cue.End)).Append("\r\n");
            foreach (string line in lines) builder.Append(line).Append("\r\n");
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    /// <summary>Formats a time as HH:MM:SS,mmm, rounding to the nearest millisecond.</summary>
    public static string FormatTime(ProjectTime time)
    {
        long milliseconds = (long)Math.Round(time.TotalSeconds * 1000m, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture,
            $"{milliseconds / 3_600_000:00}:{milliseconds / 60_000 % 60:00}:{milliseconds / 1000 % 60:00},{milliseconds % 1000:000}");
    }
}
