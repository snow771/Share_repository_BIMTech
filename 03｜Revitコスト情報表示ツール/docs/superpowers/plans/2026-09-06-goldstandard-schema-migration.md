# GoldStandard正規化スキーマ移行と概要タブ製品カード 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 製品データの読み取りをGoldStandard正規化スキーマ（フォルダ内の複数CSV・縦持ち属性）へ置き換え、概要タブを製品カードに作り替える。

**Architecture:** フォルダ内のCSV群を読んで結合・縦横変換し、既存の `ProductRecord` へ平坦化する。これにより絞り込み・採番・重複検出・新規登録は変更不要になる。概要カードだけは表示グループと情報源の構造が必要なため、`ProductDetail` という別モデルと `IProductDetailSource` という差し替え可能な窓口を新設する。SQL Server移行時に置き換わるのは読み取り実装だけになる。

**Tech Stack:** .NET 8 / C# 12 / WPF / MSTest。Revit 2025 API。新規コードはすべて `Bui.Shared_共有ロジック`（Revit非依存）に置く。

## Global Constraints

- 設計の根拠は `docs/superpowers/specs/2026-09-06-goldstandard-schema-migration-design.md`。判断に迷ったらこれに従う。
- 新規コードは **すべて `src_ソース/Bui.Shared_共有ロジック/` 配下**に置く。Revit参照を持ち込まない。
- ファイル名は既存の慣習に従い `英語名_日本語説明.cs` とする。ファイル先頭に「このファイルの目的」「修正時の影響」のコメントを書く。
- テストは `tests_テスト/Bui.Tests/<領域>/<Name>Tests_<日本語>.cs`。
- テスト実行コマンド（リポジトリのルートから実行）:
  `./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
- `.ps1` は必ず **UTF-8 BOM付き**で保存する。BOM無しはWindows PowerShell 5.1がShift_JISとして読み、日本語が壊れる。
- CSVは **UTF-8 BOM付き**で書き出す。書き込みは必ず既存の `CsvFileWriter.ReplaceWithBackupAsync` を通す（日時付き `.bak` を残すため）。
- `264` は無効値。単独で現れたら空欄として扱う。
- 単位は `Spec_Master.standard_unit` を使う。`Product_Specs.unit` は使わない。
- commit前に、変更したファイルに競合マーカー（`<<<<<<<`）と制御文字が無いことを確認する。
- 既存の293件のテストを壊さない。壊れた場合は原因を直す（テストを消して通さない）。

---

## File Structure

**新規（`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/`）**

| ファイル | 責務 |
| --- | --- |
| `GoldStandardTables_表定義.cs` | 表名・ファイル名・列名の定数だけ。ロジックを持たない |
| `GoldStandardCsvSet_表一括読込.cs` | フォルダ内CSVの読み込みと欠損時の警告。生の行辞書を返すだけ |
| `SpecValueFormatter_属性値整形.cs` | `264` の除去、桁区切り、単位の正規化。純粋関数 |
| `ExcelSerialDate_日付変換.cs` | Excelシリアル値と通常の日付文字列の解釈。純粋関数 |
| `GoldStandardFlattener_製品平坦化.cs` | 結合と縦→横変換。`ProductRecord` を作る。純粋関数 |
| `GoldStandardProductRepository_製品データ取得.cs` | `IProductRepository` 実装。キャッシュと `Products.csv` への書込み |
| `GoldStandardDetailSource_製品詳細取得.cs` | `IProductDetailSource` 実装。概要カードを組み立てる |

**新規（その他）**

| ファイル | 責務 |
| --- | --- |
| `Products_製品/ProductDetail_製品詳細.cs` | 概要カードの表示モデル（不変） |
| `Data_データ窓口/IProductDetailSource_製品詳細窓口.cs` | 画面と詳細実装の唯一の境界 |
| `scripts_開発補助/ConvertGoldStandard_データ変換.ps1` | xlsx → CSV一式 |
| `scripts_開発補助/ConvertGoldStandard_データ変換.txt` | 会社PCへ貼り付ける実行手順 |

**変更**

| ファイル | 変更内容 |
| --- | --- |
| `Data_データ窓口/ProductCandidateFilter_候補絞り込み.cs` | 選択肢の並びを数値順にできるようにする |
| `Data_データ窓口/CsvColumnMapping_CSV列対応.cs` | `GoldStandardDefault` を足す |
| `Configuration_設定/BuiSettings_BUI設定.cs` | `ProductCsvPath` → `DataFolderPath` |
| `Configuration_設定/SettingsMerger_設定統合.cs` | `DataFolderPath` の欠損補完 |
| `Configuration_設定/CsvPathNormalizer_CSVパス整形.cs` | フォルダパスも整形対象にする |
| `UI_画面/BuiViewContracts_画面連携契約.cs` | `IFilePickerService.PickExistingFolderAsync` を足す |
| `UI_画面/BuiDockPaneViewModel_パネル状態.cs` | `ProductCard` と `DataFolderPath` |
| `Bui.Addin/UI_画面/WpfFilePickerService_ファイル選択画面.cs` | フォルダ選択の実装 |
| `Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml` | 製品カードを最上部に追加 |
| `Bui.Addin/UI_画面/Tabs/SettingsView_設定.xaml` | 製品CSV → データフォルダ |
| `Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs` | 新リポジトリと詳細窓口を組み立てる |
| `config_設定例/Settings.Default_共通設定.json` | 新しい既定値 |
| `config_設定例/Settings.Local.example_個別設定例.json` | 同上 |
| `docs_説明書/Troubleshooting_問題解決.md` | 新しい失敗パターン |
| `docs_説明書/操作手順_会社PC_次にやること.txt` | 検証手順の書き直し |

**削除**

| ファイル | 理由 |
| --- | --- |
| `Data_データ窓口/Csv/CsvProductRepository_CSV製品データ取得.cs` | 横持ち1枚読みは使わなくなる |
| `tests_テスト/Bui.Tests/Data_データ/CsvProductRepositoryTests_CSV製品データテスト.cs` | 上の専用テスト |
| `tests_テスト/Bui.Tests/Data_データ/ProductRowMapperTests_製品行変換テスト.cs` | `ProductRowMapper` は製品CSV専用。単価側は `PriceRowMapper` で別物 |
| `Data_データ窓口/ProductRowMapper_製品行変換.cs` | 同上 |

---

## Task 1: 表定義とフォルダ一括読込

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardTables_表定義.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardCsvSet_表一括読込.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardCsvSetTests_表一括読込テスト.cs`

**Interfaces:**
- Consumes: 既存の `internal static class Bui.Data.Csv.CsvDocumentCodec`（`CsvDocument Read(string path)`、`string Write(...)`）と `internal sealed record Bui.Data.Csv.CsvDocument(IReadOnlyList<string> Headers, IReadOnlyList<Dictionary<string,string>> Rows, bool HasUtf8Bom)`。どちらも同一アセンブリ内なので `internal` のまま使える。
- Produces:
  - `Bui.Data.GoldStandard.GoldStandardTables` … `const string Products/Manufacturers/MaterialCategories/SpecMaster/DisplayGroups/ProductSpecs/Sources/ReliabilityRanks/ProductVariants/ProductImages`、`static IReadOnlyList<string> All`、および `static class Columns`
  - `Bui.Data.GoldStandard.GoldStandardTable` … `internal sealed record GoldStandardTable(IReadOnlyList<string> Headers, IReadOnlyList<Dictionary<string,string>> Rows, bool HasUtf8Bom)`
  - `Bui.Data.GoldStandard.GoldStandardCsvSet` … `internal sealed class`。`static GoldStandardCsvSet Load(string? folderPath)`、`static DateTime FolderTimestampUtc(string? folderPath)`、`IReadOnlyList<IReadOnlyDictionary<string,string>> Rows(string table)`、`GoldStandardTable? Table(string table)`、`bool HasProducts`、`IReadOnlyList<string> Problems`、`DateTime TimestampUtc`、`string FolderPath`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/GoldStandardCsvSetTests_表一括読込テスト.cs`:

```csharp
// このファイルの目的:
//   データフォルダの読み込みが、欠けた表やフォルダ自体の不在で止まらないことを守ります。
// 修正時の影響:
//   ここが緩むと、CSVが1枚足りないだけでBUIパネルが開かなくなります。
using System.Text;
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data;

