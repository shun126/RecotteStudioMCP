# 字幕ファイルの書き出し

プロジェクトの台詞を SubRip 字幕ファイル（`.srt`）として書き出します。台詞を翻訳して渡すと、英語・中国語などの字幕ファイルも作成できます。プロジェクトは変更しません。

## 字幕になる台詞

`RecotteProjectDocument.GetSubtitleCues()` は、テキストを持ち、終了時刻が開始時刻より後の Speaker Voice を、タイムライン順に返します。

- 字幕の表示時間は、各台詞の開始・終了時刻と同じです。
- 話者の異なる台詞が重なる場合は、字幕も重ねて書き出します。
- 字幕（テロップ）を表示しない設定の台詞も含みます。
- 音声の有無は問いません。

## 書き出す形式

`SubRipFormatter.Format(cues)` が整形します。

- 文字コードは BOM 付き UTF-8、改行は CRLF です。BOM により、Windows の動画プレーヤーでも中国語などが文字化けしにくくなります。
- 字幕番号は 1 から振ります。
- 時刻は `HH:MM:SS,mmm` 形式で、ミリ秒未満は四捨五入します。
- 台詞の改行はそのまま字幕の改行になります。SubRip では空行が字幕の区切りになるため、台詞内の空行と行末の空白は除きます。

## MCP

- `recotte_list_subtitle_cues`: `projectPath` の台詞を、`layerIndex`、`objectKey`、`layerName`、`start`、`end`、`text` で一覧します。`objectKey` はレイヤーをまたいで重複することがあるため、台詞は `layerIndex` と `objectKey` の組で特定します。
- `recotte_export_subtitles`: `projectPath` の台詞を `outputPath` に書き出します。`outputPath` の拡張子は `.srt` に限ります。既存ファイルは `allowOverwrite` が true の場合だけ置き換えます。一時ファイルに書いてから置き換えるため、失敗しても書きかけのファイルは残りません。

`texts` を省略すると、プロジェクトの台詞をそのまま書き出します。翻訳した字幕を作る手順は次のとおりです。

1. `recotte_list_subtitle_cues` で台詞を一覧します。
2. MCP クライアントが各台詞を翻訳します。台詞の表示時間内に読める長さにし、長い行は `\n` で改行します。
3. `recotte_export_subtitles` の `texts` に、全ての台詞の訳文を 1 つずつ渡します。ファイル名は `MyMovie.en.srt`、`MyMovie.zh-Hans.srt` のように言語ごとに分けます。

```json
{
  "projectPath": "D:\\Movies\\MyMovie.ccproj",
  "outputPath": "D:\\Movies\\MyMovie.en.srt",
  "allowOverwrite": false,
  "texts": [
    { "layerIndex": 1, "objectKey": 1002, "text": "Hello" },
    { "layerIndex": 2, "objectKey": 1002, "text": "Hi there!" }
  ]
}
```

翻訳前と翻訳後の台詞が 1 つのファイルに混ざらないよう、`texts` が台詞と 1 対 1 で対応しない場合はファイルを書き出しません。

| エラーコード | 内容 |
| --- | --- |
| `MCP_SUBTITLE_NO_CUES` | 字幕にする台詞がない |
| `MCP_SUBTITLE_TEXT_MISSING` | 訳文のない台詞がある |
| `MCP_SUBTITLE_TEXT_DUPLICATE` | 同じ台詞の訳文が複数ある |
| `MCP_SUBTITLE_TEXT_UNKNOWN` | 該当する台詞のない訳文がある |
| `MCP_SUBTITLE_TEXT_EMPTY` | 訳文が空 |
| `MCP_PATH_EXTENSION` | `outputPath` が `.srt` ではない |
| `MCP_PATH_OUTPUT_EXISTS` | `outputPath` が既にあり、`allowOverwrite` が false |

翻訳は MCP クライアントが行い、MCP サーバーは翻訳サービスに接続しません。
