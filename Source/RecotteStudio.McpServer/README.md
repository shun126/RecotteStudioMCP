# RecotteStudio.McpServer

`RecotteStudio.McpServer` は、[RecotteStudio.Core](../RecotteStudio.Core/README.md) の機能を Streamable HTTP MCP ツールとして公開する WinUI 3 アプリです。ウィンドウを閉じるとサーバーは停止します。

接続先は `http://127.0.0.1:8765/mcp` です。全てのリクエストに GUI が生成した `Authorization: Bearer <token>` ヘッダーが必要です。待受先はループバックのみで、`Host` と `Origin` を検証します。

利用手順、ビルド、Codex と Claude Code の接続方法は[ルート README](../../README.md)を参照してください。既存の stdio 実行ファイルは廃止しました。

## バージョン

アプリのバージョンは `RecotteStudio.McpServer.csproj` の `Version` で管理します。リリースタグ（例: `v0.5.0`）はこの値と一致させてください。リリースワークフローも一致を確認します。`RecotteStudio.Core` のバージョンは [Core のプロジェクトファイル](../RecotteStudio.Core/RecotteStudio.Core.csproj) で独立して管理します。

ツールはプロジェクトの調査、検証、タイムライン検索、操作のプレビュー、別名保存、安全な上書き、新規作成を提供します。保存処理は引き続き Core の検証とバックアップ規則に従います。Recotte Studio 本体は起動しません。
