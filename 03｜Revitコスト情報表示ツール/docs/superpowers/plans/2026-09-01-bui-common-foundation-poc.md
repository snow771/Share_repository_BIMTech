# BUI Common Foundation PoC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Revit 2025上で、CSV／将来SQL Server差し替え、Product_ID／Assembly_ID、複合レイヤー、任意のローカルLLM接続を安全に扱えるBUI共通基盤PoCを構築する。

**Architecture:** 配布時の主要アセンブリは `Bui.dll` 1つとし、Revit非依存ソースを `Bui.Shared_共有ロジック` に集約して、アドインとテストへソースリンクする。WPF画面、Revitアダプター、業務ロジック、CSV、LLMをインターフェースで分離し、Revitへの書き込みはExternal Eventだけで行う。

**Tech Stack:** C# 12、.NET 8 (`net8.0-windows`)、WPF、Autodesk Revit 2025 API、MSTest、`System.Text.Json`、`HttpClient`、PowerShell

**Spec:** `docs/superpowers/specs/2026-09-01-bui-common-foundation-poc-design.md`

## Global Constraints

- 既存の `建材・単価情報.extension`、`維持管理情報.extension`、元CSVを変更・削除しない。
- 新規アドインファイルは `BUI_Addin_CSharpアドイン` 配下だけに作成する。
- 対象はRevit 2025、`net8.0-windows`、x64、WPFとする。
- 開発中バージョンは `0.1.0`、正式初版は `1.0.0` とする。
- C#ファイル名は `English_日本語.cs`、主要クラスは原則1ファイル1つとする。
- 名前空間ルートは `Bui` とし、合意なく主要な型名・公開メソッド名を変更しない。
- 各ファイル冒頭と主要処理内に、目的・理由・修正時の影響が分かる日本語コメントを記載する。
- Product_IDは不変・再利用禁止のインスタンス共有パラメータとする。
- Assembly_IDは複合ホストタイプ用のタイプ共有パラメータとする。
- Product_IDはRevit APIが許可する全カテゴリへバインドし、不可カテゴリは結果へ出す。
- PoCでは事前複製したCSVを1人・1台で直接読み書きし、書き込み前バックアップを作る。
- LLMは初期OFF、接続先はローカルPCまたは社内LAN、プロバイダーはOllamaとOpenAI互換APIとする。
- LLMなしでも検索、比較、ID付与、点検が動作しなければならない。
- 接続URLと非秘密設定はGit管理可、パスワード・トークン・会社データはGit管理しない。
- 実装はTDDで進め、各タスク終了時に対象テストと全自動テストを実行して小さくコミットする。

---

## File Map

```text
BUI_Addin_CSharpアドイン/
├─ Bui.sln
├─ .gitignore
├─ src_ソース/
│  ├─ Bui.Shared_共有ロジック/
│  │  ├─ Common_共通/
│  │  ├─ Metadata_アドイン情報/
│  │  ├─ Products_製品/
│  │  ├─ Assemblies_複合構造/
│  │  ├─ Data_データ窓口/
│  │  ├─ Llm_LLM接続/
│  │  ├─ Configuration_設定/
│  │  ├─ Logging_ログ/
│  │  └─ UI_画面/
│  └─ Bui.Addin/
│     ├─ Bui.Addin.csproj
│     ├─ Application_アプリ起動/
│     ├─ Revit_連携/
│     ├─ UI_画面/
│     ├─ Data_データ接続/
│     ├─ Llm_LLM接続/
│     ├─ Configuration_設定/
│     ├─ Logging_ログ/
│     └─ Resources_画像と定義/
├─ tests_テスト/Bui.Tests/
├─ scripts_開発補助/
├─ config_設定例/
└─ docs_説明書/
```

`Bui.Shared_共有ロジック` は独立DLLにせず、`Bui.Addin.csproj` と `Bui.Tests.csproj` の双方から `<Compile Include=... Link=...>` で取り込む。これにより配布DLLを増やさず、Revit非依存テストはRevitAPI.dllなしでビルドできる。

この計画で固定するGUIDは次のとおりとし、実装中に再生成しない。

- Revit Application AddInId: `62ba1d8c-426d-45f5-b1b9-9b6407284423`
- DockablePaneId: `b2484bd2-fe75-42b2-8822-7d7bfcf6aa79`
- Product_ID共有パラメータ: `569e8df1-4ca6-448c-8fb9-496cb39b4458`
- Assembly_ID共有パラメータ: `883267b2-8771-498a-a77e-a698e06b3453`

---

### Task 1: Solution Bootstrap, Metadata, and Local-File Safety

**Files:**
- Create: `BUI_Addin_CSharpアドイン/Bui.sln`
- Create: `BUI_Addin_CSharpアドイン/.gitignore`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`
- Create: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Metadata_アドイン情報/AddinMetadata_アドイン情報.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Metadata_アドイン情報/MetadataLoader_情報読込.cs`
- Create: `BUI_Addin_CSharpアドイン/config_設定例/AddinMetadata_アドイン情報.json`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Metadata_アドイン情報/MetadataLoaderTests_情報読込テスト.cs`

**Interfaces:**
- Consumes: None.
- Produces: `Bui.Metadata.AddinMetadata`, `Bui.Metadata.MetadataLoader.Load(string path)`, and the solution/project layout used by all later tasks.

- [ ] **Step 1: Create solution and test project scaffolding**

Run from `BUI_Addin_CSharpアドイン` on a PC with .NET 8 SDK:

```powershell
dotnet new sln -n Bui
dotnet new classlib -n Bui.Addin -o "src_ソース/Bui.Addin" -f net8.0
dotnet new mstest -n Bui.Tests -o "tests_テスト/Bui.Tests" -f net8.0
dotnet sln Bui.sln add "src_ソース/Bui.Addin/Bui.Addin.csproj"
dotnet sln Bui.sln add "tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

Delete the generated `Class1.cs` and `UnitTest1.cs`. Configure `Bui.Addin.csproj` with:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <PlatformTarget>x64</PlatformTarget>
  <Version>0.1.0</Version>
  <AssemblyName>Bui</AssemblyName>
  <RootNamespace>Bui</RootNamespace>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
<ItemGroup>
  <Compile Include="..\Bui.Shared_共有ロジック\**\*.cs"
           Link="Shared\%(RecursiveDir)%(Filename)%(Extension)" />
  <Content Include="..\..\config_設定例\AddinMetadata_アドイン情報.json"
           Link="Config\AddinMetadata_アドイン情報.json"
           CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Add the same `Compile Include` to `Bui.Tests.csproj`, using `..\..\src_ソース\Bui.Shared_共有ロジック\**\*.cs`.

- [ ] **Step 2: Protect generated and local files**

Create `.gitignore` with exactly:

```gitignore
**/bin/
**/obj/
.vs/
config_設定例/Settings.Local_個別設定.json
config_設定例/Secrets.Local_認証情報.json
TestData_テストデータ/Company_会社データ/
*.user
*.suo
*.log
*.bak
```

- [ ] **Step 3: Write the failing metadata test**

