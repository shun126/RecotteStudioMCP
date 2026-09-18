using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Recotte.McpServer;

/// <summary>Registers the Recotte MCP protocol surface and its required model guidance.</summary>
public static class McpServerRegistration
{
    /// <summary>Instructions sent in the MCP initialization response.</summary>
    public const string ServerInstructions =
        "All .ccproj creation and editing must use the recotte_studio MCP tools. To create a new project, always call " +
        "recotte_create_project. Do not create, patch, or hand-write .ccproj JSON because manual JSON can omit Recotte " +
        "Studio-required metadata and break compatibility. Use recotte_create_project_from_template only for an intentional, " +
        "verified existing template. If the required Recotte tool is unavailable, stop and report that the MCP server must be " +
        "restored; never fall back to filesystem or shell-based JSON creation.";

    /// <summary>Adds the stdio server, discovered tools, and initialization guidance.</summary>
    public static IMcpServerBuilder AddRecotteMcpServer(this IServiceCollection services) =>
        services.AddMcpServer(options => options.ServerInstructions = ServerInstructions)
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(RecotteMcpTools).Assembly);
}
