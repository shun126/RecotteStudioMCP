# RecotteStudioMCP

Recotte Studio のプロジェクトファイル（`.ccproj`）を、LLM から安全に調査・生成・編集・保存するための MCP サーバーです。

`Recotte.McpServer` → `Recotte.Core` → `.ccproj` の構成で、Recotte Studio 本体を起動せずにプロジェクトを生成・編集・保存します。`SaveCopy` は元ファイルを変更しない安全な別名保存です。明示的な `Overwrite` は Core の `Save()` のみを使用し、必須バックアップ、外部変更検出、Validation、一時保存後の再読み込み検証を経て置換します。Recotte Studio は生成処理には使用せず、必要なら人間が生成後の `.ccproj` を開きます。

> 本プロジェクトは非公式ツールです。

## 構成

| パス | 内容 |
| --- | --- |
| [Source/Recotte.McpServer](Source/Recotte.McpServer/README.md) | stdio MCP サーバー |
| [Source/Recotte.Core](Source/Recotte.Core/README.md) | `.ccproj` の読み込み・検証・編集・保存 SDK |
| `Source/*.Tests` | xUnit テスト |
| [Documents](Documents/README.md) | ファイル形式と Core の仕様 |
| [Samples/RecotteProjects](Samples/RecotteProjects/README.md) | Recotte Studio が生成した検証用プロジェクト（Core の組み込みテンプレートとしても使用） |
| [Tests](Tests/README.md) | MCP を使って動画プロジェクトを生成する作業ディレクトリ（`AGENTS.md` など） |

## ビルドとテスト

前提は .NET 9 SDK です。

```powershell
dotnet build RecotteStudioMCP.sln -c Release
dotnet test RecotteStudioMCP.sln
```

## MCP クライアントへの登録

Release ビルド後、`Recotte.McpServer.exe` を MCP サーバーとして登録します。以下の例のパスは環境に合わせて変更してください。（任意）アクセス範囲を特定のディレクトリに制限したい場合は、引数に `--workspace "C:/RecotteWorkspace"` を追加するか、環境変数 `RECOTTE_WORKSPACE_ROOT` を設定します。指定しない場合は、存在する任意のディレクトリの `.ccproj` を扱えます。

### Claude Code

コマンドで登録します。`--scope user` を付けると全プロジェクト共通、省略すると現在のプロジェクトだけで有効になります。

```powershell
claude mcp add --scope user recotte -- "C:/RecotteStudioMCP/Source/Recotte.McpServer/bin/Release/net9.0/Recotte.McpServer.exe"
```

プロジェクト単位でチームと共有したい場合は、プロジェクト直下の `.mcp.json` に記述します。

```json
{
  "mcpServers": {
    "recotte": {
      "command": "C:/RecotteStudioMCP/Source/Recotte.McpServer/bin/Release/net9.0/Recotte.McpServer.exe",
      "args": []
    }
  }
}
```

`claude mcp list` で登録状態を確認できます。

### Codex

コマンドで登録します。

```powershell
codex mcp add recotte -- "C:/RecotteStudioMCP/Source/Recotte.McpServer/bin/Release/net9.0/Recotte.McpServer.exe"
```

または `~/.codex/config.toml`（Windows では `%USERPROFILE%\.codex\config.toml`）に直接記述します。

```toml
[mcp_servers.recotte]
command = "C:/RecotteStudioMCP/Source/Recotte.McpServer/bin/Release/net9.0/Recotte.McpServer.exe"
args = []
```

`codex mcp list` で登録状態を確認できます。

どちらのクライアントも、登録後に新しいセッションを開始すると `recotte_*` ツールが使えるようになります。ツール一覧、セキュリティと保存契約は [Source/Recotte.McpServer/README.md](Source/Recotte.McpServer/README.md) を参照してください。

## ライセンス

[GPL-3.0](LICENSE)
