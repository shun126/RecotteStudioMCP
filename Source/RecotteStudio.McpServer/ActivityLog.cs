using System.Diagnostics;

namespace RecotteStudio.McpServer;

/// <summary>Classifies one activity entry for display.</summary>
public enum ActivityKind { Info, Success, Failure, Rejected }

/// <summary>Describes one finished server event: a tool call, a rejected request, or a lifecycle change.</summary>
public sealed record ActivityEntry(DateTimeOffset Time, ActivityKind Kind, string Title, string? Detail, TimeSpan? Duration);

/// <summary>
/// Reports server activity to the desktop window. Events are raised on request threads, so subscribers must marshal to
/// their own thread. The log outlives individual server instances so that history survives a restart.
/// </summary>
public sealed class ActivityLog
{
    private readonly object runningGate = new();
    private int running;

    /// <summary>Raised for every authorized HTTP request reaching the MCP endpoint.</summary>
    public event Action? RequestReceived;

    /// <summary>
    /// Raised when a tool call starts or ends, with the number of calls still running. Notifications arrive in order
    /// and are raised while a lock is held, so handlers must return quickly.
    /// </summary>
    public event Action<int>? RunningChanged;

    /// <summary>Raised when an entry is recorded.</summary>
    public event Action<ActivityEntry>? Recorded;

    /// <summary>Signals that an authorized request arrived.</summary>
    public void Pulse() => RequestReceived?.Invoke();

    /// <summary>Records an entry that has no duration.</summary>
    public void Record(ActivityKind kind, string title, string? detail = null) =>
        Recorded?.Invoke(new(DateTimeOffset.Now, kind, title, detail, null));

    /// <summary>Runs a tool call and records its outcome and duration.</summary>
    public McpToolResult<object> Track(string tool, string? path, Func<McpToolResult<object>> call)
    {
        long started = Begin();
        try { return End(tool, path, started, call()); }
        catch (Exception exception) { Fail(tool, path, started, exception); throw; }
    }

    /// <summary>Runs an asynchronous tool call and records its outcome and duration.</summary>
    public async Task<McpToolResult<object>> TrackAsync(string tool, string? path, Func<Task<McpToolResult<object>>> call)
    {
        long started = Begin();
        try { return End(tool, path, started, await call()); }
        catch (Exception exception) { Fail(tool, path, started, exception); throw; }
    }

    private long Begin()
    {
        ChangeRunning(1);
        return Stopwatch.GetTimestamp();
    }

    private McpToolResult<object> End(string tool, string? path, long started, McpToolResult<object> result)
    {
        string? reason = result.Success ? null : result.ErrorCode ?? result.Message ?? result.Category;
        Complete(result.Success ? ActivityKind.Success : ActivityKind.Failure, tool, Describe(path, reason), started);
        return result;
    }

    private void Fail(string tool, string? path, long started, Exception exception) =>
        Complete(ActivityKind.Failure, tool,
            Describe(path, exception is OperationCanceledException ? "キャンセルされました" : exception.Message), started);

    private void Complete(ActivityKind kind, string tool, string? detail, long started)
    {
        Recorded?.Invoke(new(DateTimeOffset.Now, kind, tool, detail, Stopwatch.GetElapsedTime(started)));
        ChangeRunning(-1);
    }

    // The count and its notification are published under one lock so that subscribers receive counts in the order
    // they were reached; otherwise a stale count could arrive last and stick. Subscribers must therefore not block.
    private void ChangeRunning(int delta)
    {
        lock (runningGate) RunningChanged?.Invoke(running += delta);
    }

    private static string? Describe(string? path, string? reason)
    {
        string? file = string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path.TrimEnd('\\', '/'));
        return (file, reason) switch
        {
            (null or "", _) => reason,
            (_, null) => file,
            _ => $"{file} — {reason}",
        };
    }
}
