using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Recotte.McpServer;

McpServerOptions options;
try
{
    options = McpServerOptions.Parse(args, Environment.GetEnvironmentVariable("RECOTTE_WORKSPACE_ROOT"));
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<WorkspacePathPolicy>();
builder.Services.AddSingleton<OutputPathLockManager>();
builder.Services.AddSingleton<ProjectOperationMapper>();
builder.Services.AddSingleton<RecotteToolService>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
await builder.Build().RunAsync();
return 0;
