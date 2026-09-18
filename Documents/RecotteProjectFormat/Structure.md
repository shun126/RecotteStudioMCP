# `.ccproj` 書式

## 物理形式

現在の9サンプルは、すべて次の特徴を持ちます。

- JSONオブジェクトとして保存される
- `app_version` は `1.8.5.0`
- 文字列、数値、真偽値、配列、オブジェクトという通常のJSON型を使用する
- 時刻と長さは小数の秒で表現される

JSONのキー順序、インデント、数値の表記は意味の一部として扱うべきではありません。書き換え時には、未知のフィールドを削除しないことが重要です。

## ルートオブジェクト

| キー | 型 | 現在のサンプルでの役割 |
| --- | --- | --- |
| `app_version` | string | 保存したRecotte Studioのバージョン |
| `time` | string | 保存日時。例: `2026/07/22 11:05:06.199` |
| `setting` | object | プロジェクト設定 |
| `speakers` | array | 使用するキャラクター定義。キャラクターなしでは空 |
| `ui-setting` | object | UI状態。現在のサンプルでは空 |
| `file-items` | array | 外部素材の登録表 |
| `named-colors` | array | 組み込みの名前付き色定義 |
| `named-fig-styles` | array | 組み込みの図形スタイル定義 |
| `text-styles` | array | テキストスタイル定義 |
| `text-style-lib` | array | テキストスタイルライブラリ |
| `telop-frames` | array | テロップ枠定義 |
| `additional-actions` | array | 追加アクション。現在のサンプルでは空 |
| `layers` | array | タイムラインのレイヤー |

`001`〜`007` では、`named-colors` は7件、`named-fig-styles` は7件、`text-styles` と `text-style-lib` は各93件、`telop-frames` は4件です。これらの件数が常に固定かどうかは未確認です。

## `setting`

| キー | 型 | 観察された意味 |
| --- | --- | --- |
| `guid` | string | プロジェクト固有のGUID |
| `project-name` | string | プロジェクト名 |
| `bg-color` | number[4] | 背景色のRGBA値 |
| `duration` | number | プロジェクト全体の長さ（秒） |
| `position` | number | 編集位置（秒）と推定 |
| `fps` | number | フレームレート。現在は `60.0` |
| `size` | number[2] | プロジェクトの幅と高さ。現在は `[1920,1080]` |
| `dpi` | number | DPI。現在は `72.0` |
| `auto-duration` | boolean | 長さの自動調整設定 |
| `pix-ser-sec` | number | タイムラインの表示倍率と推定 |
| `auto-seek` | boolean | 自動シーク設定と推定 |
| `editor-info` | object | スナップなどのエディター状態 |
| `voice-link` | string | 音声連携方式。現在は `VOICEROID2_EachLayer` |
| `export-video-size` | number[2] | 動画出力サイズ設定 |
| `export-video-path` | string | 動画出力先 |
| `file-explorer-path` | string | ファイル選択UIで使用したパスと推定 |
| `frame-counter` | integer | 用途未確定。現在は `0` |
| `volume` | number | 全体音量と推定 |
| `mute` | boolean | 全体ミュートと推定 |
| `textrender-compatibility-1515` | boolean | 旧テキスト描画互換設定と推定 |

`editor-info` では `time-line-left`、`object-move-mode`、`object-snap`、`pv-object-snap`、`move-obj-key` を確認しています。

## レイヤー

空プロジェクトにも3レイヤーがあります。

| 配列位置 | `type` | 初期名 | 主な用途 |
| --- | --- | --- | --- |
| 0 | `Video` | `映像・音声1` | 動画 |
| 1 | `Speaker` | `話者1` | テキスト、音声、キャラクター |
| 2 | `Annot` | `注釈1` | 画像 |

各レイヤーで次のキーを確認しています。

| キー | 型 | 役割 |
| --- | --- | --- |
| `type` | string | レイヤー種別 |
| `name` | string | 表示名 |
| `locked` | boolean | ロック状態 |
| `selected` | boolean | 選択状態 |
| `visible` | boolean | 表示状態 |
| `volume` | number | レイヤー音量 |
| `mute` | boolean | ミュート状態 |
| `view-state` | integer | UI上の表示状態と推定 |
| `properties` | object | レイヤー設定 |
| `layer-objects` | array | タイムライン上のオブジェクト |

`Speaker` レイヤーは `voice-props` を持ちます。キャラクターを追加した場合は、使用可能なアクションを列挙する `actions` も追加されます。

### プロパティ値の共通形式

レイヤーとタイムラインオブジェクトの `properties` は、プロパティ名をキーとし、概ね次の形式を持ちます。

```json
{
  "PropertyName": {
    "p-type": 10,
    "p-subtype": 0,
    "p-cig": false,
    "p-value": "value",
    "keyframes": []
  }
}
```

| キー | 確認できた内容 |
| --- | --- |
| `p-type` | 内部の値型または編集UI種別を示す数値と推定 |
| `p-subtype` | 詳細な内部種別と推定 |
| `p-cig` | 用途未確定の真偽値 |
| `p-value` | 実際の値。型はプロパティごとに異なる |
| `keyframes` | キーフレーム配列。現在の最小サンプルでは空 |

`p-type` の意味をJSON型だけから確定することはできません。同じJSON型でも異なる `p-type` が使われるため、値を書き換える際は既存のメタデータを保持する必要があります。

## タイムラインオブジェクト

`layers[].layer-objects[]` では、共通して次のキーを確認しています。