```csharp
[TestMethod]
public void Load_ReadsEditableAuthorInformation()
{
    var path = TestFiles.WriteJson("""
    {
      "displayName": "BUI",
      "englishName": "Building Unit Intelligence",
      "descriptionJa": "BIM建材・コスト・維持管理支援",
      "author": "Shingo Takagi",
      "department": "建設技術本部 BIMテック開発室",
      "company": "スターツCAM株式会社",
      "revitVersion": "2025"
    }
    """);

    var actual = MetadataLoader.Load(path);

    Assert.AreEqual("BUI", actual.DisplayName);
    Assert.AreEqual("Shingo Takagi", actual.Author);
    Assert.AreEqual("スターツCAM株式会社", actual.Company);
}
```

Add a test-only `TestFiles` helper in the same test file that creates a unique directory under `Path.GetTempPath()` and writes UTF-8 JSON.

- [ ] **Step 4: Run the focused test and verify failure**

Run:

```powershell
dotnet test "tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~MetadataLoaderTests"
```

Expected: compile failure because `AddinMetadata` and `MetadataLoader` do not exist.

- [ ] **Step 5: Implement immutable metadata loading**

```csharp
namespace Bui.Metadata;

public sealed record AddinMetadata(
    string DisplayName,
    string EnglishName,
    string DescriptionJa,
    string Author,
    string Department,
    string Company,
    string RevitVersion);

public static class MetadataLoader
{
    public static AddinMetadata Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<AddinMetadata>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("アドイン情報を読み込めませんでした。");
    }
}
```

Create the tracked JSON with the approved author, department, company, and names. Add Japanese header comments to both C# files.

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet test "tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

Expected: PASS.

Commit:

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "build: scaffold BUI solution and metadata"
```

---

### Task 2: Domain Records, Result Type, and Configurable CSV Mapping

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Common_共通/OperationResult_処理結果.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ProductRecord_製品情報.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/PriceRecord_単価情報.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/CsvColumnMapping_CSV列対応.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductRowMapper_製品行変換.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/PriceColumnMapping_CSV単価列対応.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/PriceRowMapper_単価行変換.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/IdAliasMap_旧新ID対応.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductRowMapperTests_製品行変換テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/PriceRowMapperTests_単価行変換テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/IdAliasMapTests_旧新ID対応テスト.cs`

**Interfaces:**
- Consumes: .NET BCL only.
- Produces: `OperationResult<T>`, `ProductRecord`, `PriceRecord`, product/price column mappings and row mappers, and `IdAliasMap.Resolve`.

- [ ] **Step 1: Write failing tests for `db_key` mapping and extra columns**

```csharp
[TestMethod]
public void Map_MapsDbKeyToProductIdAndKeepsUnknownColumns()
{
    var row = new Dictionary<string, string>
    {
        ["db_key"] = "P-001",
        ["製品名"] = "ビニル床タイル",
        ["メーカー名"] = "メーカーA",
        ["独自性能"] = "耐久区分4"
    };
    var mapping = new CsvColumnMapping("db_key", "製品名", "メーカー名", "型番", "単位", "更新日");

    var actual = ProductRowMapper.Map(row, mapping);

    Assert.AreEqual("P-001", actual.ProductId);
    Assert.AreEqual("ビニル床タイル", actual.ProductName);
    Assert.AreEqual("耐久区分4", actual.AdditionalAttributes["独自性能"]);
}

[TestMethod]
public void Map_ThrowsJapaneseErrorWhenRequiredIdColumnIsMissing()
{
    var row = new Dictionary<string, string> { ["製品名"] = "製品A" };
    var mapping = CsvColumnMapping.PocDefault;

    var ex = Assert.ThrowsException<InvalidDataException>(() => ProductRowMapper.Map(row, mapping));

    StringAssert.Contains(ex.Message, "db_key");
}
```

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test "tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~ProductRowMapperTests"
```

Expected: compile failure for missing domain types.

- [ ] **Step 3: Implement the records and mapper**

Use these exact public shapes:

```csharp
public sealed record ProductRecord(
    string ProductId,
    string ProductName,
    string Manufacturer,
    string ModelNumber,
    string Unit,
    DateTimeOffset? SourceUpdatedAt,
    IReadOnlyDictionary<string, string> AdditionalAttributes);

public sealed record PriceRecord(
    string ProductId,
    decimal UnitPrice,
    string Currency,
    DateOnly? ValidFrom,
    string Region,
    string InformationSource,
    IReadOnlyDictionary<string, string> AdditionalAttributes);

public sealed record CsvColumnMapping(
    string ProductIdColumn,
    string ProductNameColumn,
    string ManufacturerColumn,
    string ModelNumberColumn,
    string UnitColumn,
    string UpdatedAtColumn)
{
    public static CsvColumnMapping PocDefault { get; } =
        new("db_key", "製品名", "メーカー名", "型番", "単位", "更新日");
}
```

`ProductRowMapper` must trim values, parse an optional ISO/Japanese date using `CultureInfo.InvariantCulture` then `ja-JP`, and exclude mapped columns from `AdditionalAttributes`.

Add equivalent price tests and implementation with this exact mapping shape:

```csharp
public sealed record PriceColumnMapping(
    string ProductIdColumn,
    string UnitPriceColumn,
    string CurrencyColumn,
    string ValidFromColumn,
    string RegionColumn,
    string InformationSourceColumn)
{
    public static PriceColumnMapping PocDefault { get; } =
        new("db_key", "単価", "通貨", "適用開始日", "地域", "情報源");
}
```

`PriceRowMapper` parses prices with invariant culture and `ja-JP`, preserves unmapped future columns, and rejects blank IDs or invalid prices with Japanese `InvalidDataException` messages.

Add `IdAliasMap` using an immutable `IReadOnlyDictionary<string,string>`. `Resolve(string id)` follows old-to-new mappings to the final ID, returns the input when unmapped, and throws `InvalidDataException` for a cycle or blank target. Tests cover one hop, multiple hops, unmapped IDs, and cycles. This implements the agreed migration path without changing immutable Product_ID values in source data.

- [ ] **Step 4: Add a reusable operation result**

```csharp
public sealed record OperationError(string Code, string UserMessage, string TechnicalDetail);

public sealed record OperationResult<T>(T? Value, OperationError? Error)
{
    public bool IsSuccess => Error is null;
    public static OperationResult<T> Success(T value) => new(value, null);
    public static OperationResult<T> Failure(string code, string userMessage, string detail) =>
        new(default, new OperationError(code, userMessage, detail));
}
```

- [ ] **Step 5: Run tests and commit**

Run the focused tests, then all tests. Expected: PASS.

```powershell
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック" "BUI_Addin_CSharpアドイン/tests_テスト"
git commit -m "feat: add BUI domain records and CSV mapping"
```

---

### Task 3: Safe CSV Product and Price Repositories

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/IProductRepository_製品データ窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/IPriceRepository_単価データ窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/DataSourceStatus_データ状態.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvProductRepository_CSV製品データ取得.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvPriceRepository_CSV単価データ取得.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvIdAliasLoader_CSV旧新ID読込.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvFileWriter_CSV安全更新.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/CsvProductRepositoryTests_CSV製品データテスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/CsvPriceRepositoryTests_CSV単価データテスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/CsvIdAliasLoaderTests_CSV旧新ID読込テスト.cs`

