# 立ち絵の表示時間・位置・サイズ

`recotte_list_timeline` で対象の `objectType: "Speaker Character"` と `layerIndex`、`objectKey` を取得します。`capabilities.canMove` が表示時間、`capabilities.canSetBounds` が位置・サイズを変更できるかを表します。

次の操作を `recotte_preview_operations` で確認し、`recotte_apply_operations_and_save_copy` などの `operations` に渡します。対象IDは実際の一覧の値に置き換えてください。

```json
[
  {
    "type": "moveTimelineObject",
    "target": { "layerIndex": 1, "objectKey": 1000 },
    "start": 2,
    "end": 12
  },
  {
    "type": "setObjectBounds",
    "target": { "layerIndex": 1, "objectKey": 1000 },
    "x": 100,
    "y": 50,
    "width": 400,
    "height": 800
  }
]
```

この例では2秒から12秒まで立ち絵を表示し、左上を (100, 50)、サイズを400×800ピクセルにします。縦横比を保つには幅と高さを同じ比率で変えてください。`setObjectBounds` で省略した値は維持されます。

立ち絵の表示時間を変えても同じレイヤーの台詞・表情・動作の時刻は変わりません。終端に付いたフェードアウトは新しい終端に追従します。新しい長さに収まらないエフェクトがある場合は `RC4302` で失敗します。終了時刻は開始時刻より後にしてください（`RC4301`）。レイヤーやタイムラインのロックは尊重し（`RC4207`）、プレビュー用ロックは保持したまま編集します。キーフレーム付きの位置・サイズの変更は `RC4912` で失敗します。
