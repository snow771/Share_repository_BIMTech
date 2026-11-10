# 仮登録レビューツール（Bui.Curation） Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 複数の現場CSVフォルダから仮登録データ（`BM_NEW-` 接頭辞のProduct_ID）を取り込み、不足情報を加筆・正式IDへ書き換えたうえでSQL Serverへ直接UPSERT登録する、Revit非依存の新しいWPFデスクトップアプリ `Bui.Curation` を作る。

**Architecture:** `Bui.Addin` と同列の新規プロジェクトとして追加し、`Bui.Shared_共有ロジック` のソースを同じ方式（ワイルドカードによるソースリンク）で取り込む。ロジック（取込・重複チェック・旧新ID CSV追記）はBui.Shared側の純粋なクラスとして実装しテストする。SQL接続と画面（WPF）はBui.Curation側に置く。

**Tech Stack:** C# 12 / .NET 8（`net8.0-windows`）、WPF、MSTest（Bui.Tests）、`Microsoft.Data.SqlClient`。

## Global Constraints

- 取込は元のCSVフォルダを一切書き換えない（読み取り専用）。
- 作業用データはSQLite等を使わず、ローカルのJSONファイル1つ（既定 `%LOCALAPPDATA%\StartsCAM\BUI\Curation\queue.json`）で管理する。
- 正式なProduct_IDの自動採番は行わない。管理者が手入力し、ツールは重複チェックのみ行う。
- 関連表のチェックリストはProduct_SpecsとProduct_Imagesの2表のみ（Sources・Product_Variantsは対象外。設計スペック参照）。
- テストが必要なロジックはすべてBui.Shared_共有ロジック側に置く（私物PCで `dotnet test` できるようにするため）。`Bui.Curation`はRevit非依存のため、このプロジェクト自体も私物PCでビルド・実行して確認できる。
- YAGNI: 現場CSVフォルダの自動集約、正式ID自動採番、複数管理者の排他制御、仮登録データの却下・削除機能は実装しない。

参照: [2026-09-16-curation-tool-design.md](../specs/2026-09-16-curation-tool-design.md)

---

### Task 1: Bui.Curationプロジェクトの雛形

**Files:**
- Create: `src_ソース/Bui.Curation/Bui.Curation.csproj`
- Create: `src_ソース/Bui.Curation/App.xaml`
- Create: `src_ソース/Bui.Curation/App.xaml.cs`
- Create: `src_ソース/Bui.Curation/MainWindow.xaml`
- Create: `src_ソース/Bui.Curation/MainWindow.xaml.cs`
- Modify: `Bui.sln`

**Interfaces:**
- Produces: ビルド・起動できる最小限のWPFアプリ（ウィンドウにタイトルだけ表示）。後続タスクはこのプロジェクトへファイルを追加していく。

- [ ] **Step 1: プロジェクトファイルを作成する**

`src_ソース/Bui.Curation/Bui.Curation.csproj` を新規作成する（UTF-8 BOM付き）。

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <PlatformTarget>x64</PlatformTarget>
    <Version>0.1.0</Version>
    <AssemblyName>Bui.Curation</AssemblyName>
    <RootNamespace>Bui.Curation</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!--
    Bui.Shared_共有ロジックはプロジェクトファイルを持たない「ソースリンクだけの
    フォルダ」です（Bui.Addin・Bui.Testsと同じ方式）。新しいファイルを
    Bui.Shared側へ追加しても、このワイルドカードが自動的に拾います。
  -->
  <ItemGroup>
    <Compile Include="..\Bui.Shared_共有ロジック\**\*.cs"
             Link="Shared\%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: App.xaml / App.xaml.cs を作成する**

`src_ソース/Bui.Curation/App.xaml` を新規作成する。

```xml
<Application x:Class="Bui.Curation.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
</Application>
```

`src_ソース/Bui.Curation/App.xaml.cs` を新規作成する。

```csharp
using System.Windows;

namespace Bui.Curation;

public partial class App : Application
{
}
```

- [ ] **Step 3: MainWindow.xaml / MainWindow.xaml.cs を作成する（最小限）**

`src_ソース/Bui.Curation/MainWindow.xaml` を新規作成する。

```xml
<Window x:Class="Bui.Curation.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="BUI 仮登録レビューツール" Height="600" Width="900">
    <Grid>
        <TextBlock Text="BUI 仮登録レビューツール" FontSize="20" Margin="16" />
    </Grid>
</Window>
```

`src_ソース/Bui.Curation/MainWindow.xaml.cs` を新規作成する。

```csharp
using System.Windows;

namespace Bui.Curation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Bui.slnへプロジェクトを追加する**

`Bui.sln` を開き、`Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests_テスト", ...` の `EndProject` の直後（`Global` の直前）に、次の1行を追加する。

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Bui.Curation", "src_ソース\Bui.Curation\Bui.Curation.csproj", "{3C5D0225-C0D5-43B0-9369-B6D70CB9A62E}"
EndProject
```

`GlobalSection(ProjectConfigurationPlatforms) = postSolution` の中、既存の2プロジェクト分の行の直後に、次の4行を追加する。

```
		{3C5D0225-C0D5-43B0-9369-B6D70CB9A62E}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{3C5D0225-C0D5-43B0-9369-B6D70CB9A62E}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{3C5D0225-C0D5-43B0-9369-B6D70CB9A62E}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{3C5D0225-C0D5-43B0-9369-B6D70CB9A62E}.Release|Any CPU.Build.0 = Release|Any CPU
```

`GlobalSection(NestedProjects)` は変更しない（Bui.Curationは `src_ソース` フォルダの子ではなく、ソリューション直下に置くため）。

- [ ] **Step 5: ビルドを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe build src_ソース\Bui.Curation\Bui.Curation.csproj -c Debug`
Expected: `ビルドに成功しました。`（このプロジェクトはRevit非依存のため、私物PCでビルドが通ることを確認できる）

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Curation Bui.sln
git commit -m "feat: Bui.Curationプロジェクトの雛形を追加する"
```

---

### Task 2: ProvisionalRecordScanner（仮登録データの取込）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/ProvisionalRecordScanner_仮登録取込.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/ProvisionalRecordScannerTests_仮登録取込テスト.cs`

**Interfaces:**
- Consumes: `Bui.Data.GoldStandard.GoldStandardTableSet.Load(string? folderPath) : GoldStandardTableSet`、
  `GoldStandardTableSet.Rows(string table) : IReadOnlyList<IReadOnlyDictionary<string, string>>`、
  `Bui.Data.GoldStandard.GoldStandardTables.Products`（`"Products"`）、`.ProductSpecs`（`"Product_Specs"`）、
  `.Columns.ProductId`（`"product_id"`）