[TestClass]
public sealed class GoldStandardCsvSetTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BuiGs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void WriteCsv(string table, string content) =>
        File.WriteAllText(Path.Combine(_folder, table + ".csv"), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    [TestMethod]
    public void 存在する表を読み取れる()
    {
        WriteCsv(GoldStandardTables.Products, "product_id,product_name\nBD-1,タイガーボード\n");

        var set = GoldStandardCsvSet.Load(_folder);

        Assert.IsTrue(set.HasProducts);
        var rows = set.Rows(GoldStandardTables.Products);
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("BD-1", rows[0]["product_id"]);
        Assert.AreEqual("タイガーボード", rows[0]["product_name"]);
    }

    [TestMethod]
    public void 任意の表が無くても空として扱い警告を残す()
    {
        WriteCsv(GoldStandardTables.Products, "product_id,product_name\nBD-1,タイガーボード\n");

        var set = GoldStandardCsvSet.Load(_folder);

        Assert.AreEqual(0, set.Rows(GoldStandardTables.Sources).Count);
        Assert.IsTrue(set.Problems.Any(p => p.Contains(GoldStandardTables.Sources, StringComparison.Ordinal)),
            "欠けた表の名前が警告に出ていません。");
    }

    [TestMethod]
    public void 製品表が無い場合は利用不可として扱う()
    {
        WriteCsv(GoldStandardTables.Manufacturers, "manufacturer_id,manufacturer_name\nMFR-1,吉野石膏\n");

        var set = GoldStandardCsvSet.Load(_folder);

        Assert.IsFalse(set.HasProducts);
        Assert.IsTrue(set.Problems.Any(p => p.Contains(GoldStandardTables.Products, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void フォルダが無くても例外にならず案内を返す()
    {
        var set = GoldStandardCsvSet.Load(Path.Combine(_folder, "存在しない"));

        Assert.IsFalse(set.HasProducts);
        Assert.AreEqual(0, set.Rows(GoldStandardTables.Products).Count);
        Assert.IsTrue(set.Problems.Any(p => p.Contains("データフォルダが見つかりません", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void パスが未設定でも例外にならない()
    {
        var set = GoldStandardCsvSet.Load(null);

        Assert.IsFalse(set.HasProducts);
        Assert.IsTrue(set.Problems.Count > 0);
    }

    [TestMethod]
    public void 壊れた表だけを警告にして他の表は読める()
    {
        WriteCsv(GoldStandardTables.Products, "product_id,product_id\nBD-1,BD-1\n");
        WriteCsv(GoldStandardTables.Manufacturers, "manufacturer_id,manufacturer_name\nMFR-1,吉野石膏\n");

        var set = GoldStandardCsvSet.Load(_folder);

        Assert.IsFalse(set.HasProducts);
        Assert.AreEqual(1, set.Rows(GoldStandardTables.Manufacturers).Count);
        Assert.IsTrue(set.Problems.Count > 0);
    }

    [TestMethod]
    public void 更新時刻はフォルダ内で最も新しいCSVに一致する()
    {
        WriteCsv(GoldStandardTables.Products, "product_id\nBD-1\n");
        WriteCsv(GoldStandardTables.Sources, "source_id\nSRC-1\n");
        var newest = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_folder, GoldStandardTables.Sources + ".csv"), newest);

        var set = GoldStandardCsvSet.Load(_folder);

        Assert.AreEqual(newest, set.TimestampUtc);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

リポジトリのルートで実行:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardCsvSetTests
```

期待: ビルドエラー（`GoldStandardTables` / `GoldStandardCsvSet` が存在しない）。

- [ ] **Step 3: 表定義を書く**

`GoldStandardTables_表定義.cs`:

```csharp
// このファイルの目的:
//   GoldStandardスキーマの表名・ファイル名・列名を1か所に集めます。
//   文字列リテラルを各所に散らすと、列名の変更時に直し漏れが起きます。
//
// 修正時の影響:
//   ここの表名がそのままCSVのファイル名（<表名>.csv）になります。
//   SQL Serverへ移す際も、この名前がテーブル名に対応します。

namespace Bui.Data.GoldStandard;

/// <summary>GoldStandardスキーマの表名と列名です。</summary>
internal static class GoldStandardTables
{
    public const string Products = "Products";
    public const string Manufacturers = "Manufacturers";
    public const string MaterialCategories = "Material_Categories";
    public const string SpecMaster = "Spec_Master";
    public const string DisplayGroups = "Display_Groups";
    public const string ProductSpecs = "Product_Specs";
    public const string Sources = "Sources";
    public const string ReliabilityRanks = "Reliability_Ranks";
    public const string ProductVariants = "Product_Variants";
    public const string ProductImages = "Product_Images";

    /// <summary>読み込みを試みる表の全体です。Productsだけが必須で、他は無くても動きます。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Products, Manufacturers, MaterialCategories, SpecMaster, DisplayGroups,
        ProductSpecs, Sources, ReliabilityRanks, ProductVariants, ProductImages
    };

    /// <summary>各表で参照する列名です。ER図（GoldStandard_ER_v1.dbml）と一致させます。</summary>
    public static class Columns
    {
        public const string ProductId = "product_id";
        public const string ProductName = "product_name";
        public const string ManufacturerId = "manufacturer_id";
        public const string ManufacturerName = "manufacturer_name";
        public const string MaterialCategoryId = "material_category_id";
        public const string CategoryNameJa = "category_name_ja";
        public const string PrimarySourceId = "primary_source_id";
        public const string ProductUrl = "product_url";
        public const string Note = "note";

        public const string SpecKey = "spec_key";
        public const string DisplayNameJa = "display_name_ja";
        public const string DisplayGroupId = "display_group_id";
        public const string DisplayGroupNameJa = "display_group_name_ja";
        public const string DisplayOrder = "display_order";
        public const string DataType = "data_type";
        public const string StandardUnit = "standard_unit";

        public const string ValueType = "value_type";
        public const string ValueNumber = "value_number";
        public const string ValueString = "value_string";
        public const string SourceId = "source_id";
        public const string ReliabilityRankId = "reliability_rank_id";

        public const string SourceName = "source_name";
        public const string SourceType = "source_type";
        public const string Publisher = "publisher";
        public const string Url = "url";
        public const string FileName = "file_name";
        public const string PublicationDate = "publication_date";

        public const string RankCode = "rank_code";
        public const string RankNameJa = "rank_name_ja";

        public const string ImageId = "image_id";
        public const string ImageUrl = "image_url";
        public const string IsPrimary = "is_primary";
    }
}
```

- [ ] **Step 4: 一括読込を書く**

`GoldStandardCsvSet_表一括読込.cs`:

```csharp
// このファイルの目的:
//   データフォルダ内のCSVをまとめて読み、生の行のまま保持します。
//   結合や変換はここで行いません（GoldStandardFlattenerの仕事です）。
//
// なぜ「欠けても止めない」のか:
//   Product_VariantsとProduct_Imagesは現時点で0件であり、
//   ファイルごと存在しないことが普通に起こります。
//   1枚欠けただけでBUIパネルが開かなくなるのは、実運用で許容できません。
//
// 修正時の影響:
//   Productsだけは必須です。ここが読めないと候補が一切作れないため、
//   HasProductsをfalseにして利用側へ伝えます。

using System.IO;
using Bui.Data.Csv;

namespace Bui.Data.GoldStandard;

/// <summary>1つの表の内容です。書き戻しのためBOM有無も保持します。</summary>
internal sealed record GoldStandardTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<Dictionary<string, string>> Rows,
    bool HasUtf8Bom);

/// <summary>データフォルダ内のCSV一式です。</summary>
internal sealed class GoldStandardCsvSet
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> NoRows =
        Array.Empty<IReadOnlyDictionary<string, string>>();

    private readonly Dictionary<string, GoldStandardTable> _tables;

    private GoldStandardCsvSet(
        string folderPath,
        Dictionary<string, GoldStandardTable> tables,
        IReadOnlyList<string> problems,
        DateTime timestampUtc)
    {
        FolderPath = folderPath;
        _tables = tables;
        Problems = problems;
        TimestampUtc = timestampUtc;
    }

    public string FolderPath { get; }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>フォルダ内CSVの最終更新時刻の最大値です。キャッシュの有効判定に使います。</summary>
    public DateTime TimestampUtc { get; }

    /// <summary>Productsが読めたかどうかです。falseなら候補は0件になります。</summary>
    public bool HasProducts => _tables.ContainsKey(GoldStandardTables.Products);

    public GoldStandardTable? Table(string table) =>
        _tables.TryGetValue(table, out var value) ? value : null;

    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string table) =>
        _tables.TryGetValue(table, out var value) ? value.Rows : NoRows;

    /// <summary>
    /// フォルダ内CSVの最終更新時刻の最大値です。キャッシュの有効判定に使います。
    /// フォルダ自体の更新時刻では、ファイル内容だけが変わった場合に気づけません。
    /// </summary>
    public static DateTime FolderTimestampUtc(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return DateTime.MinValue;

        var newest = DateTime.MinValue;
        foreach (var table in GoldStandardTables.All)
        {
            var path = Path.Combine(folderPath, table + ".csv");
            if (!File.Exists(path)) continue;
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime > newest) newest = writeTime;
        }

        return newest;
    }

    /// <summary>フォルダを読みます。例外は投げず、問題は Problems に集めます。</summary>
    public static GoldStandardCsvSet Load(string? folderPath)
    {
        var tables = new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            // 例外本文はパスを含むため画面に出せません。原因が分かる案内だけを返します。
            problems.Add("データフォルダが見つかりません。設定タブのパスを確認してください。");
            return new GoldStandardCsvSet(folderPath ?? string.Empty, tables, problems, DateTime.MinValue);
        }

        var newest = DateTime.MinValue;
        foreach (var table in GoldStandardTables.All)
        {
            var path = Path.Combine(folderPath, table + ".csv");
            if (!File.Exists(path))
            {
                problems.Add($"{table}.csv がありません。この表は空として扱います。");
                continue;
            }

            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime > newest) newest = writeTime;

            try
            {
                var document = CsvDocumentCodec.Read(path);
                tables[table] = new GoldStandardTable(document.Headers, document.Rows, document.HasUtf8Bom);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{table}.csv を読み取れません。{exception.Message}");
            }
        }

        if (!tables.ContainsKey(GoldStandardTables.Products))
        {
            problems.Insert(0, $"{GoldStandardTables.Products}.csv を読み取れないため、製品候補を表示できません。");
        }

        return new GoldStandardCsvSet(folderPath, tables, problems, newest);
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardCsvSetTests
```

期待: 7件すべてPASS。

- [ ] **Step 6: 全テストを実行して既存を壊していないことを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。**出力を `tail` などに通さず、終了コードで判定すること。**

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardCsvSetTests_表一括読込テスト.cs"
git commit -m "feat: load the GoldStandard CSV folder without failing on missing tables"
```

---

## Task 2: 値の整形と日付の解釈

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/SpecValueFormatter_属性値整形.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/ExcelSerialDate_日付変換.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SpecValueFormatterTests_属性値整形テスト.cs`

**Interfaces:**
- Consumes: なし（純粋関数のみ）
- Produces:
  - `Bui.Data.GoldStandard.SpecValueFormatter` … `internal static class`
    - `string Clean(string? value)`
    - `string FormatNumber(string? raw)`
    - `string NormalizeUnit(string? unit)`
    - `string SelectValue(string? valueNumber, string? valueString)`
  - `Bui.Data.GoldStandard.ExcelSerialDate` … `internal static class`、`DateTimeOffset? Parse(string? value)`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/SpecValueFormatterTests_属性値整形テスト.cs`:

```csharp
// このファイルの目的:
//   元Excelの取り込み事故（264）と表記ゆれを、画面へ出す前に落とすことを守ります。
// 修正時の影響:
//   264を通してしまうと、概要カードの数値欄に意味のない264が並びます。
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data;

[TestClass]
public sealed class SpecValueFormatterTests
{
    [TestMethod]
    public void 取り込み事故の264は空欄として扱う()
    {
        Assert.AreEqual(string.Empty, SpecValueFormatter.Clean("264"));
        Assert.AreEqual(string.Empty, SpecValueFormatter.Clean(" 264 "));
    }

    [TestMethod]
    public void 264を含むだけの正当な値は残す()
    {
        // 「264」という部分文字列を持つ本物の値まで消すと、実データを失います。
        Assert.AreEqual("NM-264", SpecValueFormatter.Clean("NM-264"));
        Assert.AreEqual("2640", SpecValueFormatter.Clean("2640"));
    }

    [TestMethod]
    public void 空白とnullは空欄になる()
    {
        Assert.AreEqual(string.Empty, SpecValueFormatter.Clean(null));
        Assert.AreEqual(string.Empty, SpecValueFormatter.Clean("   "));
    }

    [TestMethod]
    public void 整数には桁区切りを付ける()
    {
        Assert.AreEqual("1,820", SpecValueFormatter.FormatNumber("1820"));
        Assert.AreEqual("910", SpecValueFormatter.FormatNumber("910"));
    }

    [TestMethod]
    public void 小数はそのままの桁で表示する()
    {
        Assert.AreEqual("12.5", SpecValueFormatter.FormatNumber("12.5"));
        Assert.AreEqual("0.22", SpecValueFormatter.FormatNumber("0.22"));
    }

    [TestMethod]
    public void 数値として読めない値はそのまま文字列で返す()
    {
        // areal_weightの「8.1～11.7」は実データに存在します。エラーにしてはいけません。
        Assert.AreEqual("8.1～11.7", SpecValueFormatter.FormatNumber("8.1～11.7"));
    }

    [TestMethod]
    public void 単位のハイフンと長音は単位なしとして扱う()
    {
        Assert.AreEqual(string.Empty, SpecValueFormatter.NormalizeUnit("-"));
        Assert.AreEqual(string.Empty, SpecValueFormatter.NormalizeUnit("ー"));
        Assert.AreEqual(string.Empty, SpecValueFormatter.NormalizeUnit(null));
        Assert.AreEqual("mm", SpecValueFormatter.NormalizeUnit(" mm "));
    }

    [TestMethod]
    public void 数値欄が空なら文字列欄を使う()
    {
        Assert.AreEqual("不燃", SpecValueFormatter.SelectValue(string.Empty, "不燃"));
        Assert.AreEqual("12.5", SpecValueFormatter.SelectValue("12.5", "264"));
        Assert.AreEqual(string.Empty, SpecValueFormatter.SelectValue("264", "264"));
    }

    [TestMethod]
    public void Excelのシリアル値を日付として読む()
    {
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), ExcelSerialDate.Parse("45658"));
        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ExcelSerialDate.Parse("46023"));
    }

    [TestMethod]
    public void 通常の日付文字列もそのまま読める()
    {
        Assert.AreEqual(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), ExcelSerialDate.Parse("2026-03-15"));
    }

    [TestMethod]
    public void 日付として読めない値はnullを返す()
    {
        Assert.IsNull(ExcelSerialDate.Parse(null));
        Assert.IsNull(ExcelSerialDate.Parse("-"));
        Assert.IsNull(ExcelSerialDate.Parse("未定"));
        // 小さすぎる整数はシリアル値ではなく、単なる数値の混入とみなします。
        Assert.IsNull(ExcelSerialDate.Parse("264"));
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter SpecValueFormatterTests
```

期待: ビルドエラー（`SpecValueFormatter` / `ExcelSerialDate` が存在しない）。

- [ ] **Step 3: 値の整形を書く**

`SpecValueFormatter_属性値整形.cs`:

```csharp
// このファイルの目的:
//   GoldStandardの元データに混ざっている取り込み事故と表記ゆれを、
//   画面へ出す前に一か所で落とします。
//
// なぜ264を消すのか:
//   value_stringに264が120件現れますが、その全件がvalue_type=numberの行、
//   つまり文字列値を持たない行です。note・table_no・source_textにも同じ値が出ます。
//   元Excelからの取り込み事故と判断しました。
//   ただし「264を含む値」（NM-264、2640）は本物なので消してはいけません。
//   完全一致だけを対象にします。
//
// 修正時の影響:
//   ここを緩めると概要カードの数値欄に264が並びます。
//   ここを厳しくしすぎると本物の値が消えます。判定は完全一致のまま保ってください。

using System.Globalization;

namespace Bui.Data.GoldStandard;

/// <summary>属性値の整形をまとめた純粋関数です。</summary>
internal static class SpecValueFormatter
{
    /// <summary>元Excelの取り込み事故で入り込んだ無効値です。</summary>
    private const string InvalidPlaceholder = "264";

    /// <summary>単位として意味を持たない表記です。表示しません。</summary>
    private static readonly string[] EmptyUnits = { "-", "ー", "－", "‐", "—" };

    /// <summary>前後の空白を落とし、無効値を空欄へ変えます。</summary>
    public static string Clean(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, InvalidPlaceholder, StringComparison.Ordinal) ? string.Empty : trimmed;
    }

    /// <summary>
    /// 数値として読めれば桁区切りを付けます。読めなければ元の文字列を返します。
    /// 実データのareal_weightには「8.1～11.7」という範囲文字列があり、これを落としてはいけません。
    /// </summary>
    public static string FormatNumber(string? raw)
    {
        var value = Clean(raw);
        if (value.Length == 0) return string.Empty;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return value;
        }

        // 元の小数桁数を保ちます。0.22を0.2に丸めると別の値になります。
        var separatorIndex = value.IndexOf('.');
        var decimals = separatorIndex < 0 ? 0 : value.Length - separatorIndex - 1;
        return number.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>単位を表示できる形へ整えます。意味を持たない記号は空欄にします。</summary>
    public static string NormalizeUnit(string? unit)
    {
        var value = Clean(unit);
        return EmptyUnits.Contains(value, StringComparer.Ordinal) ? string.Empty : value;
    }

    /// <summary>
    /// value_numberとvalue_stringのうち、実際に値がある方を選びます。
    /// value_typeは空欄の行があるため（実データで20件）、型ではなく中身で判断します。
    /// </summary>
    public static string SelectValue(string? valueNumber, string? valueString)
    {
        var number = Clean(valueNumber);
        return number.Length > 0 ? FormatNumber(number) : Clean(valueString);
    }
}
```

- [ ] **Step 4: 日付変換を書く**

`ExcelSerialDate_日付変換.cs`:

```csharp
// このファイルの目的:
//   Excelから書き出した日付が、日付文字列とシリアル値のどちらでも読めるようにします。
//
// なぜ必要か:
//   GoldStandardのSources.publication_dateは、元のxlsxでは46023のような
//   Excelのシリアル値（1899-12-30からの経過日数）です。
//   変換スクリプトで日付文字列へ直しますが、
//   手作業で書き出したCSVがシリアル値のまま渡ってくることは十分あります。
//   どちらでも読めるようにして、資料の発行日が画面から消えないようにします。
//
// 修正時の影響:
//   小さな整数までシリアル値として扱うと、無関係な数値が1900年の日付に化けます。
//   下限・上限を外さないでください。

using System.Globalization;

namespace Bui.Data.GoldStandard;

/// <summary>日付文字列とExcelシリアル値の両方を解釈します。</summary>
internal static class ExcelSerialDate
{
    /// <summary>Excelのシリアル値1が1900-01-01になる基準日です。</summary>
    private static readonly DateTimeOffset Epoch = new(1899, 12, 30, 0, 0, 0, TimeSpan.Zero);

    /// <summary>シリアル値として扱う範囲です。おおよそ1954年から2064年に相当します。</summary>
    private const int MinimumSerial = 20000;
    private const int MaximumSerial = 60000;

    /// <summary>読み取れない場合はnullを返します。例外は投げません。</summary>
    public static DateTimeOffset? Parse(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serial))
        {
            return serial is >= MinimumSerial and <= MaximumSerial ? Epoch.AddDays(serial) : null;
        }

        var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out var parsed)
            || DateTimeOffset.TryParse(text, CultureInfo.GetCultureInfo("ja-JP"), styles, out parsed))
        {
            return parsed;
        }

        return null;
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter SpecValueFormatterTests
```

期待: 11件すべてPASS。

- [ ] **Step 6: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SpecValueFormatterTests_属性値整形テスト.cs"
git commit -m "feat: clean the 264 import artefact and read Excel serial dates"
```

---

## Task 3: 製品の平坦化（結合と縦→横変換）

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardFlattener_製品平坦化.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/CsvColumnMapping_CSV列対応.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardFlattenerTests_製品平坦化テスト.cs`

**Interfaces:**
- Consumes: Task 1の `GoldStandardCsvSet`（`Rows(string table)`）、`GoldStandardTables.Columns`、Task 2の `SpecValueFormatter.Clean` / `SelectValue`
- Produces:
  - `Bui.Data.GoldStandard.GoldStandardFlattener` … `internal static class`
    - `const string CategoryColumn = "建材分類"`
    - `IReadOnlyList<ProductRecord> Flatten(GoldStandardCsvSet set, out IReadOnlyList<string> problems)`
  - `Bui.Data.CsvColumnMapping.GoldStandardDefault` … `static CsvColumnMapping`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/GoldStandardFlattenerTests_製品平坦化テスト.cs`:

```csharp
// このファイルの目的:
//   縦持ちの属性を、既存のProductRecord（横持ち）へ正しく畳み込めることを守ります。
// 修正時の影響:
//   ここのキーがSpec_Master.display_name_jaでなくなると、
//   設定のcandidateFilterColumnsが一致しなくなり、絞り込みが全滅します。
using System.Text;
using Bui.Data;
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data;

[TestClass]
public sealed class GoldStandardFlattenerTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BuiFlat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void WriteCsv(string table, string content) =>
        File.WriteAllText(Path.Combine(_folder, table + ".csv"), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    /// <summary>製品1件・属性3件の最小構成を書き出します。</summary>
    private void WriteMinimalSet()
    {
        WriteCsv(GoldStandardTables.Products,
            "product_id,product_name,manufacturer_id,material_category_id,primary_source_id,product_url,status_id,note\n" +
            "BD-YOSHINO-01,タイガーボード,MFR-YOSHINO,MAT-GYPSUM-BOARD,SRC-YOSHINO-MAIN,,,\n");
        WriteCsv(GoldStandardTables.Manufacturers,
            "manufacturer_id,manufacturer_name\nMFR-YOSHINO,吉野石膏\n");
        WriteCsv(GoldStandardTables.MaterialCategories,
            "material_category_id,category_name_ja\nMAT-GYPSUM-BOARD,せっこうボード\n");
        WriteCsv(GoldStandardTables.SpecMaster,
            "spec_key,display_name_ja,display_group_id,display_order,data_type,standard_unit\n" +
            "thickness,厚さ,BASIC_SPEC,10,number,mm\n" +
            "length,長さ,BASIC_SPEC,30,number,mm\n" +
            "fire_class,防火性能,PERFORMANCE,40,enum,-\n" +
            "density,密度,BASIC_SPEC,90,number,kg/m3\n");
        WriteCsv(GoldStandardTables.ProductSpecs,
            "gold_id,product_id,spec_key,value_type,value_number,value_string,unit,source_id,reliability_rank_id\n" +
            "GS-001,BD-YOSHINO-01,thickness,number,12.5,264,mm,SRC-YOSHINO-MAIN,REL-A\n" +
            "GS-002,BD-YOSHINO-01,length,number,1820,264,mm,SRC-YOSHINO-MAIN,REL-A\n" +
            "GS-003,BD-YOSHINO-01,fire_class,enum,,不燃,-,SRC-YOSHINO-MAIN,REL-A\n");
    }

    [TestMethod]
    public void 製品名とメーカー名と建材分類が結合される()
    {
        WriteMinimalSet();

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out _);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("BD-YOSHINO-01", records[0].ProductId);
        Assert.AreEqual("タイガーボード", records[0].ProductName);
        Assert.AreEqual("吉野石膏", records[0].Manufacturer);
        Assert.AreEqual("せっこうボード", records[0].AdditionalAttributes[GoldStandardFlattener.CategoryColumn]);
    }

    [TestMethod]
    public void 縦持ちの属性が表示名をキーに畳み込まれる()
    {
        WriteMinimalSet();

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out _);
        var attributes = records[0].AdditionalAttributes;

        Assert.AreEqual("12.5", attributes["厚さ"]);
        Assert.AreEqual("1,820", attributes["長さ"]);
        Assert.AreEqual("不燃", attributes["防火性能"]);
    }

    [TestMethod]
    public void 値を持たない属性は項目そのものを作らない()
    {
        WriteMinimalSet();

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out _);

        // Spec_Masterには密度があるが、この製品のProduct_Specsには行が無い。
        Assert.IsFalse(records[0].AdditionalAttributes.ContainsKey("密度"));
    }

    [TestMethod]
    public void 取り込み事故の264は属性値にならない()
    {
        WriteMinimalSet();

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out _);

        Assert.IsFalse(records[0].AdditionalAttributes.Values.Contains("264"));
    }

    [TestMethod]
    public void 絞り込みで使う項目名から値を読める()
    {
        WriteMinimalSet();

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out _);
        var mapping = CsvColumnMapping.GoldStandardDefault;

        Assert.AreEqual("せっこうボード", ProductFieldAccessor.Read(records[0], mapping, "建材分類"));
        Assert.AreEqual("吉野石膏", ProductFieldAccessor.Read(records[0], mapping, "メーカー名"));
        Assert.AreEqual("タイガーボード", ProductFieldAccessor.Read(records[0], mapping, "製品名"));
        Assert.AreEqual("不燃", ProductFieldAccessor.Read(records[0], mapping, "防火性能"));
        Assert.AreEqual("12.5", ProductFieldAccessor.Read(records[0], mapping, "厚さ"));
    }

    [TestMethod]
    public void 参照切れがあっても行を捨てずIDを表示して警告する()
    {
        WriteCsv(GoldStandardTables.Products,
            "product_id,product_name,manufacturer_id,material_category_id\n" +
            "BD-1,謎ボード,MFR-UNKNOWN,MAT-UNKNOWN\n");
        WriteCsv(GoldStandardTables.Manufacturers, "manufacturer_id,manufacturer_name\nMFR-YOSHINO,吉野石膏\n");
        WriteCsv(GoldStandardTables.MaterialCategories, "material_category_id,category_name_ja\nMAT-GYPSUM-BOARD,せっこうボード\n");

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out var problems);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("MFR-UNKNOWN", records[0].Manufacturer);
        Assert.AreEqual("MAT-UNKNOWN", records[0].AdditionalAttributes[GoldStandardFlattener.CategoryColumn]);
        Assert.IsTrue(problems.Any(p => p.Contains("MFR-UNKNOWN", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void 製品IDが空の行だけを読み飛ばす()
    {
        WriteCsv(GoldStandardTables.Products,
            "product_id,product_name\n,名前だけの行\nBD-1,正しい行\n");

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out var problems);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("BD-1", records[0].ProductId);
        Assert.IsTrue(problems.Count > 0);
    }

    [TestMethod]
    public void Spec_Masterに無い属性キーはそのまま項目名にする()
    {
        WriteCsv(GoldStandardTables.Products, "product_id,product_name\nBD-1,ボード\n");
        WriteCsv(GoldStandardTables.ProductSpecs,
            "gold_id,product_id,spec_key,value_type,value_number,value_string,unit\n" +
            "GS-1,BD-1,unknown_key,string,,ある値,-\n");

        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out _);

        Assert.AreEqual("ある値", records[0].AdditionalAttributes["unknown_key"]);
    }

    [TestMethod]
    public void 表が全く無くても例外にならず空の一覧を返す()
    {
        var records = GoldStandardFlattener.Flatten(GoldStandardCsvSet.Load(_folder), out var problems);

        Assert.AreEqual(0, records.Count);
        Assert.IsTrue(problems.Count > 0);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardFlattenerTests
```

期待: ビルドエラー（`GoldStandardFlattener` と `CsvColumnMapping.GoldStandardDefault` が存在しない）。

- [ ] **Step 3: 項目名の対応を足す**

`CsvColumnMapping_CSV列対応.cs` の `PocDefault` の直後へ追加する:

```csharp
    /// <summary>
    /// GoldStandardスキーマで使う項目名です。
    /// ここに並ぶのはCSVの見出しではなく、画面の絞り込み・検索で使う「項目名」です。
    /// GoldStandardFlattenerがこの名前で値を詰め、設定のcandidateFilterColumnsが
    /// 同じ名前を指します。片方だけ変えると絞り込みが空になります。
    /// 型番・単位・更新日はProduct_Variantsが0件のため、現時点では常に空です。
    /// </summary>
    public static CsvColumnMapping GoldStandardDefault { get; } =
        new("product_id", "製品名", "メーカー名", "型番", "単位", "更新日");
```

- [ ] **Step 4: 平坦化を書く**

`GoldStandardFlattener_製品平坦化.cs`:

```csharp
// このファイルの目的:
//   正規化された複数の表を結合し、縦持ちの属性を横持ちへ畳んで、
//   既存のProductRecord（1行＝1製品）を作ります。
//
// なぜこの形にするのか:
//   絞り込み・採番・重複検出・新規登録はすべてProductRecordを前提にしています。
//   ここで形を合わせておけば、それらは一切変更せずに済みます。
//   SQL Serverへ移した際も、JOINして1行へ平坦化する形は同じです。
//
// 属性のキー:
//   Spec_Master.display_name_ja（厚さ、防火性能など）を使います。
//   設定のcandidateFilterColumnsが同じ名前を指すため、
//   ここをspec_keyに変えると絞り込みが全滅します。
//
// 修正時の影響:
//   参照切れ（マスタに無いID）でも行を捨てません。捨てると製品が黙って消えます。
//   IDをそのまま表示し、警告に積みます。

using Bui.Products;
using System.Collections.ObjectModel;

namespace Bui.Data.GoldStandard;

/// <summary>表の結合と縦→横変換を行う純粋関数です。</summary>
internal static class GoldStandardFlattener
{
    /// <summary>建材分類を表す項目名です。設定のcandidateFilterColumnsと一致させます。</summary>
    public const string CategoryColumn = "建材分類";

    /// <summary>利用者向けメッセージに並べる警告の最大数です。多すぎると読めなくなります。</summary>
    private const int MaximumReportedProblems = 3;

    public static IReadOnlyList<ProductRecord> Flatten(GoldStandardCsvSet set, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(set);

        var collected = new List<string>(set.Problems);
        var manufacturers = Lookup(set, GoldStandardTables.Manufacturers,
            GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Columns.ManufacturerName);
        var categories = Lookup(set, GoldStandardTables.MaterialCategories,
            GoldStandardTables.Columns.MaterialCategoryId, GoldStandardTables.Columns.CategoryNameJa);
        var specNames = Lookup(set, GoldStandardTables.SpecMaster,
            GoldStandardTables.Columns.SpecKey, GoldStandardTables.Columns.DisplayNameJa);
        var specsByProduct = GroupSpecs(set, specNames);

        var missingReferences = new SortedSet<string>(StringComparer.Ordinal);
        var skippedRows = 0;
        var records = new List<ProductRecord>();

        foreach (var row in set.Rows(GoldStandardTables.Products))
        {
            var productId = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.ProductId));
            if (productId.Length == 0)
            {
                skippedRows++;
                continue;
            }

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CategoryColumn] = Resolve(
                    Value(row, GoldStandardTables.Columns.MaterialCategoryId), categories, missingReferences)
            };

            if (specsByProduct.TryGetValue(productId, out var specs))
            {
                foreach (var (name, value) in specs)
                {
                    attributes[name] = value;
                }
            }

            records.Add(new ProductRecord(
                productId,
                SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.ProductName)),
                Resolve(Value(row, GoldStandardTables.Columns.ManufacturerId), manufacturers, missingReferences),
                string.Empty,   // 型番はProduct_Variantsにあるが現時点で0件のため常に空
                string.Empty,   // 単位はGoldStandardスキーマに存在しない
                null,           // 製品単位の更新日はスキーマに存在しない
                new ReadOnlyDictionary<string, string>(attributes)));
        }

        if (skippedRows > 0)
        {
            collected.Add($"{skippedRows}行を読み飛ばしました（{GoldStandardTables.Columns.ProductId}が空欄）。");
        }

        if (missingReferences.Count > 0)
        {
            var shown = missingReferences.Take(MaximumReportedProblems);
            collected.Add(
                $"マスタに無いIDが{missingReferences.Count}種類あります（{string.Join(" / ", shown)}）。IDをそのまま表示しています。");
        }

        problems = collected;
        return records;
    }

    /// <summary>製品IDごとに「表示名 → 値」の一覧を作ります。値が空の属性は項目を作りません。</summary>
    private static Dictionary<string, List<KeyValuePair<string, string>>> GroupSpecs(
        GoldStandardCsvSet set, IReadOnlyDictionary<string, string> specNames)
    {
        var grouped = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        foreach (var row in set.Rows(GoldStandardTables.ProductSpecs))
        {
            var productId = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.ProductId));
            var specKey = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.SpecKey));
            if (productId.Length == 0 || specKey.Length == 0) continue;

            var value = SpecValueFormatter.SelectValue(
                Value(row, GoldStandardTables.Columns.ValueNumber),
                Value(row, GoldStandardTables.Columns.ValueString));
            // 値が無い属性は項目そのものを作りません。空欄の行を並べても読み手の役に立ちません。
            if (value.Length == 0) continue;

            // Spec_Masterに定義が無いキーは、キーそのものを項目名にします。データを黙って捨てません。
            var name = specNames.TryGetValue(specKey, out var displayName) && displayName.Length > 0
                ? displayName
                : specKey;

            if (!grouped.TryGetValue(productId, out var list))
            {
                list = new List<KeyValuePair<string, string>>();
                grouped[productId] = list;
            }

            list.Add(new KeyValuePair<string, string>(name, value));
        }

        return grouped;
    }

    private static Dictionary<string, string> Lookup(
        GoldStandardCsvSet set, string table, string keyColumn, string valueColumn)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in set.Rows(table))
        {
            var key = SpecValueFormatter.Clean(Value(row, keyColumn));
            if (key.Length == 0) continue;
            lookup[key] = SpecValueFormatter.Clean(Value(row, valueColumn));
        }

        return lookup;
    }

    /// <summary>IDを名称へ解決します。解決できない場合はIDをそのまま返し、警告へ積みます。</summary>
    private static string Resolve(
        string? id, IReadOnlyDictionary<string, string> lookup, ISet<string> missingReferences)
    {
        var key = SpecValueFormatter.Clean(id);
        if (key.Length == 0) return string.Empty;
        if (lookup.TryGetValue(key, out var name) && name.Length > 0) return name;

        missingReferences.Add(key);
        return key;
    }

    private static string Value(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value ?? string.Empty : string.Empty;
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardFlattenerTests
```

期待: 9件すべてPASS。

- [ ] **Step 6: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardFlattenerTests_製品平坦化テスト.cs"
git commit -m "feat: join the GoldStandard tables and pivot specs into ProductRecord"
```

---

## Task 4: 製品データ窓口の実装

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardProductRepository_製品データ取得.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardProductRepositoryTests_製品データテスト.cs`

**Interfaces:**
- Consumes: Task 1の `GoldStandardCsvSet`、Task 3の `GoldStandardFlattener.Flatten`、既存の `IProductRepository`、`ProductQuery`、`DataSourceStatus`、`ProductFieldAccessor.Fields`、`CsvFileWriter.ReplaceWithBackupAsync`、`CsvDocumentCodec.Write`
- Produces:
  - `Bui.Data.GoldStandard.GoldStandardProductRepository` … `public sealed class` implements `IProductRepository`
    - `GoldStandardProductRepository(string folderPath, CsvFileWriter? writer = null)`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/GoldStandardProductRepositoryTests_製品データテスト.cs`:

```csharp
// このファイルの目的:
//   GoldStandardフォルダを製品データ窓口として使えること、
//   および書き込みがProducts.csvだけに閉じることを守ります。
// 修正時の影響:
//   マスタに無いメーカー名を受け入れると、参照切れの製品行がCSVへ残ります。
using System.Text;
using Bui.Data;
using Bui.Data.GoldStandard;
using Bui.Products;
using System.Collections.ObjectModel;

namespace Bui.Tests.Data;

[TestClass]
public sealed class GoldStandardProductRepositoryTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BuiRepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        WriteCsv(GoldStandardTables.Products,
            "product_id,product_name,manufacturer_id,material_category_id,note\n" +
            "BD-YOSHINO-01,タイガーボード,MFR-YOSHINO,MAT-GYPSUM-BOARD,\n" +
            "BD-CHIYODA-01,チヨダボード,MFR-CHIYODA,MAT-GYPSUM-BOARD,\n");
        WriteCsv(GoldStandardTables.Manufacturers,
            "manufacturer_id,manufacturer_name\nMFR-YOSHINO,吉野石膏\nMFR-CHIYODA,チヨダウーテ\n");
        WriteCsv(GoldStandardTables.MaterialCategories,
            "material_category_id,category_name_ja\nMAT-GYPSUM-BOARD,せっこうボード\n");
        WriteCsv(GoldStandardTables.SpecMaster,
            "spec_key,display_name_ja,display_group_id,display_order,data_type,standard_unit\n" +
            "thickness,厚さ,BASIC_SPEC,10,number,mm\n");
        WriteCsv(GoldStandardTables.ProductSpecs,
            "gold_id,product_id,spec_key,value_type,value_number,value_string,unit\n" +
            "GS-1,BD-YOSHINO-01,thickness,number,12.5,264,mm\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void WriteCsv(string table, string content) =>
        File.WriteAllText(Path.Combine(_folder, table + ".csv"), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private GoldStandardProductRepository CreateRepository() => new(_folder);

    private static ProductRecord NewProduct(string id, string name, string manufacturer, string category) =>
        new(id, name, manufacturer, string.Empty, string.Empty, null,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [GoldStandardFlattener.CategoryColumn] = category
            }));

    [TestMethod]
    public async Task 状態は利用可能で件数を返す()
    {
        var status = await CreateRepository().GetStatusAsync(CancellationToken.None);

        Assert.IsTrue(status.IsAvailable);
        Assert.AreEqual(2, status.RowCount);
    }

    [TestMethod]
    public async Task フォルダが無ければ利用不可で案内を返す()
    {
        var repository = new GoldStandardProductRepository(Path.Combine(_folder, "存在しない"));

        var status = await repository.GetStatusAsync(CancellationToken.None);

        Assert.IsFalse(status.IsAvailable);
        Assert.IsTrue(status.Problems.Any(p => p.Contains("データフォルダが見つかりません", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task IDで1件取得できる()
    {
        var record = await CreateRepository().GetByIdAsync("BD-YOSHINO-01", CancellationToken.None);

        Assert.IsNotNull(record);
        Assert.AreEqual("吉野石膏", record!.Manufacturer);
        Assert.AreEqual("12.5", record.AdditionalAttributes["厚さ"]);
    }

    [TestMethod]
    public async Task 属性値でも全文検索できる()
    {
        var results = await CreateRepository().SearchAsync(
            new ProductQuery("12.5", new Dictionary<string, string>()), CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("BD-YOSHINO-01", results[0].ProductId);
    }

    [TestMethod]
    public async Task 項目名で絞り込める()
    {
        var results = await CreateRepository().SearchAsync(
            new ProductQuery(string.Empty, new Dictionary<string, string> { ["メーカー名"] = "チヨダウーテ" }),
            CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("BD-CHIYODA-01", results[0].ProductId);
    }

    [TestMethod]
    public async Task 新規登録はProducts_csvにだけ行を足す()
    {
        var repository = CreateRepository();

        await repository.UpsertAsync(
            NewProduct("BM_NEW-0001", "新しいボード", "吉野石膏", "せっこうボード"), CancellationToken.None);

        var written = await File.ReadAllTextAsync(Path.Combine(_folder, GoldStandardTables.Products + ".csv"));
        StringAssert.Contains(written, "BM_NEW-0001");
        StringAssert.Contains(written, "MFR-YOSHINO");
        StringAssert.Contains(written, "MAT-GYPSUM-BOARD");

        // 属性表には触れない。
        var specs = await File.ReadAllTextAsync(Path.Combine(_folder, GoldStandardTables.ProductSpecs + ".csv"));
        Assert.IsFalse(specs.Contains("BM_NEW-0001", StringComparison.Ordinal));

        var reread = await repository.GetByIdAsync("BM_NEW-0001", CancellationToken.None);
        Assert.IsNotNull(reread);
        Assert.AreEqual("吉野石膏", reread!.Manufacturer);
    }

    [TestMethod]
    public async Task マスタに無いメーカー名は拒否する()
    {
        var repository = CreateRepository();

        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            repository.UpsertAsync(
                NewProduct("BM_NEW-0002", "謎ボード", "存在しないメーカー", "せっこうボード"), CancellationToken.None));

        StringAssert.Contains(exception.Message, "存在しないメーカー");
    }

    [TestMethod]
    public async Task マスタに無い建材分類は拒否する()
    {
        var repository = CreateRepository();

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            repository.UpsertAsync(
                NewProduct("BM_NEW-0003", "謎ボード", "吉野石膏", "存在しない分類"), CancellationToken.None));
    }

    [TestMethod]
    public async Task 登録の取り消しで行が消える()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(
            NewProduct("BM_NEW-0004", "取り消す行", "吉野石膏", "せっこうボード"), CancellationToken.None);

        await repository.RemoveAsync("BM_NEW-0004", CancellationToken.None);

        Assert.IsNull(await repository.GetByIdAsync("BM_NEW-0004", CancellationToken.None));
        // 元からある行は残る。
        Assert.IsNotNull(await repository.GetByIdAsync("BD-YOSHINO-01", CancellationToken.None));
    }

    [TestMethod]
    public async Task 書込み前に日時付きのバックアップが作られる()
    {
        var repository = CreateRepository();

        await repository.UpsertAsync(
            NewProduct("BM_NEW-0005", "控えを取る行", "吉野石膏", "せっこうボード"), CancellationToken.None);

        Assert.IsTrue(Directory.GetFiles(_folder, "Products*.bak").Length > 0, "バックアップが作られていません。");
    }

    [TestMethod]
    public async Task 上限なし指定で全件を返す()
    {
        var results = await CreateRepository().SearchAsync(
            new ProductQuery(string.Empty, new Dictionary<string, string>(), ProductQuery.NoLimit),
            CancellationToken.None);

        Assert.AreEqual(2, results.Count);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardProductRepositoryTests
```

期待: ビルドエラー（`GoldStandardProductRepository` が存在しない）。

- [ ] **Step 3: リポジトリを書く**

`GoldStandardProductRepository_製品データ取得.cs`:

```csharp
// このファイルの目的:
//   GoldStandardのデータフォルダを、既存のIProductRepositoryとして使えるようにします。
//   読み取りはフォルダ全体、書き込みはProducts.csvだけに閉じます。
//
// なぜ書き込みをProducts.csvに限るのか:
//   新規登録は「まだ属性が調べられていない製品」を仮登録する操作です。
//   属性が無いのにProduct_Specsへ空行を足すと、後から本物の属性を入れる際に
//   重複行の掃除が必要になります。PoCではProductsへの1行だけにします。
//
// なぜマスタに無い名前を拒むのか:
//   Products.csvが持つのはmanufacturer_idとmaterial_category_idです。
//   名称からIDを決められない以上、書けば必ず参照切れになります。
//   書いた後に気づくより、書く前に断る方が安全です。
//
// 修正時の影響:
//   キャッシュはフォルダ内CSVの最終更新時刻で判定します。
//   ここを外すと、CSVを直した後もBUIが古い内容を表示し続けます。

using System.IO;
using Bui.Data.Csv;
using Bui.Products;

namespace Bui.Data.GoldStandard;

/// <summary>GoldStandardのデータフォルダから製品情報を読み書きします。</summary>
public sealed class GoldStandardProductRepository : IProductRepository
{
    private static readonly CsvColumnMapping Mapping = CsvColumnMapping.GoldStandardDefault;

    private readonly string _folderPath;
    private readonly CsvFileWriter _writer;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private Snapshot? _cache;

    public GoldStandardProductRepository(string folderPath, CsvFileWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        _folderPath = folderPath;
        _writer = writer ?? new CsvFileWriter();
    }

    private string ProductsPath => Path.Combine(_folderPath, GoldStandardTables.Products + ".csv");

    public async Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Status;

    public async Task<ProductRecord?> GetByIdAsync(string productId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Records.FirstOrDefault(record =>
            string.Equals(record.ProductId, productId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Filters);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Status.IsAvailable) return Array.Empty<ProductRecord>();

        var text = (query.Text ?? string.Empty).Trim();
        // 画面一覧の暴走を防ぐため既定は200件で頭打ちにしますが、
        // ProductQuery.NoLimitだけは「全件が要る」指定として上限を外します。
        var limit = query.Limit == ProductQuery.NoLimit ? int.MaxValue : Math.Clamp(query.Limit, 1, 200);
        return snapshot.Records
            .Where(record => MatchesText(record, text) && MatchesFilters(record, query.Filters))
            .Take(limit)
            .ToArray();
    }

    public async Task UpsertAsync(ProductRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProductId))
        {
            throw new InvalidDataException("製品IDがありません。");
        }

        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var manufacturerId = ResolveId(
            snapshot.Set, GoldStandardTables.Manufacturers,
            GoldStandardTables.Columns.ManufacturerName, GoldStandardTables.Columns.ManufacturerId,
            record.Manufacturer, "メーカー名");
        var categoryName = record.AdditionalAttributes.TryGetValue(GoldStandardFlattener.CategoryColumn, out var category)
            ? category
            : string.Empty;
        var categoryId = ResolveId(
            snapshot.Set, GoldStandardTables.MaterialCategories,
            GoldStandardTables.Columns.CategoryNameJa, GoldStandardTables.Columns.MaterialCategoryId,
            categoryName, GoldStandardFlattener.CategoryColumn);

        var products = snapshot.Set.Table(GoldStandardTables.Products)
            ?? throw new InvalidDataException($"{GoldStandardTables.Products}.csv を読み取れません。");
        var headers = products.Headers.ToList();
        var rows = products.Rows.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList();

        var target = rows.FirstOrDefault(row => string.Equals(
            row.GetValueOrDefault(GoldStandardTables.Columns.ProductId)?.Trim(),
            record.ProductId.Trim(), StringComparison.Ordinal));
        if (target is null)
        {
            target = headers.ToDictionary(header => header, _ => string.Empty, StringComparer.Ordinal);
            rows.Add(target);
        }

        target[GoldStandardTables.Columns.ProductId] = record.ProductId.Trim();
        SetWhenHeaderExists(target, headers, GoldStandardTables.Columns.ProductName, record.ProductName);
        SetWhenHeaderExists(target, headers, GoldStandardTables.Columns.ManufacturerId, manufacturerId);
        SetWhenHeaderExists(target, headers, GoldStandardTables.Columns.MaterialCategoryId, categoryId);

        await WriteProductsAsync(headers, rows, products.HasUtf8Bom, snapshot.ProductsWriteTimeUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>指定したProduct_IDの行を削除します。新規登録の取り消しに使います。</summary>
    public async Task RemoveAsync(string productId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var products = snapshot.Set.Table(GoldStandardTables.Products)
            ?? throw new InvalidDataException($"{GoldStandardTables.Products}.csv を読み取れません。");
        var headers = products.Headers.ToList();
        // 生の行から作り直すため、対象以外は内容が保たれます。
        var rows = products.Rows
            .Where(row => !string.Equals(
                row.GetValueOrDefault(GoldStandardTables.Columns.ProductId)?.Trim(),
                productId.Trim(), StringComparison.Ordinal))
            .Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal))
            .ToList();

        await WriteProductsAsync(headers, rows, products.HasUtf8Bom, snapshot.ProductsWriteTimeUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteProductsAsync(
        List<string> headers,
        List<Dictionary<string, string>> rows,
        bool hasUtf8Bom,
        DateTime expectedWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        await _writer.ReplaceWithBackupAsync(
            ProductsPath, CsvDocumentCodec.Write(headers, rows), expectedWriteTimeUtc, hasUtf8Bom, cancellationToken)
            .ConfigureAwait(false);

        _cache = null;
    }

    /// <summary>名称からIDを引きます。引けない場合は書き込み前に断ります。</summary>
    private static string ResolveId(
        GoldStandardCsvSet set, string table, string nameColumn, string idColumn, string? name, string label)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException($"「{label}」が空欄です。マスタに登録されている値を選んでください。");
        }

        foreach (var row in set.Rows(table))
        {
            var candidate = row.TryGetValue(nameColumn, out var raw) ? (raw ?? string.Empty).Trim() : string.Empty;
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return row.TryGetValue(idColumn, out var id) ? (id ?? string.Empty).Trim() : string.Empty;
            }
        }

        throw new InvalidDataException(
            $"「{label}」の値『{value}』は{table}.csvに登録されていません。既存の値から選んでください。");
    }

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var timestamp = GoldStandardCsvSet.FolderTimestampUtc(_folderPath);
            if (_cache is not null && _cache.TimestampUtc == timestamp)
            {
                return _cache;
            }

            var set = GoldStandardCsvSet.Load(_folderPath);
            var records = GoldStandardFlattener.Flatten(set, out var problems);
            var status = new DataSourceStatus(
                set.HasProducts,
                records.Count,
                set.TimestampUtc == DateTime.MinValue ? null : new DateTimeOffset(set.TimestampUtc, TimeSpan.Zero),
                problems);
            var productsWriteTime = File.Exists(ProductsPath) ? File.GetLastWriteTimeUtc(ProductsPath) : DateTime.MinValue;

            _cache = new Snapshot(timestamp, set, records, status, productsWriteTime);
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static void EnsureWritable(DataSourceStatus status)
    {
        if (!status.IsAvailable)
        {
            throw new InvalidDataException(status.Problems.FirstOrDefault() ?? "製品データを更新できません。");
        }
    }

    private static bool MatchesText(ProductRecord record, string text) =>
        text.Length == 0 || ProductFieldAccessor.Fields(record, Mapping).Values
            .Any(value => value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesFilters(ProductRecord record, IReadOnlyDictionary<string, string> filters)
    {
        var fields = ProductFieldAccessor.Fields(record, Mapping);
        return filters.All(filter => fields.TryGetValue(filter.Key, out var actual)
            && string.Equals(actual.Trim(), filter.Value?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetWhenHeaderExists(
        Dictionary<string, string> row, IReadOnlyList<string> headers, string header, string? value)
    {
        if (headers.Contains(header, StringComparer.Ordinal))
        {
            row[header] = value ?? string.Empty;
        }
    }

    private sealed record Snapshot(
        DateTime TimestampUtc,
        GoldStandardCsvSet Set,
        IReadOnlyList<ProductRecord> Records,
        DataSourceStatus Status,
        DateTime ProductsWriteTimeUtc);
}
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardProductRepositoryTests
```

期待: 11件すべてPASS。

- [ ] **Step 5: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 6: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardProductRepositoryTests_製品データテスト.cs"
git commit -m "feat: serve products from the GoldStandard folder and write only Products.csv"
```

---

## Task 5: 絞り込み選択肢の数値順ソート

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductCandidateFilter_候補絞り込み.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductCandidateFilterTests_候補絞り込みテスト.cs`（既存に追記）

**Interfaces:**
- Consumes: 既存の `ProductCandidateFilter.Apply` と `CandidateFilterOptions`
- Produces: 変更なし（`Apply` の署名は変えない）。並び順の挙動だけが変わる

- [ ] **Step 1: 失敗するテストを既存ファイルへ追記する**

`ProductCandidateFilterTests_候補絞り込みテスト.cs` のクラス内へ次を追加する。既存のテストは触らない。

```csharp
    [TestMethod]
    public void 数値だけの選択肢は数値順に並ぶ()
    {
        // 文字列順だと「12.5」が「9.5」より前に来て、厚さの一覧が読めなくなります。
        var products = new[]
        {
            Product("P1", new Dictionary<string, string> { ["厚さ"] = "9.5" }),
            Product("P2", new Dictionary<string, string> { ["厚さ"] = "12.5" }),
            Product("P3", new Dictionary<string, string> { ["厚さ"] = "100" }),
        };

        var view = ProductCandidateFilter.Apply(
            products, CsvColumnMapping.GoldStandardDefault, new[] { "厚さ" },
            new CandidateQuery(new Dictionary<string, string>(), string.Empty));

        CollectionAssert.AreEqual(new[] { "9.5", "12.5", "100" }, view.Filters[0].Values.ToArray());
    }

    [TestMethod]
    public void 数値と文字が混ざる選択肢は文字順のままにする()
    {
        var products = new[]
        {
            Product("P1", new Dictionary<string, string> { ["厚さ"] = "12.5" }),
            Product("P2", new Dictionary<string, string> { ["厚さ"] = "8.1～11.7" }),
        };

        var view = ProductCandidateFilter.Apply(
            products, CsvColumnMapping.GoldStandardDefault, new[] { "厚さ" },
            new CandidateQuery(new Dictionary<string, string>(), string.Empty));

        CollectionAssert.AreEqual(new[] { "12.5", "8.1～11.7" }, view.Filters[0].Values.ToArray());
    }

    /// <summary>追加属性だけを持つ最小の製品を作ります。</summary>
    private static ProductRecord Product(string id, Dictionary<string, string> attributes) =>
        new(id, id, string.Empty, string.Empty, string.Empty, null,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(attributes, StringComparer.Ordinal)));
```

このファイルの `using` に不足があれば追加する（`Bui.Products`、`Bui.Data`）。既存に同名のヘルパーがある場合は追加せず既存を使う。

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter 数値だけの選択肢は数値順に並ぶ
```

期待: FAIL（`9.5` が `100` / `12.5` より後ろに来る）。

- [ ] **Step 3: 並び順を直す**

`ProductCandidateFilter_候補絞り込み.cs` の `DistinctValues` の末尾を変更する。

変更前:

```csharp
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
```

変更後:

```csharp
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Pipe(OrderValues);
```

`Pipe` のような拡張は増やさず、次のように書き換える。`DistinctValues` の本体全体を次で置き換える:

```csharp
    private static IReadOnlyList<string> DistinctValues(
        IReadOnlyList<ProductRecord> products,
        CsvColumnMapping mapping,
        IReadOnlyList<string> filterColumns,
        IReadOnlyDictionary<string, string> selections,
        string text,
        string ignoreColumn)
    {
        var values = products
            .Where(record => Matches(record, mapping, filterColumns, selections, text, ignoreColumn))
            .Select(record => ProductFieldAccessor.Read(record, mapping, ignoreColumn))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return OrderValues(values);
    }

    /// <summary>
    /// すべての値が数値として読める項目は数値順に並べます。
    /// 厚さのような項目を文字列順に並べると「12.5」が「9.5」より前に来て、
    /// 一覧から目的の値を見つけられなくなります。
    /// 1つでも数値でない値（「8.1～11.7」など）があれば、
    /// 一部だけ数値順にはせず、全体を文字列順のままにします。
    /// </summary>
    private static IReadOnlyList<string> OrderValues(IReadOnlyList<string> values)
    {
        var parsed = new decimal[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (!decimal.TryParse(
                    values[index],
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parsed[index]))
            {
                return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }
        }

        return values.OrderBy(value => decimal.Parse(
            value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter ProductCandidateFilterTests
```

期待: 既存分を含めすべてPASS。

- [ ] **Step 5: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 6: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductCandidateFilter_候補絞り込み.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductCandidateFilterTests_候補絞り込みテスト.cs"
git commit -m "fix: order numeric filter options by value instead of by text"
```

---

## Task 6: 製品詳細モデルと詳細取得の実装

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ProductDetail_製品詳細.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/IProductDetailSource_製品詳細窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardDetailSource_製品詳細取得.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardDetailSourceTests_製品詳細取得テスト.cs`

**Interfaces:**
- Consumes: Task 1の `GoldStandardCsvSet`（`Load` / `Rows` / `Table` / `FolderTimestampUtc`）、`GoldStandardTables.Columns`、Task 2の `SpecValueFormatter` と `ExcelSerialDate`
- Produces:
  - `Bui.Products.ProductDetail` … `public sealed record ProductDetail(string ProductId, string ProductName, string ManufacturerName, string CategoryName, string ImagePath, string ProductUrl, IReadOnlyList<SpecGroup> Groups, IReadOnlyList<SourceReference> Sources, IReadOnlyList<RelatedResource> Related)`
  - `Bui.Products.SpecGroup` … `public sealed record SpecGroup(string GroupId, string GroupName, IReadOnlyList<SpecLine> Lines)`
  - `Bui.Products.SpecLine` … `public sealed record SpecLine(string DisplayName, string Value, string Unit, string ReliabilityRank, string SourceName)`
  - `Bui.Products.SourceReference` … `public sealed record SourceReference(string SourceName, string SourceType, string Publisher, DateTimeOffset? PublishedOn, string Url, string ReliabilityRank)`
  - `Bui.Products.RelatedResource` … `public sealed record RelatedResource(string Title, string Kind, string LocalImagePath, string Url)`
  - `Bui.Data.IProductDetailSource` … `Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken)`
  - `Bui.Data.GoldStandard.GoldStandardDetailSource` … `public sealed class`、`GoldStandardDetailSource(string folderPath)`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/GoldStandardDetailSourceTests_製品詳細取得テスト.cs`:

```csharp
// このファイルの目的:
//   概要タブの製品カードに出す内容が、表示グループと並び順どおりに組み立てられることを守ります。
// 修正時の影響:
//   並び順をdisplay_order以外で決めると、寸法より先に認定番号が出るなど読みにくくなります。
using System.Text;
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data;

[TestClass]
public sealed class GoldStandardDetailSourceTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BuiDetail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        WriteCsv(GoldStandardTables.Products,
            "product_id,product_name,manufacturer_id,material_category_id,primary_source_id,product_url,note\n" +
            "BD-YOSHINO-01,タイガーボード,MFR-YOSHINO,MAT-GYPSUM-BOARD,SRC-YOSHINO-MAIN,https://example.invalid/board,\n");
        WriteCsv(GoldStandardTables.Manufacturers, "manufacturer_id,manufacturer_name\nMFR-YOSHINO,吉野石膏\n");
        WriteCsv(GoldStandardTables.MaterialCategories,
            "material_category_id,category_name_ja\nMAT-GYPSUM-BOARD,せっこうボード\n");
        WriteCsv(GoldStandardTables.DisplayGroups,
            "display_group_id,display_group_name_ja,display_order\n" +
            "PERFORMANCE,性能,20\n" +
            "BASIC_SPEC,基本仕様,10\n");
        WriteCsv(GoldStandardTables.SpecMaster,
            "spec_key,display_name_ja,display_group_id,display_order,data_type,standard_unit\n" +
            "length,長さ,BASIC_SPEC,30,number,mm\n" +
            "thickness,厚さ,BASIC_SPEC,10,number,mm\n" +
            "fire_class,防火性能,PERFORMANCE,40,enum,-\n" +
            "density,密度,BASIC_SPEC,90,number,kg/m3\n");
        WriteCsv(GoldStandardTables.ProductSpecs,
            "gold_id,product_id,spec_key,value_type,value_number,value_string,unit,source_id,reliability_rank_id\n" +
            "GS-1,BD-YOSHINO-01,length,number,1820,264,mm,SRC-YOSHINO-MAIN,REL-A\n" +
            "GS-2,BD-YOSHINO-01,thickness,number,12.5,264,kg/m2,SRC-YOSHINO-MAIN,REL-A\n" +
            "GS-3,BD-YOSHINO-01,fire_class,enum,,不燃,-,SRC-MLIT-YOSHINO,REL-B\n");
        WriteCsv(GoldStandardTables.Sources,
            "source_id,source_name,source_type,publisher,url,file_name,publication_date,reliability_rank_id\n" +
            "SRC-YOSHINO-MAIN,吉野石膏 総合カタログ,catalog,吉野石膏,https://example.invalid/cat,-,46023,REL-A\n" +
            "SRC-MLIT-YOSHINO,国土交通省 防火認定書,certificate,国土交通省,https://example.invalid/mlit,-,2025-06-01,REL-A\n");
        WriteCsv(GoldStandardTables.ReliabilityRanks,
            "reliability_rank_id,rank_code,rank_name_ja,score,display_order\n" +
            "REL-A,A,高信頼,3,10\nREL-B,B,中信頼,2,20\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void WriteCsv(string table, string content) =>
        File.WriteAllText(Path.Combine(_folder, table + ".csv"), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private GoldStandardDetailSource CreateSource() => new(_folder);

    [TestMethod]
    public async Task ヘッダーに製品名とメーカー名と建材分類が入る()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        Assert.IsNotNull(detail);
        Assert.AreEqual("タイガーボード", detail!.ProductName);
        Assert.AreEqual("吉野石膏", detail.ManufacturerName);
        Assert.AreEqual("せっこうボード", detail.CategoryName);
        Assert.AreEqual("BD-YOSHINO-01", detail.ProductId);
    }

    [TestMethod]
    public async Task 表示グループはdisplay_order順に並ぶ()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "基本仕様", "性能" }, detail!.Groups.Select(group => group.GroupName).ToArray());
    }

    [TestMethod]
    public async Task グループ内の属性もdisplay_order順に並ぶ()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var basic = detail!.Groups.Single(group => group.GroupId == "BASIC_SPEC");
        CollectionAssert.AreEqual(new[] { "厚さ", "長さ" }, basic.Lines.Select(line => line.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task 値の無い属性は行を作らない()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var basic = detail!.Groups.Single(group => group.GroupId == "BASIC_SPEC");
        Assert.IsFalse(basic.Lines.Any(line => line.DisplayName == "密度"));
    }

    [TestMethod]
    public async Task 単位は属性行ではなくSpec_Masterの標準単位を使う()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var basic = detail!.Groups.Single(group => group.GroupId == "BASIC_SPEC");
        var thickness = basic.Lines.Single(line => line.DisplayName == "厚さ");
        // 属性行のunitはkg/m2だが、Spec_Masterのstandard_unitはmm。
        Assert.AreEqual("mm", thickness.Unit);
        Assert.AreEqual("12.5", thickness.Value);
    }

    [TestMethod]
    public async Task 単位のハイフンは表示しない()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var performance = detail!.Groups.Single(group => group.GroupId == "PERFORMANCE");
        Assert.AreEqual(string.Empty, performance.Lines.Single().Unit);
        Assert.AreEqual("不燃", performance.Lines.Single().Value);
    }

    [TestMethod]
    public async Task 数値には桁区切りが付く()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var basic = detail!.Groups.Single(group => group.GroupId == "BASIC_SPEC");
        Assert.AreEqual("1,820", basic.Lines.Single(line => line.DisplayName == "長さ").Value);
    }

    [TestMethod]
    public async Task 属性行には信頼度と情報源名が付く()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var performance = detail!.Groups.Single(group => group.GroupId == "PERFORMANCE");
        Assert.AreEqual("B", performance.Lines.Single().ReliabilityRank);
        Assert.AreEqual("国土交通省 防火認定書", performance.Lines.Single().SourceName);
    }

    [TestMethod]
    public async Task 情報源は重複を除いて一覧になる()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        // 属性3件が参照する情報源は2種類。カタログは2回参照されるが1件にまとまる。
        Assert.AreEqual(2, detail!.Sources.Count);
        Assert.IsTrue(detail.Sources.Any(source => source.SourceName == "吉野石膏 総合カタログ"));
    }

    [TestMethod]
    public async Task 情報源の発行日はシリアル値でも日付になる()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        var catalog = detail!.Sources.Single(source => source.SourceName == "吉野石膏 総合カタログ");
        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), catalog.PublishedOn);
    }

    [TestMethod]
    public async Task 製品URLが関連資料に入る()
    {
        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        Assert.IsTrue(detail!.Related.Any(item => item.Url == "https://example.invalid/board"));
    }

    [TestMethod]
    public async Task 画像はフォルダ内のファイルだけを使いURLは使わない()
    {
        WriteCsv(GoldStandardTables.ProductImages,
            "image_id,product_id,variant_id,source_id,image_url,file_name,image_type,is_primary,display_order,note\n" +
            "IMG-1,BD-YOSHINO-01,,,https://example.invalid/photo.png,board.png,product,true,10,\n");
        await File.WriteAllTextAsync(Path.Combine(_folder, "board.png"), "dummy");

        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        Assert.AreEqual(Path.Combine(_folder, "board.png"), detail!.ImagePath);
        Assert.IsFalse(detail.Related.Any(item => item.Url.Contains("photo.png", StringComparison.Ordinal)),
            "画像のURLを外部参照として出してはいけません。");
    }

    [TestMethod]
    public async Task 画像ファイルが無ければ画像なしとして扱う()
    {
        WriteCsv(GoldStandardTables.ProductImages,
            "image_id,product_id,image_url,file_name,is_primary\n" +
            "IMG-1,BD-YOSHINO-01,https://example.invalid/photo.png,missing.png,true\n");

        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        Assert.AreEqual(string.Empty, detail!.ImagePath);
    }

    [TestMethod]
    public async Task 存在しない製品IDはnullを返す()
    {
        Assert.IsNull(await CreateSource().GetDetailAsync("NO-SUCH-ID", CancellationToken.None));
    }

    [TestMethod]
    public async Task 任意の表が無くても詳細を返せる()
    {
        File.Delete(Path.Combine(_folder, GoldStandardTables.Sources + ".csv"));
        File.Delete(Path.Combine(_folder, GoldStandardTables.ReliabilityRanks + ".csv"));

        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        Assert.IsNotNull(detail);
        Assert.AreEqual(0, detail!.Sources.Count);
        Assert.IsTrue(detail.Groups.Count > 0);
    }

    [TestMethod]
    public async Task 表示グループの定義が無い属性もその他としてまとめる()
    {
        File.Delete(Path.Combine(_folder, GoldStandardTables.DisplayGroups + ".csv"));

        var detail = await CreateSource().GetDetailAsync("BD-YOSHINO-01", CancellationToken.None);

        // グループ名が引けなくても属性を捨てない。
        Assert.IsTrue(detail!.Groups.Sum(group => group.Lines.Count) >= 3);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardDetailSourceTests
```

期待: ビルドエラー（`GoldStandardDetailSource` が存在しない）。

- [ ] **Step 3: 表示モデルを書く**

`Products_製品/ProductDetail_製品詳細.cs`:

```csharp
// このファイルの目的:
//   概要タブの製品カードに出す内容を、画面から独立した不変の値として表します。
//   画面はこの形だけを見るため、CSVからSQLへ実装を差し替えても変わりません。
//
// なぜProductRecordと分けるのか:
//   ProductRecordは「1行＝1製品」の平坦な入れ物で、絞り込みと採番のための形です。
//   カードは表示グループ・信頼度・情報源という構造を必要とします。
//   同じ型に両方を詰めると、絞り込み側がカード用の項目を無視する作りになり、
//   どちらの用途にも中途半端になります。
//
// 修正時の影響:
//   項目を増やすと、詳細取得の実装と概要タブのXAMLの両方に反映が要ります。

namespace Bui.Products;

/// <summary>概要タブの製品カード1枚分の内容です。</summary>
public sealed record ProductDetail(
    string ProductId,
    string ProductName,
    string ManufacturerName,
    string CategoryName,
    /// <summary>ローカルの画像ファイルの絶対パス。無ければ空文字。外部URLは入りません。</summary>
    string ImagePath,
    string ProductUrl,
    IReadOnlyList<SpecGroup> Groups,
    IReadOnlyList<SourceReference> Sources,
    IReadOnlyList<RelatedResource> Related);

/// <summary>カードのタブ1枚に相当する属性のまとまりです。</summary>
public sealed record SpecGroup(string GroupId, string GroupName, IReadOnlyList<SpecLine> Lines);

/// <summary>属性1行です。値と単位は表示できる形に整形済みです。</summary>
public sealed record SpecLine(
    string DisplayName, string Value, string Unit, string ReliabilityRank, string SourceName);

/// <summary>この製品の属性が根拠にしている資料1件です。</summary>
public sealed record SourceReference(
    string SourceName, string SourceType, string Publisher,
    DateTimeOffset? PublishedOn, string Url, string ReliabilityRank);

/// <summary>関連資料タブに並べる1件です。</summary>
public sealed record RelatedResource(string Title, string Kind, string LocalImagePath, string Url);
```

- [ ] **Step 4: 差し替えの継ぎ目を書く**

`Data_データ窓口/IProductDetailSource_製品詳細窓口.cs`:

```csharp
// このファイルは画面と製品詳細実装の唯一の境界です。
// 概要タブはこの契約だけを見るため、CSVからSQL Serverへ実装を差し替えても
// 画面とそのテストは変更が要りません。ICandidateSourceと同じ役割です。
// 修正時の影響: 署名を変えると、将来のSQL実装と概要タブの両方に波及します。
using Bui.Products;

namespace Bui.Data;

/// <summary>Product_IDから、概要タブの製品カードに出す内容を取得します。</summary>
public interface IProductDetailSource
{
    /// <summary>見つからない場合はnullを返します。例外は投げません。</summary>
    Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: 詳細取得を書く**

`GoldStandard/GoldStandardDetailSource_製品詳細取得.cs`:

```csharp
// このファイルの目的:
//   GoldStandardの表から、概要タブの製品カード1枚分を組み立てます。
//
// 並び順:
//   タブはDisplay_Groups.display_order、行はSpec_Master.display_orderに従います。
//   ここを固定の順序に変えると、Display_Groupsに行を足しても画面へ反映されません。
//
// 画像を外部から取らない理由:
//   Product_Images.image_urlは外部URLです。LLM接続ではローカル・社内限定の
//   方針を敷いており、画像だけ外部通信するのは一貫しません。
//   データフォルダ内のfile_nameだけを使い、無ければ「画像なし」にします。
//
// 修正時の影響:
//   任意の表（Sources、Reliability_Ranks、Display_Groups）が欠けても
//   詳細を返せる状態を保ってください。欠けたら画面が空になるだけで済みます。

using System.IO;
using Bui.Products;

namespace Bui.Data.GoldStandard;

/// <summary>GoldStandardのデータフォルダから製品カードの内容を作ります。</summary>
public sealed class GoldStandardDetailSource : IProductDetailSource
{
    /// <summary>表示グループの定義が引けない属性をまとめる先です。</summary>
    private const string UngroupedId = "OTHER";
    private const string UngroupedName = "その他";

    private readonly string _folderPath;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GoldStandardCsvSet? _cache;
    private DateTime _cacheTimestampUtc = DateTime.MinValue;

    public GoldStandardDetailSource(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        _folderPath = folderPath;
    }

    public async Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId)) return null;

        var set = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var id = productId.Trim();
        var product = set.Rows(GoldStandardTables.Products).FirstOrDefault(row =>
            string.Equals(Value(row, GoldStandardTables.Columns.ProductId).Trim(), id, StringComparison.OrdinalIgnoreCase));
        if (product is null) return null;

        var manufacturers = Lookup(set, GoldStandardTables.Manufacturers,
            GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Columns.ManufacturerName);
        var categories = Lookup(set, GoldStandardTables.MaterialCategories,
            GoldStandardTables.Columns.MaterialCategoryId, GoldStandardTables.Columns.CategoryNameJa);
        var ranks = Lookup(set, GoldStandardTables.ReliabilityRanks,
            GoldStandardTables.Columns.ReliabilityRankId, GoldStandardTables.Columns.RankCode);
        var specs = set.Rows(GoldStandardTables.SpecMaster)
            .Where(row => Value(row, GoldStandardTables.Columns.SpecKey).Trim().Length > 0)
            .ToDictionary(row => Value(row, GoldStandardTables.Columns.SpecKey).Trim(), row => row, StringComparer.Ordinal);
        var sources = set.Rows(GoldStandardTables.Sources)
            .Where(row => Value(row, GoldStandardTables.Columns.SourceId).Trim().Length > 0)
            .ToDictionary(row => Value(row, GoldStandardTables.Columns.SourceId).Trim(), row => row, StringComparer.Ordinal);

        var rows = set.Rows(GoldStandardTables.ProductSpecs)
            .Where(row => string.Equals(Value(row, GoldStandardTables.Columns.ProductId).Trim(), id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new ProductDetail(
            id,
            SpecValueFormatter.Clean(Value(product, GoldStandardTables.Columns.ProductName)),
            Resolve(Value(product, GoldStandardTables.Columns.ManufacturerId), manufacturers),
            Resolve(Value(product, GoldStandardTables.Columns.MaterialCategoryId), categories),
            FindImagePath(set, id),
            SpecValueFormatter.Clean(Value(product, GoldStandardTables.Columns.ProductUrl)),
            BuildGroups(set, rows, specs, sources, ranks),
            BuildSources(rows, sources, ranks),
            BuildRelated(product, set, id));
    }

    private IReadOnlyList<SpecGroup> BuildGroups(
        GoldStandardCsvSet set,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> specs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sources,
        IReadOnlyDictionary<string, string> ranks)
    {
        var groupNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupOrders = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in set.Rows(GoldStandardTables.DisplayGroups))
        {
            var groupId = Value(row, GoldStandardTables.Columns.DisplayGroupId).Trim();
            if (groupId.Length == 0) continue;
            groupNames[groupId] = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.DisplayGroupNameJa));
            groupOrders[groupId] = Order(Value(row, GoldStandardTables.Columns.DisplayOrder));
        }

        var lines = new List<(string GroupId, int Order, SpecLine Line)>();
        foreach (var row in rows)
        {
            var specKey = Value(row, GoldStandardTables.Columns.SpecKey).Trim();
            if (specKey.Length == 0) continue;

            var value = SpecValueFormatter.SelectValue(
                Value(row, GoldStandardTables.Columns.ValueNumber),
                Value(row, GoldStandardTables.Columns.ValueString));
            // 値が無い属性は行を作りません。空欄が並んでも読み手の役に立ちません。
            if (value.Length == 0) continue;

            specs.TryGetValue(specKey, out var definition);
            var groupId = definition is null
                ? UngroupedId
                : Value(definition, GoldStandardTables.Columns.DisplayGroupId).Trim();
            if (groupId.Length == 0) groupId = UngroupedId;

            var displayName = definition is null
                ? specKey
                : SpecValueFormatter.Clean(Value(definition, GoldStandardTables.Columns.DisplayNameJa));
            if (displayName.Length == 0) displayName = specKey;

            // 単位はSpec_Masterの標準単位を使います。属性行のunitは表記ゆれがあります。
            var unit = definition is null
                ? string.Empty
                : SpecValueFormatter.NormalizeUnit(Value(definition, GoldStandardTables.Columns.StandardUnit));

            var sourceId = Value(row, GoldStandardTables.Columns.SourceId).Trim();
            var sourceName = sources.TryGetValue(sourceId, out var source)
                ? SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.SourceName))
                : string.Empty;
            var rank = ranks.TryGetValue(Value(row, GoldStandardTables.Columns.ReliabilityRankId).Trim(), out var code)
                ? code
                : string.Empty;

            var order = definition is null ? int.MaxValue : Order(Value(definition, GoldStandardTables.Columns.DisplayOrder));
            lines.Add((groupId, order, new SpecLine(displayName, value, unit, rank, sourceName)));
        }

        return lines
            .GroupBy(item => item.GroupId, StringComparer.Ordinal)
            .OrderBy(group => groupOrders.TryGetValue(group.Key, out var order) ? order : int.MaxValue)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SpecGroup(
                group.Key,
                groupNames.TryGetValue(group.Key, out var name) && name.Length > 0 ? name : UngroupedName,
                group.OrderBy(item => item.Order).Select(item => item.Line).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<SourceReference> BuildSources(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sources,
        IReadOnlyDictionary<string, string> ranks)
    {
        var used = rows
            .Select(row => Value(row, GoldStandardTables.Columns.SourceId).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var references = new List<SourceReference>();
        foreach (var id in used)
        {
            if (!sources.TryGetValue(id, out var source)) continue;
            references.Add(new SourceReference(
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.SourceName)),
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.SourceType)),
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.Publisher)),
                ExcelSerialDate.Parse(Value(source, GoldStandardTables.Columns.PublicationDate)),
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.Url)),
                ranks.TryGetValue(Value(source, GoldStandardTables.Columns.ReliabilityRankId).Trim(), out var code)
                    ? code
                    : string.Empty));
        }

        return references.OrderBy(item => item.SourceName, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<RelatedResource> BuildRelated(
        IReadOnlyDictionary<string, string> product, GoldStandardCsvSet set, string productId)
    {
        var related = new List<RelatedResource>();

        var productUrl = SpecValueFormatter.Clean(Value(product, GoldStandardTables.Columns.ProductUrl));
        if (productUrl.Length > 0)
        {
            related.Add(new RelatedResource("製品ページ", "product_url", string.Empty, productUrl));
        }

        // 画像は必ずローカルのファイルとして扱います。image_urlは意図的に無視します。
        foreach (var row in ImageRows(set, productId))
        {
            var path = LocalImagePath(row);
            if (path.Length == 0) continue;
            var title = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.FileName));
            related.Add(new RelatedResource(
                title.Length > 0 ? title : "製品画像", "image", path, string.Empty));
        }

        return related;
    }

    private string FindImagePath(GoldStandardCsvSet set, string productId)
    {
        var rows = ImageRows(set, productId).ToArray();
        // 代表画像があればそれを、無ければ最初に見つかったものを使います。
        var primary = rows.FirstOrDefault(row =>
            string.Equals(Value(row, GoldStandardTables.Columns.IsPrimary).Trim(), "true", StringComparison.OrdinalIgnoreCase));
        foreach (var row in primary is null ? rows : new[] { primary }.Concat(rows))
        {
            var path = LocalImagePath(row);
            if (path.Length > 0) return path;
        }

        return string.Empty;
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> ImageRows(GoldStandardCsvSet set, string productId) =>
        set.Rows(GoldStandardTables.ProductImages).Where(row =>
            string.Equals(Value(row, GoldStandardTables.Columns.ProductId).Trim(), productId, StringComparison.OrdinalIgnoreCase));

    /// <summary>データフォルダ内に実在する画像ファイルだけを返します。外部URLは使いません。</summary>
    private string LocalImagePath(IReadOnlyDictionary<string, string> row)
    {
        var fileName = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.FileName));
        if (fileName.Length == 0) return string.Empty;

        try
        {
            // フォルダの外を指す相対パスを弾きます。データの誤りで別の場所を読ませないためです。
            var candidate = Path.GetFullPath(Path.Combine(_folderPath, fileName));
            var root = Path.GetFullPath(_folderPath);
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return File.Exists(candidate) ? candidate : string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private async Task<GoldStandardCsvSet> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var timestamp = GoldStandardCsvSet.FolderTimestampUtc(_folderPath);
            if (_cache is not null && _cacheTimestampUtc == timestamp) return _cache;

            _cache = GoldStandardCsvSet.Load(_folderPath);
            _cacheTimestampUtc = timestamp;
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static Dictionary<string, string> Lookup(
        GoldStandardCsvSet set, string table, string keyColumn, string valueColumn)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in set.Rows(table))
        {
            var key = Value(row, keyColumn).Trim();
            if (key.Length == 0) continue;
            lookup[key] = SpecValueFormatter.Clean(Value(row, valueColumn));
        }

        return lookup;
    }

    private static string Resolve(string? id, IReadOnlyDictionary<string, string> lookup)
    {
        var key = (id ?? string.Empty).Trim();
        if (key.Length == 0) return string.Empty;
        return lookup.TryGetValue(key, out var name) && name.Length > 0 ? name : key;
    }

    /// <summary>並び順が読めない場合は末尾へ送ります。値が無いことで行が消えるより安全です。</summary>
    private static int Order(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), out var order) ? order : int.MaxValue;

    private static string Value(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value ?? string.Empty : string.Empty;
}
```

`specs` と `sources` の型が `Dictionary<string, IReadOnlyDictionary<string, string>>` になるよう、`ToDictionary` の要素型を明示すること（`set.Rows` の要素は `IReadOnlyDictionary<string, string>`）。

- [ ] **Step 6: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter GoldStandardDetailSourceTests
```

