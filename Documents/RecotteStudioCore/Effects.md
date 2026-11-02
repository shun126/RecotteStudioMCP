# クリップのエフェクト

動画・挿絵・話者のキャラクターに、同じ操作でエフェクトを追加・変更・削除できます。トラック全体への適用ではありません。

## 対象と対応する種類

| 対象クリップ | レイヤー |
| --- | --- |
| `Video` | `Video` |
| `Image`、`Figure`（スタンプ・注釈テキスト） | `Annot` |
| `Speaker Character`（キャラクターの表示クリップ） | `Speaker` |

話者では `recotte_list_timeline` に出る `Speaker Character` の `objectKey` を指定します。台詞（`Speaker Voice`）や表情・動作（`Speaker Action`）は対象にしません。

対応する種類は `Alpha Fade`（フェードイン）、`Alpha Fade Out`（フェードアウト）、`Vibration`（振動）です。ロックされたレイヤー・クリップは編集できません。プレビュー用ロック（`st-locked`、`pv-locked`）は編集を妨げず、その値を保持します。

## MCP

- `recotte_list_effects`: インストール済みの通常・文字エフェクト・トランジションの定義一覧。`kind`、`tag`、`nameContains` で絞り込み、`editable` で対応種類を確認します。Lua は実行せず、リテラルのメタデータだけを読み取ります。
- `recotte_list_clip_effects`: `projectPath` と `target` を指定して適用済みのエフェクトを取得します。各項目に `EffectIndex`、`EffectKey`、`Start`、`Duration`、`Properties`（設定値）、`CanEdit` が入ります。未対応の種類も保存形式を識別できれば `CanEdit: false` で表示し、変更・削除はしません。
- `addClipEffect`、`updateClipEffect`、`removeClipEffect`: 既存のプレビュー・別名保存・バックアップ付き上書きのバッチ操作として使用します。

`EffectIndex` は完全な `sub-objects` 配列での0始まりの番号です。削除すると後続の番号が変わるため、一覧を再取得するか、同一バッチでは大きい番号から削除してください。

### 追加

以下は話者のキャラクタークリップを振動させる操作例です。対象IDは実際の一覧結果に置き換えてください。

```json
{
  "type": "addClipEffect",
  "target": { "layerIndex": 1, "objectKey": 1000 },
  "effectKey": "Vibration",
  "start": 0.2,
  "duration": 0.4,
  "effectProperties": { "SwingWidth": 12, "cycle": 0.08 }
}
```

- `duration` は必須で、正の秒数です。
- `start` はクリップ先頭からの相対秒です。省略すると0秒、フェードアウトだけは「クリップ長 − duration」です。
- 開始から終了まで、クリップ内に収まる必要があります。短いリアクション用に1秒未満も指定できます。
- 振動の `SwingWidth` は揺れ幅（pt、0以上、既定8）、`cycle` は周期（秒、0より大きい値、既定0.1）です。`effectProperties` を省略すると既定値を使います。
- フェードの曲線編集は対象外です。

### 変更・移動

```json
{
  "type": "updateClipEffect",
  "target": { "layerIndex": 1, "objectKey": 1000 },
  "effectIndex": 0,
  "start": 0.5,
  "effectProperties": { "SwingWidth": 6 }
}
```

`start`、`duration`、`effectProperties` のいずれかを指定します。省略した項目は保持します。時間移動は `start` の変更に統合し、専用の移動操作は設けません。エフェクトの種類を変える場合は削除して追加します。キーフレーム付きの設定値は書き換えませんが、設定を保持した時間移動は可能です。

### 削除

```json
{
  "type": "removeClipEffect",
  "target": { "layerIndex": 1, "objectKey": 1000 },
  "effectIndex": 0
}
```

## 保存・クリップの長さ変更

既存の曲線・未知フィールド・対象外のエフェクトは保持します。フェードの追加には `009FullProject` に保存された定義を使います。振動はインストール済み `effects/effects/vibration.lua` の既定値と単位、および既存の数値プロパティ形式から構成しています。

画像・動画・図形を `moveTimelineObject` で動かすと、エフェクトはクリップ相対の位置を維持します。長さを変える場合、末尾に揃ったフェードアウトは新しい末尾に追従し、途中に配置したエフェクトは開始位置を維持します。新しいクリップ長からはみ出す場合は `RC4302` で拒否します。

Core では `AddClipEffectOperation`、`UpdateClipEffectOperation`、`RemoveClipEffectOperation`、`ProjectEditor` の同名メソッド、`document.GetClipEffects(id)` を使用します。`document.Capabilities.CanEditClipEffects` はプロジェクトバージョン上の操作可否を表します。

## APIの整理

フェード専用の操作・型・能力名と、`recotte_list_object_effects` は廃止しました。互換エイリアスはありません。呼び出し元は上記のクリップ用APIへ更新してください。既存プロジェクト内のフェードは引き続き読み取り・編集できます。

## エラー

| コード | 内容 |
| --- | --- |
| `RC4901` | 対象外のクリップ |
| `RC4902` | `sub-objects` が不正 |
| `RC4903` | 指定番号が存在しない、または未対応のエフェクト |
| `RC4904` | 未対応の種類 |
| `RC4905` | 時間が不正、またはクリップからはみ出す |
| `RC4906` | 変更項目がない |
| `RC4907` | 設定名・値が不正、またはキーフレーム付きの設定 |
| `RC4207` | レイヤー・クリップがロックされている |

## 検証状況

自動テストでは画像・動画・図形・キャラクターへの追加、変更・移動・削除、保存・再読込、既存フェードとの構造一致、未知フィールド・曲線の保持、不正入力・ロックの拒否、失敗時のロールバックを確認します。MCP経由でも画像のフェードと動画・キャラクターの振動を検証します。

**Recotte Studio 本体での読み込み・描画・再保存は未確認です。** 特に振動の `category`・`eff-obj-type` と数値プロパティ形式は、振動を本体で保存したサンプルとの比較がまだできていません。自動テストはこの推定形式を読み書きできることの確認であり、本体互換性の確認とは区別します。

実機では、動画・挿絵・キャラクターにフェードと振動を付けて別名保存し、本体で表示・時間移動・設定変更・削除を確認してください。本体で再保存したファイルをMCPで再読込し、構造の差分も確認します。
