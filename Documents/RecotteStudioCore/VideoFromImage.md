# 画像から動画素材を作る

静止画から `.mp4` の動画素材を ffmpeg で作ります。ツールは 2 つあります。

- `recotte_create_video_from_image`: 静止画 1 枚に、ゆっくりしたズームやパンを付けます。
- `recotte_create_flipbook_video`: 複数の静止画を順に切り替えて、パラパラアニメのような動画にします。

どちらも画像とプロジェクトは変更しません。作った動画は、既存の `addVideoObject` でプロジェクトに配置します。

ffmpeg は同梱していません。インストールして、設定画面の「ffmpeg」で指定するか、PATH に追加してください。見つからない場合、ツールは `MCP_FFMPEG_UNAVAILABLE` を返します。

## 入力と出力

| 項目 | 内容 |
| --- | --- |
| 画像 | Recotte Studio が対応する PNG、JPEG、BMP、GIF。アニメーション GIF は最初のコマだけを使います |
| プロジェクト | 解像度とフレームレートを読むだけです。1920×1080 のプロジェクトに限ります |
| 出力 | `.mp4`。H.264 Constrained Baseline、`yuv420p`、CRF 18、無音の AAC-LC 44.1 kHz ステレオ、`+faststart` |

出力形式は、Recotte Studio で読み込めたサンプル `Documents/RecotteProjects/005OneVideo/Sample.mp4`（H.264 Constrained Baseline、レベル 4.2、`yuv420p`、AAC-LC 44.1 kHz ステレオ）に合わせています。H.264 と AAC は Recotte Studio の公式の対応コーデックです。High プロファイルと、音声トラックのない動画は、まだ Recotte Studio で確認していないため使いません。

1920×1080 以外のプロジェクトは `MCP_VIDEO_PROJECT_SIZE_UNSUPPORTED` で断ります。`addVideoObject` が使う動画のひな形は表示範囲が 1920×1080 に固定されており、Core は動画の解像度を読まないためです。

## 引数

| 引数 | 内容 | 既定値・範囲 |
| --- | --- | --- |
| `projectPath` | 解像度とフレームレートを読むプロジェクト | 必須 |
| `imagePath` | 元の画像 | 必須 |
| `outputPath` | 書き出す動画。拡張子は `.mp4` | 必須 |
| `duration` | 動画の長さ（秒）。フレーム数は「秒数 × フレームレート」を四捨五入した値です | 0.5〜60 |
| `allowOverwrite` | true の場合だけ既存ファイルを置き換えます | false |
| `motion` | `none`、`zoomIn`、`zoomOut`、`panLeft`、`panRight`、`panUp`、`panDown`。パンは見ている位置が動く向きで、`panRight` では画像が左へ流れます | `zoomIn` |
| `zoom` | ズームの最大倍率。パンではこの倍率に拡大してから動かします | 1.2（1.05〜1.5） |
| `focusX`、`focusY` | 画面に対する 0〜1 の割合。ズームではこの点が画面上の同じ位置にとどまり、パンでは動かない側の位置になります。顔などを字幕の帯から外すのに使います | 0.5、0.5 |
| `easing` | `easeInOut`（ゆっくり始まりゆっくり終わる）、`linear`（一定の速さ） | `easeInOut` |
| `fit` | `cover`（切り抜いて画面を埋める）、`contain`（全体を収めて余白を付ける） | `cover` |
| `background` | `contain` の余白の色。`#RRGGBB` | `#000000` |

結果には、出力先、解像度、フレームレート、実際の長さ、フレーム数、使った ffmpeg とそのバージョン、所要時間と、`addVideoObject` 操作のひな形 `suggestedOperation` が入ります。`suggestedOperation` の `start` と `end` を配置したい位置に合わせ、`projectPath` を保存するプロジェクトにして、`recotte_apply_operations_and_save_copy` などに渡してください。

```json
{
  "projectPath": "D:\\Movies\\MyMovie.ccproj",
  "imagePath": "D:\\Movies\\Assets\\castle.jpg",
  "outputPath": "D:\\Movies\\Assets\\castle.mp4",
  "duration": 5,
  "motion": "zoomIn",
  "focusY": 0.4
}
```

## パラパラアニメ

`recotte_create_flipbook_video` は、`frames` に並べた静止画を順に表示します。LLM が描いたり選んだりした複数の画像を、1 本の動画にまとめるのに使えます。

```json
{
  "projectPath": "D:\\Movies\\MyMovie.ccproj",
  "outputPath": "D:\\Movies\\Assets\\walk.mp4",
  "frameDuration": 0.125,
  "loops": 3,
  "frames": [
    { "imagePath": "D:\\Movies\\Assets\\walk1.png" },
    { "imagePath": "D:\\Movies\\Assets\\walk2.png" },
    { "imagePath": "D:\\Movies\\Assets\\walk3.png", "duration": 0.25 },
    { "imagePath": "D:\\Movies\\Assets\\walk2.png" }
  ]
}
```

