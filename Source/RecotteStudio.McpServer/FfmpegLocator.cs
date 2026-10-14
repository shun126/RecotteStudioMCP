namespace RecotteStudio.McpServer;

/// <summary>Where the ffmpeg in use was found.</summary>
public enum FfmpegSource { Configured, SearchPath }

/// <summary>An ffmpeg executable chosen for use, or the reason none is available.</summary>
public sealed record FfmpegResolution(string? ExecutablePath, FfmpegSource? Source, string? Message)
{
    public bool Found => ExecutablePath is not null;
}

/// <summary>The result of running <c>ffmpeg -version</c>.</summary>
public sealed record FfmpegProbeResult(bool Success, string? Version, string? Message);

/// <summary>
/// Chooses the ffmpeg executable: the path set in the settings window, otherwise ffmpeg.exe on PATH. ffmpeg is not
/// bundled. Only the settings window changes the configured path; MCP tools only read it.
/// </summary>
public sealed class FfmpegLocator
{
    public const string ExecutableName = "ffmpeg.exe";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<IEnumerable<string>> searchDirectories;
    private volatile string? configuredPath;

    public FfmpegLocator(string? configuredPath = null, Func<IEnumerable<string>>? searchDirectories = null)
    {
        this.configuredPath = configuredPath;
        this.searchDirectories = searchDirectories ?? SearchPathDirectories;
    }

    /// <summary>Gets or sets the path chosen in the settings window; null searches PATH.</summary>
    public string? ConfiguredPath { get => configuredPath; set => configuredPath = value; }

    /// <summary>Returns why a path cannot be configured, or null when it is an existing .exe file.</summary>
    public static string? ValidateConfiguredPath(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return "ffmpeg のパスが正しくありません。"; }
        if (!string.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase))
            return "ffmpeg の実行ファイル（.exe）を指定してください。";
        return File.Exists(full) ? null : "指定した ffmpeg が見つかりません。";
    }

    /// <summary>Chooses the configured ffmpeg when set, otherwise the first ffmpeg.exe on PATH.</summary>
    public FfmpegResolution Resolve()
    {
        if (configuredPath is string configured)
        {
            string? problem = ValidateConfiguredPath(configured);
            return problem is null ? new(Path.GetFullPath(configured), FfmpegSource.Configured, null) : new(null, null, problem);
        }
        foreach (string directory in searchDirectories())
        {
            string candidate;
            try { candidate = Path.Combine(Path.GetFullPath(directory), ExecutableName); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (File.Exists(candidate)) return new(candidate, FfmpegSource.SearchPath, null);
        }
        return new(null, null, "ffmpeg が設定されておらず、PATH にも見つかりません。");
    }

    /// <summary>Runs <c>ffmpeg -version</c> and accepts the executable only when it reports itself as ffmpeg.</summary>
    public static Task<FfmpegProbeResult> ProbeAsync(string executablePath, CancellationToken cancellationToken = default) =>
        ProbeAsync(new FfmpegProcess(), executablePath, cancellationToken);

    /// <inheritdoc cref="ProbeAsync(string, CancellationToken)"/>
    public static async Task<FfmpegProbeResult> ProbeAsync(IFfmpegRunner runner, string executablePath,
        CancellationToken cancellationToken = default)
    {
        FfmpegRunResult run = await runner.RunAsync(executablePath, new[] { "-version" }, ProbeTimeout, cancellationToken);
        if (run.StartError is not null) return new(false, null, $"ffmpeg を起動できません: {run.StartError}");
        if (run.TimedOut) return new(false, null, "ffmpeg が応答しません。");
        string firstLine = run.StandardOutput.Split('\n', 2)[0].Trim();
        if (!firstLine.StartsWith("ffmpeg version ", StringComparison.Ordinal))
            return new(false, null, "指定したファイルは ffmpeg ではありません。");
        int copyright = firstLine.IndexOf(" Copyright", StringComparison.Ordinal);
        return new(true, (copyright < 0 ? firstLine : firstLine[..copyright])["ffmpeg version ".Length..], null);
    }

    // PATH is read from the registry as well, so an ffmpeg installed while this window is open is found without a restart.
    private static IEnumerable<string> SearchPathDirectories() =>
        new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            .SelectMany(target => (Environment.GetEnvironmentVariable("PATH", target) ?? string.Empty).Split(Path.PathSeparator))
            .Select(directory => Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')))
            .Where(directory => directory.Length > 0 && Path.IsPathFullyQualified(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
