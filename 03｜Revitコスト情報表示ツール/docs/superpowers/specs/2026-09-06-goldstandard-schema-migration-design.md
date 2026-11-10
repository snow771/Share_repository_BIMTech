# GoldStandard正規化スキーマへの移行と概要タブの製品カード化 — 設計

日付: 2026-09-06
対象: BUI Revit 2025アドイン（`BUI_Addin_CSharpアドイン`）

## 1. 背景と目的

現在のPoCは、製品データを **1枚の横持ちCSV**（1行＝1製品、列＝属性）として読んでいる。
一方で建材データの正式な設計は `GoldStandard_SQLテーブル再構築_v2.xlsx` と
`GoldStandard_ER_v1.dbml` にまとまり、**13テーブルの正規化スキーマ**に確定した。
属性は `Product_Specs` に **縦持ち**（1製品×1属性＝1行）で保持される。

このPoCは最終的にSQL Serverへ移す。したがって今回の移行の目的は2つある。

1. 製品データの読み取りを、正規化スキーマの構造そのままで扱えるようにする。
   CSVからSQLへ移す際に**変わるのは読み取り部分だけ**になる形にする。
2. 概要タブを、設計者・プロジェクト関係者が床・壁・天井の製品仕様を
   **視覚的に把握できる製品カード**へ作り替える。

## 2. 対象データの実測値

`GoldStandard_SQLテーブル再構築_v2.xlsx` を機械的に読んだ結果。

| 表 | 行数 | 備考 |
| --- | --- | --- |
| Manufacturers | 7 | |
| Material_Categories | 6 | うち実データがあるのは3 |
| Status_Master | 7 | `status_group` が `record` と `product_lifecycle` の2系統 |
| Reliability_Ranks | 3 | A/B/C |
| Sources | 27 | |
| Products | 30 | |
| Product_Variants | **0** | スキーマのみ |
| Display_Groups | 2 | `BASIC_SPEC`（基本仕様）、`PERFORMANCE`（性能） |
| Spec_Master | 18 | うち実データがあるのは9 |
| Product_Specs | 230 | 縦持ち |
| Product_Images | **0** | スキーマのみ |
| Migration_Audit | 43 | 移行確認用。アドインでは使わない |
| Legacy_Notes | 309 | 移行確認用。アドインでは使わない |

`Products` の内訳は建材分類3種（ALC / せっこうボード / 床材（塩ビシート））、メーカー7社。

`Product_Specs` に実在する `spec_key` は9種類。

| spec_key | 件数 | 表示名 | グループ |
| --- | --- | --- | --- |
| thickness | 30 | 厚さ | 基本仕様 |
| width | 30 | 幅 | 基本仕様 |
| length | 30 | 長さ | 基本仕様 |
| thermal_conductivity | 30 | 熱伝導率 | 性能 |
| fire_class | 30 | 防火性能 | 性能 |
| fire_cert_no | 30 | 防火認定番号 | 性能 |
| jis_standard_no | 30 | JIS規格番号 | 性能 |
| areal_weight | 10 | 面密度 | 基本仕様 |
| piece_weight | 10 | 1枚あたり重量 | 基本仕様 |

### 2.1 データ品質の実測（設計に反映する）

- **`264` は無効値。** `value_string` に120件現れるが、その全件が `value_type` = `number` の行、
  つまり文字列値を持たない行である。`note` / `table_no` / `source_text` にも同じ値が現れる。
  元Excelからの取り込み事故と判断し、**`264` 単独の値は空欄として扱う**。
- **発行日がExcelシリアル値**（`46023` など）で入っている。日付として解釈する。
- **単位に表記ゆれがある**（`kg/m2` / `kg/m²` / `kg/m³` / `ー`）。
  表示は `Spec_Master.standard_unit` を優先し、`Product_Specs.unit` は使わない。
- **数値欄に範囲文字列がある**（`areal_weight` = `8.1～11.7`、`value_type` は空）。
  数値として読めない場合はエラーにせず、文字列としてそのまま表示する。
- **`Product_Variants` と `Product_Images` は0件。** 参照はするが、空でも機能が止まらないこと。

## 3. 決定事項

利用者と合意済み。

1. **データはフォルダ1つに複数CSVで持つ。** テーブル名＝ファイル名。
   設定は「CSVのパス」ではなく「**データフォルダのパス**」1つになる。
   PoCで必要なのは8ファイル（`Products` / `Manufacturers` / `Material_Categories` /
   `Spec_Master` / `Display_Groups` / `Product_Specs` / `Sources` / `Reliability_Ranks`）。
   残りは無くても動く。
2. **Revitに書くIDは `Products.product_id`。** 旧 `db_key` / `建材コード` から切り替える。
   付与済みの旧IDは既存の旧新ID対応CSV（`idAliasCsvPath`）の仕組みで扱える。PoCでは空でよい。
3. **絞り込み軸を変更する。** 旧6軸（カテゴリー／用途／メーカー名／製品名／型番／構造）のうち
   用途・構造は新スキーマに対応列が無く、型番は0件の `Product_Variants` にしか無い。
   新しい軸は **建材分類 / メーカー名 / 製品名 / 防火性能 / 厚さ** の5つ。
   後半2つは `Product_Specs` 由来で、「不燃で12.5mmのボード」という探し方ができる。
