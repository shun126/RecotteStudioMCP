using Recotte.McpServer;

namespace Recotte.McpServer.Tests;

public sealed class WorkspacePathPolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-mcp-{Guid.NewGuid():N}");

    public WorkspacePathPolicyTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void AllowsWorkspaceProjectAndRejectsOutsideAndExtensions()
    {
        string project = Path.Combine(root, "input.ccproj");
        File.WriteAllText(project, "{}");
        WorkspacePathPolicy policy = new(new(root));
        Assert.True(policy.ValidateInput(project).Success);
        Assert.Equal("MCP_PATH_EXTENSION", policy.ValidateOutput(Path.Combine(root, "output.json")).ErrorCode);
        Assert.Equal("MCP_PATH_OUTSIDE_WORKSPACE", policy.ValidateOutput(Path.Combine(root, "..", "output.ccproj")).ErrorCode);
    }

    [Fact]
    public void RejectsMissingOutputDirectory()
    {
        WorkspacePathPolicy policy = new(new(root));
        Assert.Equal("MCP_PATH_DIRECTORY_NOT_FOUND", policy.ValidateOutput(Path.Combine(root, "missing", "output.ccproj")).ErrorCode);
    }

    [Fact]
    public void AllowsAnyExistingDirectoryWhenWorkspaceIsNotConfigured()
    {
        WorkspacePathPolicy policy = new(new McpServerOptions((string?)null));
        string output = Path.Combine(Path.GetTempPath(), $"recotte-unrestricted-{Guid.NewGuid():N}.ccproj");

        Assert.True(policy.ValidateOutput(output).Success);
    }

    [Fact]
    public void OptionsDefaultToUnrestrictedPaths()
    {
        McpServerOptions options = McpServerOptions.Parse(Array.Empty<string>(), null);

        Assert.Null(options.WorkspaceRoot);
    }
}
