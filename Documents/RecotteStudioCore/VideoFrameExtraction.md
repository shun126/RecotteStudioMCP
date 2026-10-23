# 動画から解析用の画像を抽出する

`recotte_extract_video_frames` は、既存動画から JPEG と時刻付きの JSON を作ります。実況台本を生成する前に、映像を確認するための機能です。動画の内容理解・台詞生成・プロジェクトへの追加は行いません。

ffmpeg は既存の動画生成機能と同じ設定を使用します。設定画面で `ffmpeg.exe` を指定するか、PATH に登録してください。

## 使用例

最初に `recotte_inspect_assets` でプロジェクトが参照する動画ファイルを調べ、実在するローカルパスを `videoPath` に渡します。

```json
{
  "videoPath": "D:\\Movies\\gameplay.mp4",
  "outputPath": "D:\\Movies\\gameplay-analysis.json",
  "mode": "combined",
  "start": 0,
  "duration": 60,
  "interval": 5,
  "sceneThreshold": 0.3,
  "maxFrames": 60,
  "maxWidth": 1280
}
```

`outputPath` は、既存フォルダ内の新しい `.json` ファイルにします。画像はその隣に作る一意な `gameplay-analysis.frames-<ID>` フォルダへ保存します。既存の JSON は上書きしません。失敗・キャンセル時は今回の画像と一時ファイルを削除します。プロジェクトと元動画は変更しません。作業フォルダが設定されていれば、入出力ともその範囲内である必要があります。

| 引数 | 内容 | 初期値・範囲 |
|---|---|---|
| `mode` | `interval`（一定間隔）、`scene`（場面変化）、`combined`（両方） | `interval` |
| `start` | 素材内の開始秒 | 0、0～86400 |
| `duration` | 解析範囲の秒数 | 60、0.1～600 |
| `interval` | 前回抽出から次の定期抽出までの秒数 | 5、0.1～600 |
| `sceneThreshold` | 場面変化のしきい値。小さいほど敏感 | 0.3、0より大きく1以下 |
| `maxFrames` | 抽出枚数の上限 | 60、1～120 |
| `maxWidth` | JPEG の最大幅。縦横比を維持し、拡大しない | 1280、160～1920 |

すべてのモードで範囲内の最初のフレームを含めます。一定間隔の抽出時刻は、利用可能なフレームの時刻に丸められます。`combined` では場面変化の抽出も次の定期抽出までの間隔をリセットします。範囲は開始を含み、終了を含みません。処理時間は1回最大10分です。

戻り値と JSON の `frames` は、`index`、`sourceTime`、`imagePath` を持ちます。`sourceTime` は動画の最初のデコード済みフレームを0秒とした素材内の時刻です。プロジェクト上の配置・トリミング・再生速度は反映していません。台詞配置への変換は別途必要です。`limitReached: true` は枚数上限に到達したことを示し、後続の候補が残っている可能性があります。

画像はパスとして返し、MCP 応答への画像埋め込みは行いません。画像の閲覧には、利用するクライアントのローカル画像読み込み機能が必要です。場面変化は ffmpeg の画面差分によるもので、ゲーム内イベントの意味を判定するものではありません。

## エラー

| コード | 内容 |
|---|---|
| `MCP_FRAME_ARGUMENT_INVALID` | 抽出条件が範囲外、または未知のモード |
| `MCP_FRAME_NO_FRAMES` | 指定範囲に抽出可能な動画フレームがない |
| `MCP_FRAME_TIMESTAMPS_INVALID` | 画像と実時刻を対応付けられない |
| `MCP_PATH_OUTPUT_EXISTS` | 出力 JSON が既に存在する |
| `MCP_FFMPEG_UNAVAILABLE` | ffmpeg が未設定、または起動確認に失敗 |
| `MCP_FFMPEG_FAILED` | デコード・画像出力に失敗 |
| `MCP_FFMPEG_TIMEOUT` | 処理が時間制限を超過 |

ほかに既存の `MCP_PATH_*` エラーを返します。キャンセルは既存ツールと同様に伝播します。
