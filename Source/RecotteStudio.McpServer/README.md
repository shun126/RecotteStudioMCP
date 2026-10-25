# RecotteStudio.McpServer

音声未生成の台詞は `updateSpeakerText` 操作で差し替えられます。`recotte_list_timeline` の対象IDを指定し、既存の時刻やレイヤーを維持して保存できます。[台詞の差し替え例](../../Documents/RecotteStudioCore/DialogueReplacement.md)を参照してください。

`recotte_extract_video_frames` で動画から解析用の画像と素材内の時刻を抽出できます。一定間隔、場面変化、両方の組み合わせに対応し、JPEG と JSON を保存します。[使用例と制限](../../Documents/RecotteStudioCore/VideoFrameExtraction.md)を参照してください。

`recotte_get_generation_rules` は、実行ファイルと同じフォルダにある編集可能な `GenerationRules.md` を呼び出しのたびに読み込み、Markdownで返します。ワークスペースの場所には依存しません。ファイルがなくてもサーバーは停止せず、アクティビティに警告を記録して `available=false` と空の本文を返します。MCP初期指示と生成・編集ツールの説明から取得を案内します。リリースZIPにもファイルを収録します。

エフェクトの一覧取得と画像・動画へのフェード追加・変更・削除に対応しています。[使用例と検証状況](../../Documents/RecotteStudioCore/Effects.md)を参照してください。

台詞を SubRip 字幕（.srt）として書き出せます。`recotte_list_subtitle_cues` で台詞を一覧し、翻訳したテキストを `recotte_export_subtitles` に渡すと、英語や中国語などの字幕を作成できます。[仕組みとエラーコード](../../Documents/RecotteStudioCore/Subtitles.md)を参照してください。

キャラクターは、Recotte Studio で保存した別のプロジェクト（ドナー）から操作 `importCharacter` で取り込めます。`recotte_list_characters` でドナーのキャラクターと `speakerKey` を確認してください。キャラクターを新しく作ることはできません。

縦動画（ショート）にも対応しています。`recotte_create_project` に `width: 1080`、`height: 1920` を指定すると縦長のプロジェクトを作れます。作成済みのプロジェクトは操作 `setProjectSize` で画面サイズを変えられ、字幕枠・キャラクター・画像・動画が新しい画面に合わせて配置し直されます。配置の規則は[生成ルール](../../Documents/RecotteStudioCore/GenerationRules.md)を参照してください。

ffmpeg を指定すると、`recotte_create_video_from_image` で静止画にズームやパンを付けた動画素材（`.mp4`）を、`recotte_create_flipbook_video` で複数の静止画をつないだパラパラアニメを作れます。[仕様とエラーコード](../../Documents/RecotteStudioCore/VideoFromImage.md)を参照してください。

`RecotteStudio.McpServer` は、[RecotteStudio.Core](../RecotteStudio.Core/README.md) の機能を Streamable HTTP MCP ツールとして公開する WinUI 3 アプリです。ウィンドウを閉じるとサーバーは停止します。

接続先は `http://127.0.0.1:8765/mcp` です。全てのリクエストに GUI が生成した `Authorization: Bearer <token>` ヘッダーが必要です。待受先はループバックのみで、`Host` と `Origin` を検証します。

利用手順、ビルド、Codex と Claude Code の接続方法は[ルート README](../../README.md)を参照してください。既存の stdio 実行ファイルは廃止しました。
