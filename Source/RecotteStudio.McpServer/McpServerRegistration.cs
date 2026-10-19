using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RecotteStudio.McpServer;

/// <summary>Registers the Recotte MCP protocol surface and its required model guidance.</summary>
public static class McpServerRegistration
{
    /// <summary>Instructions sent in the MCP initialization response.</summary>
    public const string ServerInstructions =
        "Before generating or editing a Recotte Studio project, call recotte_get_generation_rules for optional user-editable guidance and follow it. " +
        "It returns the distributed GenerationRules.md as content and the user's own UserRules.md as userRules; where they " +
        "conflict, follow userRules. If GenerationRules.md is missing, the tool logs a warning and returns available=false; " +
        "continue with the built-in tool constraints. A missing or empty UserRules.md is normal. " +
        "All .ccproj creation and editing must use the recotte_studio MCP tools. To create a new project, always call " +
        "recotte_create_project. Do not create, patch, or hand-write .ccproj JSON because manual JSON can omit Recotte " +
        "Studio-required metadata and break compatibility. Use recotte_create_project_from_template only for an intentional, " +
        "verified existing template. If the required Recotte tool is unavailable, stop and report that the MCP server must be " +
        "restored; never fall back to filesystem or shell-based JSON creation.";

    /// <summary>Adds the HTTP server, discovered tools, and initialization guidance.</summary>
    public static IMcpServerBuilder AddRecotteMcpServer(this IServiceCollection services) =>
        services.AddMcpServer(options => options.ServerInstructions = ServerInstructions)
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(RecotteMcpTools).Assembly);
}