期待: 15件すべてPASS。

- [ ] **Step 7: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 8: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardDetailSourceTests_製品詳細取得テスト.cs"
git commit -m "feat: build the overview product card from the GoldStandard tables"
```

---

## Task 7: パネル状態に製品カードを持たせる

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/ProductCardViewModelTests_製品カード画面テスト.cs`

**Interfaces:**
- Consumes: Task 6の `Bui.Data.IProductDetailSource` と `Bui.Products.ProductDetail`
- Produces:
  - `BuiDockPaneViewModel` のコンストラクタ末尾に `IProductDetailSource? productDetails = null` を追加（**既存の引数の順序と既定値は変えない**）
  - `public ProductDetail? ProductCard { get; private set; }`
  - `public bool HasProductCard => ProductCard is not null;`
  - `public string ProductCardMessage { get; private set; }`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/UI_画面/ProductCardViewModelTests_製品カード画面テスト.cs`:

```csharp
// このファイルの目的:
//   概要タブの製品カードが、選択に追従して更新されることを守ります。
//   詳細が取れない場合でも、パネル全体が壊れないことも同時に守ります。
// 修正時の影響:
//   詳細取得の例外をここで止めないと、要素を選ぶたびにBUIパネルが落ちます。
using Bui.Data;
using Bui.Products;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class ProductCardViewModelTests_製品カード画面テスト
{
    [TestMethod]
    public async Task 選択要素にIDがあればカードを表示する()
    {
        var sut = Factory.Create(currentProductId: "BD-1", detail: Factory.Detail("BD-1"));

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsTrue(sut.HasProductCard);
        Assert.AreEqual("タイガーボード", sut.ProductCard!.ProductName);
        Assert.AreEqual("吉野石膏", sut.ProductCard.ManufacturerName);
    }

    [TestMethod]
    public async Task IDが無ければカードを出さず理由を示す()
    {
        var sut = Factory.Create(currentProductId: null, detail: Factory.Detail("BD-1"));

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsFalse(sut.HasProductCard);
        StringAssert.Contains(sut.ProductCardMessage, "付与されていません");
    }

    [TestMethod]
    public async Task IDはあるが製品データに無ければ理由を示す()
    {
        var sut = Factory.Create(currentProductId: "旧-999", detail: null);

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsFalse(sut.HasProductCard);
        StringAssert.Contains(sut.ProductCardMessage, "旧-999");
    }

    [TestMethod]
    public async Task 詳細取得が失敗してもパネルは動き続ける()
    {
        var sut = Factory.Create(currentProductId: "BD-1", detail: null, throwOnDetail: true);

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsFalse(sut.HasProductCard);
        Assert.IsTrue(sut.ProductCardMessage.Length > 0);
        // 選択情報そのものは更新されている。
        Assert.AreEqual(1, sut.Selection.SelectedCount);
    }

    [TestMethod]
    public async Task 選択が変わればカードも入れ替わる()
    {
        var source = new SwitchingDetailSource();
        var sut = Factory.Create(currentProductId: "BD-1", detailSource: source);
        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        source.Next = Factory.Detail("BD-2", "チヨダボード", "チヨダウーテ");
        await sut.RefreshSelectionAsync(Factory.Snapshot(11));

        Assert.AreEqual("チヨダボード", sut.ProductCard!.ProductName);
    }

    [TestMethod]
    public async Task 詳細窓口が未設定でも例外にならない()
    {
        var sut = Factory.Create(currentProductId: "BD-1", detailSource: null);

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsFalse(sut.HasProductCard);
    }

    private sealed class SwitchingDetailSource : IProductDetailSource
    {
        public ProductDetail? Next { get; set; } = Factory.Detail("BD-1");

        public Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken) =>
            Task.FromResult(Next);
    }

    private static class Factory
    {
        public static SelectionSnapshot Snapshot(long elementId) => new(true, new[] { elementId }, false, false);

        public static ProductDetail Detail(
            string productId, string name = "タイガーボード", string manufacturer = "吉野石膏") =>
            new(productId, name, manufacturer, "せっこうボード", string.Empty, string.Empty,
                new[]
                {
                    new SpecGroup("BASIC_SPEC", "基本仕様", new[]
                    {
                        new SpecLine("厚さ", "12.5", "mm", "A", "吉野石膏 総合カタログ")
                    })
                },
                Array.Empty<SourceReference>(),
                Array.Empty<RelatedResource>());

        public static BuiDockPaneViewModel Create(
            string? currentProductId,
            ProductDetail? detail = null,
            bool throwOnDetail = false,
            IProductDetailSource? detailSource = null) =>
            new(
                new ProductRepository(Array.Empty<ProductRecord>()),
                new EmptyPriceRepository(),
                new SelectionService(currentProductId),
                new EmptyAuditService(),
                new ImmediateDispatcher(),
                new Bui.Llm.DisabledLlmProvider(),
                new Bui.Metadata.AddinMetadata("BUI", "Building Unit Intelligence", "説明", "作成者", "部署", "会社", "2025"),
                "0.1.0", "2026-09-06 UTC",
                productDetails: detailSource ?? (throwOnDetail
                    ? new ThrowingDetailSource()
                    : detail is null ? new NullDetailSource() : new FixedDetailSource(detail)));

        private sealed class FixedDetailSource(ProductDetail detail) : IProductDetailSource
        {
            public Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken) =>
                Task.FromResult<ProductDetail?>(
                    string.Equals(detail.ProductId, productId, StringComparison.Ordinal) ? detail : null);
        }

        private sealed class NullDetailSource : IProductDetailSource
        {
            public Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken) =>
                Task.FromResult<ProductDetail?>(null);
        }

        private sealed class ThrowingDetailSource : IProductDetailSource
        {
            public Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken) =>
                throw new IOException("読めません");
        }
    }
}
```

`ProductRepository` / `EmptyPriceRepository` / `SelectionService` / `EmptyAuditService` / `ImmediateDispatcher` は既存のテスト用実装を使う。`CandidateSelectionViewModelTests_候補選択画面テスト.cs` の `Factory` 内にある同名クラスが `private` の場合は、そこから共有可能な場所（`tests_テスト/Bui.Tests/UI_画面/` 内の既存の共有ヘルパー）へ移すか、このファイル内に同じ実装を用意する。**既存テストの挙動は変えない。**

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter ProductCardViewModelTests
```

