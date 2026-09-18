# Recotte Studio プロジェクト書式解析

このディレクトリは、Recotte Studio が生成したプロジェクトファイル（`.ccproj`）のサンプルから確認できた書式を記録します。実ファイルと管理方針は [Samples/RecotteProjects](../../Samples/RecotteProjects/README.md) を参照してください。

この文書は Recotte Studio の公式仕様ではありません。書式を書き換える場合は、未知のキーと値を保持し、元ファイルのバックアップを作成することを推奨します。

## 対象

- Recotte Studio `1.8.5.0` が生成した `001EmptyProject` から `009FullProject`
- JSON として保存されたプロジェクト本体
- プロジェクトから参照される音声、画像、動画、キャラクターモデル

詳細なファイル構造、レイヤー、オブジェクト、参照関係は [Structure.md](Structure.md) に記載しています。

## サンプルと確認結果

| サンプル | `speakers` | `file-items` | タイムラインオブジェクト | 主な確認目的 |
| --- | ---: | ---: | --- | --- |
| `001EmptyProject` | 0 | 0 | なし | 空プロジェクトの基準構造 |
| `002OneText` | 0 | 0 | `Speaker Voice` × 1 | 音声を持たないテキスト |
| `003OneVoice` | 0 | 1 | `Speaker Voice` × 1 | WAVを参照する音声 |
| `004OneImage` | 0 | 1 | `Image` × 1 | 画像素材と注釈レイヤー |
| `005OneVideo` | 0 | 1 | `Video` × 1 | 動画素材と映像レイヤー |
| `006OneCharacter` | 1 | 0 | `Speaker Character` × 1 | 単一キャラクター |
| `007OneCharacterWithVoice` | 1 | 1 | `Speaker Character` × 1、`Speaker Voice` × 1 | キャラクターと音声の共存 |
| `008TwoCharacters` | 2 | 0 | `Speaker Character` × 2 | 複数キャラクターと話者レイヤー |
| `009FullProject` | 2 | 2 | 10オブジェクト | 複数話者、動画、遷移、図形などの複合構成 |

すべてのサンプルは Recotte Studio `1.8.5.0` で保存されています。

### 複数キャラクター

`008TwoCharacters` では、キャラクターごとに独立した `Speaker` レイヤーが作られます。各レイヤーの `properties.Speaker.p-value` は対応する `speakers[].key` と一致し、`Speaker Character` はそのレイヤー内に配置されます。空プロジェクトの3レイヤーに話者レイヤーが1つ追加され、合計4レイヤーになります。

### 複合構成

`009FullProject` は4レイヤーと10個のタイムラインオブジェクトを持ちます。

- `Video` レイヤー: `Video` × 2、`Transition` × 1
- 2つの `Speaker` レイヤー: それぞれ `Speaker Character`、`Speaker Action`、`Speaker Voice`
- `Annot` レイヤー: `Figure` × 1
- `file-items`: SVGとMP4の2件

## 差分解析時に除外する値

次の値は機能差分ではなく、作成時や保存環境によって変化します。

- ルートの `time`
- `setting.guid`
- `setting.project-name`
- `setting.file-explorer-path`
- `file-items[].apath`
- `file-items[].ik`
- 音声生成時のファイル名とファイルキー
- 選択状態、編集位置、タイムライン表示状態

これらを正規化してから比較すると、機能ごとの構造差分を追いやすくなります。

## 未確定事項

- `name`、`text.text`、`text.stext[].text` の同期範囲
- `voice-hash` と `file-items[].ik` の生成規則
- `rpath` と `apath` の探索優先順位
- `p-type`、`p-subtype`、`p-cig` の正式な意味
- `objkey` と `frame-counter` の採番・更新規則
- 小数時刻をフレームへ丸める正式な規則
- `sub-objects`、`additional-actions`、キーフレームの詳細構造
- 素材の移動、欠落、重複登録時の挙動
- Recotte Studio `1.7.1.2` と `1.8.5.0` の互換性

## 解析方法

各 `.ccproj` を JSON として読み込み、基準となる空プロジェクトと、機能ごとに追加された配列要素、オブジェクト型、プロパティ、素材参照、話者参照を比較しています。

記述は次の区分で扱います。

- **確認済み**: 現在のサンプルで直接確認できた事実
- **推定**: 複数の値や既存実装から意味を推測できるが、追加検証が必要な事項
- **未確認**: 現在のサンプルでは判断できない事項
