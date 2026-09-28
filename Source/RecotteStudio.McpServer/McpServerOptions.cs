namespace RecotteStudio.McpServer;

public sealed record McpServerOptions(string? WorkspaceRoot)
{
    public static McpServerOptions Create(string? workspaceRoot)
    {
        string? root = string.IsNullOrWhiteSpace(workspaceRoot) ? null : workspaceRoot;
        if (root is null) return new McpServerOptions((string?)null);

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new ArgumentException($"The workspace directory does not exist: {root}");
        return new(root);
    }
}