4. **概要タブの既存機能は残す。** 製品カードを最上部に置き、
   その下に現在の「複合レイヤー」「ID付与」をそのまま残す。
   複合レイヤーの層を選んで材料にIDを付ける操作は概要タブにしかないため、消せない。
5. **カードのタブ構成。** 情報源タブはその製品の属性が参照している資料の一覧（重複除去）。
   関連資料タブは製品URLと製品画像。`Product_Images` が0件のためPoCでは概ね空になるが、
   データが入れば自動で埋まるためタブは用意しておく。
6. **画像はインターネットから取得しない。** データフォルダ内の画像ファイル（`file_name`）
   だけを表示する。`image_url` へのHTTP取得は行わない。
   LLM接続でローカル・社内限定の方針を敷いており、画像だけ外部通信するのは一貫しない。

単価情報CSV（`IPriceRepository`）は資料の対象外。**今回は変更しない。**

## 4. 方式

### 採用案：`ProductRecord` を平坦な入れ物として残し、結合結果を流し込む

新しいリポジトリがフォルダ内のCSVを読み、結合と縦→横変換をして `ProductRecord` を作る。

- `ProductId` = `product_id`
- `ProductName` = `product_name`
- `Manufacturer` = 結合した `manufacturer_name`
- `AdditionalAttributes` = `{"建材分類":"せっこうボード", "防火性能":"不燃", "厚さ":"12.5", ...}`
  （キーは `Spec_Master.display_name_ja`）

これにより**絞り込み・採番・重複検出・新規登録は変更不要**になり、既存テストが生き残る。
概要カードだけは表示グループ・情報源・信頼度という構造が必要なので、別の型を新設する。

SQLに移した際も `JOIN` して1行へ平坦化する形と同じであり、置き換わるのは読み取り部分だけになる。

### 不採用案

- **`ProductRecord` を構造化された型に置き換える。**
  絞り込み・採番・登録・テストのすべてに波及する。得るものに対して壊す範囲が大きすぎる。
- **旧CSVと新スキーマの両方を残し設定で切り替える。**
  二重メンテになる。資料は置き換えの指示であり、両立させる要求は無い。

## 5. 構成

### 5.1 データ層（新規 `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/`）

| ファイル | 役割 |
| --- | --- |
| `GoldStandardTables_表定義.cs` | テーブル名・ファイル名・列名の定数を1か所に集める |
| `GoldStandardCsvSet_表一括読込.cs` | フォルダ内のCSVを読む。欠けた表は空として扱い警告を積む |
| `GoldStandardFlattener_製品平坦化.cs` | 結合と縦→横変換。`ProductRecord` を作る |
| `GoldStandardProductRepository_製品データ取得.cs` | `IProductRepository` 実装。書き込みは `Products.csv` のみ |
| `GoldStandardDetailSource_製品詳細取得.cs` | `IProductDetailSource` 実装。概要カード用 |
| `SpecValueFormatter_属性値整形.cs` | 桁区切り、範囲文字列、単位の解決、`264` の除去 |
| `ExcelSerialDate_日付変換.cs` | 発行日がシリアル値だった場合の変換 |

既存の `CsvFileWriter`（バックアップ付き置換）、`CsvDocumentCodec`、`CsvRowSkipReport` は再利用する。
`CsvProductRepository_CSV製品データ取得.cs`（旧・横持ち1枚読み）とその専用テストは削除する。

### 5.2 概要カード用モデル（新規 `Products_製品/ProductDetail_製品詳細.cs`）

```
ProductDetail
  ProductId / ProductName / ManufacturerName / CategoryName / ImagePath / ProductUrl
  Groups   : IReadOnlyList<SpecGroup>
  Sources  : IReadOnlyList<SourceReference>
  Related  : IReadOnlyList<RelatedResource>

SpecGroup        GroupId / GroupName / Lines
SpecLine         DisplayName / Value / Unit / ReliabilityRank / SourceName
SourceReference  SourceName / SourceType / Publisher / PublishedOn / Url / ReliabilityRank
RelatedResource  Title / Kind / LocalImagePath / Url
```

並び順は `Display_Groups.display_order` → `Spec_Master.display_order` に従う。
値を持たない属性は行を作らない（18種すべてを空行で並べない）。

### 5.3 差し替えの継ぎ目（新規 `IProductDetailSource_製品詳細窓口.cs`）

```csharp
Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken);
```

`ICandidateSource` と同じ役割。画面はこの契約だけを見るため、
SQL実装へ差し替えても画面とそのテストは変わらない。

### 5.4 画面

`OverviewView_概要.xaml` の**最上部に製品カード**を置く。

- ヘッダー: 画像（無ければ「画像なし」の枠）／製品名／メーカー名／建材分類名／`Product ID: xxx`
- `TabControl`: `Display_Groups` の行数だけ動的に作り、そこへ「情報源」「関連資料」を足す。
  今のデータでは結果的に4タブ（基本仕様・性能・情報源・関連資料）になる。