**Interfaces:**
- Consumes: `ProductRecord`, `CsvColumnMapping`, `ProductRowMapper` from Task 2.
- Produces: product and price repository contracts and CSV implementations; `CsvFileWriter.ReplaceWithBackupAsync`.

- [ ] **Step 1: Define the repository contract**

```csharp
public sealed record ProductQuery(string Text, IReadOnlyDictionary<string, string> Filters, int Limit = 50);
public sealed record DataSourceStatus(bool IsAvailable, int RowCount, DateTimeOffset? LastUpdatedAt, IReadOnlyList<string> Problems);

public interface IProductRepository
{
    Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<ProductRecord?> GetByIdAsync(string productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery query, CancellationToken cancellationToken);
    Task UpsertAsync(ProductRecord record, CancellationToken cancellationToken);
}

public sealed record PriceQuery(string? ProductId, string? Region, DateOnly? EffectiveOn, int Limit = 100);

public interface IPriceRepository
{
    Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery query, CancellationToken cancellationToken);
    Task UpsertAsync(PriceRecord record, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write failing tests for read, search, backup, and malformed headers**

Create tests using a temporary copied CSV:

```csharp
[TestMethod]
public async Task SearchAsync_FindsCaseInsensitiveTextAcrossMappedAndExtraFields()
{
    using var files = CsvTestFiles.Create("db_key,製品名,メーカー名,独自性能\nP-001,床タイル,ABC,耐久区分4\n");
    var sut = new CsvProductRepository(files.CsvPath, CsvColumnMapping.PocDefault);

    var rows = await sut.SearchAsync(new ProductQuery("abc", new Dictionary<string, string>()), default);

    Assert.AreEqual("P-001", rows.Single().ProductId);
}

[TestMethod]
public async Task UpsertAsync_CreatesTimestampedBackupBeforeReplacingFile()
{
    using var files = CsvTestFiles.Create("db_key,製品名\nP-001,製品A\n");
    var sut = new CsvProductRepository(files.CsvPath, CsvColumnMapping.PocDefault);

    await sut.UpsertAsync(ProductFactory.Create("P-002", "製品B"), default);

    Assert.AreEqual(1, Directory.GetFiles(files.DirectoryPath, "*.bak").Length);
    StringAssert.Contains(await File.ReadAllTextAsync(files.CsvPath), "P-002");
}
```

Also test that a missing `db_key` header returns a status problem and does not rewrite the file. Add price tests for Product_ID lookup, region/effective-date filtering, decimal parsing, future extra columns, backup creation, and malformed headers. `CsvIdAliasLoader.Load(path)` reads the exact headers `old_id,new_id`, rejects duplicates, blank targets, and cycles, and returns `IdAliasMap`; its path is optional in PoC settings, so a missing configured path yields an empty map plus a status warning rather than changing IDs.

- [ ] **Step 3: Run the focused tests and verify failure**

Expected: compile failure because the CSV repository does not exist.

- [ ] **Step 4: Implement deterministic CSV parsing and search**

Requirements for `CsvProductRepository`:

```csharp
public sealed class CsvProductRepository : IProductRepository
{
    public CsvProductRepository(string csvPath, CsvColumnMapping mapping, CsvFileWriter? writer = null);
    // Cache rows together with File.GetLastWriteTimeUtc(csvPath).
    // Reload only when the timestamp changes.
    // Search uses OrdinalIgnoreCase over mapped values and AdditionalAttributes.
    // Filters require exact normalized match; Text uses substring match.
    // Limit is clamped to 1..200.
}
```

Use `Microsoft.VisualBasic.FileIO.TextFieldParser` for both repositories. Cover quoted commas, embedded newlines, and doubled quotes with tests. Do not use `string.Split(',')`.

- [ ] **Step 5: Implement atomic replacement with backup**

`CsvFileWriter.ReplaceWithBackupAsync` must:

1. Read current `LastWriteTimeUtc` before editing.
2. Write all output to `<file>.tmp` with UTF-8 BOM policy matching the source.
3. Recheck source `LastWriteTimeUtc`; throw `IOException` if another process changed it.
4. Copy the source to `<name>.yyyyMMddHHmmssfff.bak`.
5. Replace the source with the temp file.
6. If replacement fails after the source becomes unavailable, restore the exact backup and return the original exception plus restoration result.
7. Delete the temp file in `finally`.

Read with strict UTF-8, accepting both UTF-8 BOM and UTF-8 without BOM, strip a BOM from the first header, and preserve the source BOM policy on write. Invalid UTF-8 produces a Japanese status problem and never rewrites the file. Tests cover both BOM modes, invalid bytes, a simulated replacement failure with restoration, and no leftover temp file.

- [ ] **Step 6: Run tests and commit**

Run focused and full tests. Expected: PASS and no temp files after any test.

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add safe CSV product and price repositories"
```

---

### Task 4: Assembly Model, Fingerprint, Repository, and Semi-Automatic Numbering

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/AssemblyLayerRecord_構成層.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/AssemblyRecord_構成情報.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/AssemblyFingerprint_構成照合.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/AssemblyIdAllocator_構成ID採番.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/IAssemblyRepository_構成データ窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvAssemblyRepository_CSV構成データ取得.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造/AssemblyFingerprintTests_構成照合テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造/AssemblyIdAllocatorTests_構成採番テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/CsvAssemblyRepositoryTests_CSV構成データテスト.cs`

**Interfaces:**
- Consumes: `OperationResult<T>` and CSV safe writer.
- Produces: `AssemblyRecord`, `AssemblyLayerRecord`, `AssemblyFingerprint.Create`, `AssemblyIdAllocator.Next`, and the complete `IAssemblyRepository` contract.

- [ ] **Step 1: Write failing fingerprint tests**

```csharp
[TestMethod]
public void Create_IsStableForEquivalentThicknessRepresentations()
{
    var a = AssemblyFactory.Floor(("Finish1", 2.0m, "P-001"), ("Structure", 150.00m, "P-105"));
    var b = AssemblyFactory.Floor(("Finish1", 2.000m, "P-001"), ("Structure", 150m, "P-105"));

    Assert.AreEqual(AssemblyFingerprint.Create(a), AssemblyFingerprint.Create(b));
}

[TestMethod]
public void Create_ChangesWhenLayerOrderChanges()
{
    var a = AssemblyFactory.Floor(("Finish1", 2m, "P-001"), ("Substrate", 10m, "P-032"));
    var b = AssemblyFactory.Floor(("Substrate", 10m, "P-032"), ("Finish1", 2m, "P-001"));

    Assert.AreNotEqual(AssemblyFingerprint.Create(a), AssemblyFingerprint.Create(b));
}
```

Add a failing test that any blank ProductId returns `OperationResult.Failure` with code `ASSEMBLY_PRODUCT_ID_MISSING`.

- [ ] **Step 2: Write failing numbering tests**

```csharp
[TestMethod]
public void Next_DefaultFormatStartsAtAsm000001()
{
    Assert.AreEqual("ASM-000001", AssemblyIdAllocator.Next(Array.Empty<string>(), "ASM-", 6));
}

