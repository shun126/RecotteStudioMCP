namespace Recotte.McpServer;

public sealed record McpServerOptions(string? WorkspaceRoot)
{
    public static McpServerOptions Parse(IReadOnlyList<string> args, string? environmentRoot)
    {
        string? commandLineRoot = null;
        for (int index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], "--workspace", StringComparison.Ordinal)) continue;
            if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException("--workspace requires a directory path.");
            commandLineRoot = args[index];
        }

        string? root = commandLineRoot ??
            (string.IsNullOrWhiteSpace(environmentRoot) ? null : environmentRoot);
        if (root is null) return new McpServerOptions((string?)null);

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new ArgumentException($"The workspace directory does not exist: {root}");
        return new(root);
    }
}
