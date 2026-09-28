# RecotteStudioMCP

Recotte Studio の `.ccproj` プロジェクトを MCP クライアントから調査・作成・編集・保存する Windows アプリです。Recotte Studio 自体は起動せず、[RecotteStudio.Core](Source/RecotteStudio.Core/README.md) がプロジェクトファイルを処理します。

## 使い始める

1. [Releases](https://github.com/shun126/RecotteStudioMCP/releases) の `RecotteStudioMCP-win-x64.zip` を展開します。
2. `RecotteStudio.McpServer.exe` を起動します。ウィンドウが開いている間、`http://127.0.0.1:8765/mcp` で MCP サーバーが稼働します。最小化しても稼働し、閉じると停止します。
3. 必要なら画面左下の設定を開き、作業フォルダを指定して「作業フォルダを適用」を押します。未指定の場合は従来どおり、既存ディレクトリ内の任意の `.ccproj` を扱えます。
4. 設定画面の Codex または Claude Code の接続例をコピーして接続します。接続例にはこの PC 専用の接続トークンが含まれるため、公開しないでください。

接続先は固定です。ポート `8765` が使用中なら、アプリにエラーが表示されます。別のアプリでこのポートを使用している場合は、そのアプリを終了してから Recotte Studio MCP を再起動してください。

### Codex

画面の「Codex 設定をコピー」で得た内容を `%USERPROFILE%\.codex\config.toml` に追加します。旧 `[mcp_servers.recotte]` がある場合は削除し、`[mcp_servers.recotte_studio]` を追加・置換してください。新しい Codex セッションで接続を確認します。Codex のHTTPサーバー設定では `url` と `http_headers` を使用します。

### Claude Code

設定画面の「Claude Code 設定をコピー」で得たJSONを `%USERPROFILE%\.claude.json` のトップレベル `mcpServers.recotte_studio` に追加します。旧 `mcpServers.recotte` があれば削除し、他の設定は残してください。新しいセッションで接続を確認します。設定例にはトークンが含まれるため、このファイルを共有しないでください。Claude Code のユーザー範囲MCP設定では `~/.claude.json` の `mcpServers` を使用します。

トークンを再発行すると既存のクライアント設定は使えなくなります。画面から設定例をコピーし直してください。クライアントの設定ファイルはアプリから変更しません。

設定画面には Codex と Claude Code それぞれの「エージェントへの設定依頼文をコピー」もあります。コピーした文を対象のエージェントに送ると、他の設定を残しながら旧 `recotte` を `recotte_studio` に移行できます。依頼文には接続トークンが含まれるため、送信先と会話履歴の取り扱いを確認してください。

## 開発

.NET 10 SDK と Windows App SDK の NuGet パッケージを使用します。

```powershell
dotnet build RecotteStudioMCP.sln
dotnet test RecotteStudioMCP.sln
./Scripts/Publish-WinX64.ps1
```

発行スクリプトは自己完結型の単一 EXE を `artifacts/publish/win-x64` に作成し、`artifacts/RecotteStudioMCP-win-x64.zip` に収めます。対象は Windows x64 です。WinUI 3 の同梱ライブラリは初回起動時に一時ディレクトリへ展開されます。

| 場所 | 内容 |
| --- | --- |
| `Source/RecotteStudio.McpServer` | WinUI 3 画面、HTTP サーバー、MCP ツール群 |
| `Source/RecotteStudio.Core` | `.ccproj` を扱う SDK |
| `Source/*.Tests` | xUnit テスト |
| [Documents](Documents/README.md) | ファイル形式と Core の設計資料 |

本プロジェクトは AHS および Recotte Studio の開発元とは関係のない非公式ツールです。ライセンスは [GPL-3.0](LICENSE) です。