期待: ビルドエラー（`productDetails` 引数と `ProductCard` が存在しない）。

- [ ] **Step 3: コンストラクタと状態を足す**

`BuiDockPaneViewModel_パネル状態.cs` のコンストラクタ引数の**末尾**へ追加する（既存の引数は動かさない）:

```csharp
        ICandidateSource? candidateSource = null,
        IProductDetailSource? productDetails = null)
```

フィールドを追加する（`_candidateSource` の隣）:

```csharp
    private readonly IProductDetailSource? _productDetails;
```

コンストラクタ本体で代入する（`_candidateSource = candidateSource;` の直後）:

```csharp
        _productDetails = productDetails;
```

状態を追加する（`SelectedProductId` の近くに置く）:

```csharp
    private ProductDetail? _productCard;
    private string _productCardMessage = "要素を選択すると製品情報を表示します。";

    /// <summary>概要タブの製品カードの内容です。表示できない場合はnullになります。</summary>
    public ProductDetail? ProductCard
    {
        get => _productCard;
        private set
        {
            if (SetProperty(ref _productCard, value)) RaisePropertyChanged(nameof(HasProductCard));
        }
    }

    /// <summary>カードを表示できるかどうかです。XAMLの表示切替に使います。</summary>
    public bool HasProductCard => ProductCard is not null;

    /// <summary>カードを表示できない理由です。空欄のまま何も出さないと利用者が原因を追えません。</summary>
    public string ProductCardMessage
    {
        get => _productCardMessage;
        private set => SetProperty(ref _productCardMessage, value);
    }
```