| 引数 | 内容 | 既定値・範囲 |
| --- | --- | --- |
| `frames` | 表示する順に並べた画像。`imagePath` と、任意の `duration`（秒）を持ちます。同じ画像を何度使っても構いません | 1〜600 個。異なる画像は 200 枚まで |
| `frameDuration` | `duration` を省いた画像の表示時間（秒） | 0.125（1 秒に 8 枚）。0.01〜10 |
| `loops` | 全体を繰り返す回数 | 1（1〜100） |
| `fit` | `contain`（全体を収めて余白を付ける）、`cover`（切り抜いて画面を埋める） | `contain` |
| `background` | 余白と、画像の透明な部分に見える色。`#RRGGBB` | `#FFFFFF` |
| `projectPath`、`outputPath`、`allowOverwrite` | 1 枚の場合と同じです | |

- 各画像の表示時間は、プロジェクトのフレーム数に四捨五入します。最短は 1 フレームです。60fps のプロジェクトで `frameDuration` が 0.125 秒なら、1 枚あたり 8 フレームになります。
- 繰り返しを含めた長さは 60 秒までです。
- 結果の `frames` には、1 回目の繰り返しで各画像が始まる時刻（`start`）、表示時間（`duration`）、フレーム数（`frameCount`）が入ります。台詞のタイミングに合わせるときに使えます。
- 既定の `fit` と `background` が 1 枚の場合と違うのは、線画など透明な部分を持つ画像が多いためです。切り抜かずに、紙のような白い背景に描きます。

## ffmpeg の実行

- 外部プロセスの起動は `FfmpegProcess` だけが行います。シェルを経由せず、引数は 1 つずつ渡し、標準入力は閉じます。
- ffmpeg のフィルタに入るのは、検証済みの数値と固定のキーワードだけです。数値は地域設定に関係なく `.` を小数点にします。
- 入力と出力のパスには `file:` を付け、パスが URL などとして解釈されないようにします。
- ズーム／パンは、出力の 2 倍の大きさに拡大した画像に `zoompan` をかけます。拡大量が整数に丸められて映像ががたつくのを防ぐためです。
- 同じフォルダの一時ファイルに書き、成功したときだけ出力先に移動します。失敗、時間切れ、キャンセルのときは一時ファイルを消します。
- 動画の作成は同時に 1 つだけ行います。時間切れは「60 秒 + 動画の長さ × 10」です。
- 実行前に `ffmpeg -version` で、指定されたファイルが ffmpeg であることを確かめます。
- パラパラアニメは 2 段階で作ります。まず異なる画像ごとに 1 回ずつ、画面サイズの背景に描いた PNG を一時フォルダ（`%TEMP%\RecotteStudioMCP`）に作ります。次に、その PNG を表示時間付きで並べた concat の一覧から動画を作ります。一時フォルダは成功しても失敗しても消します。
- concat の一覧では、各画像の時間の単位をプロジェクトのフレームレートにそろえます（`option framerate`）。表示時間がフレームの境目にぴったり合い、指定したフレーム数どおりに切り替わります。この指定には concat の safe モードを外す必要があります（`-safe 0`）が、一覧に書くのはツールが作った `0001.png` のようなファイル名だけです。

## エラーコード

| コード | 内容 |
| --- | --- |
| `MCP_FFMPEG_UNAVAILABLE` | ffmpeg が設定されておらず PATH にもない、または指定されたファイルが ffmpeg ではない |
| `MCP_FFMPEG_FAILED` | ffmpeg が失敗した。終了コードと ffmpeg のエラー出力の末尾を含みます |
| `MCP_FFMPEG_TIMEOUT` | 時間切れ |
| `MCP_VIDEO_ARGUMENT_INVALID` | `duration`、`motion`、`zoom`、`focusX`/`focusY`、`easing`、`fit`、`background`、`frames`、`frameDuration`、`loops` のいずれかが範囲外。パラパラアニメでは、繰り返しを含めて 60 秒を超える場合も含みます |
| `MCP_VIDEO_IMAGE_UNSUPPORTED` | 画像が PNG、JPEG、BMP、GIF のどれでもない。パラパラアニメでは `frames[番号]` でどの画像かを示します |
| `MCP_VIDEO_PROJECT_SIZE_UNSUPPORTED` | プロジェクトが 1920×1080 ではない |
| `MCP_PATH_EXTENSION`、`MCP_PATH_OUTPUT_EXISTS`、`MCP_PATH_FILE_NOT_FOUND`、`MCP_PATH_OUTSIDE_WORKSPACE` | パスの検証エラー |

## 検証状況

- 自動テスト: 引数の検証と組み立て、失敗・時間切れ・キャンセル時の後始末、既存ファイルの扱いを確認しています。ffmpeg がある環境では、実際に作った動画が H.264 Constrained Baseline、1920×1080、60fps、AAC であることを ffprobe で確かめ、`addVideoObject` で配置して保存できることも確認します。パラパラアニメは、赤・緑・青の画像を 7・8・1 フレームずつ 2 回繰り返し、全 32 フレームの色が指定どおりの順に並ぶことを確認します。
- 未確認: 作った動画を Recotte Studio で読み込み、再生し、書き出せること。
