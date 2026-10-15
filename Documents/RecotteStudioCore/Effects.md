# エフェクト対応

`EffectCatalog.Load(effectsDirectory)` は `effects`、`text`、`transitions` の Lua 定義から、内部名、日本語・英語名、分類、リテラルの既定時間、プロパティ名を静的に読み取ります。Lua の実行、動的な式の評価、プロパティの型・選択肢の推定は行いません。読み取れない定義と内部名の重複は `Diagnostics` に返します。

2026-10-14 にこの環境で確認した定義は通常31、文字7、トランジション15の合計53です。実際の件数はインストール内容に依存します。標準パスは `C:\Program Files\RecotteStudio\effects`。Core と `EffectCatalogProvider` は別のパスも指定できます。

## MCP

- `recotte_list_effects`: 全分類の一覧。`kind`、`tag`、`nameContains` で絞り込み。既定の `limit` は100。`editable` がtrueなのは通常エフェクトの `Alpha Fade` と `Alpha Fade Out` です。
- `recotte_list_object_effects`: `projectPath` と `target` を指定し、対応する適用済みフェードを取得します。`EffectIndex` は完全な `sub-objects` 配列での0始まりの番号です。
- `addFadeEffect`、`updateFadeEffect`、`removeFadeEffect`: 既存のプレビュー、SaveCopy、バックアップ付き上書きのバッチ操作として使用します。

追加例:

```json
{
  "type": "addFadeEffect",
  "target": { "layerIndex": 2, "objectKey": 1000 },
  "effectKey": "Alpha Fade",
  "duration": 0.5
}
```

変更例:

```json
{
  "type": "updateFadeEffect",
  "target": { "layerIndex": 2, "objectKey": 1000 },
  "effectIndex": 0,
  "effectKey": "Alpha Fade Out",
  "duration": 1.0
}
```

削除例:

```json
{
  "type": "removeFadeEffect",
  "target": { "layerIndex": 2, "objectKey": 1000 },
  "effectIndex": 0
}
```

対象はロックされていない Image / Video です。時間は秒単位で、正の値かつクリップの長さ以内に制限します。フェードインは先頭、フェードアウトは末尾に配置し、保存する `start-time` はクリップ相対です。曲線の編集は今回の範囲外です。追加時は `009FullProject` の保存済みフェードを複製します。変更時は既存フェードの曲線と未知フィールドを保持します。その他のエフェクトは保持します。削除後は後続の番号が変わるため、一覧を再取得するか同一バッチ内では大きい番号から削除してください。

`document.Capabilities.CanEditFadeEffects` はバージョン上の操作可否です。対象のロックや時間は操作時に検証します。Core には同名の操作レコード、`ProjectEditor` のメソッド、`document.GetFadeEffects(id)` があります。

## 検証状況

自動テストでは画像・動画の保存・再読込、保存済みサンプルとの構造一致、他フィールドの保持、更新・削除、プレビューの非変更性、失敗バッチのロールバック、不正入力とロックの拒否を確認します。MCPテストでは画像作成から追加、一覧取得、変更、削除、保存までを確認します。

画像への適用とクリップ相対時間の解釈は既存形式を使った実装であり、Recotte Studio 本体での受け入れ・描画は未確認です。

実機確認の手順:

1. MCPで画像・動画にフェードイン／アウトを追加して別名保存する。
2. Studioで開き、先頭・末尾の透明度変化を確認する。
3. 時間・種類を変更して再生し、削除後に他の設定が保持されることも確認する。
4. Studioで別名保存したファイルをMCPで再読込・検証する。
5. Studioのバージョンと結果を記録する。