`using Bui.Products;` と `using Bui.Data;` が既にあることを確認する（無ければ追加する）。

- [ ] **Step 4: 選択更新でカードを取り直す**

`RefreshSelectionAsync` の中、`var status = await _products.GetStatusAsync(request.Token).ConfigureAwait(false);` の**直前**へ追加する:

```csharp
            var card = await LoadProductCardAsync(details.ProductId, request.Token).ConfigureAwait(false);
```

`_ui.InvokeAsync` のラムダ内、`SelectedProductId = details.ProductId;` の**直後**へ追加する:

```csharp
                ProductCard = card.Detail;
                ProductCardMessage = card.Message;
```

同じクラスへ次のメソッドを追加する:

```csharp
    /// <summary>カード1件の内容と、出せない場合の理由をまとめて返します。</summary>
    private readonly record struct ProductCardLoad(ProductDetail? Detail, string Message);

    /// <summary>
    /// 製品カードを取り直します。
    /// 詳細が取れないことは珍しくありません（IDが未付与、旧IDのまま、データフォルダが未設定）。
    /// どの場合もパネル全体を止めず、理由だけを画面へ返します。
    /// ここで例外を素通しすると、要素を選ぶたびにBUIパネルが落ちます。
    /// </summary>
    private async Task<ProductCardLoad> LoadProductCardAsync(string? productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return new ProductCardLoad(null, "この要素にはProduct_IDが付与されていません。");
        }

        if (_productDetails is null)
        {
            return new ProductCardLoad(null, "製品データの参照先が設定されていません。設定タブを確認してください。");
        }

        try
        {
            var detail = await _productDetails.GetDetailAsync(productId, cancellationToken).ConfigureAwait(false);
            return detail is null
                ? new ProductCardLoad(null, $"Product_ID『{productId}』は製品データにありません。")
                : new ProductCardLoad(detail, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportException(exception);
            // 例外本文はパスを含みうるため画面には出しません。
            return new ProductCardLoad(null, "製品情報を読み込めませんでした。設定タブのデータフォルダを確認してください。");
        }
    }
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter ProductCardViewModelTests
```

期待: 6件すべてPASS。

