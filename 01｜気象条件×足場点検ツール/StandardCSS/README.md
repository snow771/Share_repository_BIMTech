# テンプレートCSS（新規アプリ共通の見た目）

既存のプロジェクト管理アプリのCSSから、アプリ固有部分を除いた「共通の下地」だけを
抜き出したものです。新規アプリはこれを土台にすることで、色・角丸・影・フォーム・
モーダル・パネルの見た目がそろいます。

## 収録ファイル

| ファイル | 役割 |
|---|---|
| `variables.css` | デザイントークン（**すべての色・寸法の出どころ**） |
| `base.css` | リセット・box-sizing統一・`.hidden`・ツールチップ・ローディング |
| `button.css` | ボタン（`.active-btn` / `.passive-btn` / `.toggle-btn` / `.small-btn`） |
| `modal.css` | モーダル骨格・フォーム・`.button-row`・チェックボックス・`.close-btn` |
| `panel.css` | 画面に浮かせる小パネル・セグメント（`.segmented`） |
| `switch.css` | ON/OFF トグルスイッチ（`.toggle-switch`） |
| `header.css` | 上部メニューバーとフィルタ（`.top-menu` / `.top-filters`） |

## 読み込み順（重要）

トークン → 下地 → 部品の順で読み込みます。`panel.css` は `.close-btn` を
`modal.css` から継承するので、`modal.css` を先に読み込んでください。

```html
<link rel="stylesheet" href="css/variables.css">
<link rel="stylesheet" href="css/base/base.css">
<link rel="stylesheet" href="css/components/button.css">
<link rel="stylesheet" href="css/components/modal.css">
<link rel="stylesheet" href="css/components/panel.css">
<link rel="stylesheet" href="css/components/switch.css">
<link rel="stylesheet" href="css/components/header.css">
<!-- 以降にアプリ固有CSSを置く -->
<link rel="stylesheet" href="css/app.css">
```

（ファイル冒頭コメントの `css/...` は推奨配置パスです。出力はフラットにしてあります。）

## 使ううえでの約束ごと

1. **直値の色を書かない。** `#4EA1D5` などは禁止。必ず `var(--color-primary)` のように
   `variables.css` のトークンを参照する。新しい色が要るときはまずトークンを追加する。
2. **色の意味を守る。** 青 = 操作可（`--color-primary`）／グレー = 無効・中立
   （`--color-muted`）／赤 = 破壊的（`--color-danger`）。ボタンもスイッチもこの3色で表す。
3. **部品CSSに固有サイズを書かない。** 個別モーダル等のCSSは grid レイアウトと
   固有の幅・高さだけを書き、色・角丸・影は共通CSSに任せる。
4. **自動フォーマット注意。** VS の「ドキュメントのフォーマット」等でハイフン前後に
   空白が入る（`.floating - panel` など）とルールが全滅する。整形設定を確認する。

## アプリ固有の色を足すとき

`variables.css` は触らず、アプリ側で `:root` に追記してトークンを組み立てる。

```css
/* app/tokens.css */
:root {
    --task-color: var(--color-primary-weak);
    --delay-color: var(--color-danger-weak);
    /* … ガント等、そのアプリだけの色 */
}
```

## 既存アプリからの流用メモ

このテンプレートには、元アプリにあった以下は**含めていません**（アプリ固有のため）。
必要なアプリ側で個別に用意してください。

- ガント／タスク種別の色、共同編集ロック、重要度、ウィジェット
- 通知ベル（`#notice-button`）・更新履歴パネル
- チャット、担当者管理モーダルの grid、`app_offline` 等
- 旧エイリアス（`--button-default` / `--default-color` / `--negative-color` など）
  → 移行する場合のみ、互換のためアプリ側で定義する

## テンプレートで直した既知の不具合

- `button.css` の `.small-btn`：元の `border: 1px solid dashed`（不正値）を `1px dashed` に修正。
- `header.css`：旧エイリアス依存をやめ、`--color-primary-*` を直接参照。
- z-index：ローディングを `--z-loading (10000)` にし、モーダル／ツールチップより前面に。