[TestMethod]
public void Next_IgnoresNonMatchingLegacyIds()
{
    var actual = AssemblyIdAllocator.Next(new[] { "OLD-A", "ASM-000009" }, "ASM-", 6);
    Assert.AreEqual("ASM-000010", actual);
}
```

- [ ] **Step 3: Implement exact records and fingerprint**

```csharp
public sealed record AssemblyLayerRecord(int Order, string Function, decimal ThicknessMillimeters, string ProductId, string RevitMaterialName);
public sealed record AssemblyRecord(string AssemblyId, string HostCategory, IReadOnlyList<AssemblyLayerRecord> OrderedLayers, DateTimeOffset? SourceUpdatedAt, IReadOnlyDictionary<string, string> AdditionalAttributes);
```

Normalize category/function/ProductId with `Trim().ToUpperInvariant()`, thickness to 0.001 mm, preserve layer order, serialize the normalized tuple, and hash with SHA-256. Do not include Revit material display name in the fingerprint.

- [ ] **Step 4: Define repository behavior**

```csharp
public interface IAssemblyRepository
{
    Task<AssemblyRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken cancellationToken);
    Task AddAsync(AssemblyRecord record, string fingerprint, CancellationToken cancellationToken);
    Task RemoveAsync(string assemblyId, CancellationToken cancellationToken);
}
```

Implement CSV storage with one assembly header row per AssemblyId and ordered layer rows. Use the Task 3 safe writer. Reject duplicate AssemblyId or fingerprint. `RemoveAsync` removes only the exact AssemblyId and all of its layer rows, uses the same backup/concurrency guard, and is covered by an add-then-remove compensation test.

- [ ] **Step 5: Run tests and commit**

Run all tests. Expected: PASS.

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add assembly matching and ID allocation"
```

---

### Task 5: Configurable Local LLM Providers with Offline Fallback

The OpenAI-compatible provider is the PoC extension point for local/LAN servers such as LM Studio and llama.cpp; no cloud endpoint is permitted by the endpoint policy.

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/ILlmProvider_LLM接続窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LlmModels_LLMデータ.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LocalEndpointPolicy_ローカル接続制限.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/OllamaProvider_Ollama接続.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/OpenAiCompatibleProvider_OpenAI互換接続.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LlmSearchAssistant_LLM検索補助.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/LlmProviderTests_LLM接続テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/LocalEndpointPolicyTests_接続制限テスト.cs`

**Interfaces:**
- Consumes: `ProductRecord`, `OperationResult<T>`.
- Produces: `ILlmProvider`, `LlmModelInfo`, `LlmChatRequest`, `LlmChatResponse`, `LocalEndpointPolicy.ValidateAsync`, and `LlmSearchAssistant.SummarizeAsync`.

- [ ] **Step 1: Define provider-neutral contracts**

```csharp
public sealed record LlmModelInfo(string Id, string DisplayName);
public sealed record LlmMessage(string Role, string Content);
public sealed record LlmChatRequest(string Model, IReadOnlyList<LlmMessage> Messages, TimeSpan Timeout);
public sealed record LlmChatResponse(string Content);