- [ ] **Step 6: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面"
git commit -m "feat: track the overview product card in the pane view model"
```

---

## Task 8: カードのタブ一覧を組み立てる

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/ProductCardTabBuilder_カードタブ組立.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/ProductCardTabBuilderTests_カードタブ組立テスト.cs`

**Interfaces:**
- Consumes: Task 6の `ProductDetail` / `SpecGroup` / `SpecLine` / `SourceReference` / `RelatedResource`
- Produces:
  - `Bui.UI.ProductCardRow` … `public sealed record ProductCardRow(string Label, string Value, string Note, string Url)`
  - `Bui.UI.ProductCardTab` … `public sealed record ProductCardTab(string Header, IReadOnlyList<ProductCardRow> Rows, string EmptyMessage)`
  - `Bui.UI.ProductCardTabBuilder` … `public static IReadOnlyList<ProductCardTab> Build(ProductDetail? detail)`
  - `BuiDockPaneViewModel.ProductCardTabs` … `public ObservableCollection<ProductCardTab> ProductCardTabs { get; }`

**なぜ行の形を1つに揃えるのか:** タブごとに違う形のデータを出すと、XAML側で
テンプレート選択の仕組みが必要になり、画面が読めなくなります。
どのタブも「見出し／値／補足」の3列に落としておけば、テンプレートは1つで済み、
Revit無しで内容を検証できます。

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/UI_画面/ProductCardTabBuilderTests_カードタブ組立テスト.cs`:

```csharp
// このファイルの目的:
//   製品カードのタブ構成が、表示グループの数だけ作られ、情報源と関連資料が末尾に付くことを守ります。
// 修正時の影響:
//   タブを固定の4つにすると、Display_Groupsに行を足しても画面へ出なくなります。
using Bui.Products;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class ProductCardTabBuilderTests_カードタブ組立テスト
{
    private static ProductDetail Detail(
        IReadOnlyList<SpecGroup>? groups = null,
        IReadOnlyList<SourceReference>? sources = null,
        IReadOnlyList<RelatedResource>? related = null) =>
        new("BD-1", "タイガーボード", "吉野石膏", "せっこうボード", string.Empty, string.Empty,
            groups ?? Array.Empty<SpecGroup>(),
            sources ?? Array.Empty<SourceReference>(),
            related ?? Array.Empty<RelatedResource>());

    [TestMethod]
    public void 表示グループの数だけタブを作り情報源と関連資料を末尾に足す()
    {
        var detail = Detail(new[]
        {
            new SpecGroup("BASIC_SPEC", "基本仕様", new[] { new SpecLine("厚さ", "12.5", "mm", "A", "カタログ") }),
            new SpecGroup("PERFORMANCE", "性能", new[] { new SpecLine("防火性能", "不燃", "", "A", "認定書") }),
        });

        var tabs = ProductCardTabBuilder.Build(detail);

        CollectionAssert.AreEqual(
            new[] { "基本仕様", "性能", "情報源", "関連資料" }, tabs.Select(tab => tab.Header).ToArray());
    }

    [TestMethod]
    public void 属性の行は表示名と値と単位になる()
    {
        var detail = Detail(new[]
        {
            new SpecGroup("BASIC_SPEC", "基本仕様", new[] { new SpecLine("長さ", "1,820", "mm", "A", "カタログ") }),
        });

        var row = ProductCardTabBuilder.Build(detail)[0].Rows.Single();

        Assert.AreEqual("長さ", row.Label);
        Assert.AreEqual("1,820 mm", row.Value);
        StringAssert.Contains(row.Note, "カタログ");
    }

    [TestMethod]
    public void 単位が無ければ値だけを出す()
    {
        var detail = Detail(new[]
        {
            new SpecGroup("PERFORMANCE", "性能", new[] { new SpecLine("防火性能", "不燃", "", "A", "") }),
        });

        Assert.AreEqual("不燃", ProductCardTabBuilder.Build(detail)[0].Rows.Single().Value);
    }

    [TestMethod]
    public void 信頼度は補足に含める()
    {
        var detail = Detail(new[]
        {
            new SpecGroup("BASIC_SPEC", "基本仕様", new[] { new SpecLine("厚さ", "12.5", "mm", "B", "カタログ") }),
        });

        StringAssert.Contains(ProductCardTabBuilder.Build(detail)[0].Rows.Single().Note, "B");
    }

    [TestMethod]
    public void 情報源タブは資料名と発行日と信頼度を出す()
    {
        var detail = Detail(sources: new[]
        {
            new SourceReference("吉野石膏 総合カタログ", "catalog", "吉野石膏",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "https://example.invalid/cat", "A"),
        });

        var row = ProductCardTabBuilder.Build(detail).Single(tab => tab.Header == "情報源").Rows.Single();

        Assert.AreEqual("吉野石膏 総合カタログ", row.Label);
        StringAssert.Contains(row.Value, "2026-01-01");
        StringAssert.Contains(row.Note, "A");
        Assert.AreEqual("https://example.invalid/cat", row.Url);
    }

    [TestMethod]
    public void 発行日が無くても情報源を出せる()
    {
        var detail = Detail(sources: new[]
        {
            new SourceReference("出所不明の資料", "catalog", "", null, "", ""),
        });

        var row = ProductCardTabBuilder.Build(detail).Single(tab => tab.Header == "情報源").Rows.Single();

        Assert.AreEqual("出所不明の資料", row.Label);
        Assert.IsFalse(row.Value.Contains("0001", StringComparison.Ordinal));
    }

    [TestMethod]
    public void 空のタブには理由を持たせる()
    {
        var tabs = ProductCardTabBuilder.Build(Detail());

        var sources = tabs.Single(tab => tab.Header == "情報源");
        Assert.AreEqual(0, sources.Rows.Count);
        Assert.IsTrue(sources.EmptyMessage.Length > 0);
    }

    [TestMethod]
    public void カードが無ければタブも作らない()
    {
        Assert.AreEqual(0, ProductCardTabBuilder.Build(null).Count);
    }

    [TestMethod]
    public void 関連資料は製品ページと画像を並べる()
    {
        var detail = Detail(related: new[]
        {
            new RelatedResource("製品ページ", "product_url", string.Empty, "https://example.invalid/board"),
            new RelatedResource("board.png", "image", @"C:\data\board.png", string.Empty),
        });

        var rows = ProductCardTabBuilder.Build(detail).Single(tab => tab.Header == "関連資料").Rows;

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("https://example.invalid/board", rows[0].Url);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter ProductCardTabBuilderTests
```

期待: ビルドエラー（`ProductCardTabBuilder` が存在しない）。

- [ ] **Step 3: タブ組立を書く**

`UI_画面/ProductCardTabBuilder_カードタブ組立.cs`:

```csharp
// このファイルの目的:
//   製品カードの中身を、どのタブでも同じ「見出し／値／補足」の3列へ落とします。
//
// なぜ形を揃えるのか:
//   タブごとに違う形を渡すと、XAML側でテンプレートを出し分ける仕組みが必要になり、
//   画面の定義が読めなくなります。形を1つにすればテンプレートは1つで済み、
//   表示内容そのものをRevit無しでテストできます。
//
// タブの数:
//   表示グループの数だけ作り、そこへ「情報源」「関連資料」を足します。
//   固定の4つにすると、Display_Groupsに行を足しても画面へ出ません。
//
// 修正時の影響:
//   ここを変えると概要タブの見え方が変わります。XAMLは3列を前提にしています。

using System.Globalization;
using Bui.Products;

namespace Bui.UI;

/// <summary>カードの1行です。どのタブでも同じ形です。</summary>
public sealed record ProductCardRow(string Label, string Value, string Note, string Url);

/// <summary>カードのタブ1枚です。</summary>
public sealed record ProductCardTab(string Header, IReadOnlyList<ProductCardRow> Rows, string EmptyMessage);

/// <summary>製品カードの内容をタブの一覧へ組み立てます。</summary>
public static class ProductCardTabBuilder
{
    private const string SourcesHeader = "情報源";
    private const string RelatedHeader = "関連資料";

    public static IReadOnlyList<ProductCardTab> Build(ProductDetail? detail)
    {
        if (detail is null) return Array.Empty<ProductCardTab>();

        var tabs = new List<ProductCardTab>();
        foreach (var group in detail.Groups)
        {
            tabs.Add(new ProductCardTab(
                group.GroupName,
                group.Lines.Select(ToRow).ToArray(),
                "この分類の情報は登録されていません。"));
        }

        tabs.Add(new ProductCardTab(
            SourcesHeader,
            detail.Sources.Select(ToRow).ToArray(),
            "この製品の属性に情報源が登録されていません。"));

        tabs.Add(new ProductCardTab(
            RelatedHeader,
            detail.Related.Select(ToRow).ToArray(),
            "関連資料は登録されていません。"));

        return tabs;
    }

    private static ProductCardRow ToRow(SpecLine line)
    {
        var value = line.Unit.Length > 0 ? $"{line.Value} {line.Unit}" : line.Value;
        var notes = new List<string>();
        if (line.ReliabilityRank.Length > 0) notes.Add($"信頼度 {line.ReliabilityRank}");
        if (line.SourceName.Length > 0) notes.Add(line.SourceName);
        return new ProductCardRow(line.DisplayName, value, string.Join(" / ", notes), string.Empty);
    }

    private static ProductCardRow ToRow(SourceReference source)
    {
        var parts = new List<string>();
        if (source.Publisher.Length > 0) parts.Add(source.Publisher);
        // 発行日が無い資料もあります。既定値の0001年を表示すると誤解を招くため出しません。
        if (source.PublishedOn is { } published)
        {
            parts.Add(published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var notes = new List<string>();
        if (source.SourceType.Length > 0) notes.Add(source.SourceType);
        if (source.ReliabilityRank.Length > 0) notes.Add($"信頼度 {source.ReliabilityRank}");

        return new ProductCardRow(source.SourceName, string.Join(" / ", parts), string.Join(" / ", notes), source.Url);
    }

    private static ProductCardRow ToRow(RelatedResource resource) =>
        new(resource.Title, resource.Kind, resource.LocalImagePath, resource.Url);
}
```

- [ ] **Step 4: ViewModelへタブ一覧を足す**

`BuiDockPaneViewModel_パネル状態.cs` に追加する。

プロパティ（`ProductCardMessage` の隣）:

```csharp
    /// <summary>概要タブのカードに並べるタブです。表示グループの数に応じて増減します。</summary>
    public ObservableCollection<ProductCardTab> ProductCardTabs { get; } = new();
```

`ProductCard` のsetterを次のように差し替える（タブ一覧をカードと必ず同時に入れ替えるため）:

```csharp
    public ProductDetail? ProductCard
    {
        get => _productCard;
        private set
        {
            if (!SetProperty(ref _productCard, value)) return;
            RaisePropertyChanged(nameof(HasProductCard));
            // カードとタブがずれると、前の製品の仕様が新しい製品の名前の下に出ます。
            // 必ず同時に入れ替えます。
            Replace(ProductCardTabs, ProductCardTabBuilder.Build(value));
        }
    }
```

`Replace` は既存のヘルパー（`Replace(Layers, ...)` などで使用中）をそのまま使う。

- [ ] **Step 5: ViewModel側のテストを1件足す**

`ProductCardViewModelTests_製品カード画面テスト.cs` へ追加する:

```csharp
    [TestMethod]
    public async Task カードと同時にタブ一覧も入れ替わる()
    {
        var sut = Factory.Create(currentProductId: "BD-1", detail: Factory.Detail("BD-1"));

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        // 基本仕様 + 情報源 + 関連資料
        Assert.AreEqual(3, sut.ProductCardTabs.Count);
        Assert.AreEqual("基本仕様", sut.ProductCardTabs[0].Header);
    }

    [TestMethod]
    public async Task カードが出せない場合はタブも空にする()
    {
        var sut = Factory.Create(currentProductId: null);

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.AreEqual(0, sut.ProductCardTabs.Count);
    }
```

- [ ] **Step 6: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter ProductCard
```

期待: `ProductCardTabBuilderTests` 9件と `ProductCardViewModelTests` 8件がPASS。

- [ ] **Step 7: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 8: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面"
git commit -m "feat: flatten the product card into uniform three-column tabs"
```

---

## Task 9: 概要タブに製品カードを表示する

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/ProductCardViewModelTests_製品カード画面テスト.cs`（既存に追記）

**Interfaces:**
- Consumes: Task 8の `ProductCardTabs`、Task 7の `ProductCard` / `HasProductCard` / `ProductCardMessage`
- Produces: `BuiDockPaneViewModel.HasProductImage` … `public bool`

**注意:** 既存の「複合レイヤー」と「ID付与」は**消さない**。カードはその上に足す。
複合レイヤーの層を選んで材料へIDを付ける操作は概要タブにしかなく、消すと機能が失われる。

- [ ] **Step 1: 失敗するテストを追記する**

`ProductCardViewModelTests_製品カード画面テスト.cs` へ追加する:

```csharp
    [TestMethod]
    public async Task 画像ファイルがある場合だけ画像を出す()
    {
        var withImage = new ProductDetail(
            "BD-1", "タイガーボード", "吉野石膏", "せっこうボード", @"C:\data\board.png", string.Empty,
            Array.Empty<SpecGroup>(), Array.Empty<SourceReference>(), Array.Empty<RelatedResource>());
        var sut = Factory.Create(currentProductId: "BD-1", detail: withImage);

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsTrue(sut.HasProductImage);
    }

    [TestMethod]
    public async Task 画像が無ければ画像なしとして扱う()
    {
        var sut = Factory.Create(currentProductId: "BD-1", detail: Factory.Detail("BD-1"));

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.IsFalse(sut.HasProductImage);
    }
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter 画像ファイルがある場合だけ画像を出す
```

期待: ビルドエラー（`HasProductImage` が存在しない）。

- [ ] **Step 3: 画像の有無を状態として出す**

`BuiDockPaneViewModel_パネル状態.cs`。`HasProductCard` の直後に追加する:

```csharp
    /// <summary>
    /// 表示できる製品画像があるかどうかです。
    /// Imageのバインドに空文字を渡すと、画面には何も出ないのにバインドエラーだけが出続けます。
    /// 画像の枠ごと出し分けるため、状態として持ちます。
    /// </summary>
    public bool HasProductImage => ProductCard is not null && ProductCard.ImagePath.Length > 0;
```

`ProductCard` のsetter内、`RaisePropertyChanged(nameof(HasProductCard));` の直後に追加する:

```csharp
            RaisePropertyChanged(nameof(HasProductImage));
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter ProductCardViewModelTests
```

期待: 10件すべてPASS。

- [ ] **Step 5: 概要タブへカードを追加する**

`OverviewView_概要.xaml` の `<StackPanel Margin="12">` の**直後**（`<TextBlock Text="現在の選択" ...>` の直前）へ、次を挿入する。既存の要素は削除しない。

```xml
   <!-- 製品カード：選択要素に付与されたProduct_IDの製品仕様を、設計者が読める形で示します。 -->
   <!-- タブは固定ではなく ProductCardTabs の数だけ作られます（Display_Groupsに追随します）。 -->
   <Border BorderBrush="#FF9E9E9E" BorderThickness="1" Padding="10" Margin="0,0,0,14"
           Visibility="{Binding HasProductCard, Converter={StaticResource BoolToVisibility}}">
    <StackPanel>
     <!-- ヘッダー：画像／製品名／メーカー名／建材分類／Product ID -->
     <Grid>
      <Grid.ColumnDefinitions>
       <ColumnDefinition Width="Auto"/>
       <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <Border Grid.Column="0" Width="96" Height="96" Margin="0,0,12,0"
              BorderBrush="#FFBDBDBD" BorderThickness="1" VerticalAlignment="Top">
       <Grid>
        <!-- 画像が無い場合も枠は出します。欄そのものが消えると、レイアウトが製品ごとに変わって読みにくくなります。 -->
        <TextBlock Text="画像なし" HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="#FF757575"/>
        <Image Source="{Binding ProductCard.ImagePath}" Stretch="Uniform" Margin="2"
               Visibility="{Binding HasProductImage, Converter={StaticResource BoolToVisibility}}"/>
       </Grid>
      </Border>
      <StackPanel Grid.Column="1" VerticalAlignment="Top">
       <TextBlock Text="{Binding ProductCard.ProductName}" FontSize="18" FontWeight="SemiBold" TextWrapping="Wrap"/>
       <TextBlock Text="{Binding ProductCard.ManufacturerName}" TextWrapping="Wrap" Margin="0,2,0,0"/>
       <TextBlock Text="{Binding ProductCard.CategoryName}" TextWrapping="Wrap" Foreground="#FF616161"/>
       <TextBlock Text="{Binding ProductCard.ProductId, StringFormat=Product ID: {0}}"
                  TextWrapping="Wrap" Margin="0,4,0,0"/>
      </StackPanel>
     </Grid>

     <TabControl ItemsSource="{Binding ProductCardTabs}" Margin="0,10,0,0" MinHeight="150">
      <TabControl.ItemTemplate>
       <DataTemplate>
        <TextBlock Text="{Binding Header}"/>
       </DataTemplate>
      </TabControl.ItemTemplate>
      <TabControl.ContentTemplate>
       <DataTemplate>
        <Grid>
         <!-- 行が無いタブでも、なぜ空なのかを必ず文字で示します。 -->
         <TextBlock Text="{Binding EmptyMessage}" TextWrapping="Wrap" Margin="6" Foreground="#FF616161">
          <TextBlock.Style>
           <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
             <DataTrigger Binding="{Binding Rows.Count}" Value="0">
              <Setter Property="Visibility" Value="Visible"/>
             </DataTrigger>
            </Style.Triggers>
           </Style>
          </TextBlock.Style>
         </TextBlock>
         <ItemsControl ItemsSource="{Binding Rows}" Margin="2">
          <ItemsControl.ItemTemplate>
           <DataTemplate>
            <Grid Margin="4,3">
             <Grid.ColumnDefinitions>
              <ColumnDefinition Width="140"/>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
             </Grid.ColumnDefinitions>
             <TextBlock Grid.Column="0" Text="{Binding Label}" TextWrapping="Wrap" Foreground="#FF616161"/>
             <TextBlock Grid.Column="1" Text="{Binding Value}" TextWrapping="Wrap"/>
             <TextBlock Grid.Column="2" Text="{Binding Note}" TextWrapping="Wrap"
                        Margin="8,0,0,0" Foreground="#FF757575"/>
            </Grid>
           </DataTemplate>
          </ItemsControl.ItemTemplate>
         </ItemsControl>
        </Grid>
       </DataTemplate>
      </TabControl.ContentTemplate>
     </TabControl>
    </StackPanel>
   </Border>

   <!-- カードを出せない場合は、その理由だけを同じ位置に出します。 -->
   <TextBlock Text="{Binding ProductCardMessage}" TextWrapping="Wrap" Margin="0,0,0,14" Foreground="#FF616161">
    <TextBlock.Style>
     <Style TargetType="TextBlock">
      <Setter Property="Visibility" Value="Visible"/>
      <Style.Triggers>
       <DataTrigger Binding="{Binding HasProductCard}" Value="True">
        <Setter Property="Visibility" Value="Collapsed"/>
       </DataTrigger>
      </Style.Triggers>
     </Style>
    </TextBlock.Style>
   </TextBlock>
```

`BoolToVisibility` は同ファイルの `UserControl.Resources` に既にある。追加しない。

- [ ] **Step 6: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 7: XAMLの構文を確認する**

XAMLはテストでは検証されない。次で **Bui.Addin のビルドが通ること**を確認する（Revit 2025がある環境でのみ実行可能。私物PCでは不可のため、その場合はこの手順を飛ばし、会社PCでの確認事項として残す）:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj"
```

私物PCでビルドできない場合は、次を目視で確認する。

1. 開きタグと閉じタグが対応している
2. `{Binding ...}` の中に `<` や `>` が入っていない
3. `StaticResource BoolToVisibility` が `UserControl.Resources` に定義されている

- [ ] **Step 8: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/ProductCardViewModelTests_製品カード画面テスト.cs"
git commit -m "feat: show the product card at the top of the overview tab"
```

---

## Task 10: データ層の切り替え（設定・組み立て・旧実装の削除）

このタスクは**まとめて行う**。設定の形、画面の組み立て、旧リポジトリの削除は互いに依存しており、
分けるとビルドが通らない中間状態が生まれる。

**Files:**
- Modify: `src_ソース/Bui.Shared_共有ロジック/Configuration_設定/BuiSettings_BUI設定.cs`
- Modify: `src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SettingsMerger_設定統合.cs`
- Modify: `src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SettingsService_設定管理.cs`（`DataProviderPolicy`）
- Modify: `src_ソース/Bui.Shared_共有ロジック/Configuration_設定/CsvPathNormalizer_CSVパス整形.cs`
- Modify: `src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiViewContracts_画面連携契約.cs`
- Modify: `src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Modify: `src_ソース/Bui.Addin/UI_画面/WpfFilePickerService_ファイル選択画面.cs`
- Modify: `src_ソース/Bui.Addin/UI_画面/Tabs/SettingsView_設定.xaml`
- Modify: `src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`
- Modify: `config_設定例/Settings.Default_共通設定.json`
- Modify: `config_設定例/Settings.Local.example_個別設定例.json`
- Delete: `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvProductRepository_CSV製品データ取得.cs`
- Delete: `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductRowMapper_製品行変換.cs`
- Delete: `tests_テスト/Bui.Tests/Data_データ/CsvProductRepositoryTests_CSV製品データテスト.cs`
- Delete: `tests_テスト/Bui.Tests/Data_データ/ProductRowMapperTests_製品行変換テスト.cs`
- Test: `tests_テスト/Bui.Tests/Configuration_設定/SettingsMergerTests_設定統合テスト.cs`（既存に追記）

**Interfaces:**
- Consumes: Task 4の `GoldStandardProductRepository`、Task 6の `GoldStandardDetailSource`、Task 7の `productDetails` 引数
- Produces:
  - `DataSettings` の第2引数が `string DataFolderPath` になる（`ProductCsvPath` は無くなる）
  - `IFilePickerService.PickExistingFolderAsync(string title, string? initialPath, CancellationToken cancellationToken)`
  - `BuiDockPaneViewModel.DataFolderPath`（`ProductCsvPath` の置き換え）
  - `BuiDockPaneViewModel.BrowseDataFolderCommand`（`BrowseProductCsvCommand` の置き換え）

**この2つを外すと、更新前から設定ファイルを持っている利用者のBUIが起動時に開かなくなる:**

1. `DataFolderPath` の欠損補完（古いJSONにはこのキーが無く、nullとして読まれる）
2. `provider` の `"Csv"` → `"GoldStandard"` への読み替え（古いJSONは `"Csv"` を保存しており、
   そのままでは `DataProviderPolicy` が起動時に例外を投げる）

2026-09-04の実装で1と同種の事故を起こしている。テストで固定すること。

- [ ] **Step 1: 失敗するテストを追記する**

`SettingsMergerTests_設定統合テスト.cs` へ追加する:

```csharp
    [TestMethod]
    public void データフォルダのキーが無い古い設定でも起動できる()
    {
        // 更新前のSettings.Local_個別設定.jsonにはdataFolderPathがありません。
        // nullのまま渡すと、画面の初期化が起動時に落ちてBUI自体が開かなくなります。
        var local = LocalWithoutDataFolder();

        var merged = Merge(local);

        Assert.IsNotNull(merged.Settings.Data.DataFolderPath);
    }

    [TestMethod]
    public void 旧いCsv指定はGoldStandardとして読み替える()
    {
        // 古い設定ファイルはprovider="Csv"を保存しています。
        // 読み替えないとDataProviderPolicyが起動時に例外を投げ、BUIが開かなくなります。
        var merged = Merge(LocalWithProvider("Csv"));

        Assert.AreEqual("GoldStandard", merged.Settings.Data.Provider);
        Assert.IsTrue(DataProviderPolicy.Validate(merged.Settings.Data.Provider).IsSuccess);
    }

    [TestMethod]
    public void データフォルダの引用符を外す()
    {
        // Explorerの「パスのコピー」は引用符を付けます。
        var merged = Merge(LocalWithDataFolder("\"C:\\BUI_PoCData\""));

        Assert.AreEqual(@"C:\BUI_PoCData", merged.Settings.Data.DataFolderPath);
    }
```

`LocalWithoutDataFolder` / `LocalWithProvider` / `LocalWithDataFolder` は、このファイルに既にある
設定JSONの組み立てヘルパーに合わせて実装する。既存のヘルパー名と作法をそのまま使い、
**既存テストは書き換えない**（`ProductCsvPath` を参照している箇所だけ `DataFolderPath` へ直す）。

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter SettingsMergerTests
```

期待: ビルドエラー（`DataFolderPath` が存在しない）。

- [ ] **Step 3: 設定の形を変える**

`BuiSettings_BUI設定.cs` の `DataSettings` を変更する。

変更前:

```csharp
    string ProductCsvPath,
```

変更後:

```csharp
    // 製品データはGoldStandardスキーマの複数CSVをフォルダ単位で扱います。
    // 1枚のCSVパスではなく、Products.csvなどが入ったフォルダを指します。
    string DataFolderPath,
```

- [ ] **Step 4: 欠損補完と読み替えを足す**

`SettingsMerger_設定統合.cs` の `FillMissingData` へ2項目を追加する:

```csharp
    private static DataSettings FillMissingData(DataSettings local, DataSettings defaults) => local with
    {
        // 更新前の設定ファイルにはdataFolderPathがありません。nullのままだと画面の初期化が落ちます。
        DataFolderPath = string.IsNullOrWhiteSpace(local.DataFolderPath)
            ? defaults.DataFolderPath ?? string.Empty
            : local.DataFolderPath,
        // 更新前の設定ファイルはprovider="Csv"を保存しています。
        // 読み替えないとDataProviderPolicyが起動時に例外を投げ、BUIが開かなくなります。
        Provider = string.IsNullOrWhiteSpace(local.Provider)
            || string.Equals(local.Provider, "Csv", StringComparison.OrdinalIgnoreCase)
                ? "GoldStandard"
                : local.Provider,
        CandidateFilterColumns = local.CandidateFilterColumns ?? defaults.CandidateFilterColumns,
        NewProductColumns = local.NewProductColumns ?? defaults.NewProductColumns,
        NewProductRequiredColumns = local.NewProductRequiredColumns ?? defaults.NewProductRequiredColumns,
        DuplicateCompareColumns = local.DuplicateCompareColumns ?? defaults.DuplicateCompareColumns,
        NewProductIdPrefix = string.IsNullOrWhiteSpace(local.NewProductIdPrefix)
            ? defaults.NewProductIdPrefix
            : local.NewProductIdPrefix,
        NewProductIdDigits = local.NewProductIdDigits < 1 ? defaults.NewProductIdDigits : local.NewProductIdDigits
    };
```

`Merge` の `local is null` 分岐でも読み替えが効くよう、次のように変更する:

```csharp
        var nonSecret = local is null
            ? defaults with { Data = FillMissingData(defaults.Data, defaults.Data) }
            : local with { Data = FillMissingData(local.Data, defaults.Data) };
```

- [ ] **Step 5: 利用可能な方式を変える**

`SettingsService_設定管理.cs` の `DataProviderPolicy` を変更する:

```csharp
public static class DataProviderPolicy
{
    /// <summary>
    /// PoCで実装済みのデータ方式です。
    /// 旧設定の "Csv" はSettingsMergerが "GoldStandard" へ読み替えるため、ここには到達しません。
    /// </summary>
    public static OperationResult<string> Validate(string provider) =>
        string.Equals(provider, "GoldStandard", StringComparison.OrdinalIgnoreCase)
            ? OperationResult<string>.Success("GoldStandard")
            : OperationResult<string>.Failure("DATA_PROVIDER_NOT_IMPLEMENTED",
                $"データ方式「{provider}」はまだ利用できません。PoCではGoldStandardを選択してください。",
                "Only GoldStandard is implemented in the PoC.");
}
```

- [ ] **Step 6: パス整形の対象を変える**

`CsvPathNormalizer_CSVパス整形.cs` の

```csharp
            ProductCsvPath = Normalize(data.ProductCsvPath),
```

を次へ変更する:

```csharp
            // フォルダパスも「パスのコピー」で引用符が付きます。ファイルと同じ扱いにします。
            DataFolderPath = Normalize(data.DataFolderPath),
```

- [ ] **Step 7: フォルダ選択の窓口を足す**

`BuiViewContracts_画面連携契約.cs` の `IFilePickerService` へ追加する:

```csharp
    /// <summary>既に存在するフォルダを選びます。GoldStandardのデータフォルダで使います。</summary>
    Task<string?> PickExistingFolderAsync(string title, string? initialPath, CancellationToken cancellationToken);
```

`WpfFilePickerService_ファイル選択画面.cs` へ実装を追加する:

```csharp
    /// <summary>既存のフォルダを選びます。GoldStandardのデータフォルダ用です。</summary>
    public async Task<string?> PickExistingFolderAsync(
        string title, string? initialPath, CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        string? selected = null;
        // ダイアログは必ずUIスレッドで開きます。親を指定しないとRevitの背面に隠れます。
        await _ui.InvokeAsync(() =>
        {
            using var owner = CreateOwnerWindow();
            var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (accepted == true) selected = dialog.FolderName;
        }, cancellationToken).ConfigureAwait(false);

        return selected;
    }
```

テスト用の偽実装（`tests_テスト/Bui.Tests/UI_画面/FilePickerCommandTests_CSV参照ボタンテスト.cs` 内）にも
同じメソッドを追加する。追加しないとテストがビルドできない。

- [ ] **Step 8: ViewModelの項目名を変える**

`BuiDockPaneViewModel_パネル状態.cs` を変更する。

1. フィールドとプロパティ（既存の `_productCsvPath` / `ProductCsvPath`）を次へ差し替える:

```csharp
    /// <summary>GoldStandardのCSV一式が入ったフォルダです。1枚のCSVではありません。</summary>
    public string DataFolderPath { get => _dataFolderPath; set => SetProperty(ref _dataFolderPath, value); }
```

フィールド名も `_dataFolderPath` へ変える。

2. コマンドを差し替える:

```csharp
    public AsyncCommand BrowseDataFolderCommand { get; }
```

コンストラクタ内の生成を次へ変える:

```csharp
        BrowseDataFolderCommand = new AsyncCommand(BrowseDataFolderAsync, commandErrorHandler);
```

3. 参照処理を差し替える（`BrowseProductCsvAsync` を削除し、次を追加する）:

```csharp
    /// <summary>
    /// データフォルダを選びます。既存のBrowseCsvAsyncと同じ作法に揃えます。
    /// 状態表示はUIスレッド経由（SetSettingsStatusAsync / _ui.InvokeAsync）で行い、
    /// 取り消し（null）のときは何も変えません。空文字を入れると設定が壊れます。
    /// </summary>
    public async Task BrowseDataFolderAsync(CancellationToken cancellationToken = default)
    {
        if (_filePicker is null)
        {
            await SetSettingsStatusAsync("フォルダ選択画面を利用できません。パスを直接入力してください。", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var picked = await _filePicker
            .PickExistingFolderAsync("GoldStandardのデータフォルダを選択", DataFolderPath, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(picked)) return;

        await _ui.InvokeAsync(() =>
        {
            DataFolderPath = picked;
            SettingsStatusMessage = "データフォルダを設定しました。「保存」を押すと反映されます。";
        }, cancellationToken).ConfigureAwait(false);
    }
```

既存の `BrowseCsvAsync` ヘルパーは単価CSV・構成CSVで使い続けるため、**削除しない**。

4. 設定の読み込み（既存の `ProductCsvPath = settings.Data.ProductCsvPath;`）を次へ変える:

```csharp
        DataFolderPath = settings.Data.DataFolderPath;
```

5. 設定の保存（既存の `new DataSettings("Csv", ProductCsvPath ?? string.Empty, ...`）を次へ変える:

```csharp
            new DataSettings("GoldStandard", DataFolderPath ?? string.Empty, PriceCsvPath ?? string.Empty,
                AssemblyCsvPath ?? string.Empty,
```

6. 既定の項目対応 2か所（`?? CsvColumnMapping.PocDefault`）を `?? CsvColumnMapping.GoldStandardDefault` へ変える。

- [ ] **Step 9: 設定タブの表示を変える**

`SettingsView_設定.xaml` の製品CSVの部分を差し替える。

変更前:

```xml
<TextBlock Text="製品CSV"/><DockPanel LastChildFill="True"><Button DockPanel.Dock="Right" Content="参照..." Width="72" Margin="6,0,0,0" Command="{Binding BrowseProductCsvCommand}"/><TextBox Text="{Binding ProductCsvPath}" VerticalContentAlignment="Center"/></DockPanel>
```

変更後:

```xml
<TextBlock Text="データフォルダ（Products.csv などが入ったフォルダ）"/><DockPanel LastChildFill="True"><Button DockPanel.Dock="Right" Content="参照..." Width="72" Margin="6,0,0,0" Command="{Binding BrowseDataFolderCommand}"/><TextBox Text="{Binding DataFolderPath}" VerticalContentAlignment="Center"/></DockPanel>
```

- [ ] **Step 10: 画面の組み立てを差し替える**

`BuiDockPaneComposition_画面構成.cs` を変更する。

`using Bui.Data.GoldStandard;` を追加する。

変更前:

```csharp
        var productPath = ResolveCsvPath(loaded.Settings.Data.ProductCsvPath, dataRoot, "Products.csv");
```

変更後:

```csharp
        // 出荷JSONのパスは意図的に空欄です。初回起動を失敗させず設定画面を開けるよう、
        // 未設定のときはBUI管理下の未作成フォルダを仮に使います。保存設定の空欄は勝手に書き換えません。
        var dataFolder = string.IsNullOrWhiteSpace(loaded.Settings.Data.DataFolderPath)
            ? dataRoot
            : loaded.Settings.Data.DataFolderPath;
```

変更前:

```csharp
        var products = new CsvProductRepository(productPath, loaded.Settings.Data.ProductColumns);
```

変更後:

```csharp
        var products = new GoldStandardProductRepository(dataFolder);
        var productDetails = new GoldStandardDetailSource(dataFolder);
```

`BuiDockPaneViewModel` の生成へ、`candidateSource:` の後ろに次を足す:

```csharp
            candidateSource: new InMemoryCandidateSource(
                products, loaded.Settings.Data.ProductColumns, loaded.Settings.Data.CandidateFilterColumns),
            productDetails: productDetails);
```

- [ ] **Step 11: 出荷設定と例を書き換える**

`config_設定例/Settings.Default_共通設定.json` を次へ差し替える:

```json
{
  "data": {
    "provider": "GoldStandard",
    "dataFolderPath": "",
    "priceCsvPath": "",
    "assemblyCsvPath": "",
    "idAliasCsvPath": null,
    "productColumns": {
      "productIdColumn": "product_id",
      "productNameColumn": "製品名",
      "manufacturerColumn": "メーカー名",
      "modelNumberColumn": "型番",
      "unitColumn": "単位",
      "updatedAtColumn": "更新日"
    },
    "priceColumns": {
      "productIdColumn": "建材コード",
      "unitPriceColumn": "単価",
      "currencyColumn": "通貨",
      "validFromColumn": "適用開始日",
      "regionColumn": "地域",
      "informationSourceColumn": "情報源"
    },
    "candidateFilterColumns": ["建材分類", "メーカー名", "製品名", "防火性能", "厚さ"],
    "newProductColumns": ["建材分類", "メーカー名", "製品名"],
    "newProductRequiredColumns": ["建材分類", "メーカー名", "製品名"],
    "duplicateCompareColumns": ["建材分類", "メーカー名", "製品名"],
    "newProductIdPrefix": "BM_NEW-",
    "newProductIdDigits": 4
  },
  "llm": {
    "enabled": false,
    "provider": "Ollama",
    "baseUrl": "http://localhost:11434",
    "model": "",
    "timeoutSeconds": 30,
    "apiKey": null
  }
}
```

`config_設定例/Settings.Local.example_個別設定例.json` も同じ形に合わせ、
`dataFolderPath` に `C:\\BUI_PoCData` のような例を入れる（`productCsvPath` は消す）。

- [ ] **Step 12: 旧実装を削除する**

```bash
git rm "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvProductRepository_CSV製品データ取得.cs"
git rm "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductRowMapper_製品行変換.cs"
git rm "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/CsvProductRepositoryTests_CSV製品データテスト.cs"
git rm "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductRowMapperTests_製品行変換テスト.cs"
```

削除後、これらを参照している箇所が残っていないことを確認する:

```bash
grep -rn "CsvProductRepository\|ProductRowMapper\|ProductCsvPath\|BrowseProductCsvCommand" --include=*.cs --include=*.xaml --include=*.json "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト" "BUI_Addin_CSharpアドイン/config_設定例" | grep -v "/obj/"
```

期待: 何も出ない。出た場合はその箇所を直す。

- [ ] **Step 13: 残ったテストのコンパイルエラーを直す**

`DataSettings` の位置引数が変わったため、テスト内の `new DataSettings("Csv", ...)` は
`new DataSettings("GoldStandard", ...)` へ、第2引数の意味は「データフォルダ」になる。
`CsvColumnMapping.PocDefault` を使っているテストのうち、GoldStandard前提のものは
`CsvColumnMapping.GoldStandardDefault` へ変える。**テストの意図は変えない。**

- [ ] **Step 14: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 15: commit**

```bash
git add -A "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト" "BUI_Addin_CSharpアドイン/config_設定例"
git commit -m "feat: switch the product data source to the GoldStandard folder"
```

---

## Task 11: データ変換スクリプト

**Files:**
- Create: `BUI_Addin_CSharpアドイン/scripts_開発補助/ConvertGoldStandard_データ変換.ps1`
- Create: `BUI_Addin_CSharpアドイン/scripts_開発補助/ConvertGoldStandard_データ変換.txt`

**Interfaces:**
- Consumes: `GoldStandard_SQLテーブル再構築_v2.xlsx`（リポジトリのルートにある）
- Produces: 指定フォルダへ13枚のUTF-8 BOM付きCSV。ファイル名はシート名と同じ

**制約:**
- `.ps1` は **UTF-8 BOM付き**で保存する。BOM無しはWindows PowerShell 5.1がShift_JISとして読み、日本語が壊れる
- Excelを使わない（COMに依存しない）。xlsxをZIPとして直接読む
- 出力先は既定でPoC用の複製フォルダ。**元データのフォルダへは書かない**

- [ ] **Step 1: スクリプトを書く**

`scripts_開発補助/ConvertGoldStandard_データ変換.ps1`:

```powershell
# このスクリプトの目的:
#   GoldStandardのxlsxを、BUIが読むCSV一式（1シート＝1CSV）へ書き出します。
#
# なぜExcelを使わないか:
#   COM経由はExcelの有無と設定に左右され、会社PCで動かない可能性があります。
#   xlsxはZIPなので、中のXMLを直接読めば環境に依存しません。
#
# 修正時の影響:
#   出力はUTF-8 BOM付きです。BOMを外すとBUIがCSVを読めません。
#   このファイル自体もUTF-8 BOM付きで保存してください。BOM無しは日本語が壊れます。

[CmdletBinding()]
param(
    # 変換元のxlsx。既定はリポジトリのルートにあるもの。
    [string]$SourceXlsx = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "GoldStandard_SQLテーブル再構築_v2.xlsx"),
    # 出力先。PoC用の複製フォルダを既定にします。元データのフォルダを指定しないでください。
    [string]$OutputFolder = (Join-Path $env:USERPROFILE "BUI_PoCData")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SourceXlsx)) {
    Write-Host "[NG] 変換元が見つかりません: $SourceXlsx"
    exit 1
}

Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

# 日付として書き出す列。Excelのシリアル値を yyyy-MM-dd へ直します。
$dateColumns = @("publication_date", "reviewed_date")
$serialEpoch = Get-Date -Year 1899 -Month 12 -Day 30 -Hour 0 -Minute 0 -Second 0

function Convert-ColumnLetterToIndex([string]$reference) {
    $letters = ($reference -replace '[0-9]', '')
    $index = 0
    foreach ($character in $letters.ToCharArray()) {
        $index = $index * 26 + ([int][char]::ToUpper($character) - 64)
    }
    return $index - 1
}

function Convert-ToCsvField([string]$value) {
    if ($null -eq $value) { return '' }
    if ($value -match '[",\r\n]') {
        return '"' + ($value -replace '"', '""') + '"'
    }
    return $value
}

function Convert-SerialToDate([string]$value) {
    $number = 0
    if ([int]::TryParse($value, [ref]$number) -and $number -ge 20000 -and $number -le 60000) {
        return $serialEpoch.AddDays($number).ToString("yyyy-MM-dd")
    }
    return $value
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($SourceXlsx)
try {
    function Read-Entry([string]$name) {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq $name }
        if ($null -eq $entry) { return $null }
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    # 共有文字列表。セルの t="s" はこの表への添字です。
    $sharedStrings = @()
    $sharedXml = Read-Entry "xl/sharedStrings.xml"
    if ($null -ne $sharedXml) {
        foreach ($si in $sharedXml.sst.si) {
            $text = ""
            foreach ($t in $si.SelectNodes(".//*[local-name()='t']")) { $text += $t.InnerText }
            $sharedStrings += $text
        }
    }

    $workbook = Read-Entry "xl/workbook.xml"
    $relations = Read-Entry "xl/_rels/workbook.xml.rels"
    $relationMap = @{}
    foreach ($relation in $relations.Relationships.Relationship) { $relationMap[$relation.Id] = $relation.Target }

    if (-not (Test-Path -LiteralPath $OutputFolder)) {
        New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
    }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    $written = 0

    foreach ($sheet in $workbook.workbook.sheets.sheet) {
        $sheetName = $sheet.name
        $relationId = $sheet.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
        $target = $relationMap[$relationId] -replace '^/xl/', '' -replace '^/', ''
        if ($target -notlike "xl/*") { $target = "xl/$target" }

        $sheetXml = Read-Entry $target
        if ($null -eq $sheetXml) {
            Write-Host "[NG] シートを読めません: $sheetName"
            continue
        }

        $rows = @()
        foreach ($row in $sheetXml.SelectNodes("//*[local-name()='row']")) {
            $values = @{}
            $maximum = -1
            foreach ($cell in $row.SelectNodes("*[local-name()='c']")) {
                $index = Convert-ColumnLetterToIndex $cell.GetAttribute("r")
                $type = $cell.GetAttribute("t")
                $node = $cell.SelectSingleNode("*[local-name()='v']")
                $text = ""
                if ($type -eq "inlineStr") {
                    $inline = $cell.SelectSingleNode(".//*[local-name()='t']")
                    if ($null -ne $inline) { $text = $inline.InnerText }
                } elseif ($null -ne $node) {
                    if ($type -eq "s") { $text = $sharedStrings[[int]$node.InnerText] }
                    else { $text = $node.InnerText }
                }
                $values[$index] = $text
                if ($index -gt $maximum) { $maximum = $index }
            }
            $rows += ,@{ Values = $values; Max = $maximum }
        }

        if ($rows.Count -eq 0) {
            Write-Host "[--] 空のシートです: $sheetName"
            continue
        }

        # 見出し行から列数と、日付として直す列の位置を決めます。
        $headerCount = $rows[0].Max + 1
        $headers = @()
        for ($column = 0; $column -lt $headerCount; $column++) {
            $headers += [string]$rows[0].Values[$column]
        }

        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add((($headers | ForEach-Object { Convert-ToCsvField $_ }) -join ','))

        for ($rowIndex = 1; $rowIndex -lt $rows.Count; $rowIndex++) {
            $fields = @()
            for ($column = 0; $column -lt $headerCount; $column++) {
                $value = [string]$rows[$rowIndex].Values[$column]
                if ($dateColumns -contains $headers[$column]) { $value = Convert-SerialToDate $value }
                $fields += Convert-ToCsvField $value
            }
            # 全部空の行は書き出しません。空行はBUI側で読み飛ばしの警告になります。
            if (($fields -join '') -ne '') { $lines.Add(($fields -join ',')) }
        }

        $path = Join-Path $OutputFolder ($sheetName + ".csv")
        if (Test-Path -LiteralPath $path) {
            Copy-Item -LiteralPath $path -Destination "$path.$stamp.bak" -Force
        }
        [System.IO.File]::WriteAllLines($path, $lines, $utf8Bom)
        Write-Host ("[OK] {0,-22} {1,5} 行" -f ($sheetName + ".csv"), ($lines.Count - 1))
        $written++
    }

    Write-Host ""
    Write-Host "出力先: $OutputFolder"
    Write-Host "書き出したCSV: $written 枚"
    Write-Host "BUIの設定タブ「データフォルダ」にこのフォルダを指定してください。"
}
finally {
    $archive.Dispose()
}
```

- [ ] **Step 2: BOM付きで保存されているか確認する**

```bash
python -c "print(open(r'BUI_Addin_CSharpアドイン/scripts_開発補助/ConvertGoldStandard_データ変換.ps1','rb').read(3) == b'\xef\xbb\xbf')"
```

期待: `True`。`False` の場合はBOMを付けて保存し直す。

- [ ] **Step 3: 実行して結果を確認する**

```bash
powershell -ExecutionPolicy Bypass -File "BUI_Addin_CSharpアドイン/scripts_開発補助/ConvertGoldStandard_データ変換.ps1" -OutputFolder "$env:TEMP/BuiGsCheck"
```

期待:
- `[OK] Products.csv 30 行`
- `[OK] Product_Specs.csv 230 行`
- `[OK] Manufacturers.csv 7 行`
- `[OK] Spec_Master.csv 18 行`
- 13枚すべてが書き出される（`Product_Variants` と `Product_Images` は0行）

- [ ] **Step 4: 出力が仕様どおりか確認する**

```bash
python -c "
import io
p=r'$TEMP/BuiGsCheck/Sources.csv'
b=open(p,'rb').read()
print('BOM:', b[:3]==b'\xef\xbb\xbf')
t=b.decode('utf-8-sig').splitlines()
h=t[0].split(',')
i=h.index('publication_date')
print('publication_date sample:', t[1].split(',')[i])
"
```

期待: `BOM: True`、発行日が `2026-01-01` のような日付形式（`46023` ではない）。

- [ ] **Step 5: 貼り付け用の手順を書く**

`scripts_開発補助/ConvertGoldStandard_データ変換.txt`（UTF-8 BOM付き・CRLF）。
会社PCのPowerShellへそのまま貼れる内容にする:

```
【GoldStandardのExcelをCSVへ変換する】

1. Visual Studio の「表示」→「ターミナル」を開く

2. 次の1行を貼り付けて実行する
   （出力先を変えたい場合は -OutputFolder のパスだけ書き換える）

powershell -ExecutionPolicy Bypass -File ".\BUI_Addin_CSharpアドイン\scripts_開発補助\ConvertGoldStandard_データ変換.ps1" -OutputFolder "C:\BUI_PoCData"

3. [OK] が13行出れば成功。次の件数になっていることを確認する
   Products.csv        30 行
   Product_Specs.csv  230 行
   Manufacturers.csv    7 行

4. BUIの「設定」タブを開き、「データフォルダ」に手順2で指定したフォルダを入れて保存する

※ 出力先には必ず複製用のフォルダを指定してください。
   会社の元データが入っているフォルダを指定しないでください。
```

- [ ] **Step 6: 一時出力を消す**

```bash
rm -rf "$TEMP/BuiGsCheck"
```

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/scripts_開発補助/ConvertGoldStandard_データ変換.ps1" "BUI_Addin_CSharpアドイン/scripts_開発補助/ConvertGoldStandard_データ変換.txt"
git commit -m "feat: convert the GoldStandard workbook into the CSV folder BUI reads"
```

---

## Task 12: 手順書と問題解決の更新

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt`
- Modify: `BUI_Addin_CSharpアドイン/scripts_開発補助/CheckCsv_CSV診断.ps1`

**Interfaces:**
- Consumes: Task 10までの成果すべて
- Produces: なし（文書のみ）

- [ ] **Step 1: CSV診断をデータフォルダ対応にする**

`CheckCsv_CSV診断.ps1` は1枚の製品CSVを前提にしている。全文を次で置き換える。
**UTF-8 BOM付きで保存する。**

```powershell
# このスクリプトの目的:
#   BUIがデータを読めない原因を、Revitを立ち上げずに切り分けます。
#   原因はほぼ「フォルダの指定違い」「文字コード」「列名」のどれかです。
#
# 修正時の影響:
#   このファイル自体もUTF-8 BOM付きで保存してください。
#   BOM無しはWindows PowerShell 5.1がShift_JISとして読み、日本語が壊れます。

[CmdletBinding()]
param(
    [string]$DataFolder = (Join-Path $env:USERPROFILE "BUI_PoCData")
)

$ErrorActionPreference = "Stop"

Write-Host "データフォルダ: $DataFolder"
Write-Host ""

if (-not (Test-Path -LiteralPath $DataFolder)) {
    Write-Host "[NG] フォルダがありません。設定タブの「データフォルダ」を確認してください。"
    Write-Host "     未変換の場合は ConvertGoldStandard_データ変換.ps1 を先に実行してください。"
    exit 1
}

# BUIが使う表。Productsだけが必須で、他は無くても動きます（警告になります）。
$required = @("Products")
$optional = @("Manufacturers", "Material_Categories", "Spec_Master", "Display_Groups",
              "Product_Specs", "Sources", "Reliability_Ranks")

# 表ごとに、これが無いと機能しない列。
$requiredColumns = @{
    "Products"           = @("product_id", "product_name", "manufacturer_id", "material_category_id")
    "Manufacturers"      = @("manufacturer_id", "manufacturer_name")
    "Material_Categories" = @("material_category_id", "category_name_ja")
    "Spec_Master"        = @("spec_key", "display_name_ja", "display_group_id", "display_order", "standard_unit")
    "Display_Groups"     = @("display_group_id", "display_group_name_ja", "display_order")
    "Product_Specs"      = @("product_id", "spec_key", "value_number", "value_string")
    "Sources"            = @("source_id", "source_name", "publication_date")
    "Reliability_Ranks"  = @("reliability_rank_id", "rank_code")
}

$failures = 0

function Test-Table([string]$table, [bool]$isRequired) {
    $path = Join-Path $DataFolder ($table + ".csv")
    if (-not (Test-Path -LiteralPath $path)) {
        if ($isRequired) {
            Write-Host ("[NG] {0,-20} ファイルがありません" -f ($table + ".csv"))
            return 1
        }
        Write-Host ("[--] {0,-20} ファイルがありません（空として扱われます）" -f ($table + ".csv"))
        return 0
    }

    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)

    try {
        $strict = New-Object System.Text.UTF8Encoding($false, $true)
        $offset = if ($hasBom) { 3 } else { 0 }
        $text = $strict.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    catch {
        Write-Host ("[NG] {0,-20} UTF-8として読めません。Excelで「CSV UTF-8」を選んで保存し直してください" -f ($table + ".csv"))
        return 1
    }

    $lines = $text -split "`r?`n" | Where-Object { $_ -ne "" }
    if ($lines.Count -eq 0) {
        Write-Host ("[NG] {0,-20} 中身が空です" -f ($table + ".csv"))
        return 1
    }

    $headers = $lines[0] -split ","
    $headers = $headers | ForEach-Object { $_.Trim('"').Trim() }
    $missing = @()
    foreach ($column in $requiredColumns[$table]) {
        if ($headers -notcontains $column) { $missing += $column }
    }

    if ($missing.Count -gt 0) {
        Write-Host ("[NG] {0,-20} 列が足りません: {1}" -f ($table + ".csv"), ($missing -join ", "))
        Write-Host ("     実際の列名: {0}" -f ($headers -join ", "))
        return 1
    }

    $bomLabel = if ($hasBom) { "BOM付き" } else { "BOM無し" }
    Write-Host ("[OK] {0,-20} {1,5} 行  UTF-8 {2}" -f ($table + ".csv"), ($lines.Count - 1), $bomLabel)
    return 0
}

foreach ($table in $required) { $failures += Test-Table $table $true }
foreach ($table in $optional) { $failures += Test-Table $table $false }

Write-Host ""
if ($failures -eq 0) {
    Write-Host "すべて [OK] です。この状態でRevitを起動してください。"
    exit 0
}

Write-Host "[NG] が $failures 件あります。上の指示に従って直してから、もう一度実行してください。"
exit 1
```

- [ ] **Step 2: 問題解決へ新しい失敗パターンを足す**

`Troubleshooting_問題解決.md` を次のように変更する。

1. 「CSVが読めない、列不足、またはロックされている」の節を、データフォルダ前提へ書き直す
2. 次の節を追加する:

```markdown
## データフォルダが見つからない

設定タブの「データフォルダ」に、Products.csv などが入ったフォルダの絶対パスを入れます。
**1枚のCSVファイルではなくフォルダを指定します。** 未変換の場合は先に次を実行します。

```powershell
.\scripts_開発補助\ConvertGoldStandard_データ変換.ps1 -OutputFolder "C:\BUI_PoCData"
```

## 一部の表だけが無いと言われる

Product_Variants と Product_Images は現時点で0件です。ファイルが無くてもBUIは動きます。
警告は「その表を空として扱った」という報告であり、異常ではありません。

Sources.csv や Reliability_Ranks.csv が無い場合は、概要タブの「情報源」が空になります。
属性の表示そのものには影響しません。

## 概要タブに製品カードが出ない

表示される文言で原因が分かります。

| 画面の表示 | 原因 | 対処 |
| --- | --- | --- |
| この要素にはProduct_IDが付与されていません。 | 未付与 | 検索タブで候補を選んで付与します。 |
| Product_ID『xxx』は製品データにありません。 | 旧IDのまま、またはデータフォルダが別物 | データフォルダを確認します。旧IDは旧新ID CSVで対応付けます。 |
| 製品データの参照先が設定されていません。 | データフォルダが未設定 | 設定タブで指定します。 |
| 製品情報を読み込めませんでした。 | CSVが壊れている、または開かれている | Excelで開いていれば閉じ、CSV診断を実行します。 |

## 概要タブの数値が「264」になっている

`264` は元Excelからの取り込み事故で、BUIは空欄として扱います。
画面に264が出ている場合、それは変換スクリプトを通していないCSVです。
`ConvertGoldStandard_データ変換.ps1` で作り直してください。

## 製品画像が出ない

BUIは**データフォルダ内の画像ファイルだけ**を表示します。
`Product_Images.csv` の `image_url`（外部URL）は意図的に読みません。
画像を出すには、`file_name` と同じ名前の画像ファイルをデータフォルダへ置いてください。
```

3. 「候補が0件になる」「絞り込み項目が空」の節にある旧6項目（カテゴリー／用途／メーカー名／製品名／型番／構造）を、
   新しい5項目（建材分類／メーカー名／製品名／防火性能／厚さ）へ書き換える

- [ ] **Step 3: 会社PC向けの手順書を書き直す**

`docs_説明書/操作手順_会社PC_次にやること.txt` を全面的に書き直す。**UTF-8 BOM付き・CRLF。**
番号付きで、各段階に確認ポイントを付ける。内容は次の順序にする。

1. Revitを完全に終了する（タスクマネージャーで `Revit.exe` が残っていないことも確認）
2. `git pull origin main`
3. Visual Studio で「ビルド」→「ソリューションのリビルド」
4. データ変換（`ConvertGoldStandard_データ変換.ps1`）。13枚の `[OK]` と件数を確認
5. CSV診断（`CheckCsv_CSV診断.ps1`）。8枚すべて `[OK]` を確認
6. **Revitより先に**配置診断（`CheckDeploy_配置診断.ps1`）。すべて `[OK]` を確認
7. Revitを起動し、BUIの「設定」タブでデータフォルダを指定して保存
8. 確認すること（これはRevitがある環境でしか確認できない）
   - 検索タブ: 絞り込みが 建材分類／メーカー名／製品名／防火性能／厚さ の5項目になっている
   - 検索タブ: 「厚さ」の選択肢が 9.5 → 12.5 → 15 の順（数値順）に並ぶ
   - 概要タブ: Product_IDを付けた要素を選ぶと製品カードが出る
   - 概要タブ: タブが 基本仕様／性能／情報源／関連資料 の4つになっている
   - 概要タブ: 長さが `1,820 mm` と桁区切りで出る
   - 概要タブ: 画面のどこにも `264` が出ていない
   - Ctrl+Z 1回で付与が元に戻る
9. 新規登録を試す場合の注意（**必ず複製フォルダに対して行う。会社の元データへは向けない**）

- [ ] **Step 4: 文書に壊れた文字が無いことを確認する**

```bash
python -c "
import glob
for p in ['BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md','BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt']:
    b=open(p,'rb').read()
    bad=[c for c in b.decode('utf-8-sig') if ord(c)<32 and c not in '\r\n\t']
    print(p, 'BOM' if b[:3]==b'\xef\xbb\xbf' else 'no-BOM', 'control-chars:', bad)
"
```

期待: `.txt` は `BOM`、制御文字は空。

- [ ] **Step 5: commit**

```bash
git add "BUI_Addin_CSharpアドイン/docs_説明書" "BUI_Addin_CSharpアドイン/scripts_開発補助"
git commit -m "docs: rewrite the walkthrough and troubleshooting for the data folder"
```

---

## 最終確認（全タスク完了後）

- [ ] **全テストを実行し、終了コードで判定する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

- [ ] **競合マーカーが無いことを確認する**

```bash
grep -rn "^<<<<<<<\|^>>>>>>>" --include=* "BUI_Addin_CSharpアドイン" docs | grep -v "/obj/" | grep -v "/bin/"
```

期待: 何も出ない。

- [ ] **制御文字が混入していないことを確認する**

```bash
python -c "
import subprocess
files = subprocess.check_output(['git','ls-files']).decode('utf-8').split('\n')
for f in files:
    if not f or f.endswith(('.xlsx','.png','.jpg','.dll','.bak')): continue
    try: t = open(f,'rb').read().decode('utf-8-sig')
    except Exception: continue
    bad = sorted({hex(ord(c)) for c in t if ord(c) < 32 and c not in '\r\n\t'})
    if bad: print(f, bad)
"
```

期待: 何も出ない。

- [ ] **削除漏れが無いことを確認する**

```bash
grep -rn "CsvProductRepository\|ProductRowMapper\|ProductCsvPath\|BrowseProductCsvCommand\|productCsvPath" --include=*.cs --include=*.xaml --include=*.json --include=*.md --include=*.ps1 "BUI_Addin_CSharpアドイン" | grep -v "/obj/" | grep -v "/bin/"
```

期待: 何も出ない。

- [ ] **手順書を利用者へ渡す**

`docs_説明書/操作手順_会社PC_次にやること.txt` を SendUserFile で渡す。
「私が検証できたのは単体テストまで。画面の表示・実際の書き込み・Ctrl+Zは会社PCでの確認が必要」
であることを明示する。

