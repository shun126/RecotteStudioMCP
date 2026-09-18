using System.Collections.Concurrent;

namespace Recotte.McpServer;

public sealed class OutputPathLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = locks.GetOrAdd(path, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; }
    }
}
