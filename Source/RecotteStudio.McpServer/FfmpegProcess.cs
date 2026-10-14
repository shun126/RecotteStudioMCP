using System.Diagnostics;

namespace RecotteStudio.McpServer;

/// <summary>The outcome of one ffmpeg run. StartError is set when the executable could not be started at all.</summary>
public sealed record FfmpegRunResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, string? StartError = null)
{
    public bool Succeeded => StartError is null && !TimedOut && ExitCode == 0;
}

/// <summary>Runs ffmpeg; replaced in tests.</summary>
public interface IFfmpegRunner
{
    Task<FfmpegRunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// The only place that starts an external process. Arguments go through ArgumentList without a shell, stdin is closed,
/// and on timeout or cancellation the whole process tree is killed and awaited, so its output file is no longer held.
/// </summary>
public sealed class FfmpegProcess : IFfmpegRunner
{
    public async Task<FfmpegRunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(executablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        try { process.Start(); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return new(-1, string.Empty, string.Empty, false, exception.Message); }
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, await output, await error, true);
        }
        return new(process.ExitCode, await output, await error, false);
    }
}
