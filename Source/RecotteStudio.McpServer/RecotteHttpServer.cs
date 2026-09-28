using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RecotteStudio.McpServer;

/// <summary>Owns a loopback-only MCP endpoint for the lifetime of the desktop window.</summary>
public sealed class RecotteHttpServer : IAsyncDisposable
{
    public const int DefaultPort = 8765;
    private readonly WebApplication app;
    private readonly byte[] tokenBytes;

    private RecotteHttpServer(WebApplication app, string token)
    {
        this.app = app;
        tokenBytes = Encoding.UTF8.GetBytes(token);
    }

    public Uri Endpoint { get; private set; } = null!;

    public static async Task<RecotteHttpServer> StartAsync(string? workspaceRoot, string token,
        int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("An access token is required.", nameof(token));
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        McpServerOptions options = McpServerOptions.Create(workspaceRoot);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<WorkspacePathPolicy>();
        builder.Services.AddSingleton<OutputPathLockManager>();
        builder.Services.AddSingleton<ProjectOperationMapper>();
        builder.Services.AddSingleton<RecotteToolService>();
        builder.Services.AddRecotteMcpServer();

        WebApplication app = builder.Build();
        RecotteHttpServer server = new(app, token);
        app.Use(async (context, next) =>
        {
            if (!IsValidHost(context.Request) || !IsValidOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (!server.IsAuthorized(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next(context);
        });
        app.MapMcp("/mcp");
        try
        {
            await app.StartAsync(cancellationToken);
            string address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            server.Endpoint = new Uri(new Uri(address), "/mcp");
            return server;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private bool IsAuthorized(HttpRequest request)
    {
        const string prefix = "Bearer ";
        string value = request.Headers.Authorization.ToString();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        byte[] supplied = Encoding.UTF8.GetBytes(value[prefix.Length..]);
        return supplied.Length == tokenBytes.Length && CryptographicOperations.FixedTimeEquals(supplied, tokenBytes);
    }

    private static bool IsValidOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var values)) return true;
        return values.Count == 1 && Uri.TryCreate(values[0], UriKind.Absolute, out Uri? origin)
            && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback
            && origin.Port == request.Host.Port && origin.AbsolutePath == "/"
            && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment);
    }

    private static bool IsValidHost(HttpRequest request) =>
        request.Host.Port == request.HttpContext.Connection.LocalPort &&
        request.Host.Host is "127.0.0.1" or "localhost";

    public async ValueTask DisposeAsync()
    {
        using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(15));
        try { await app.StopAsync(shutdown.Token); }
        finally { await app.DisposeAsync(); }
    }
}
