using System.Text.Json;

namespace RecotteStudio.McpServer.UI;

internal static class ConnectionSnippets
{
    private const string Url = "http://127.0.0.1:8765/mcp";

    public static string Codex(string token) =>
        "[mcp_servers.recotte_studio]\r\n" +
        $"url = \"{Url}\"\r\n" +
        $"http_headers = {{ Authorization = \"Bearer {token}\" }}";

    public static string Claude(string token) => JsonSerializer.Serialize(
        new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["recotte_studio"] = new
                {
                    type = "http",
                    url = Url,
                    headers = new { Authorization = $"Bearer {token}" }
                }
            }
        },
        new JsonSerializerOptions { WriteIndented = true });

    public static string CodexAgentRequest(string token) =>
        "この Windows PC の Codex ユーザー設定を更新してください。\r\n" +
        "対象: %USERPROFILE%\\.codex\\config.toml\r\n" +
        "他の設定を保持し、旧 mcp_servers.recotte があれば削除して、mcp_servers.recotte_studio を次の内容に追加または置換してください。\r\n\r\n" +
        Codex(token) + "\r\n\r\n" +
        "編集後に TOML の形式を確認し、可能なら接続も確認してください。" +
        "接続トークンは回答に再表示しないでください。";

    public static string ClaudeAgentRequest(string token) =>
        "この Windows PC の Claude Code ユーザー設定を更新してください。\r\n" +
        "対象: %USERPROFILE%\\.claude.json\r\n" +
        "他の設定を保持し、旧 mcpServers.recotte があれば削除して、トップレベルの " +
        "mcpServers.recotte_studio を次の JSON の項目に追加または置換してください。\r\n\r\n" +
        Claude(token) + "\r\n\r\n" +
        "編集後に JSON の形式を確認し、可能なら接続も確認してください。" +
        "接続トークンは回答に再表示しないでください。";
}
