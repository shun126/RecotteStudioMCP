# Recotte.McpServer

`Recotte.McpServer` は、LLM が `.ccproj` を調査し、安全に編集・保存するための薄い stdio MCP アダプターです。構成は `Recotte.McpServer` → `Recotte.Core` → `.ccproj` です。Recotte Studio 本体を起動せず、Studio がインストールされていない環境でも Core の対応範囲にある生成・編集・保存が完結します。CLI も子プロセスとして起動しません。

Coreと同様に、4要素のバージョン番号が `1.8.x.x` のプロジェクトを同一互換系列として受け入れます。その他の未知バージョンは編集・保存しません。

## ビルドと起動

前提は .NET 9 SDK です。

```powershell
dotnet build Source/Recotte.McpServer/Recotte.McpServer.csproj
dotnet run --project Source/Recotte.McpServer
```

デフォルトでは、既存ディレクトリ内の任意の `.ccproj` を入出力に指定できます。アクセス範囲を制限したい場合だけ `--workspace "C:/RecotteWorkspace"` または `RECOTTE_WORKSPACE_ROOT` を指定します。両方がある場合は引数を優先し、指定したディレクトリが存在しない場合はサーバーが明示的なエラーで起動を中止します。stdout は MCP プロトコル専用で、通常ログは stderr に送ります。

公式 C# SDK `ModelContextProtocol` 0.4.0-preview.3 の Generic Host、stdio transport、attribute-based tool discovery を使用しています。

## Codex への登録

Codex の `config.toml` に次を追加します。

```toml
[mcp_servers.recotte]
command = "dotnet"
args = [
  "run", "--project", "C:/RecotteStudioMCP/Source/Recotte.McpServer"
]
```

または `codex mcp add recotte -- dotnet run --project C:/RecotteStudioMCP/Source/Recotte.McpServer` で登録できます。

## セキュリティと保存契約

入力・出力は絶対パスへ正規化され、可能ならシンボリックリンクの最終参照先も解決されます。`.ccproj` 以外、存在しない入力、存在しない出力ディレクトリ、入力と同一の出力を拒否します。`--workspace` または `RECOTTE_WORKSPACE_ROOT` を指定した場合は、そのワークスペース外も拒否します。ディレクトリを作りません。

各呼び出しは新しい Core document を読み込むステートレス処理です。編集は Core の一括トランザクションで Atomic に行います。`SaveCopy` は元ファイルを変更せず別名保存します。明示的な `Overwrite` ツールだけが Core の `Save()` を呼び、必須バックアップ、外部変更検出、完全な Validation、一時ファイルの再読み込み検証、Semantic JSON 比較を経て元ファイルを置換します。強制・unsafe 保存はありません。パス単位の排他により同じ保存先への処理を直列化します。未知フィールドは Core が保持し、MCP は JSON を直接操作しません。Core の RC 診断コードは文字列を変えず返します。

Recotte Studio はプロジェクト生成、編集、検証、保存に使用しません。必要な場合、人間が生成済み `.ccproj` を後から Studio で開きます。サーバーが生成後や保存後に自動で開くことはありません。

音声追加、キャラクター追加、`voice-hash` や file-item key の推測は未対応です。Core が公開しない処理をサーバーで推測しません。

## ツール

| ツール | 概要 |
|---|---|
| `recotte_inspect_project` | 概要、Capability、検証を取得 |
| `recotte_validate_project` | 構造化診断を取得 |
| `recotte_list_layers` | レイヤー一覧 |
| `recotte_list_timeline` | Core の時間範囲規則によるタイムライン一覧 |
| `recotte_find_timeline_objects` | レイヤー、型、本文、時間で候補検索 |
| `recotte_get_capabilities` | バージョン別の Core Capability |
| `recotte_inspect_assets` | Core に安全な集約 API がない現状は structured unsupported |
| `recotte_preview_operations` | ファイルを作らない Atomic プレビュー |
| `recotte_preview_operations_and_save` | 編集後状態と SaveCopy 計画のプレビュー |
| `recotte_apply_operations_and_save_copy` | Atomic 編集して別名保存 |
| `recotte_preview_operations_and_overwrite` | ファイルを変更せず、安全な上書き可否・競合・予定バックアップを確認 |
| `recotte_apply_operations_and_overwrite` | Core `Save()` で必須バックアップ付きの安全な上書き |
| `recotte_add_text_sequence_and_save_copy` | Core が時刻を計算する連続テキスト配置 |
| `recotte_create_project` | 組み込み済み1.8.5.0テンプレートから厳格検証付きで新規作成 |
| `recotte_create_project_from_template` | テンプレートを変更せず操作して別名保存 |

対応操作は `updateSpeakerText`、`moveTimelineObject`、`addTextOnlySpeakerVoice`、`removeTimelineObject`、`addImageObject`、`addVideoObject`、`addAnnotationText` です。
`removeTimelineObject` はロックされていない任意のタイムラインオブジェクトを削除します。素材を参照する最後の
オブジェクトを削除した場合は未参照になった `file-items` 登録も整理しますが、ディスク上の素材ファイルは削除しません。
`addAnnotationText` は指定した注釈レイヤーへ文字クリップを追加します。BGM、画像切替、間、効果音など、生成後の編集作業に残したいヒントに使用できます。

## 入力例

```json
{"projectPath":"C:/RecotteWorkspace/Base.ccproj"}
```

安全な新規作成では `recotte_create_project` を使用します。Core が組み込みテンプレートを検証し、操作適用後の
一時ファイルを再読み込みして新規作成固有の検証に合格した場合だけ出力先へ配置します。

```json
{
  "projectName":"New Project",
  "outputPath":"C:/RecotteWorkspace/New Project.ccproj",
  "allowOverwrite":false,
  "operations":[]
}
```

```json
{
  "projectPath":"C:/RecotteWorkspace/Base.ccproj",
  "outputPath":"C:/RecotteWorkspace/Result.ccproj",
  "allowOverwrite":false,
  "operations":[{
    "type":"addTextOnlySpeakerVoice",
    "layer":{"name":"朗読"},
    "text":"春はあけぼの。","start":0,"end":3
  }]
}
```

```json
{
  "projectPath":"C:/RecotteWorkspace/Base.ccproj",
  "outputPath":"C:/RecotteWorkspace/WithNotes.ccproj",
  "allowOverwrite":false,
  "operations":[{
    "type":"addAnnotationText",
    "layer":{"name":"注釈"},
    "text":"編集メモ：ここから軽快なBGM。ナレーションより控えめに。",
    "start":5,"end":10
  }]
}
```

```json
{
  "projectPath":"C:/RecotteWorkspace/Base.ccproj",
  "outputPath":"C:/RecotteWorkspace/Makuranosoushi.ccproj",
  "layer":{"name":"朗読"},
  "start":0,"defaultDuration":3,"gap":0.2,"allowOverwrite":false,
  "items":[{"text":"春はあけぼの。","duration":3},{"text":"やうやう白くなりゆく山ぎは。"}]
}
```

## 推奨ワークフロー

* 調査: `recotte_inspect_project` → `recotte_validate_project` → `recotte_list_timeline`
* テキスト追加: `recotte_get_capabilities` → `recotte_preview_operations_and_save` → `recotte_apply_operations_and_save_copy`
* 朗読テンプレート: `recotte_inspect_project` → `recotte_add_text_sequence_and_save_copy` → `recotte_validate_project`

結果は `success`、`category`、`errorCode`、`message`、`data`、`diagnostics` の共通形です。想定外例外では型、短いメッセージ、相関 ID だけを返し、スタックトレースをレスポンスに含めません。
