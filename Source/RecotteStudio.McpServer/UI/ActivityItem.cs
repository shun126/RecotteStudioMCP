using Microsoft.UI.Xaml;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.UI;

/// <summary>Presents one activity entry as a row of the home page log.</summary>
public sealed class ActivityItem(ActivityEntry entry)
{
    public string Time { get; } = entry.Time.ToLocalTime().ToString("HH:mm:ss");
    public string Title { get; } = entry.Title;
    public string Detail { get; } = entry.Detail ?? string.Empty;
    public string Duration { get; } = entry.Duration is TimeSpan duration ? Format(duration) : string.Empty;

    public Visibility DetailVisibility => Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InfoVisibility => Show(ActivityKind.Info);
    public Visibility SuccessVisibility => Show(ActivityKind.Success);
    public Visibility FailureVisibility => Show(ActivityKind.Failure);
    public Visibility RejectedVisibility => Show(ActivityKind.Rejected);

    private Visibility Show(ActivityKind kind) => entry.Kind == kind ? Visibility.Visible : Visibility.Collapsed;

    private static string Format(TimeSpan duration) => duration.TotalSeconds >= 1
        ? $"{duration.TotalSeconds:0.0} 秒"
        : $"{Math.Max(1, (int)Math.Round(duration.TotalMilliseconds))} ms";
}
