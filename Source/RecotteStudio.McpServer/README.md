# RecotteStudio.McpServer

`recotte_get_generation_rules` は、ビルド時に埋め込んだ `Documents/RecotteStudioCore/GenerationRules.md` をMarkdownで返します。配布先の作業フォルダやソースツリーには依存しません。MCP初期指示と生成・編集ツールの説明から取得を案内します。リリースZIPにもルールのMarkdownファイルを収録します。

エフェクトの一覧取得と画像・動画へのフェード追加・変更・削除に対応しています。[使用例と検証状況](../../Documents/RecotteStudioCore/Effects.md)を参照してください。

台詞を SubRip 字幕（.srt）として書き出せます。`recotte_list_subtitle_cues` で台詞を一覧し、翻訳したテキストを `recotte_export_subtitles` に渡すと、英語や中国語などの字幕を作成できます。[仕組みとエラーコード](../../Documents/RecotteStudioCore/Subtitles.md)を参照してください。

ffmpeg を指定すると、`recotte_create_video_from_image` で静止画にズームやパンを付けた動画素材（`.mp4`）を、`recotte_create_flipbook_video` で複数の静止画をつないだパラパラアニメを作れます。[仕様とエラーコード](../../Documents/RecotteStudioCore/VideoFromImage.md)を参照してください。

`RecotteStudio.McpServer` は、[RecotteStudio.Core](../RecotteStudio.Core/README.md) の機能を Streamable HTTP MCP ツールとして公開する WinUI 3 アプリです。ウィンドウを閉じるとサーバーは停止します。

接続先は `http://127.0.0.1:8765/mcp` です。全てのリクエストに GUI が生成した `Authorization: Bearer <token>` ヘッダーが必要です。待受先はループバックのみで、`Host` と `Origin` を検証します。

利用手順、ビルド、Codex と Claude Code の接続方法は[ルート README](../../README.md)を参照してください。既存の stdio 実行ファイルは廃止しました。
