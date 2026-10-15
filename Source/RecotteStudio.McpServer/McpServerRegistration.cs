using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RecotteStudio.McpServer;

/// <summary>Registers the Recotte MCP protocol surface and its required model guidance.</summary>
public static class McpServerRegistration
{
    /// <summary>Instructions sent in the MCP initialization response.</summary>
    public const string ServerInstructions =
        "Before generating or editing a Recotte Studio project, call recotte_get_generation_rules for optional user-editable guidance. " +
        "If the file is missing, the tool logs a warning and returns available=false; continue with the built-in tool constraints. " +
        "All .ccproj creation and editing must use the recotte_studio MCP tools. To create a new project, always call " +
        "recotte_create_project. Do not create, patch, or hand-write .ccproj JSON because manual JSON can omit Recotte " +
        "Studio-required metadata and break compatibility. Use recotte_create_project_from_template only for an intentional, " +
        "verified existing template. If the required Recotte tool is unavailable, stop and report that the MCP server must be " +
        "restored; never fall back to filesystem or shell-based JSON creation. " +
        "Dialogue is text on a Speaker layer, placed with addTextOnlySpeakerVoice or recotte_add_text_sequence_and_save_copy: " +
        "Recotte Studio works with a configured speech synthesis engine and draws the telop itself. So never generate " +
        "audio, run TTS tools, or create .wav files, do not report missing audio as a problem or as remaining work, and never " +
        "draw dialogue into an image or video, where it would appear twice. " + TelopLayout;

    /// <summary>Where the dialogue telop sits on screen, shared by the instructions and recotte_get_capabilities.</summary>
    public const string TelopLayout =
        "The telop covers about one fifth of the screen height: the bottom of the screen when the voice's TelopFrame is " +
        "LowerFrame, the top when it is UpperFrame. Keep faces, captions, and other important content of images and videos " +
        "out of that band; recotte_get_capabilities returns the exact frame bounds.";

    /// <summary>Adds the HTTP server, discovered tools, and initialization guidance.</summary>
    public static IMcpServerBuilder AddRecotteMcpServer(this IServiceCollection services) =>
        services.AddMcpServer(options => options.ServerInstructions = ServerInstructions)
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(RecotteMcpTools).Assembly);
}
