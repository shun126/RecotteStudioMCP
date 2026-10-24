using System.Globalization;
using System.Text.RegularExpressions;

namespace RecotteStudio.McpServer;

/// <summary>Bounded frame sampling for visual analysis; timestamps remain in source-video seconds.</summary>
public sealed record VideoFrameExtraction(string Mode = "interval", decimal Start = 0, decimal Duration = 60,
    decimal Interval = 5, decimal SceneThreshold = 0.3m, int MaxFrames = 60, int MaxWidth = 1280)
{
    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v"];

    public void Validate()
    {
        if (Mode is not ("interval" or "scene" or "combined")) throw new ArgumentException("mode must be interval, scene, or combined.");
        if (Start is < 0 or > 86400 || Duration is < 0.1m or > 600) throw new ArgumentException("start must be 0-86400 and duration 0.1-600 seconds.");
        if (Interval is < 0.1m or > 600 || SceneThreshold is <= 0 or > 1) throw new ArgumentException("interval must be 0.1-600 seconds and sceneThreshold greater than 0 and at most 1.");
        if (MaxFrames is < 1 or > 120 || MaxWidth is < 160 or > 1920) throw new ArgumentException("maxFrames must be 1-120 and maxWidth 160-1920.");
    }

    public IReadOnlyList<string> Arguments(string videoPath, string directory)
    {
        Validate();
        string periodic = $"gte(t-prev_selected_t,{Number(Interval)})";
        string scene = $"gt(scene,{Number(SceneThreshold)})";
        string selection = Mode switch { "scene" => scene, "combined" => $"{periodic}+{scene}", _ => periodic };
        // Normalize container timestamps once, then retain source time through trim and select.
        // showinfo runs after selection, so n maps directly to the numbered JPEGs.
        string filter = $"setpts=PTS-STARTPTS,trim=start={Number(Start)}:end={Number(Start + Duration)}," +
            $"select='isnan(prev_selected_t)+{selection}',scale=w='min(iw,{MaxWidth})':h=-2,showinfo";
        return ["-hide_banner", "-nostdin", "-loglevel", "info", "-n", "-protocol_whitelist", "file,pipe", "-t", Number(Start + Duration), "-i", "file:" + videoPath,
            "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", filter, "-fps_mode", "vfr", "-frames:v", MaxFrames.ToString(CultureInfo.InvariantCulture),
            "-q:v", "3", "-f", "image2", Path.Combine(directory, "frame-%04d.jpg")];
    }

    public static IReadOnlyDictionary<int, decimal> ReadTimestamps(string log)
    {
        Dictionary<int, decimal> times = new();
        foreach (Match match in Regex.Matches(log, @"\[Parsed_showinfo_[^\]]+\]\s+n:\s*(\d+)\s+pts:\s*-?\d+\s+pts_time:([-+\d.eE]+)", RegexOptions.CultureInvariant))
        {
            if (int.TryParse(match.Groups[1].Value, out int index) && decimal.TryParse(match.Groups[2].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out decimal seconds)) times[index + 1] = seconds;
        }
        return times;
    }

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