- 各行は「表示名 ／ 値 ／ 単位」の3列。

その下に現在の「複合レイヤー」「ID付与」を**そのまま残す**。

`BuiDockPaneViewModel` に `ProductCard` と `SelectedCardTab` を持たせる。
選択要素の Product_ID が変わったら詳細を取り直す。
Product_ID が未設定なら「Product_IDが付与されていません」とだけ表示する。

### 5.5 設定

```
provider                  : "Csv" → "GoldStandard"
productCsvPath            : 廃止 → dataFolderPath（データフォルダ1つ）
candidateFilterColumns    : ["建材分類", "メーカー名", "製品名", "防火性能", "厚さ"]
newProductColumns         : ["建材分類", "メーカー名", "製品名"]
newProductRequiredColumns : ["建材分類", "メーカー名", "製品名"]
duplicateCompareColumns   : ["建材分類", "メーカー名", "製品名"]
```

`SettingsMerger.FillMissingData` に `DataFolderPath` の欠損補完を追加する。
**これを入れないと、更新前から設定ファイルを持っている利用者のBUIが起動時に開かなくなる。**
2026-09-04の実装で同じ事故を起こしており、再発防止として必須。

`CsvPathNormalizer` はフォルダパスにも適用する（Explorerの「パスのコピー」が付ける引用符を外す）。

### 5.6 データ変換スクリプト（新規 `scripts_開発補助/`）

利用者が手作業でExcelから13枚のCSVを書き出すのは現実的でない。
`GoldStandard_SQLテーブル再構築_v2.xlsx` を読み、指定フォルダへ
**UTF-8 BOM付きCSV**として書き出すスクリプトを用意する。

- 発行日などのExcelシリアル値は `yyyy-MM-dd` に変換して書き出す
- 空のシート（`Product_Variants` / `Product_Images`）は見出し行だけのCSVを作る
- 既存ファイルは日時付きバックアップを取ってから置き換える
- `.ps1` は **UTF-8 BOM付き**で保存する（BOM無しはWindows PowerShell 5.1がShift_JISとして読み、日本語が壊れる）

出力先はPoC用の複製フォルダ（`BUI_PoCData`）を既定にする。**元データのフォルダへは書かない。**

## 6. 異常時の扱い

| 状況 | 動作 |
| --- | --- |
| データフォルダが無い | 「データフォルダが見つかりません。設定タブのパスを確認してください。」を状態に出す。機能は止めない |
| 必須の表（`Products`）が無い | 上と同様に警告。候補0件として動く |
| 任意の表（`Sources` など）が無い | 空として扱い、警告を積む。カードの該当タブは空になる |
| 参照切れ（`manufacturer_id` がマスタに無い） | IDをそのまま表示し、警告に積む。行は捨てない |
| `Product_Specs` の行が読めない | 既存の `CsvRowSkipReport` と同じく、その行だけ読み飛ばして報告する |
| 新規登録でメーカー名・建材分類名がマスタに無い | **拒否する**。対応するIDを決められないため |

## 7. 表示の規則

- 数値は桁区切りを付ける（`1820` → `1,820`）。小数はそのまま（`12.5`）。
- 数値として読めない値（`8.1～11.7`）はそのまま文字列で表示する。
- 単位は `Spec_Master.standard_unit`。`-` と `ー` は単位なしとして表示しない。
- `264` 単独の値は空欄として扱う。
- 絞り込みの「厚さ」の選択肢は、全値が数値として読める場合は**数値順**に並べる
  （文字列順だと `12.5` の後に `9.5` が来る）。

## 8. 検証

新規コードはすべて `Bui.Shared_共有ロジック` に置くため、Revit無しで単体テストできる。
テストは一時フォルダへ小さなCSV一式を書き出して読ませる。

主な検証項目:

1. 縦持ち→横持ちの変換が、`Spec_Master.display_name_ja` をキーに正しく行われる
2. `264` が空欄になる
3. 参照切れがあっても行が捨てられず、警告が出る
4. 表が欠けても例外にならず、警告付きで動く
5. `Display_Groups` / `Spec_Master` の `display_order` どおりに並ぶ
6. 値を持たない属性の行が作られない
7. 厚さの選択肢が数値順に並ぶ
8. `SettingsMerger` が `dataFolderPath` を欠く古い設定を補完する
9. 新規登録がマスタに無いメーカー名を拒否する
10. Excelシリアル値の発行日が日付になる

**Revitが必要なのは、画面の見た目・実際の書き込み・Ctrl+Zの動作だけ。**
これらは会社支給PCでの確認事項として手順書に分ける。

## 9. 対象外

- 単価情報CSVとの連携（積材コード→建材コードの対応表が未完成のため保留中）
- `Product_Variants` を使った `product_id` + `variant_id` の参照（本番の候補。PoCでは `product_id` のみ）
- `Migration_Audit` / `Legacy_Notes` の表示（移行確認用でありアドインの機能ではない）
- SQL Server実装そのもの（今回は差し替えの継ぎ目を用意するところまで）