- Produces: `Bui.Curation.ScannedProvisionalRecord`
  （`SourceFolder: string, ProvisionalId: string, Values: IReadOnlyDictionary<string,string>, SpecRows: IReadOnlyList<IReadOnlyDictionary<string,string>>`）
  `Bui.Curation.ProvisionalRecordScanner.Scan(string folderPath, string provisionalIdPrefix) : IReadOnlyList<ScannedProvisionalRecord>`
  （後続タスクの `CurationQueueStore` がこれを受け取ってキューへ追加する）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Curation_仮登録レビュー/ProvisionalRecordScannerTests_仮登録取込テスト.cs` を新規作成する。テスト用のGoldStandardフォルダは、`CsvTestFiles`と同じ考え方で一時ディレクトリへ複数CSVを書き出す。

```csharp
// このファイルはProvisionalRecordScannerが、仮ID接頭辞に一致する行だけを
// 正しく拾い、関連するProduct_Specsも突き合わせられることを守ります。
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public class ProvisionalRecordScannerTests_仮登録取込テスト
{
    [TestMethod]
    public void Scan_仮ID接頭辞に一致する行だけを拾う()
    {
        using var folder = GoldStandardFolderTestFiles.Create(
            products: "product_id,product_name,manufacturer_id,material_category_id\r\n"
                + "BM_NEW-0001,テスト新製品,,\r\n"
                + "P-001,既存製品,M-1,C-1\r\n",
            productSpecs: "product_id,spec_key,value_type,value_number,value_string\r\n");

        var result = ProvisionalRecordScanner.Scan(folder.DirectoryPath, "BM_NEW-");

        var record = result.Single();
        Assert.AreEqual("BM_NEW-0001", record.ProvisionalId);
        Assert.AreEqual(folder.DirectoryPath, record.SourceFolder);
        Assert.AreEqual("テスト新製品", record.Values["product_name"]);
    }

    [TestMethod]
    public void Scan_関連するProductSpecs行を突き合わせる()
    {
        using var folder = GoldStandardFolderTestFiles.Create(
            products: "product_id,product_name,manufacturer_id,material_category_id\r\n"
                + "BM_NEW-0001,テスト新製品,,\r\n",
            productSpecs: "product_id,spec_key,value_type,value_number,value_string\r\n"
                + "BM_NEW-0001,thickness,number,12.5,\r\n"
                + "P-001,thickness,number,9.5,\r\n");

        var result = ProvisionalRecordScanner.Scan(folder.DirectoryPath, "BM_NEW-");

        var record = result.Single();
        Assert.AreEqual(1, record.SpecRows.Count, "他の製品のProduct_Specs行を混ぜないこと。");
        Assert.AreEqual("thickness", record.SpecRows[0]["spec_key"]);
    }

    [TestMethod]
    public void Scan_仮IDが無ければ空を返す()
    {
        using var folder = GoldStandardFolderTestFiles.Create(
            products: "product_id,product_name,manufacturer_id,material_category_id\r\n"
                + "P-001,既存製品,M-1,C-1\r\n",
            productSpecs: "product_id,spec_key,value_type,value_number,value_string\r\n");

        var result = ProvisionalRecordScanner.Scan(folder.DirectoryPath, "BM_NEW-");

        Assert.AreEqual(0, result.Count);
    }
}
```

`GoldStandardFolderTestFiles` はこのタスクで同じフォルダへ新規作成する共有テストヘルパーです（`Bui.Tests.Curation_仮登録レビュー` 名前空間の他のテストからも使う）。同じテストファイル内、上のクラスの外（同一ファイル内の別クラス）に追加する。

```csharp
internal sealed class GoldStandardFolderTestFiles : IDisposable
{
    private GoldStandardFolderTestFiles(string directoryPath) => DirectoryPath = directoryPath;

    public string DirectoryPath { get; }