public interface ILlmProvider
{
    string ProviderId { get; }
    Task<OperationResult<IReadOnlyList<LlmModelInfo>>> GetModelsAsync(CancellationToken cancellationToken);
    Task<OperationResult<LlmChatResponse>> ChatAsync(LlmChatRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write failing endpoint-policy tests**

Test these exact outcomes:

```csharp
[DataRow("http://localhost:11434", true)]
[DataRow("http://127.0.0.1:1234", true)]
[DataRow("https://api.openai.com/v1", false)]
[DataRow("file:///c:/temp/model", false)]
[DataTestMethod]
public async Task ValidateAsync_EnforcesLocalOrLanPolicy(string url, bool expectedAllowed)
{
    var actual = await LocalEndpointPolicy.ValidateAsync(new Uri(url), FakeDns.PrivateNetworkOnly, default);
    Assert.AreEqual(expectedAllowed, actual.IsAllowed);
}
```

Resolve hostnames and allow only loopback, RFC1918 IPv4, or IPv6 unique-local/link-local addresses. Reject unresolved hostnames and public IPs with a Japanese message.

- [ ] **Step 3: Write failing provider serialization tests**

Use a fake `HttpMessageHandler` and assert:

- Ollama models: `GET /api/tags` maps `models[].name`.
- Ollama chat: `POST /api/chat` sends `stream=false` and parses `message.content`.
- OpenAI-compatible models: `GET /v1/models` maps `data[].id`.
- OpenAI-compatible chat: `POST /v1/chat/completions` parses `choices[0].message.content`.
- timeout/cancel returns `OperationError` without throwing into the UI.

- [ ] **Step 4: Implement both providers with injected `HttpClient`**

Constructors:

```csharp
public OllamaProvider(HttpClient httpClient, Uri baseUri);
public OpenAiCompatibleProvider(HttpClient httpClient, Uri baseUri, Func<string?> apiKeyProvider);
```

Treat `baseUri` as the server root: Ollama defaults to `http://localhost:11434/`, and OpenAI-compatible servers use the root before `/v1`. A provider factory must successfully call `LocalEndpointPolicy.ValidateAsync` before constructing a provider or sending a connection-test request. Never log authorization headers or full prompts. Apply per-request linked cancellation with the request timeout.

- [ ] **Step 5: Implement deterministic-first search assistance**

`LlmSearchAssistant.SummarizeAsync` accepts only already-filtered `ProductRecord` values, limits to 20 records, omits unmapped attributes over 500 characters, and requires Product_ID plus source date in its system instruction. If LLM is disabled or fails, return a deterministic Japanese summary built from the records.

- [ ] **Step 6: Run tests and commit**

Run all tests. Expected: PASS with no network calls.

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add configurable local LLM providers"
```

---

### Task 6: Revit 2025 Add-in Shell, Ribbon, and Dockable Pane

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/BuiApplication_アドイン起動.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/RibbonService_リボン作成.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/BuiPaneProvider_パネル登録.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ShowBuiPaneCommand_パネル表示.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPane_ドッキングパネル.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPane_ドッキングパネル.xaml.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/BUI.addin`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/Identifiers_固定GUID.cs`
- Test: `BUI_Addin_CSharpアドイン/docs_説明書/CompanyPcBuildCheck_会社PCビルド確認.md`

**Interfaces:**
- Consumes: Task 1 metadata and fixed approved UI tab names.
- Produces: `BuiApplication : IExternalApplication`, `BuiPaneProvider : IDockablePaneProvider`, `ShowBuiPaneCommand : IExternalCommand`, and a loadable Revit 2025 shell.

- [ ] **Step 1: Add Revit 2025 references without copying Autodesk DLLs**

Add to `Bui.Addin.csproj`:

```xml
<PropertyGroup>
  <Revit2025Path Condition="'$(Revit2025Path)' == ''">C:\Program Files\Autodesk\Revit 2025</Revit2025Path>
</PropertyGroup>
<ItemGroup>
  <Reference Include="RevitAPI">
    <HintPath>$(Revit2025Path)\RevitAPI.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Reference Include="RevitAPIUI">
    <HintPath>$(Revit2025Path)\RevitAPIUI.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

Also add `<None Update="Resources_画像と定義\BUI.addin" CopyToOutputDirectory="PreserveNewest" />` and the equivalent `None Update` for `SharedParameters_共有パラメータ.txt`; both retain their `Resources_画像と定義` output folder. The Autodesk DLL references remain `Private=false` and must not enter the output.

- [ ] **Step 2: Create stable identifiers and manifest**

Paste the four fixed GUIDs declared near the top of this plan into `Identifiers_固定GUID.cs`; do not generate new values. Expose them as `ApplicationAddInId`, `DockablePaneId`, `ProductIdParameterGuid`, and `AssemblyIdParameterGuid`. Add a Japanese comment stating that these values must never be regenerated after first release.

Manifest shape:

```xml
<RevitAddIns>
  <AddIn Type="Application">
    <Name>BUI</Name>
    <Assembly>{{BUI_ASSEMBLY_PATH}}</Assembly>
    <AddInId>62ba1d8c-426d-45f5-b1b9-9b6407284423</AddInId>
    <FullClassName>Bui.Application.BuiApplication</FullClassName>
    <VendorId>STARTSCAM</VendorId>
    <VendorDescription>スターツCAM株式会社</VendorDescription>
  </AddIn>
</RevitAddIns>
```

`{{BUI_ASSEMBLY_PATH}}` is a deliberate literal deployment token. The deployment script in Task 12 replaces only this token with the absolute built DLL path and fails if the token is absent or remains afterward.

- [ ] **Step 3: Implement startup, ribbon, and pane registration**

`OnStartup` must create a `BUI` ribbon tab/panel, buttons `BUIパネル` and `ID初期設定`, and register a right-docked pane titled `BUI`. `OnShutdown` unsubscribes events and disposes owned external events.

The initial XAML contains the approved tabs: `概要`, `検索`, `比較`, `点検`, `設定`, `ver情報`. The ver tab reads metadata plus `AssemblyInformationalVersionAttribute` (display `0.1.0`, excluding any `+` build suffix) rather than hardcoding a version. It displays the DLL file's `File.GetLastWriteTimeUtc(assembly.Location)` as the PoC build timestamp.

- [ ] **Step 4: Build on the company PC and verify load**

Run:

```powershell
dotnet build "BUI_Addin_CSharpアドイン/Bui.sln" -c Debug -p:Revit2025Path="C:\Program Files\Autodesk\Revit 2025"
```

Expected: build succeeds, output contains `Bui.dll`, and no copied `RevitAPI.dll` or `RevitAPIUI.dll`.

Temporarily place the manifest in `%APPDATA%\Autodesk\Revit\Addins\2025`, set its assembly path to the Debug DLL, start Revit 2025, and verify the BUI pane opens. Record the result in `CompanyPcBuildCheck_会社PCビルド確認.md`.

- [ ] **Step 5: Commit**

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add Revit 2025 BUI shell and dockable pane"
```

---

### Task 7: Shared Product_ID and Assembly_ID Parameter Installation

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ParameterBindingModels_パラメータ登録結果.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/SharedParameters/SharedParameterService_共有パラメータ処理.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/SharedParameters/InstallIdentifiersCommand_ID初期設定.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/SharedParameters_共有パラメータ.txt`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/ParameterBindingDecisionTests_パラメータ判定テスト.cs`

**Interfaces:**
- Consumes: Fixed GUIDs from Task 6.
- Produces: `ParameterBindingDecision`, `ParameterBindingReport`, and `SharedParameterService.Install(Document, Application)`.

- [ ] **Step 1: Write failing decision tests**

Model the Revit-independent decisions:

```csharp
[TestMethod]
public void Decide_SameNameDifferentGuidReturnsConflict()
{
    var actual = ParameterBindingDecision.Decide(
        expectedName: "Product_ID",
        expectedGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        existingName: "Product_ID",
        existingGuid: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        hasExpectedBinding: false);

    Assert.AreEqual(ParameterBindingAction.Conflict, actual.Action);
}
```

Also test `AlreadyCorrect`, `ExtendCategories`, and `CreateNew`.

- [ ] **Step 2: Implement report and decision types**

```csharp
public enum ParameterBindingAction { CreateNew, ExtendCategories, AlreadyCorrect, Conflict }
public sealed record CategoryBindingResult(string CategoryName, string Status, string Message);
public sealed record ParameterBindingReport(IReadOnlyList<CategoryBindingResult> Categories, IReadOnlyList<string> Conflicts);
```

- [ ] **Step 3: Implement the Revit shared-parameter transaction**

Create `SharedParameters_共有パラメータ.txt` with these exact tab-separated definitions (the apparent spacing between fields must be tab characters):

```text
# This is a Revit shared parameter file.
# Do not edit manually.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
GROUP	1	BUI
*PARAM	GUID	NAME	DATATYPE	DATACATEGORY	GROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	HIDEWHENNOVALUE
PARAM	569e8df1-4ca6-448c-8fb9-496cb39b4458	Product_ID	TEXT		1	1	BUI product identifier	1	0
PARAM	883267b2-8771-498a-a77e-a698e06b3453	Assembly_ID	TEXT		1	1	BUI assembly identifier	1	0
```

`SharedParameterService.Install` must:

1. Save `Application.SharedParametersFilename`.
2. Point to the shipped BUI shared parameter file.
3. Resolve definitions by fixed GUID, not `LookupParameter` name only.
4. Build Product_ID category set from all top-level `Document.Settings.Categories` where `AllowsBoundParameters` is true.
5. Bind Product_ID as instance text under `GroupTypeId.IdentityData`.
6. Bind Assembly_ID as type text to Walls, Floors, Ceilings, and Roofs.
7. Extend an existing correct binding instead of replacing values.
8. Return conflicts without changing a same-name/different-GUID parameter.
9. Restore the original shared parameter filename in `finally`.
10. Roll back the transaction on any unhandled Revit exception.

- [ ] **Step 4: Run automated tests, build, and perform Revit smoke test**

Expected in a blank test project:

- Product_ID appears under Identity Data for every bindable category checked by the report.
- Assembly_ID appears on wall/floor/ceiling/roof types.
- Re-running reports already-correct or extends categories without duplicates.
- Revit shared parameter file preference is unchanged after completion.

- [ ] **Step 5: Commit**

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: install BUI shared identifiers safely"
```

---

### Task 8: Selection Tracking, External Events, Bulk Product_ID Assignment, and Audit

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/BulkAssignmentPlan_一括付与計画.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/IdentifierAuditModels_ID点検結果.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/SelectionSnapshot_選択状態.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Selection/SelectionTracker_選択追跡.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/RevitActionDispatcher_外部イベント仲介.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/ProductIdWriteHandler_製品ID書込.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Audit/IdentifierAuditService_ID点検.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/BulkAssignmentPlanTests_一括付与テスト.cs`

**Interfaces:**
- Consumes: Product repository and fixed Product_ID GUID.
- Produces: `BulkAssignmentPlan.Build`, `SelectionSnapshot`, `SelectionTracker`, `RevitActionDispatcher`, `ProductIdWriteHandler`, `IdentifierAuditService`.

- [ ] **Step 1: Write failing bulk-plan tests**

```csharp
[TestMethod]
public void Build_ClassifiesEmptySameConflictAndReadOnly()
{
    var candidates = new[]
    {
        new AssignmentCandidate(1, null, true),
        new AssignmentCandidate(2, "P-001", true),
        new AssignmentCandidate(3, "P-999", true),
        new AssignmentCandidate(4, null, false)
    };

    var plan = BulkAssignmentPlan.Build("P-001", candidates);

    CollectionAssert.AreEqual(new[] { 1 }, plan.ToWrite.ToArray());
    CollectionAssert.AreEqual(new[] { 2 }, plan.AlreadySame.ToArray());
    CollectionAssert.AreEqual(new[] { 3 }, plan.Conflicts.ToArray());
    CollectionAssert.AreEqual(new[] { 4 }, plan.NotWritable.ToArray());
}
```

- [ ] **Step 2: Implement selection tracking without Revit writes**

Use this Revit-independent shape:

```csharp
public sealed record SelectionSnapshot(
    bool HasDocument,
    IReadOnlyList<long> ElementIds,
    bool ContainsLinkedElement,
    bool IsReadOnlyContext);
```

Subscribe to `UIControlledApplication.SelectionChanged` in `BuiApplication.OnStartup` and unsubscribe the same handler in `OnShutdown`. The handler uses `SelectionChangedEventArgs.GetSelectedElements()` and `GetReferences()` to build and post a lightweight immutable `SelectionSnapshot` to the view model. If additional document reads are required, schedule them through the supported Revit UI context instead of doing them in the event callback. The callback must not start a transaction, alter selection, load CSV synchronously, call LLM, or modify the current document.

States: no document, none selected, one selected, multiple selected, linked element/read-only context.

- [ ] **Step 3: Implement one External Event dispatcher**

```csharp
public interface IRevitActionRequest
{
    string Name { get; }
    void Execute(UIApplication application);
}
```

`RevitActionDispatcher.Enqueue` stores one pending request at a time and calls `ExternalEvent.Raise()`. If a request is already pending, return a Japanese busy message rather than replacing it.

- [ ] **Step 4: Implement exact bulk-assignment behavior**

Collect target elements by scope:

- selected IDs
- active-view collector
- document-wide collector

For same-type mode compare `Element.GetTypeId()`, never the localized type-name string. Preview counts before enqueueing the write. Default behavior writes only blank values, leaves same values untouched, and skips conflicts. An explicit second confirmation may include conflict overwrite.

Execute writes in one `TransactionGroup` and one transaction so Revit Undo shows one BUI operation. Report element ID and reason for failures.

- [ ] **Step 5: Implement audit**

Audit results must include:

- parameter missing
- Product_ID blank
- ID absent from product repository
- old ID with mapping candidate
- same type containing multiple IDs
- parameter read-only or element not editable

Do not auto-fix during audit.

- [ ] **Step 6: Run tests, company-PC smoke test, and commit**

Verify project-wide, active-view, and selection scopes in a copied RVT. Verify one Undo reverses the batch.

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add Product_ID assignment and audit"
```

---

### Task 9: Compound Structures, Revit Materials, Parts, and Assembly_ID Application

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/CompoundLayerSnapshot_複合層読取結果.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/CompoundLayerConverter_複合層変換.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Assemblies/CompoundStructureReader_複合構造読取.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Assemblies/MaterialProductIdService_材料製品ID処理.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Assemblies/PartMaterialReader_パーツ材料読取.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/AssemblyIdWriteHandler_構成ID書込.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造/CompoundLayerConversionTests_複合層変換テスト.cs`

**Interfaces:**
- Consumes: `AssemblyRecord`, fingerprint, allocator, `IAssemblyRepository`, fixed identifier GUIDs.
- Produces: `CompoundLayerConverter.ToAssemblyLayers`, `CompoundStructureReader.Read`, `MaterialProductIdService`, `PartMaterialReader`, and `AssemblyIdWriteHandler`.

- [ ] **Step 1: Write failing layer-conversion tests**

```csharp
[TestMethod]
public void Convert_PreservesOrderFunctionThicknessAndProductId()
{
    var snapshots = new[]
    {
        new CompoundLayerSnapshot(0, "Finish1", 2m, 101, "床タイル", "P-001", false),
        new CompoundLayerSnapshot(1, "Structure", 150m, 102, "コンクリート", "P-105", false)
    };

    var result = CompoundLayerConverter.ToAssemblyLayers(snapshots);

    Assert.AreEqual("P-001", result.Value![0].ProductId);
    Assert.AreEqual(150m, result.Value![1].ThicknessMillimeters);
}
```

Also test that a blank material Product_ID fails with `ASSEMBLY_PRODUCT_ID_MISSING` and a vertically compound flag produces `ASSEMBLY_REVIEW_REQUIRED`.

- [ ] **Step 2: Implement Revit compound reading**

For selected Wall/Floor/Ceiling/Roof:

1. Resolve `HostObjAttributes` from type ID.
2. Call `GetCompoundStructure()`.
3. Iterate `GetLayers()` in Revit order.
4. Convert internal feet to millimeters with `UnitUtils.ConvertFromInternalUnits(..., UnitTypeId.Millimeters)`.
5. Resolve each `MaterialId`; if invalid, resolve category/subcategory fallback and mark it as fallback.
6. Read Product_ID by fixed GUID from the Material element.
7. Detect `IsVerticallyCompound` and return review-required instead of auto-registering.

- [ ] **Step 3: Implement material and part behavior**

`MaterialProductIdService` reads/writes Product_ID on Material through External Event. If one material is about to receive a new conflicting ID, preview every host type using that material and require confirmation.

`PartMaterialReader` reads the Part material and its Product_ID. A Part-level Product_ID overrides material-derived Product_ID only when the user explicitly confirms and the audit records the override.

- [ ] **Step 4: Implement semi-automatic Assembly_ID application**

The handler:

1. Converts layers.
2. Creates fingerprint.
3. Reuses matching Assembly_ID if found.
4. Otherwise allocates the next `ASM-000001` style ID and displays it for confirmation.
5. Adds the AssemblyRecord to CSV only after confirmation.
6. Writes Assembly_ID to the host type.
7. Rolls back both operations if Revit write fails; if CSV was already added, remove the new row using repository compensation and log the action.

- [ ] **Step 5: Run tests, Revit tests, and commit**

Test basic wall, floor, ceiling, roof, parts, by-category material, missing layer Product_ID, and vertically compound wall warning.

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: support compound layers and Assembly_ID"
```

---

### Task 10: Approved Tabbed UI and View Models

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Common_共通/ObservableObject_画面通知.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Common_共通/AsyncCommand_非同期操作.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiViewContracts_画面連携契約.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/UI/RevitSelectionDetailsService_選択詳細取得.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/UI/RevitAuditQueryService_点検照会.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/WpfUiDispatcher_WPF画面更新.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/CompareView_比較.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/AuditView_点検.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SettingsView_設定.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/VersionInfoView_バージョン情報.xaml`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/BuiDockPaneViewModelTests_パネル状態テスト.cs`

**Interfaces:**
- Consumes: repositories, LLM providers, selection snapshots, audit service, compound reader, metadata.
- Produces: `BuiDockPaneViewModel` and the approved A-layout tab UI.

- [ ] **Step 1: Write failing view-model tests**

```csharp
[TestMethod]
public async Task RefreshSelectionAsync_ShowsOfflineDataWithoutCallingLlmWhenDisabled()
{
    var products = new FakeProductRepository(ProductFactory.Create("P-001", "床タイル"));
    var llm = new RecordingLlmProvider();
    var sut = ViewModelFactory.Create(products, llm, llmEnabled: false);

    await sut.RefreshSelectionAsync(SelectionFactory.SingleProduct("P-001"));

    Assert.AreEqual("床タイル", sut.SelectedProduct!.ProductName);
    Assert.AreEqual(0, llm.ChatCalls);
}
```

Also test compound layer display, missing layer warning, compare selection limit, and version metadata display.

- [ ] **Step 2: Implement view-model state**

Keep the shared view model free of Autodesk types. Define these boundary contracts in `BuiViewContracts_画面連携契約.cs`, with Revit implementations in the two adapter files above and fake implementations in tests:

```csharp
public sealed record SelectionDetails(
    SelectionSnapshot Snapshot,
    string? ProductId,
    string? AssemblyId,
    IReadOnlyList<AssemblyLayerRecord> Layers,
    IReadOnlyList<string> Warnings);

public sealed record ComparisonItemViewModel(
    ProductRecord Product,
    PriceRecord? Price,
    IReadOnlyList<string> MissingFields);

public interface ISelectionDetailsService
{
    Task<SelectionDetails> LoadAsync(SelectionSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditIssue>> AuditAsync(CancellationToken cancellationToken);
}

public interface IUiDispatcher
{
    Task InvokeAsync(Action action, CancellationToken cancellationToken);
}
```

The view model depends only on these interfaces, repositories, metadata, and `ILlmProvider`; it must not reference `Autodesk.Revit.*` or `System.Windows.*`. `WpfUiDispatcher` is the only adapter that uses WPF `Dispatcher`, while tests use an immediate fake dispatcher.

Required properties:

```csharp
public SelectionSummary Selection { get; }
public ProductRecord? SelectedProduct { get; }
public ObservableCollection<PriceRecord> SelectedPrices { get; }
public string? AssemblyId { get; }
public ObservableCollection<CompoundLayerViewModel> Layers { get; }
public ObservableCollection<ProductRecord> SearchResults { get; }
public ObservableCollection<ComparisonItemViewModel> CompareItems { get; }
public ObservableCollection<AuditIssue> AuditIssues { get; }
public DataSourceStatus DataStatus { get; }
public bool IsLlmEnabled { get; set; }
public string StatusMessage { get; }
```

All slow operations use `AsyncCommand`, disable only the affected action, support cancellation, and marshal property updates through `IUiDispatcher`.

- [ ] **Step 3: Implement the approved tab content**

- Overview: selection, same-type count, Product_ID/Assembly_ID, CSV status, dynamic layer list.
- Search: text and exact filters, deterministic results, optional LLM summary.
- Compare: 2–4 products, price/source/date/missing attributes.
- Audit: grouped issues with navigation, no auto-fix.
- Settings: CSV paths, LLM toggle/provider/URL/model/timeout/test.
- ver情報: metadata, assembly version, Revit 2025, build timestamp; no permanent footer.

Use standard WPF controls, keyboard focus, clear Japanese labels, and no color-only status indication.

- [ ] **Step 4: Run tests and visually verify in Revit**

Verify at narrow and wide pane widths, 100% and 150% Windows scaling, LLM OFF/ON, no selection, multiple selection, and compound floor selection.

- [ ] **Step 5: Commit**

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add approved BUI tabbed interface"
```

---

### Task 11: Layered Settings, Redacted Logging, and Error Boundaries

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/BuiSettings_BUI設定.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SettingsMerger_設定統合.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SettingsService_設定管理.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Logging_ログ/LogEntry_ログ項目.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Logging_ログ/IBuiLogger_ログ窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Logging_ログ/FileBuiLogger_ファイルログ.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Logging_ログ/ErrorBoundary_エラー保護.cs`
- Create: `BUI_Addin_CSharpアドイン/config_設定例/Settings.Default_共通設定.json`
- Create: `BUI_Addin_CSharpアドイン/config_設定例/Settings.Local.example_個別設定例.json`
- Create: `BUI_Addin_CSharpアドイン/config_設定例/Secrets.Local.example_認証情報例.json`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Configuration_設定/SettingsMergerTests_設定統合テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Logging_ログ/FileBuiLoggerTests_ログ秘匿テスト.cs`

**Interfaces:**
- Consumes: metadata, provider configuration, CSV mapping.
- Produces: `BuiSettings`, `SettingsService.Load`, `IBuiLogger`, `ErrorBoundary.Run/RunAsync`.

- [ ] **Step 1: Write failing setting precedence tests**

```csharp
[TestMethod]
public void Merge_LocalOverridesDefaultAndSecretOverridesOnlyCredentialFields()
{
    var defaults = SettingsFactory.Default(baseUrl: "http://localhost:11434", model: "qwen");
    var local = SettingsFactory.Local(baseUrl: "http://10.0.0.5:11434", model: "llama");
    var secrets = SettingsFactory.Secrets(apiKey: "secret-value");

    var actual = SettingsMerger.Merge(defaults, local, secrets);

    Assert.AreEqual("http://10.0.0.5:11434", actual.Llm.BaseUrl);
    Assert.AreEqual("llama", actual.Llm.Model);
    Assert.AreEqual("secret-value", actual.Llm.ApiKey);
}
```

Corrupt local JSON must return defaults plus a warning, not crash startup.

- [ ] **Step 2: Write failing redaction tests**

Use this exact log record so the logger and error boundary share one stable contract:

```csharp
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Operation,
    string ErrorCode,
    string ExceptionType,
    string SanitizedDetail,
    string? SensitiveBody)
{
    public static LogEntry Error(string operation, string detail, string? sensitiveBody) =>
        new(DateTimeOffset.UtcNow, "Error", operation, "UNEXPECTED", "Unknown", detail, sensitiveBody);
}
```

`SensitiveBody` exists only as an input to the filtering boundary and must never be serialized to disk.

```csharp
[TestMethod]
public async Task WriteAsync_RedactsTokensAndDoesNotWritePromptBody()
{
    using var logs = LogTestDirectory.Create();
    var sut = new FileBuiLogger(logs.Path, maxBytes: 100_000, retentionDays: 30);

    await sut.WriteAsync(LogEntry.Error("LLM", "Bearer abc123", "秘密の質問全文"), default);

    var text = await logs.ReadAllAsync();
    StringAssert.DoesNotContain(text, "abc123");
    StringAssert.DoesNotContain(text, "秘密の質問全文");
}
```

- [ ] **Step 3: Implement exact configuration locations**

Use these stable configuration records:

```csharp
public sealed record DataSettings(
    string Provider,
    string ProductCsvPath,
    string PriceCsvPath,
    string AssemblyCsvPath,
    string? IdAliasCsvPath,
    CsvColumnMapping ProductColumns,
    PriceColumnMapping PriceColumns);

public sealed record LlmSettings(
    bool Enabled,
    string Provider,
    string BaseUrl,
    string Model,
    int TimeoutSeconds,
    string? ApiKey);

public sealed record BuiSettings(DataSettings Data, LlmSettings Llm);
```

`Settings.Default_共通設定.json` contains exactly:

```json
{
  "data": {
    "provider": "Csv",
    "productCsvPath": "",
    "priceCsvPath": "",
    "assemblyCsvPath": "",
    "idAliasCsvPath": null,
    "productColumns": {
      "productIdColumn": "db_key",
      "productNameColumn": "製品名",
      "manufacturerColumn": "メーカー名",
      "modelNumberColumn": "型番",
      "unitColumn": "単位",
      "updatedAtColumn": "更新日"
    },
    "priceColumns": {
      "productIdColumn": "db_key",
      "unitPriceColumn": "単価",
      "currencyColumn": "通貨",
      "validFromColumn": "適用開始日",
      "regionColumn": "地域",
      "informationSourceColumn": "情報源"
    }
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

The local example uses the same shape with dummy paths and no credential. The secrets example contains only `{ "llm": { "apiKey": "" } }`. `SettingsMerger` allows the secret layer to override only `ApiKey`; any data path, provider, URL, or model found in the secrets file is ignored and logged as a warning. `Provider` remains `Csv` in this PoC; a future `SqlServer` implementation will extend the same data boundary without changing callers.

Add `Settings.Default_共通設定.json` to `Bui.Addin.csproj` as linked `Content` at `Config/Settings.Default_共通設定.json` with `CopyToOutputDirectory="PreserveNewest"`. The two example files remain documentation-only and are not loaded at runtime.

- Shipped defaults: add-in `config_設定例` copy.
- Local settings: `%LOCALAPPDATA%\StartsCAM\BUI\Settings.Local_個別設定.json`.
- Secrets: `%LOCALAPPDATA%\StartsCAM\BUI\Secrets.Local_認証情報.json`.
- Logs: `%LOCALAPPDATA%\StartsCAM\BUI\Logs`.

Settings UI saves only local settings. Secrets UI masks values and never writes them to the default file.

- [ ] **Step 4: Implement rolling logs and error boundary**

Default retention is 30 days and 10 MB total. Redact values for keys matching `token`, `password`, `secret`, `authorization`, and connection-string password aliases. Log timestamp, version, operation, error code, exception type, and sanitized detail.

`ErrorBoundary` returns `OperationResult<T>` with Japanese user message and logs technical detail. Revit entry points, External Event handlers, file operations, and LLM calls must use it.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add layered settings and safe logging"
```

---

### Task 12: Development Deployment, User Documentation, and End-to-End PoC Verification

**Files:**
- Create: `BUI_Addin_CSharpアドイン/scripts_開発補助/DeployDev_開発用配置.ps1`
- Create: `BUI_Addin_CSharpアドイン/scripts_開発補助/RemoveDev_開発用解除.ps1`
- Create: `BUI_Addin_CSharpアドイン/docs_説明書/README_はじめに.md`
- Create: `BUI_Addin_CSharpアドイン/docs_説明書/BuildAndDeploy_ビルド配置手順.md`
- Create: `BUI_Addin_CSharpアドイン/docs_説明書/TestChecklist_Revit実機確認.md`
- Create: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/PowerShell/DeployDev.Tests.ps1`

**Interfaces:**
- Consumes: built `Bui.dll`, manifest template, config examples, all approved acceptance criteria.
- Produces: repeatable manual deployment/removal and a completed verification record.

- [ ] **Step 1: Write Pester tests for deployment target isolation**

Use Pester 5. Before the first run, verify it with `Get-Module -ListAvailable Pester`; when absent, install it on the company PC with `Install-Module Pester -Scope CurrentUser -Force` only after confirming company policy permits PowerShell Gallery access. Both deployment scripts must define their functions first and execute their main routine only when the file is run normally, not when Pester dot-sources it (`$MyInvocation.InvocationName -ne '.'`).

Tests must prove:

- deployment target resolves under `%APPDATA%\Autodesk\Revit\Addins\2025` only;
- the script refuses any Revit year other than `2025`;
- the script copies only BUI-owned files;
- manifest deployment token is replaced with an absolute DLL path;
- removal deletes only the BUI manifest/folder and refuses a broad or unresolved path.

Example assertion:

```powershell
It 'refuses a destination outside Revit Addins 2025' {
    { Resolve-BuiDeployTarget -Destination 'C:\' } | Should -Throw
}
```

- [ ] **Step 2: Implement development deployment**

`DeployDev_開発用配置.ps1` parameters:

```powershell
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$RevitPath = 'C:\Program Files\Autodesk\Revit 2025',
    [switch]$Build
)
```

It validates RevitAPI.dll/UI, optionally runs `dotnet build`, copies output to `%APPDATA%\Autodesk\Revit\Addins\2025\BUI`, renders `%APPDATA%\Autodesk\Revit\Addins\2025\BUI.addin`, and prints exact next steps. It never edits ProgramData or Revit.exe.config.

`RemoveDev_開発用解除.ps1` resolves literal absolute targets, verifies both stay under the expected Addins 2025 directory, and removes only `BUI.addin` and the `BUI` child folder.

- [ ] **Step 3: Write beginner-oriented documentation**

`README_はじめに.md` explains DLL, `.addin`, solution, build, deployment, and where to edit author metadata.

`BuildAndDeploy_ビルド配置手順.md` contains copy-paste commands for Visual Studio 2026 and PowerShell, including close-Revit-before-rebuild.

`Troubleshooting_問題解決.md` covers missing Revit API, pane not visible, manifest path, CSV missing columns, file locked, shared parameter conflict, LLM unavailable, public LLM URL rejected, and log location.

- [ ] **Step 4: Execute automated verification**

Run:

```powershell
dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" -c Debug
dotnet build "BUI_Addin_CSharpアドイン/Bui.sln" -c Release -p:Revit2025Path="C:\Program Files\Autodesk\Revit 2025"
Invoke-Pester "BUI_Addin_CSharpアドイン/tests_テスト/PowerShell/DeployDev.Tests.ps1"
```

Expected: all unit tests pass, Release build succeeds, deployment safety tests pass.

- [ ] **Step 5: Execute and record the Revit 2025 acceptance checklist**

Use a copied company test RVT and copied CSV. Check every item from spec section 21, including:

- ribbon/pane;
- ID installation and rerun;
- CSV health;
- deterministic search/compare;
- single and bulk Product_ID assignment with Undo;
- compound floor/wall/ceiling/roof and parts;
- Assembly_ID reuse/new numbering;
- audit;
- LLM OFF, Ollama, OpenAI-compatible API, timeout fallback;
- CSV backup/restore and local redacted logs.

Record date, PC, Revit build, BUI version, result, evidence path, and issue link for each row. Do not commit company screenshots/data if they contain confidential content.

- [ ] **Step 6: Final version and commit**

Keep version `0.1.0` until all PoC criteria pass. Do not change to `1.0.0` during PoC.

```powershell
git add "BUI_Addin_CSharpアドイン"
git commit -m "docs: add BUI deployment and PoC verification"
```

---

## Implementation Completion Gate

Before claiming the PoC complete:

1. Run every automated command in Task 12 and retain outputs.
2. Confirm `git status --short` contains no generated DLL, company CSV/RVT, token, password, local settings, log, backup, or temporary file.
3. Review the final diff against `docs/superpowers/specs/2026-09-01-bui-common-foundation-poc-design.md` section by section.
4. Complete the company-PC Revit checklist with no unresolved severity-high item.
5. Keep any failed or skipped acceptance criterion explicitly open; do not report completion from build success alone.
