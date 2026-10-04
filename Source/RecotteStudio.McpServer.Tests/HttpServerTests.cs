using System.Net;
using System.Net.Http.Headers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RecotteStudio.Core;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class HttpServerTests
{
    private const string Token = "integration-test-token";

    [Fact]
    public async Task RequiresTokenAndRejectsForeignOrigin()
    {
        await using RecotteHttpServer server = await RecotteHttpServer.StartAsync(null, Token, 0);
        using HttpClient http = new();
        using HttpRequestMessage unauthenticated = new(HttpMethod.Post, server.Endpoint);
        using HttpResponseMessage noToken = await http.SendAsync(unauthenticated);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

        using HttpRequestMessage foreignOrigin = new(HttpMethod.Post, server.Endpoint);
        foreignOrigin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        foreignOrigin.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");
        using HttpResponseMessage rejected = await http.SendAsync(foreignOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task HttpClientCanDiscoverAndCallExistingTools()
    {
        string root = Path.Combine(Path.GetTempPath(), $"recotte-http-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "input.ccproj");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Empty.ccproj"), project);
            await using RecotteHttpServer server = await RecotteHttpServer.StartAsync(root, Token, 0);
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = server.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" }
            });
            await using McpClient client = await McpClient.CreateAsync(transport);
            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, tool => tool.Name == "recotte_inspect_project");
            Assert.Contains(tools, tool => tool.Name == "recotte_preview_operations_and_save");
            var result = await client.CallToolAsync("recotte_inspect_project",
                new Dictionary<string, object?> { ["projectPath"] = project });
            Assert.NotEqual(true, result.IsError);

            string output = Path.Combine(root, "copy.ccproj");
            byte[] sourceBefore = File.ReadAllBytes(project);
            var saveArguments = new Dictionary<string, object?>
            {
                ["projectPath"] = project,
                ["outputPath"] = output,
                ["allowOverwrite"] = false,
                ["operations"] = Array.Empty<object>()
            };
            var preview = await client.CallToolAsync("recotte_preview_operations_and_save", saveArguments);
            Assert.NotEqual(true, preview.IsError);
            Assert.False(File.Exists(output));
            var saved = await client.CallToolAsync("recotte_apply_operations_and_save_copy", saveArguments);
            Assert.NotEqual(true, saved.IsError);
            Assert.True(File.Exists(output));
            Assert.Equal(sourceBefore, File.ReadAllBytes(project));
            Assert.True(RecotteProject.Load(output).Validate().IsValid);

            string outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.ccproj");
            var denied = await client.CallToolAsync("recotte_inspect_project",
                new Dictionary<string, object?> { ["projectPath"] = outside });
            Assert.Contains(denied.Content, content => content is TextContentBlock text &&
                text.Text.Contains("MCP_PATH_OUTSIDE_WORKSPACE", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ActivityLogReportsRequestsToolCallsAndRejections()
    {
        ActivityLog activity = new();
        List<ActivityEntry> entries = new();
        int pulses = 0, peakRunning = 0;
        activity.Recorded += entry => { lock (entries) entries.Add(entry); };
        activity.RequestReceived += () => Interlocked.Increment(ref pulses);
        activity.RunningChanged += running => peakRunning = Math.Max(peakRunning, running);

        await using RecotteHttpServer server = await RecotteHttpServer.StartAsync(null, Token, 0, activity: activity);
        using HttpClient http = new();
        using HttpResponseMessage noToken = await http.PostAsync(server.Endpoint, null);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(0, pulses);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" }
        });
        await using McpClient client = await McpClient.CreateAsync(transport);
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Empty.ccproj");
        await client.CallToolAsync("recotte_inspect_project", new Dictionary<string, object?> { ["projectPath"] = fixture });
        await client.CallToolAsync("recotte_inspect_project",
            new Dictionary<string, object?> { ["projectPath"] = Path.Combine(Path.GetTempPath(), "missing.ccproj") });

        Assert.True(pulses > 0);
        Assert.Equal(1, peakRunning);
        Assert.Collection(entries,
            rejected => Assert.Equal(ActivityKind.Rejected, rejected.Kind),
            succeeded =>
            {
                Assert.Equal(ActivityKind.Success, succeeded.Kind);
                Assert.Equal("recotte_inspect_project", succeeded.Title);
                Assert.Equal("Empty.ccproj", succeeded.Detail);
                Assert.NotNull(succeeded.Duration);
            },
            failed =>
            {
                Assert.Equal(ActivityKind.Failure, failed.Kind);
                Assert.StartsWith("missing.ccproj — ", failed.Detail);
            });
    }

    [Fact]
    public async Task RunningCountNotificationsArriveInOrderUnderConcurrency()
    {
        ActivityLog activity = new();
        int last = -1, outOfOrder = 0, expected = 0;
        // Handlers run under the log's lock, so this replays the counter and catches any reordered notification.
        activity.RunningChanged += running =>
        {
            if (Math.Abs(running - expected) != 1) outOfOrder++;
            expected = last = running;
        };
        McpToolResult<object> result = new(true, "success", null, null, null, Array.Empty<McpDiagnosticDto>());

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            for (int call = 0; call < 200; call++) activity.Track("tool", null, () => result);
        })));

        Assert.Equal(0, outOfOrder);
        Assert.Equal(0, last);
    }

    [Fact]
    public async Task CannotConnectAfterServerStops()
    {
        RecotteHttpServer server = await RecotteHttpServer.StartAsync(null, Token, 0);
        Uri endpoint = server.Endpoint;
        await server.DisposeAsync();
        using HttpClient http = new();
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(endpoint));
    }

    [Fact]
    public async Task SecondServerCannotTakeSamePort()
    {
        await using RecotteHttpServer first = await RecotteHttpServer.StartAsync(null, Token, 0);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await RecotteHttpServer.StartAsync(null, Token, first.Endpoint.Port));
    }
}
