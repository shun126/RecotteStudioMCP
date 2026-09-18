namespace Recotte.McpServer;

public sealed record PathPolicyResult(bool Success, string? FullPath, string? ErrorCode, string? Message);

public sealed class WorkspacePathPolicy
{
    private readonly string? root;
    private readonly StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public WorkspacePathPolicy(McpServerOptions options)
    {
        if (options.WorkspaceRoot is not null)
            root = ResolveExistingPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.WorkspaceRoot)));
    }

    public PathPolicyResult ValidateInput(string? path) => Validate(path, true);
    public PathPolicyResult ValidateOutput(string? path) => Validate(path, false);

    private PathPolicyResult Validate(string? path, bool input)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure("MCP_PATH_REQUIRED", "A project path is required.");
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return Failure("MCP_PATH_INVALID", "The requested path is invalid."); }
        if (!string.Equals(Path.GetExtension(full), ".ccproj", StringComparison.OrdinalIgnoreCase))
            return Failure("MCP_PATH_EXTENSION", "Only .ccproj files are allowed.");

        string? directory = Path.GetDirectoryName(full);
        if (directory is null || !Directory.Exists(directory))
            return Failure("MCP_PATH_DIRECTORY_NOT_FOUND", "The destination directory does not exist.");
        string resolvedDirectory = ResolveExistingPath(directory);
        string resolved = Path.Combine(resolvedDirectory, Path.GetFileName(full));
        if (File.Exists(full)) resolved = ResolveExistingPath(full);
        if (root is not null && !IsWithinRoot(resolved, root))
            return Failure("MCP_PATH_OUTSIDE_WORKSPACE", "The requested path is outside the configured workspace.");
        if (input && !File.Exists(resolved)) return Failure("MCP_PATH_FILE_NOT_FOUND", "The project file does not exist.");
        return new(true, resolved, null, null);
    }

    private bool IsWithinRoot(string path, string workspaceRoot) => string.Equals(path, workspaceRoot, comparison) ||
        path.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, comparison);

    private static string ResolveExistingPath(string path)
    {
        FileSystemInfo current = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        FileSystemInfo? target = current.ResolveLinkTarget(true);
        return Path.GetFullPath(target?.FullName ?? current.FullName);
    }

    private static PathPolicyResult Failure(string code, string message) => new(false, null, code, message);
}
