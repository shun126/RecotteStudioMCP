# 音声未生成の台詞を差し替える

既存の `updateSpeakerText` 操作で、音声生成前の台詞を差し替えられます。`addTextOnlySpeakerVoice` で追加した台詞にも使えます。

1. `recotte_list_timeline` で対象の `layerIndex` と `objectKey` を確認します。`capabilities.canUpdateText` が `true` の台詞を選びます。`hasAudio: false` が音声未生成の台詞です。
2. 以下の操作を `recotte_preview_operations` に渡して確認します。
3. 同じ操作を `recotte_apply_operations_and_save_copy` に渡して別名保存するか、`recotte_apply_operations_and_overwrite` でバックアップ付き上書き保存します。

```json
{
  "projectPath": "D:\\Movies\\gameplay.ccproj",
  "outputPath": "D:\\Movies\\gameplay-replaced.ccproj",
  "allowOverwrite": false,
  "operations": [
    {
      "type": "updateSpeakerText",
      "target": { "layerIndex": 1, "objectKey": 1000 },
      "text": "敵が来た！\n距離を取ろう。"
    }
  ]
}
```

上記は別名保存ツールの引数です。プレビューと上書き保存では `projectPath` と `operations` を渡します。ID は例なので実際の一覧結果に置き換えてください。複数の台詞も1回の `operations` で更新できます。

台詞の名前・本文・対応する装飾テキストを同期して更新します。既存クリップの識別子、配置レイヤー、開始・終了時刻は維持します。改行は `\n` として渡せます。更新後の発話に合わせて時間を調整する場合は、別途 `moveTimelineObject` を使用してください。

音声未生成の台詞を差し替えても音声は生成しません。`RC4208` の音声再生成警告も発生しません。音声生成済みの場合は既存音声を維持して `RC4208` を返すため、Recotte Studio で音声を再生成してください。

ロックされた台詞やレイヤー、未対応の複雑な装飾を持つ台詞は更新できません。保存したプロジェクトを Recotte Studio で開くと、差し替え後の台詞を確認できます。