    public static GoldStandardFolderTestFiles Create(
        string products, string productSpecs, string? productImages = null)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "BuiCurationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        var encoding = new System.Text.UTF8Encoding(false, true);
        File.WriteAllText(Path.Combine(directoryPath, "Products.csv"), products, encoding);
        File.WriteAllText(Path.Combine(directoryPath, "Product_Specs.csv"), productSpecs, encoding);
        if (productImages is not null)
        {
            File.WriteAllText(Path.Combine(directoryPath, "Product_Images.csv"), productImages, encoding);
        }
        return new GoldStandardFolderTestFiles(directoryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~ProvisionalRecordScannerTests" -v minimal`
Expected: ビルドエラー（`Bui.Curation` 名前空間や `ProvisionalRecordScanner` が存在しない）。

- [ ] **Step 3: 実装を書く**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/ProvisionalRecordScanner_仮登録取込.cs` を新規作成する。

```csharp
// このファイルの目的:
//   現場のデータフォルダから、仮ID（例: BM_NEW-0001）が付いた
//   Products.csvの行を拾い、関連するProduct_Specsも突き合わせます。
//
// なぜ元のCSVを書き換えないのか:
//   このツールは複数の現場フォルダから読み取るだけの、管理者専用の
//   レビューツールです。元のフォルダを書き換えると、現場のBUIが
//   次に読んだときの内容と食い違う恐れがあります。
//
// 修正時の影響:
//   product_id列での突き合わせは大文字小文字を区別しません
//   （GoldStandardProductRepositoryのIsSameProductIdと同じ規則に揃えます）。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>1件の仮登録データです。出どころフォルダと、関連するProduct_Specs行を保持します。</summary>
public sealed record ScannedProvisionalRecord(
    string SourceFolder,
    string ProvisionalId,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<IReadOnlyDictionary<string, string>> SpecRows);

/// <summary>データフォルダから仮登録データを拾います。</summary>
public static class ProvisionalRecordScanner
{
    /// <summary>指定フォルダのProducts.csvから、provisionalIdPrefixで始まる行を拾います。</summary>
    public static IReadOnlyList<ScannedProvisionalRecord> Scan(string folderPath, string provisionalIdPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionalIdPrefix);

        var set = GoldStandardTableSet.Load(folderPath);
        var products = set.Rows(GoldStandardTables.Products);
        var specs = set.Rows(GoldStandardTables.ProductSpecs);

        var results = new List<ScannedProvisionalRecord>();
        foreach (var row in products)
        {
            var productId = row.TryGetValue(GoldStandardTables.Columns.ProductId, out var id) ? id.Trim() : string.Empty;
            if (!productId.StartsWith(provisionalIdPrefix, StringComparison.Ordinal)) continue;

            var matchedSpecs = specs
                .Where(specRow => specRow.TryGetValue(GoldStandardTables.Columns.ProductId, out var specProductId)
                    && string.Equals(specProductId.Trim(), productId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            results.Add(new ScannedProvisionalRecord(folderPath, productId, row, matchedSpecs));
        }

        return results;
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~ProvisionalRecordScannerTests" -v minimal`
Expected: PASS（3件成功）。

- [ ] **Step 5: 全テストを実行し、既存テストを壊していないことを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功）。

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー tests_テスト/Bui.Tests/Curation_仮登録レビュー
git commit -m "feat: ProvisionalRecordScannerを追加し、仮登録データの取込に対応する"
```

---

### Task 3: CurationQueueItem / CurationQueueStore（作業用データの保存）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationQueueItem_レビュー項目.cs`
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationQueueStore_作業用データ保存.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationQueueStoreTests_作業用データ保存テスト.cs`

**Interfaces:**
- Consumes: `Bui.Curation.ScannedProvisionalRecord`（Task 2）
- Produces: `Bui.Curation.CurationQueueItem`
  （`SourceFolder, ProvisionalId, Values: Dictionary<string,string>, IsCompleted: bool, OfficialId: string?, Warnings: List<string>`）
  `Bui.Curation.CurationQueueStore.Load(string filePath) : List<CurationQueueItem>`、
  `CurationQueueStore.Save(string filePath, IReadOnlyList<CurationQueueItem> items) : void`、
  `CurationQueueStore.AddIfMissing(List<CurationQueueItem> items, IReadOnlyList<ScannedProvisionalRecord> scanned) : int`
  （追加した件数を返す。後続タスクのCurationDbWriter・MainWindowがこれらを使う）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationQueueStoreTests_作業用データ保存テスト.cs` を新規作成する。

```csharp
// このファイルはCurationQueueStoreが、作業用データ(queue.json)を正しく
// 読み書きし、同じ(出どころフォルダ+元の仮ID)を二重に追加しないことを守ります。
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public class CurationQueueStoreTests_作業用データ保存テスト
{
    [TestMethod]
    public void SaveしてLoadすると同じ内容が戻る()
    {
        using var files = CurationQueueTestFiles.Create();
        var items = new List<CurationQueueItem>
        {
            new()
            {
                SourceFolder = @"C:\data\folderA",
                ProvisionalId = "BM_NEW-0001",
                Values = new Dictionary<string, string> { ["product_name"] = "テスト製品" },
            }
        };

        CurationQueueStore.Save(files.QueuePath, items);
        var loaded = CurationQueueStore.Load(files.QueuePath);

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("BM_NEW-0001", loaded[0].ProvisionalId);
        Assert.AreEqual("テスト製品", loaded[0].Values["product_name"]);
    }

    [TestMethod]
    public void Load_ファイルが無ければ空の一覧を返す()
    {
        using var files = CurationQueueTestFiles.Create();
        var missingPath = System.IO.Path.Combine(files.DirectoryPath, "no-such-file.json");

        var loaded = CurationQueueStore.Load(missingPath);

        Assert.AreEqual(0, loaded.Count);
    }

    [TestMethod]
    public void AddIfMissing_同じ出どころと仮IDは二重に追加しない()
    {
        var items = new List<CurationQueueItem>
        {
            new() { SourceFolder = @"C:\data\folderA", ProvisionalId = "BM_NEW-0001", Values = new() }
        };
        var scanned = new[]
        {
            new ScannedProvisionalRecord(
                @"C:\data\folderA", "BM_NEW-0001",
                new Dictionary<string, string> { ["product_name"] = "更新後の値" },
                Array.Empty<IReadOnlyDictionary<string, string>>()),
            new ScannedProvisionalRecord(
                @"C:\data\folderA", "BM_NEW-0002",
                new Dictionary<string, string> { ["product_name"] = "新しい行" },
                Array.Empty<IReadOnlyDictionary<string, string>>())
        };

        var added = CurationQueueStore.AddIfMissing(items, scanned);

        Assert.AreEqual(1, added, "新規の1件だけを追加すること。");
        Assert.AreEqual(2, items.Count);
        Assert.AreNotEqual("更新後の値", items[0].Values["product_name"], "既存項目は上書きしないこと。");
    }
}

internal sealed class CurationQueueTestFiles : IDisposable
{
    private CurationQueueTestFiles(string directoryPath)
    {
        DirectoryPath = directoryPath;
        QueuePath = System.IO.Path.Combine(directoryPath, "queue.json");
    }

    public string DirectoryPath { get; }
    public string QueuePath { get; }

    public static CurationQueueTestFiles Create()
    {
        var directoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BuiCurationQueueTests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directoryPath);
        return new CurationQueueTestFiles(directoryPath);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(DirectoryPath))
        {
            System.IO.Directory.Delete(DirectoryPath, true);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~CurationQueueStoreTests" -v minimal`
Expected: ビルドエラー（`CurationQueueItem`・`CurationQueueStore` が存在しない）。

- [ ] **Step 3: CurationQueueItemを作る**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationQueueItem_レビュー項目.cs` を新規作成する。

```csharp
// このファイルの目的:
//   仮登録レビューツールが作業中に持つ1件分の状態です。
//   System.Text.Jsonでそのまま読み書きできるよう、単純な可変クラスにします
//   （record + 位置引数コンストラクタだと、JSON側のキー名との対応で
//   余計な注意が必要になるため、プロパティ名だけで素直に対応させます）。

namespace Bui.Curation;

/// <summary>1件の仮登録レビュー項目です。</summary>
public sealed class CurationQueueItem
{
    public string SourceFolder { get; set; } = string.Empty;
    public string ProvisionalId { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string? OfficialId { get; set; }
    public List<string> Warnings { get; set; } = new();
}
```

- [ ] **Step 4: CurationQueueStoreを作る**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationQueueStore_作業用データ保存.cs` を新規作成する。

```csharp
// このファイルの目的:
//   仮登録レビューの作業用データ(queue.json)を読み書きします。
//   SQLite等を持ち込まず、BUI全体の「CSV/JSON中心」という方針に合わせます。
//
// なぜ二重追加を防ぐのか:
//   同じ現場フォルダを何度も取り込む運用を想定しています。
//   毎回全件追加すると、レビュー中の内容が上書きされて消えます。

using System.Text.Json;

namespace Bui.Curation;

/// <summary>作業用データ(queue.json)の読み書きです。</summary>
public static class CurationQueueStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>ファイルが無ければ空の一覧を返します（初回起動時に例外にしないため）。</summary>
    public static List<CurationQueueItem> Load(string filePath)
    {
        if (!File.Exists(filePath)) return new List<CurationQueueItem>();

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<CurationQueueItem>>(json, Options) ?? new List<CurationQueueItem>();
    }

    public static void Save(string filePath, IReadOnlyList<CurationQueueItem> items)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(items, Options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// スキャン結果のうち、(出どころフォルダ+元の仮ID)がまだ一覧に無いものだけを追加します。
    /// 追加した件数を返します。
    /// </summary>
    public static int AddIfMissing(List<CurationQueueItem> items, IReadOnlyList<ScannedProvisionalRecord> scanned)
    {
        var existingKeys = items
            .Select(item => (item.SourceFolder, item.ProvisionalId))
            .ToHashSet();

        var added = 0;
        foreach (var record in scanned)
        {
            var key = (record.SourceFolder, record.ProvisionalId);
            if (existingKeys.Contains(key)) continue;

            items.Add(new CurationQueueItem
            {
                SourceFolder = record.SourceFolder,
                ProvisionalId = record.ProvisionalId,
                Values = new Dictionary<string, string>(record.Values)
            });
            existingKeys.Add(key);
            added++;
        }

        return added;
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~CurationQueueStoreTests" -v minimal`
Expected: PASS（3件成功）。

- [ ] **Step 6: 全テストを実行する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功）。

- [ ] **Step 7: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー tests_テスト/Bui.Tests/Curation_仮登録レビュー
git commit -m "feat: CurationQueueItem/CurationQueueStoreを追加し、作業用データを保存できるようにする"
```

---

### Task 4: RelatedTableChecker（関連表の状況表示）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/RelatedTableChecker_関連表確認.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/RelatedTableCheckerTests_関連表確認テスト.cs`

**Interfaces:**
- Consumes: なし（Task 2で定義済みの `GoldStandardTables` 定数を使う）
- Produces: `Bui.Curation.RelatedTableSummary`（`ProductSpecCount: int, ProductImageCount: int, Warnings: IReadOnlyList<string>`）、
  `Bui.Curation.RelatedTableChecker.Check(string productId, string manufacturerName, string categoryName, GoldStandardTableSet masterSet) : RelatedTableSummary`
  （後続タスクのMainWindowがこれを詳細画面に表示する）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Curation_仮登録レビュー/RelatedTableCheckerTests_関連表確認テスト.cs` を新規作成する。

```csharp
// このファイルはRelatedTableCheckerが、関連表の件数を正しく数え、
// マスタに実在しないメーカー名・建材分類を警告として返すことを守ります。
// これらは情報表示だけで、登録をブロックする判断には使いません。
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public class RelatedTableCheckerTests_関連表確認テスト
{
    [TestMethod]
    public void Check_関連表の件数を数える()
    {
        using var folder = GoldStandardFolderTestFiles.Create(
            products: "product_id,product_name,manufacturer_id,material_category_id\r\n"
                + "BM_NEW-0001,テスト新製品,,\r\n",
            productSpecs: "product_id,spec_key,value_type,value_number,value_string\r\n"
                + "BM_NEW-0001,thickness,number,12.5,\r\n"
                + "BM_NEW-0001,color,string,,白\r\n"
                + "P-001,thickness,number,9.5,\r\n",
            productImages: "image_id,product_id,image_url,file_name,is_primary\r\n"
                + "IMG-1,BM_NEW-0001,,sample.jpg,1\r\n");
        var set = Bui.Data.GoldStandard.GoldStandardCsvReader.Load(folder.DirectoryPath);

        var summary = RelatedTableChecker.Check("BM_NEW-0001", "存在するメーカー", "存在する分類", set);

        Assert.AreEqual(2, summary.ProductSpecCount);
        Assert.AreEqual(1, summary.ProductImageCount);
    }

    [TestMethod]
    public void Check_マスタに無いメーカー名は警告にする()
    {
        using var folder = GoldStandardFolderTestFiles.Create(
            products: "product_id,product_name,manufacturer_id,material_category_id\r\n"
                + "BM_NEW-0001,テスト新製品,,\r\n",
            productSpecs: "product_id,spec_key,value_type,value_number,value_string\r\n");
        var set = Bui.Data.GoldStandard.GoldStandardCsvReader.Load(folder.DirectoryPath);

        var summary = RelatedTableChecker.Check("BM_NEW-0001", "存在しないメーカー", string.Empty, set);

        Assert.IsTrue(summary.Warnings.Any(warning => warning.Contains("存在しないメーカー")));
    }
}
```

このテストで `GoldStandardFolderTestFiles.Create` の `productImages` 引数を使うため、Task 2で作成した `GoldStandardFolderTestFiles` のシグネチャに既に含めている（`string? productImages = null`）ことを前提とする。マスタ（Manufacturers.csv・Material_Categories.csv）は今回のテストフォルダには含めていない（存在しないマスタファイルとして扱われ、`GoldStandardTableSet.Rows(...)` は空の一覧を返す。この場合「実在するメーカー名」は無い扱いになるため、1件目のテストで渡す名前が実際にはマスタに存在しないことになる点に注意。1件目のテストは件数の確認だけを行い、警告の有無は確認しないため問題ない。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~RelatedTableCheckerTests" -v minimal`
Expected: ビルドエラー（`RelatedTableChecker` が存在しない）。

- [ ] **Step 3: 実装を書く**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/RelatedTableChecker_関連表確認.cs` を新規作成する。

```csharp
// このファイルの目的:
//   仮登録データに関連する他の表（Product_Specs、Product_Images）の
//   件数と、メーカー名・建材分類がマスタに実在するかを情報として示します。
//
// なぜ登録をブロックしないのか:
//   0件でも正当な製品はあります（画像が無い製品、仕様が少ない製品など）。
//   最終判断は管理者に委ねます。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>関連表の状況です。すべて情報表示用で、登録の可否には使いません。</summary>
public sealed record RelatedTableSummary(int ProductSpecCount, int ProductImageCount, IReadOnlyList<string> Warnings);

/// <summary>関連表の件数とマスタ実在チェックを行います。</summary>
public static class RelatedTableChecker
{
    public static RelatedTableSummary Check(
        string productId, string manufacturerName, string categoryName, GoldStandardTableSet masterSet)
    {
        ArgumentNullException.ThrowIfNull(masterSet);
        var id = (productId ?? string.Empty).Trim();

        var specCount = masterSet.Rows(GoldStandardTables.ProductSpecs)
            .Count(row => MatchesProductId(row, id));
        var imageCount = masterSet.Rows(GoldStandardTables.ProductImages)
            .Count(row => MatchesProductId(row, id));

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(manufacturerName) && !ExistsInMaster(
            masterSet, GoldStandardTables.Manufacturers, GoldStandardTables.Columns.ManufacturerName, manufacturerName))
        {
            warnings.Add($"メーカー名「{manufacturerName}」はManufacturers.csvに存在しません。");
        }
        if (!string.IsNullOrWhiteSpace(categoryName) && !ExistsInMaster(
            masterSet, GoldStandardTables.MaterialCategories, GoldStandardTables.Columns.CategoryNameJa, categoryName))
        {
            warnings.Add($"建材分類「{categoryName}」はMaterial_Categories.csvに存在しません。");
        }

        return new RelatedTableSummary(specCount, imageCount, warnings);
    }

    private static bool MatchesProductId(IReadOnlyDictionary<string, string> row, string productId) =>
        row.TryGetValue(GoldStandardTables.Columns.ProductId, out var value)
        && string.Equals(value.Trim(), productId, StringComparison.OrdinalIgnoreCase);

    private static bool ExistsInMaster(
        GoldStandardTableSet masterSet, string table, string nameColumn, string name) =>
        masterSet.Rows(table).Any(row =>
            row.TryGetValue(nameColumn, out var value)
            && string.Equals(value.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~RelatedTableCheckerTests" -v minimal`
Expected: PASS（2件成功）。

- [ ] **Step 5: 全テストを実行する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功）。

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー tests_テスト/Bui.Tests/Curation_仮登録レビュー
git commit -m "feat: RelatedTableCheckerを追加し、関連表の状況を情報表示する"
```

---

### Task 5: OfficialIdValidator（正式ID重複チェック）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/OfficialIdValidator_正式ID重複確認.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/OfficialIdValidatorTests_正式ID重複確認テスト.cs`

**Interfaces:**
- Consumes: なし
- Produces: `Bui.Curation.OfficialIdValidator.Validate(string officialId, IReadOnlyCollection<string> existingDatabaseIds, IReadOnlyCollection<string> pendingQueueIds) : string?`
  （問題なければnull、問題があれば理由の文字列を返す。後続タスクのMainWindow・CurationDbWriterが使う）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Curation_仮登録レビュー/OfficialIdValidatorTests_正式ID重複確認テスト.cs` を新規作成する。

```csharp
// このファイルはOfficialIdValidatorが、DB上の既存IDともキュー内の
// 他のドラフトの正式IDとも重複しないことを確認できることを守ります。
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public class OfficialIdValidatorTests_正式ID重複確認テスト
{
    [TestMethod]
    public void Validate_空欄はエラーにする()
    {
        var result = OfficialIdValidator.Validate("", Array.Empty<string>(), Array.Empty<string>());

        Assert.IsNotNull(result);
        StringAssert.Contains(result, "入力");
    }

    [TestMethod]
    public void Validate_DB上の既存IDと重複していればエラーにする()
    {
        var result = OfficialIdValidator.Validate("P-001", new[] { "P-001" }, Array.Empty<string>());

        Assert.IsNotNull(result);
        StringAssert.Contains(result, "P-001");
    }

    [TestMethod]
    public void Validate_キュー内の他のドラフトと重複していればエラーにする()
    {
        var result = OfficialIdValidator.Validate("P-002", Array.Empty<string>(), new[] { "P-002" });

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void Validate_重複が無ければnullを返す()
    {
        var result = OfficialIdValidator.Validate("P-003", new[] { "P-001" }, new[] { "P-002" });

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Validate_大文字小文字は区別しない()
    {
        var result = OfficialIdValidator.Validate("p-001", new[] { "P-001" }, Array.Empty<string>());

        Assert.IsNotNull(result);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~OfficialIdValidatorTests" -v minimal`
Expected: ビルドエラー（`OfficialIdValidator` が存在しない）。

- [ ] **Step 3: 実装を書く**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/OfficialIdValidator_正式ID重複確認.cs` を新規作成する。

```csharp
// このファイルの目的:
//   管理者が手入力した正式Product_IDが、DB上の既存値およびキュー内の
//   他のドラフトの正式IDと重複しないことを確認します。
//   自動採番は行いません（決まった採番ルールが無いため）。

namespace Bui.Curation;

/// <summary>正式Product_IDの重複を確認します。</summary>
public static class OfficialIdValidator
{
    /// <summary>問題が無ければnull、問題があれば理由を返します。</summary>
    public static string? Validate(
        string? officialId, IReadOnlyCollection<string> existingDatabaseIds, IReadOnlyCollection<string> pendingQueueIds)
    {
        var trimmed = (officialId ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "正式なProduct_IDを入力してください。";
        }

        if (existingDatabaseIds.Any(id => string.Equals(id.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"正式ID「{trimmed}」はすでにデータベースに存在します。";
        }

        if (pendingQueueIds.Any(id => string.Equals(id.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"正式ID「{trimmed}」は、まだ登録していない他の仮登録データと重複しています。";
        }

        return null;
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~OfficialIdValidatorTests" -v minimal`
Expected: PASS（5件成功）。

- [ ] **Step 5: 全テストを実行する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功）。

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー tests_テスト/Bui.Tests/Curation_仮登録レビュー
git commit -m "feat: OfficialIdValidatorを追加し、正式IDの重複を確認できるようにする"
```

---

### Task 6: OldNewIdAliasWriter（旧新ID CSVへの追記）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/OldNewIdAliasWriter_旧新ID追記.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/OldNewIdAliasWriterTests_旧新ID追記テスト.cs`

**Interfaces:**
- Consumes: なし（`old_id,new_id` 形式は `CsvIdAliasLoader_CSV旧新ID読込.cs` が読む既存フォーマットと一致させる）
- Produces: `Bui.Curation.OldNewIdAliasWriter.Append(string filePath, string oldId, string newId) : string?`
  （成功すればnull、失敗すれば理由の文字列を返す。例外を投げない。CurationDbWriterが使う）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Curation_仮登録レビュー/OldNewIdAliasWriterTests_旧新ID追記テスト.cs` を新規作成する。

```csharp
// このファイルはOldNewIdAliasWriterが、old_id,new_id形式のCSVへ
// 正しく追記し、重複するold_idは拒否することを守ります。
// この形式はCsvIdAliasLoader（既存）が読む形式と一致させる必要があります。
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public class OldNewIdAliasWriterTests_旧新ID追記テスト
{
    [TestMethod]
    public void Append_ファイルが無ければヘッダー付きで新規作成する()
    {
        using var files = OldNewIdAliasTestFiles.Create();

        var error = OldNewIdAliasWriter.Append(files.FilePath, "BM_NEW-0001", "P-100");

        Assert.IsNull(error);
        var lines = File.ReadAllLines(files.FilePath);
        Assert.AreEqual("old_id,new_id", lines[0]);
        Assert.AreEqual("BM_NEW-0001,P-100", lines[1]);
    }

    [TestMethod]
    public void Append_既存ファイルへ追記する()
    {
        using var files = OldNewIdAliasTestFiles.Create();
        OldNewIdAliasWriter.Append(files.FilePath, "BM_NEW-0001", "P-100");

        var error = OldNewIdAliasWriter.Append(files.FilePath, "BM_NEW-0002", "P-101");

        Assert.IsNull(error);
        var lines = File.ReadAllLines(files.FilePath);
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("BM_NEW-0002,P-101", lines[2]);
    }

    [TestMethod]
    public void Append_同じold_idは追記しない()
    {
        using var files = OldNewIdAliasTestFiles.Create();
        OldNewIdAliasWriter.Append(files.FilePath, "BM_NEW-0001", "P-100");

        var error = OldNewIdAliasWriter.Append(files.FilePath, "BM_NEW-0001", "P-999");

        Assert.IsNotNull(error);
        var lines = File.ReadAllLines(files.FilePath);
        Assert.AreEqual(2, lines.Length, "元の行が変わらないこと。");
    }
}

internal sealed class OldNewIdAliasTestFiles : IDisposable
{
    private OldNewIdAliasTestFiles(string directoryPath)
    {
        DirectoryPath = directoryPath;
        FilePath = Path.Combine(directoryPath, "IdAlias.csv");
    }

    public string DirectoryPath { get; }
    public string FilePath { get; }

    public static OldNewIdAliasTestFiles Create()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "BuiCurationAliasTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new OldNewIdAliasTestFiles(directoryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~OldNewIdAliasWriterTests" -v minimal`
Expected: ビルドエラー（`OldNewIdAliasWriter` が存在しない）。

- [ ] **Step 3: 実装を書く**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/OldNewIdAliasWriter_旧新ID追記.cs` を新規作成する。

```csharp
// このファイルの目的:
//   仮ID→正式IDの対応を「旧新ID CSV」（old_id,new_id）へ追記します。
//   このファイルは既存のCsvIdAliasLoader（Bui本体側）が読み、
//   Revitモデルに書き込み済みの旧IDから新IDへの移行候補を示します。
//
// なぜ重複old_idを拒否するのか:
//   CsvIdAliasLoaderはold_idの重複を「壊れたCSV」として扱い、
//   ファイル全体の読み込みに失敗させます（意図的な安全策）。
//   ここで重複を作ると、Bui本体の起動時警告やチャットの移行候補機能
//   まで巻き込んで壊れます。

using System.Text;

namespace Bui.Curation;

/// <summary>旧新ID CSVへ1行追記します。</summary>
public static class OldNewIdAliasWriter
{
    private static readonly UTF8Encoding Encoding = new(true);

    /// <summary>成功すればnull、失敗すれば理由を返します。例外は投げません。</summary>
    public static string? Append(string filePath, string oldId, string newId)
    {
        var trimmedOldId = (oldId ?? string.Empty).Trim();
        var trimmedNewId = (newId ?? string.Empty).Trim();
        if (trimmedOldId.Length == 0) return "旧ID(仮ID)が空です。";
        if (trimmedNewId.Length == 0) return "新ID(正式ID)が空です。";

        try
        {
            if (!File.Exists(filePath))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, "old_id,new_id\r\n", Encoding);
            }

            var existingLines = File.ReadAllLines(filePath, Encoding);
            var alreadyExists = existingLines
                .Skip(1)
                .Select(line => line.Split(',').FirstOrDefault()?.Trim())
                .Any(existingOldId => string.Equals(existingOldId, trimmedOldId, StringComparison.OrdinalIgnoreCase));
            if (alreadyExists)
            {
                return $"旧新ID CSVには既に old_id「{trimmedOldId}」の行があります。追記しませんでした。";
            }

            File.AppendAllText(filePath, $"{trimmedOldId},{trimmedNewId}\r\n", Encoding);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"旧新ID CSVへの書き込みに失敗しました。{exception.Message}";
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~OldNewIdAliasWriterTests" -v minimal`
Expected: PASS（3件成功）。

- [ ] **Step 5: 全テストを実行する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功）。

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー tests_テスト/Bui.Tests/Curation_仮登録レビュー
git commit -m "feat: OldNewIdAliasWriterを追加し、旧新ID CSVへ追記できるようにする"
```

---

### Task 7: CurationDbWriter（SQL Serverへの登録）

**Files:**
- Create: `src_ソース/Bui.Curation/CurationDbWriter_DB登録.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationSqlCommandBuilderTests_SQL文組立テスト.cs`

このタスクだけ、SQL文組立部分（テスト可能）と実行部分（`Microsoft.Data.SqlClient`を直接使うため目視確認のみ）を分ける。組立部分は`Bui.Shared`側に置く（他はBui.Addinの`SqlClientTableReader`と同じ判断: パッケージを直接使うクラスだけBui.Curation側に置く。SQL文を組み立てるだけの部分はパッケージ非依存のためBui.Shared側でテストできる）。

**Files（詳細）:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationSqlCommandBuilder_SQL文組立.cs`
- Create: `src_ソース/Bui.Curation/CurationDbWriter_DB登録.cs`
- Test: `tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationSqlCommandBuilderTests_SQL文組立テスト.cs`

**Interfaces:**
- Consumes: `Bui.Curation.CurationQueueItem`（Task 3）
- Produces: `Bui.Curation.CurationSqlCommandBuilder.BuildProductsUpsert(string officialId, IReadOnlyDictionary<string,string> values) : (string Sql, IReadOnlyDictionary<string,object> Parameters)`、
  `Bui.Curation.CurationDbWriter.RegisterAsync(string connectionString, CurationQueueItem item, CancellationToken cancellationToken) : Task<string?>`
  （成功すればnull、失敗すれば理由。後続タスクのMainWindowが使う）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationSqlCommandBuilderTests_SQL文組立テスト.cs` を新規作成する。

```csharp
// このファイルはCurationSqlCommandBuilderが、Products表へのUPSERT文を
// 正しいパラメータで組み立てられることを守ります。実際の接続は行いません。
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public class CurationSqlCommandBuilderTests_SQL文組立テスト
{
    [TestMethod]
    public void BuildProductsUpsert_正式IDとその他の値をパラメータへ入れる()
    {
        var values = new Dictionary<string, string>
        {
            ["product_name"] = "テスト製品",
            ["manufacturer_id"] = "M-1",
            ["material_category_id"] = "C-1"
        };

        var (sql, parameters) = CurationSqlCommandBuilder.BuildProductsUpsert("P-100", values);

        StringAssert.Contains(sql, "MERGE");
        StringAssert.Contains(sql, "dbo.Products");
        Assert.AreEqual("P-100", parameters["@product_id"]);
        Assert.AreEqual("テスト製品", parameters["@product_name"]);
        Assert.AreEqual("M-1", parameters["@manufacturer_id"]);
        Assert.AreEqual("C-1", parameters["@material_category_id"]);
    }

    [TestMethod]
    public void BuildProductsUpsert_仮ID自体の列は含めない()
    {
        var values = new Dictionary<string, string>
        {
            ["product_id"] = "BM_NEW-0001",
            ["product_name"] = "テスト製品"
        };

        var (_, parameters) = CurationSqlCommandBuilder.BuildProductsUpsert("P-100", values);

        Assert.AreEqual("P-100", parameters["@product_id"], "渡した正式IDを使い、valuesの仮IDでは上書きしないこと。");
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~CurationSqlCommandBuilderTests" -v minimal`
Expected: ビルドエラー（`CurationSqlCommandBuilder` が存在しない）。

- [ ] **Step 3: SQL文組立（テスト可能な部分）を実装する**

`src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationSqlCommandBuilder_SQL文組立.cs` を新規作成する。

```csharp
// このファイルの目的:
//   Products表へのUPSERT(MERGE)文とパラメータを組み立てます。
//   Microsoft.Data.SqlClientへは依存しません（パラメータは
//   IReadOnlyDictionary<string,object>として返すだけです）。
//   実際の接続・実行はBui.Curation側のCurationDbWriterが行います。
//
// 修正時の影響:
//   product_id列は常にofficialIdを使います。valuesの中に仮ID時代の
//   product_idが残っていても、それでは上書きしません。

namespace Bui.Curation;

/// <summary>Products表へのUPSERT文を組み立てます。</summary>
public static class CurationSqlCommandBuilder
{
    /// <summary>officialIdをキーとしたMERGE文と、対応するパラメータを返します。</summary>
    public static (string Sql, IReadOnlyDictionary<string, object> Parameters) BuildProductsUpsert(
        string officialId, IReadOnlyDictionary<string, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officialId);
        ArgumentNullException.ThrowIfNull(values);

        var parameters = new Dictionary<string, object> { ["@product_id"] = officialId.Trim() };
        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, "product_id", StringComparison.Ordinal)) continue;
            parameters[$"@{pair.Key}"] = pair.Value ?? string.Empty;
        }

        var columns = parameters.Keys.Select(key => key.TrimStart('@')).ToArray();
        var insertColumns = string.Join(", ", columns);
        var insertValues = string.Join(", ", parameters.Keys);
        var updateAssignments = string.Join(", ",
            columns.Where(column => column != "product_id").Select(column => $"target.{column} = source.{column}"));

        var sql = $"""
            MERGE dbo.Products AS target
            USING (SELECT {string.Join(", ", parameters.Keys.Select(key => $"{key} AS {key.TrimStart('@')}"))}) AS source
            ON target.product_id = source.product_id
            WHEN MATCHED THEN UPDATE SET {updateAssignments}
            WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});
            """;

        return (sql, parameters);
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~CurationSqlCommandBuilderTests" -v minimal`
Expected: PASS（2件成功）。

- [ ] **Step 5: 接続・実行部分を実装する（テストなし・目視確認）**

`src_ソース/Bui.Curation/CurationDbWriter_DB登録.cs` を新規作成する。

```csharp
// このファイルの目的:
//   CurationSqlCommandBuilderが組み立てたSQL文を、実際にSQL Serverへ
//   接続して実行します。Microsoft.Data.SqlClientを直接使うのはこの
//   ファイルだけにします（Bui.Addinの SqlClientTableReader_SQL接続.cs
//   と同じ判断です）。
//
// なぜ1件を1トランザクションにするのか:
//   複数テーブルへの書き込み途中で失敗すると、一部だけ書かれた
//   中途半端な状態が残ります。1件ごとにロールバックできるようにします。

using Microsoft.Data.SqlClient;

namespace Bui.Curation;

/// <summary>1件のドラフトをSQL Serverへ登録します。</summary>
public static class CurationDbWriter
{
    /// <summary>
    /// 指定したProduct_IDがdbo.Productsに既に存在するかを確認します。
    /// OfficialIdValidatorへ渡す「DB上の既存ID一覧」を作るために、
    /// 登録前にこのメソッドで対象IDだけを確認します（全件取得はしません）。
    /// </summary>
    public static async Task<bool> ExistsAsync(string connectionString, string productId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.Products WHERE product_id = @product_id";
        command.Parameters.AddWithValue("@product_id", productId.Trim());
        var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        return count > 0;
    }

    /// <summary>成功すればnull、失敗すれば理由を返します。</summary>
    public static async Task<string?> RegisterAsync(
        string connectionString, CurationQueueItem item, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.OfficialId))
        {
            return "正式IDが設定されていません。";
        }

        var (sql, parameters) = CurationSqlCommandBuilder.BuildProductsUpsert(item.OfficialId, item.Values);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var pair in parameters)
                {
                    command.Parameters.AddWithValue(pair.Key, pair.Value);
                }
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            return $"SQL Serverへの登録に失敗しました。{exception.Message}";
        }
    }
}
```

- [ ] **Step 6: 全テストを実行する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功。このタスクの新規テストはCurationSqlCommandBuilderの2件のみで、CurationDbWriter自体はテストしない）。

- [ ] **Step 7: Bui.Curationがビルドできることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe build src_ソース\Bui.Curation\Bui.Curation.csproj -c Debug`
Expected: `ビルドに成功しました。`

- [ ] **Step 8: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationSqlCommandBuilder_SQL文組立.cs src_ソース/Bui.Curation/CurationDbWriter_DB登録.cs tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationSqlCommandBuilderTests_SQL文組立テスト.cs
git commit -m "feat: CurationDbWriterを追加し、SQL Serverへの登録に対応する"
```

---

### Task 8: MainWindow（一覧・詳細レビュー画面）

**このタスクはBui.Curation（WPF UI）に属し、設計スペックの方針どおりテストは行わず目視確認とする。** Task 1〜7で作った、テスト済みのロジックを配線するだけに留める。

**Files:**
- Modify: `src_ソース/Bui.Curation/MainWindow.xaml`
- Modify: `src_ソース/Bui.Curation/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `ProvisionalRecordScanner.Scan`（Task 2）、`CurationQueueStore.Load/Save/AddIfMissing`（Task 3）、
  `RelatedTableChecker.Check`（Task 4）、`OfficialIdValidator.Validate`（Task 5）、`OldNewIdAliasWriter.Append`（Task 6）、
  `CurationDbWriter.RegisterAsync`（Task 7）、`Bui.Configuration.SqlConnectionStringFactory.Build(string?, string?) : OperationResult<string>`
  （既存・Bui.Shared側、`Bui.Common.OperationResult<T>`の`IsSuccess`/`Value`/`Error!.UserMessage`を使う）
- Produces: なし（最終タスク）

- [ ] **Step 1: MainWindow.xamlを一覧・詳細画面へ拡張する**

`src_ソース/Bui.Curation/MainWindow.xaml` を次の内容に置き換える。

```xml
<Window x:Class="Bui.Curation.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="BUI 仮登録レビューツール" Height="700" Width="1100">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8">
            <Button x:Name="AddFolderButton" Content="フォルダを追加して取込" Padding="8,4" Click="OnAddFolder" />
            <TextBlock Text="サーバー名" Margin="24,8,4,0" />
            <TextBox x:Name="SqlServerNameBox" Width="140" Margin="0,4,0,0" Text="localhost\sqlexpress" />
            <TextBlock Text="データベース名" Margin="16,8,4,0" />
            <TextBox x:Name="SqlDatabaseNameBox" Width="140" Margin="0,4,0,0" Text="GoldStandardDB" />
        </StackPanel>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="2*" />
                <ColumnDefinition Width="3*" />
            </Grid.ColumnDefinitions>

            <DataGrid x:Name="QueueGrid" Grid.Column="0" Margin="0,0,8,0"
                      AutoGenerateColumns="False" IsReadOnly="True"
                      SelectionChanged="OnSelectedItemChanged">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="出どころ" Binding="{Binding SourceFolder}" Width="120" />
                    <DataGridTextColumn Header="仮ID" Binding="{Binding ProvisionalId}" Width="110" />
                    <DataGridTextColumn Header="状態" Binding="{Binding StatusText}" Width="70" />
                </DataGrid.Columns>
            </DataGrid>

            <StackPanel Grid.Column="1">
                <TextBlock Text="出どころフォルダ / 仮ID" FontWeight="Bold" />
                <TextBlock x:Name="SelectedHeaderText" Margin="0,0,0,8" />

                <TextBlock Text="製品名" />
                <TextBox x:Name="ProductNameBox" Margin="0,0,0,8" />

                <TextBlock Text="メーカー名" />
                <TextBox x:Name="ManufacturerBox" Margin="0,0,0,8" />

                <TextBlock Text="建材分類" />
                <TextBox x:Name="CategoryBox" Margin="0,0,0,8" />

                <TextBlock Text="正式なProduct_ID" FontWeight="Bold" />
                <TextBox x:Name="OfficialIdBox" Margin="0,0,0,8" />

                <TextBlock Text="関連表の状況（情報表示のみ）" FontWeight="Bold" Margin="0,8,0,0" />
                <TextBlock x:Name="RelatedTableSummaryText" TextWrapping="Wrap" Margin="0,0,0,8" />

                <TextBlock x:Name="ValidationText" Foreground="Red" TextWrapping="Wrap" Margin="0,0,0,8" />

                <Button x:Name="RegisterButton" Content="登録" Padding="8,4" HorizontalAlignment="Left" Click="OnRegister" />
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: MainWindow.xaml.csへロジックを配線する**

`src_ソース/Bui.Curation/MainWindow.xaml.cs` を次の内容に置き換える。

```csharp
using System.IO;
using System.Windows;
using Bui.Data.GoldStandard;
using Microsoft.Win32;

namespace Bui.Curation;

public partial class MainWindow : Window
{
    private readonly string _queuePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartsCAM", "BUI", "Curation", "queue.json");
    private readonly string _idAliasCsvPath;
    private List<CurationQueueItem> _items = new();
    private CurationQueueItem? _selected;

    public MainWindow()
    {
        InitializeComponent();
        _idAliasCsvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StartsCAM", "BUI", "Curation", "IdAlias.csv");
        _items = CurationQueueStore.Load(_queuePath);
        RefreshGrid();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "取込むデータフォルダを選択してください" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var scanned = ProvisionalRecordScanner.Scan(dialog.FolderName, "BM_NEW-");
            var added = CurationQueueStore.AddIfMissing(_items, scanned);
            CurationQueueStore.Save(_queuePath, _items);
            RefreshGrid();
            MessageBox.Show($"{added} 件を新しく取り込みました。", "取込完了");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"取込に失敗しました。{exception.Message}", "エラー");
        }
    }

    private void RefreshGrid()
    {
        QueueGrid.ItemsSource = null;
        QueueGrid.ItemsSource = _items.Select(item => new
        {
            item.SourceFolder,
            item.ProvisionalId,
            StatusText = item.IsCompleted ? "完了" : "レビュー待ち"
        }).ToList();
    }

    private void OnSelectedItemChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var index = QueueGrid.SelectedIndex;
        if (index < 0 || index >= _items.Count)
        {
            _selected = null;
            return;
        }

        _selected = _items[index];
        SelectedHeaderText.Text = $"{_selected.SourceFolder} / {_selected.ProvisionalId}";
        ProductNameBox.Text = _selected.Values.GetValueOrDefault("product_name", string.Empty);
        ManufacturerBox.Text = _selected.Values.GetValueOrDefault("manufacturer_name", string.Empty);
        CategoryBox.Text = _selected.Values.GetValueOrDefault("category_name", string.Empty);
        OfficialIdBox.Text = _selected.OfficialId ?? string.Empty;
        ValidationText.Text = string.Empty;

        try
        {
            var masterSet = GoldStandardCsvReader.Load(_selected.SourceFolder);
            var summary = RelatedTableChecker.Check(
                _selected.ProvisionalId, ManufacturerBox.Text, CategoryBox.Text, masterSet);
            var warningsText = summary.Warnings.Count > 0
                ? string.Join(Environment.NewLine, summary.Warnings)
                : "警告はありません。";
            RelatedTableSummaryText.Text =
                $"Product_Specs: {summary.ProductSpecCount}件 / Product_Images: {summary.ProductImageCount}件{Environment.NewLine}{warningsText}";
        }
        catch (Exception exception)
        {
            RelatedTableSummaryText.Text = $"関連表の確認に失敗しました。{exception.Message}";
        }
    }

    private async void OnRegister(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("一覧から行を選んでください。", "登録");
            return;
        }

        var connectionResult = Bui.Configuration.SqlConnectionStringFactory.Build(
            SqlServerNameBox.Text, SqlDatabaseNameBox.Text);
        if (!connectionResult.IsSuccess)
        {
            ValidationText.Text = connectionResult.Error!.UserMessage;
            return;
        }

        var pendingIds = _items
            .Where(item => item != _selected && item.OfficialId is not null)
            .Select(item => item.OfficialId!)
            .ToArray();
        var trimmedOfficialId = OfficialIdBox.Text.Trim();
        IReadOnlyCollection<string> databaseIds = Array.Empty<string>();
        if (trimmedOfficialId.Length > 0)
        {
            try
            {
                var exists = await CurationDbWriter.ExistsAsync(connectionResult.Value!, trimmedOfficialId, default);
                if (exists) databaseIds = new[] { trimmedOfficialId };
            }
            catch (Exception exception)
            {
                ValidationText.Text = $"DB上の重複確認に失敗しました。{exception.Message}";
                return;
            }
        }

        var validationError = OfficialIdValidator.Validate(OfficialIdBox.Text, databaseIds, pendingIds);
        if (validationError is not null)
        {
            ValidationText.Text = validationError;
            return;
        }

        _selected.Values["product_name"] = ProductNameBox.Text;
        _selected.Values["manufacturer_name"] = ManufacturerBox.Text;
        _selected.Values["category_name"] = CategoryBox.Text;
        _selected.OfficialId = trimmedOfficialId;

        var registerError = await CurationDbWriter.RegisterAsync(connectionResult.Value!, _selected, default);
        if (registerError is not null)
        {
            ValidationText.Text = registerError;
            return;
        }

        _selected.IsCompleted = true;
        var aliasError = OldNewIdAliasWriter.Append(_idAliasCsvPath, _selected.ProvisionalId, _selected.OfficialId);
        if (aliasError is not null)
        {
            _selected.Warnings.Add(aliasError);
        }

        CurationQueueStore.Save(_queuePath, _items);
        RefreshGrid();
        MessageBox.Show("登録しました。", "登録完了");
    }
}
```

- [ ] **Step 3: ビルドを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe build src_ソース\Bui.Curation\Bui.Curation.csproj -c Debug`
Expected: `ビルドに成功しました。`

- [ ] **Step 4: 目視確認する**

Bui.Curation.exe（`src_ソース\Bui.Curation\bin\Debug\net8.0-windows\Bui.Curation.exe`）を実行し、次を確認する。

- ウィンドウが開き、「フォルダを追加して取込」ボタンと、サーバー名・データベース名の入力欄（既定値 `localhost\sqlexpress` / `GoldStandardDB`）が表示される。
- テスト用のデータフォルダ（`BM_NEW-` 接頭辞の行を含むProducts.csv一式）を指定して取込むと、一覧に行が出る。
- 一覧から行を選ぶと、右側に詳細が表示され、関連表の件数が出る。
- 正式IDを空欄のまま「登録」を押すと、赤字でエラーが出て登録されないこと。
- サーバー名・データベース名を実在するSQL Serverへ向けたうえで正式IDを入力し「登録」を押すと、成功メッセージが出て、Products表に行が登録され、旧新ID CSVにも1行追記されること。

- [ ] **Step 5: 全テストを実行し、既存テストへの影響が無いことを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（全件成功。このタスクはBui.Testsへ新規テストを追加しない）。

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Curation/MainWindow.xaml src_ソース/Bui.Curation/MainWindow.xaml.cs
git commit -m "feat: MainWindowへ一覧・詳細レビュー画面を実装する"
```

---

## 自己レビュー（実施済み）

- **スペック網羅性:** 設計スペックの7コンポーネント（`ProvisionalRecordScanner`、`CurationQueueItem`/`CurationQueueStore`、`RelatedTableChecker`、`OfficialIdValidator`、`OldNewIdAliasWriter`、`CurationDbWriter`、`Bui.Curation` UI）すべてにタスクが対応している（Task 1〜8）。データフロー（取込→一覧・レビュー→重複チェック→DB登録→旧新ID CSV追記）もTask 8のMainWindowで一通り配線している。
- **プレースホルダー確認:** 「TBD」等の記載なし。すべてのコード片は完全な内容。
- **型の一貫性:** `CurationQueueItem`（Task 3で定義）のプロパティ名（`SourceFolder`, `ProvisionalId`, `Values`, `IsCompleted`, `OfficialId`, `Warnings`）は、Task 7・Task 8でも同じ名前・型で使っている。`ScannedProvisionalRecord`（Task 2）のプロパティ名もTask 3の`AddIfMissing`で一致させている。
- **スコープの妥当性:** 設計スペックの対象外項目（現場フォルダの自動集約、正式ID自動採番、複数管理者の排他制御、却下・削除機能）はどのタスクにも含めていない。SourcesとProduct_Variantsは、設計スペックの調整どおりチェック対象から外している。