- `type`, `name`, `objkey`
- `start-time`, `end-time`
- `properties`
- `selected`, `tl-locked`, `st-locked`, `pv-locked`
- `snap-to-end`, `start-snap`, `fkft`
- `sub-objects`, `keyframe-gui`

`objkey` はサンプル内で `1000` から採番されています。採番規則、上限、プロジェクト全体での一意性は追加検証が必要です。

### `Speaker Voice`

テキストだけの `002` と音声付きの `003`・`007` は、いずれも `Speaker` レイヤー内の `Speaker Voice` として保存されます。「テキスト専用」の別オブジェクト型は現在のサンプルでは確認されていません。

主な追加フィールドは次のとおりです。

| キー | 役割 |
| --- | --- |
| `text` | 表示文字列とスタイル情報 |
| `audio` | 音声制御点。実音声がある場合に存在 |
| `voice-hash` | 音声に関係する整数値。生成規則は未確定 |
| `voice-props` | 音声生成側の追加プロパティ |
| `properties.File.p-value` | `file-items[].ik` を参照。音声なしでは空文字列 |

`text` では次の構造を確認しています。

```json
{
  "wordwarp": true,
  "margin": [10.0, 10.0, 10.0, 10.0],
  "text": "こんにちは",
  "stext": [
    { "c": "s", "style": "話者1" },
    { "c": "t", "text": "こんにちは" }
  ],
  "lines": [
    { "align": 1 }
  ]
}
```

現在のサンプルでは、オブジェクトの `name`、`text.text`、`text.stext[]` 内の `text` が同じ文面です。どれが正本で、編集時にどこまで同期が必要かは未確定です。

### `Image`

`004OneImage` では `Annot` レイヤーに置かれます。`File` に加えて、`Angle`、`Bounds`、`Brightness`、`Contrast`、`CropBounds`、`Flip`、`Gamma`、`ImageMode`、`Opacity` を確認しています。

### `Video`

`005OneVideo` では `Video` レイヤーに置かれます。画像系の表示プロパティに加え、`AudioVolume`、`EndTime`、`LoopPlay`、`SetSpeed`、`SpeedStretch`、`StartTime`、`VideoOffset` と `audio` を確認しています。

### `Speaker Character`

`006` と `007` では `Speaker` レイヤーに置かれます。`Angle`、`Camera`、`CharBounds`、`CropBoundsRate`、`DefaultAction`、`Flip`、`OutSideBounds`、`TimeCurve`、`ViewMode` を確認しています。

キャラクターを追加すると、同じ `Speaker` レイヤーに次の変化が生じます。

- `properties.Speaker.p-value` にキャラクターキーが設定される
- `properties.VoiceLinker.p-value` が `Empty` から音声連携名へ変化する
- `properties.DefaultTextStyle.p-value` が話者用スタイルへ変化する
- `actions` にキャラクターのアクション一覧が追加される

## 外部素材の参照

`file-items[]` の要素では次を確認しています。

| キー | 型 | 役割 |
| --- | --- | --- |
| `ik` | string | 素材の内部キー。64桁の16進文字列 |
| `loc` | integer | 素材位置の区分と推定。現在は `0` |
| `rpath` | string | プロジェクトからの相対パス |
| `apath` | string | `file://` 形式の絶対パス |

音声、画像、動画では、タイムラインオブジェクトの `properties.File.p-value` と `file-items[].ik` が一致します。`ik` は実ファイルのSHA-256とは一致しないため、内容ハッシュと断定できません。

移動可能なプロジェクトを扱う場合は `rpath` を優先し、`apath` は元環境の補助情報として扱うのが安全です。Recotte Studio自身の探索優先順位は未確認です。

## キャラクターの参照

`speakers[]` の要素では次を確認しています。

| キー | 役割 |
| --- | --- |
| `key` | キャラクターキー |
| `name` | キャラクター表示名 |
| `file` | モデルファイルの絶対URI |
| `r-path` | モデルファイルへの相対表現 |

`006`〜`009` では、`Speaker` レイヤーの `properties.Speaker.p-value` が `speakers[].key` と一致します。`008` と `009` ではキャラクターごとに独立した `Speaker` レイヤーがあり、それぞれ対応する `Speaker Character` が配置されます。ボイスオブジェクトはキャラクターを直接参照せず、同じ `Speaker` レイヤー内に配置されることで関連付く構造に見えます。

## 既存実装との対応

[RecotteHelper](https://github.com/shun126/RecotteHelper) の字幕・翻訳処理は、次の経路を使用しています。

```text
layers[]
  -> type == "Speaker"
  -> layer-objects[]
  -> type == "Speaker Voice"
  -> text.stext[]
  -> text を持つ要素
```

字幕出力はさらに `start-time` と `end-time` を使用します。この経路は `002`、`003`、`007` の構造と一致しています。

現行の翻訳インポート処理は `text.stext[].text` のみを書き換え、`name` と `text.text` は更新しません。また、入力行数が対象テキスト数より少ない場合の境界確認を行っていません。Recotte Studio上で必要な同期範囲を検証してから、書き換え処理の仕様を確定する必要があります。

## 安全な読み書き方針

1. ルートがJSONオブジェクトであることを確認する。
2. `app_version` を読み、対応確認前のバージョンを区別する。
3. 必要なキーの存在と型を確認し、欠落時に安全に失敗する。
4. 未知のキー、プロパティメタデータ、組み込み定義を保持する。
5. 素材参照では `File.p-value` と `file-items[].ik` の整合性を保つ。
6. 時刻を小数として扱い、フレーム境界付近の誤差を許容する。
7. 書き換え前のファイルを保持し、Recotte Studioで再度開いて検証する。
