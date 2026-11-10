# 単価CSV・構成CSVの即時読み直し Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 設定タブの「切り替えて読み直す」ボタンを押すと、単価CSV（Prices.csv）と構成CSV（Assemblies.csv）もRevitを再起動せずにその場で反映されるようにする。

**Architecture:** 既存の `DataSourceSwitch`（製品データの出どころを1つの参照として持ち、中身だけ差し替える設計）と同じパターンを、単価・構成それぞれの窓口クラスに適用する。ViewModelへ注入済みのオブジェクトの実体は据え置いたまま、内部の読み込み先パスだけを差し替える。

**Tech Stack:** C# 12 / .NET 8（`net8.0-windows`）、MSTest（Bui.Tests）。Bui.Shared_共有ロジックはRevit非依存でビルド・テスト可能、Bui.Addinは会社PCでしか確認できない。

## Global Constraints

- 反映のタイミングは「切り替えて読み直す」ボタンを押したときだけ（保存だけでは反映しない）。2026-09-15にユーザー承認済みの決定。
- テストが必要なロジックはすべてBui.Shared_共有ロジック側に置く（私物PCで `dotnet test` できるようにするため）。Bui.Addin側の変更は薄い配線のみにする。
- 既存の `DataSourceSwitch` のクラス設計・実体を作り直さない方針は変えない。
- YAGNI: 単価CSVの列マッピング編集UI、専用の「単価だけ読み直す」ボタン、保存時の自動反映は実装しない（設計スペックで却下済み）。

参照: [2026-09-15-csv-price-assembly-live-reload-design.md](../specs/2026-09-15-csv-price-assembly-live-reload-design.md)

---

### Task 1: CsvPathResolver（共有パス解決ユーティリティ）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Configuration_設定/CsvPathResolver_CSVパス解決.cs`
- Test: `tests_テスト/Bui.Tests/Configuration_設定/CsvPathResolverTests_CSVパス解決テスト.cs`

**Interfaces:**
- Produces: `Bui.Configuration.CsvPathResolver.Resolve(string? configuredPath, string dataRoot, string fallbackFileName) : string`
  — 後続タスク（Task 2, 5）がこれを呼ぶ。

- [ ] **Step 1: テストディレクトリを確認する**

`tests_テスト/Bui.Tests/Configuration_設定/` フォルダが無ければ作成する（既存の `Configuration_設定` テストフォルダがあるか確認）。

- [ ] **Step 2: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Configuration_設定/CsvPathResolverTests_CSVパス解決テスト.cs` を新規作成する。

```csharp
// このファイルはCsvPathResolverの「空欄なら既定ファイルを使う」という
// 解決ルールを守ります。起動時と再読み込み時の両方がこの関数を使うため、
// ここが壊れると両方の経路が同時にズレます。
using Bui.Configuration;

namespace Bui.Tests.Configuration_設定;

[TestClass]
public class CsvPathResolverTests_CSVパス解決テスト
{
    [TestMethod]
    public void Resolve_空欄なら既定フォルダとファイル名を組み合わせる()
    {
        var result = CsvPathResolver.Resolve(string.Empty, @"C:\Data", "Prices.csv");

        Assert.AreEqual(@"C:\Data\Prices.csv", result);
    }

    [TestMethod]
    public void Resolve_nullなら既定フォルダとファイル名を組み合わせる()
    {
        var result = CsvPathResolver.Resolve(null, @"C:\Data", "Assemblies.csv");

        Assert.AreEqual(@"C:\Data\Assemblies.csv", result);
    }

    [TestMethod]
    public void Resolve_空白だけなら既定フォルダとファイル名を組み合わせる()
    {
        var result = CsvPathResolver.Resolve("   ", @"C:\Data", "Prices.csv");

        Assert.AreEqual(@"C:\Data\Prices.csv", result);
    }

    [TestMethod]
    public void Resolve_値が入っていればそのまま返す()
    {
        var result = CsvPathResolver.Resolve(@"D:\CustomFolder\MyPrices.csv", @"C:\Data", "Prices.csv");

        Assert.AreEqual(@"D:\CustomFolder\MyPrices.csv", result);
    }
}
```

- [ ] **Step 3: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~CsvPathResolverTests" -v minimal`
Expected: ビルドエラー（`CsvPathResolver` が存在しない）またはコンパイル失敗。

- [ ] **Step 4: 最小限の実装を書く**

`src_ソース/Bui.Shared_共有ロジック/Configuration_設定/CsvPathResolver_CSVパス解決.cs` を新規作成する。

```csharp
// このファイルの目的:
//   設定欄が空欄のときに使う既定CSVパスの決め方を、1か所だけで持ちます。
//
// なぜ1か所なのか:
//   起動時（BuiDockPaneComposition.Create）と、設定タブの「切り替えて
//   読み直す」を押したとき（RevitDataSourceConnector）の両方がこの
//   ルールを使います。別々に実装すると、片方だけ直してズレる事故が
//   起きます（2026-09-15のインストーラー作業で、allowlistの二重管理が
//   ズレる不具合を繰り返し踏みました。同じ轍を踏みません）。

namespace Bui.Configuration;

/// <summary>設定欄が空欄のときの既定CSVパスを解決します。</summary>
public static class CsvPathResolver
{
    /// <summary>configuredPathが空欄ならdataRoot配下のfallbackFileNameを、値があればそのまま返します。</summary>
    public static string Resolve(string? configuredPath, string dataRoot, string fallbackFileName) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(dataRoot, fallbackFileName)
            : configuredPath;
}
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~CsvPathResolverTests" -v minimal`
Expected: PASS（4件成功）。

- [ ] **Step 6: コミットする**

```bash
git add tests_テスト/Bui.Tests/Configuration_設定/CsvPathResolverTests_CSVパス解決テスト.cs src_ソース/Bui.Shared_共有ロジック/Configuration_設定/CsvPathResolver_CSVパス解決.cs
git commit -m "feat: CsvPathResolverを追加し、CSVパス解決を共有化する"
```

---

### Task 2: ICsvPathSwitchable と SwitchingPriceRepository の拡張

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ICsvPathSwitchable_CSVパス切替.cs`
- Modify: `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingPriceRepository_単価窓口切替.cs`
- Modify: `tests_テスト/Bui.Tests/Data_データ/ReadOnlyDataSourceTests_読取専用テスト.cs:55` と `:72`（コンストラクタ引数追加）
- Test: `tests_テスト/Bui.Tests/Data_データ/SwitchingPriceRepositoryTests_単価窓口切替テスト.cs`

**Interfaces:**
- Consumes: なし（このタスク単体で完結）
- Produces:
  - `Bui.Data.ICsvPathSwitchable.SwitchCsvPath(string csvPath) : void`
  - `Bui.Data.SwitchingPriceRepository` の新しいコンストラクタ:
    `SwitchingPriceRepository(IPriceRepository csvPrices, DataSourceSwitch source, PriceColumnMapping mapping)`
    （後続タスク5がこの3引数版を呼ぶ）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/SwitchingPriceRepositoryTests_単価窓口切替テスト.cs` を新規作成する。

```csharp
// このファイルはSwitchingPriceRepositoryが、実体を作り直さずに
// 読み込み先CSVのパスだけを差し替えられることを守ります。
// これが壊れると、設定タブで単価CSVのパスを変えても
// 「切り替えて読み直す」を押した後も古いファイルを読み続けます。
using Bui.Data;
using Bui.Data.Csv;
using Bui.Products;

namespace Bui.Tests.Data_データ;

[TestClass]
public class SwitchingPriceRepositoryTests_単価窓口切替テスト
{
    [TestMethod]
    public async Task SwitchCsvPath_切替後は新しいCSVの内容を返す()
    {
        using var oldFiles = CsvTestFiles.Create("db_key,単価\r\nP-001,100\r\n");
        using var newFiles = CsvTestFiles.Create("db_key,単価\r\nP-001,999\r\n");
        var mapping = PriceColumnMapping.PocDefault;
        var initial = new CsvPriceRepository(oldFiles.CsvPath, mapping);
        var sut = new SwitchingPriceRepository(initial, DataSourceSwitch.ForCsvFolder(@"C:\dummy"), mapping);

        sut.SwitchCsvPath(newFiles.CsvPath);
        var rows = await sut.SearchAsync(new PriceQuery("P-001", null, null), default);

        Assert.AreEqual(999m, rows.Single().UnitPrice);
    }

    [TestMethod]
    public async Task SwitchCsvPath_ICsvPathSwitchable経由でも切り替えられる()
    {
        using var oldFiles = CsvTestFiles.Create("db_key,単価\r\nP-001,100\r\n");
        using var newFiles = CsvTestFiles.Create("db_key,単価\r\nP-001,777\r\n");
        var mapping = PriceColumnMapping.PocDefault;
        var initial = new CsvPriceRepository(oldFiles.CsvPath, mapping);
        var sut = new SwitchingPriceRepository(initial, DataSourceSwitch.ForCsvFolder(@"C:\dummy"), mapping);
        ICsvPathSwitchable switchable = sut;

        switchable.SwitchCsvPath(newFiles.CsvPath);
        var rows = await sut.SearchAsync(new PriceQuery("P-001", null, null), default);

        Assert.AreEqual(777m, rows.Single().UnitPrice);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~SwitchingPriceRepositoryTests" -v minimal`
Expected: ビルドエラー（`SwitchCsvPath` メソッドが無い、コンストラクタが3引数を受け付けない、`ICsvPathSwitchable` が存在しない）。

- [ ] **Step 3: ICsvPathSwitchableインターフェースを作る**

`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ICsvPathSwitchable_CSVパス切替.cs` を新規作成する。

```csharp
// このファイルの目的:
//   「内部で読んでいるCSVのパスを、実体を作り直さずに差し替えられる」
//   という契約を、単価・構成の両方の窓口クラスで共有します。
//
// なぜ必要か:
//   RevitDataSourceConnector（Bui.Addin側）は、単価・構成それぞれの
//   具象クラスを知る必要なく「パスを渡して切り替える」操作だけを
//   呼べれば十分です。具象型への依存を増やさないためのインターフェイスです。

namespace Bui.Data;

/// <summary>内部で読んでいるCSVのパスを、実体を作り直さずに差し替えられることを表します。</summary>
public interface ICsvPathSwitchable
{
    /// <summary>読み込み先を新しいパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    void SwitchCsvPath(string csvPath);
}
```

- [ ] **Step 4: SwitchingPriceRepositoryを拡張する**

`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingPriceRepository_単価窓口切替.cs` を次の内容に置き換える。

```csharp
// このファイルの目的:
//   SQL接続中に単価CSVを読みに行かないようにします。
//   また、単価CSVのパスを、実体を作り直さずに切り替えられるようにします。
//
// なぜ必要か:
//   単価情報はまだSQL側にありません（GoldStandardDBに単価テーブルがない）。
//   何もしないと、SQLへ切り替えた後も選択のたびにPrices.csvを開きに行き、
//   「SQLに切り替えたのにCSVを触っている」状態になります。
//
//   2026-09-15までは、設定タブで単価CSVのパスを変えても、Revitを
//   再起動するまで反映されませんでした（BuiDockPaneComposition.Create
//   で起動時に1回だけ実体を作っていたため）。SwitchCsvPathで
//   「切り替えて読み直す」ボタンから即時反映できるようにします。
//
// 修正時の影響:
//   読めないことを黙って隠さず、状態表示に理由を出してください。
//   単価が空になった理由が分からないと、原因の切り分けができません。
//   SwitchCsvPathは列マッピング（mapping）を切り替え時にも使い回します。
//   マッピングは設定タブでは編集できない値のため、切り替え時に
//   変える理由がありません。

using System.IO;
using Bui.Products;

namespace Bui.Data;

/// <summary>データ元がCSVのときだけ単価CSVを読みます。パスは実体を作り直さずに切り替えられます。</summary>
public sealed class SwitchingPriceRepository : IPriceRepository, ICsvPathSwitchable
{
    private IPriceRepository _csvPrices;
    private readonly DataSourceSwitch _source;
    private readonly Products.PriceColumnMapping _mapping;

    public SwitchingPriceRepository(IPriceRepository csvPrices, DataSourceSwitch source, Products.PriceColumnMapping mapping)
    {
        _csvPrices = csvPrices ?? throw new ArgumentNullException(nameof(csvPrices));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
    }

    /// <summary>読み込み先を新しいCSVパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    public void SwitchCsvPath(string csvPath) =>
        Volatile.Write(ref _csvPrices, new Csv.CsvPriceRepository(csvPath, _mapping));

    public Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? Volatile.Read(ref _csvPrices).GetStatusAsync(cancellationToken)
            : Task.FromResult(new DataSourceStatus(
                false, 0, null,
                new[] { "SQL接続中は単価を表示しません。単価情報はまだSQL側にありません。設定タブでデータ元を「CSVフォルダ」に切り替えると表示されます。" }));

    public Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery query, CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? Volatile.Read(ref _csvPrices).SearchAsync(query, cancellationToken)
            : Task.FromResult<IReadOnlyList<PriceRecord>>(Array.Empty<PriceRecord>());

    public Task UpsertAsync(PriceRecord record, CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? Volatile.Read(ref _csvPrices).UpsertAsync(record, cancellationToken)
            : throw new InvalidDataException(
                "SQL接続中は単価を更新できません。設定タブでデータ元を「CSVフォルダ」に切り替えてください。");
}
```

注記: `Products.PriceColumnMapping` は既存の `CsvPriceRepository` と同じ型（`Bui.Products` 名前空間）。`using Bui.Products;` を足すよりファイル内で `Products.PriceColumnMapping` と完全修飾したのは、`Bui.Data.Csv.CsvPriceRepository` の完全修飾（`Csv.CsvPriceRepository`）と合わせて、既存の `using` 一覧を壊さないためです。テストファイル側では通常どおり `using Bui.Products;` を使っています。

- [ ] **Step 5: 既存の呼び出し箇所を直す**

`tests_テスト/Bui.Tests/Data_データ/ReadOnlyDataSourceTests_読取専用テスト.cs` の55行目と72行目を直す。

55行目、変更前:
```csharp
        var sut = new SwitchingPriceRepository(csv, ReadOnlySwitch());
```
変更後:
```csharp
        var sut = new SwitchingPriceRepository(csv, ReadOnlySwitch(), PriceColumnMapping.PocDefault);
```

72行目、変更前:
```csharp
        var sut = new SwitchingPriceRepository(csv, DataSourceSwitch.ForCsvFolder(@"C:\BUI_PoCData"));
```
変更後:
```csharp
        var sut = new SwitchingPriceRepository(csv, DataSourceSwitch.ForCsvFolder(@"C:\BUI_PoCData"), PriceColumnMapping.PocDefault);
```

このファイルの先頭に `using Bui.Products;` が無ければ追加する（`PriceColumnMapping` の名前空間）。

- [ ] **Step 6: 全テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（新規2件 + 既存の `ReadOnlyDataSourceTests` を含め全件成功。既存テストを壊していないことを必ず確認する）。

- [ ] **Step 7: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ICsvPathSwitchable_CSVパス切替.cs src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingPriceRepository_単価窓口切替.cs tests_テスト/Bui.Tests/Data_データ/SwitchingPriceRepositoryTests_単価窓口切替テスト.cs tests_テスト/Bui.Tests/Data_データ/ReadOnlyDataSourceTests_読取専用テスト.cs
git commit -m "feat: SwitchingPriceRepositoryに実行時のCSVパス切替を追加する"
```

---

### Task 3: SwitchingAssemblyRepository（構成CSVの窓口切替）

**Files:**
- Create: `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingAssemblyRepository_構成窓口切替.cs`
- Test: `tests_テスト/Bui.Tests/Data_データ/SwitchingAssemblyRepositoryTests_構成窓口切替テスト.cs`

**Interfaces:**
- Consumes: `Bui.Data.ICsvPathSwitchable`（Task 2で作成）
- Produces: `Bui.Data.SwitchingAssemblyRepository`
  コンストラクタ: `SwitchingAssemblyRepository(IAssemblyRepository initial)`
  実装インターフェース: `IAssemblyRepository`, `ICsvPathSwitchable`
  （後続タスク5がこれを組み立て、`RevitDataSourceConnector` へ渡す）

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/SwitchingAssemblyRepositoryTests_構成窓口切替テスト.cs` を新規作成する。
`CsvAssemblyTestFiles` と `AssemblyRepositoryTestFactory` は同じ `Bui.Tests.Data_データ` 名前空間の
`CsvAssemblyRepositoryTests_CSV構成データテスト.cs` に定義済みの `internal` ヘルパーで、
同一アセンブリ内なのでそのまま使える。

```csharp
// このファイルはSwitchingAssemblyRepositoryが、実体を作り直さずに
// 読み込み先の構成CSVのパスだけを差し替えられることを守ります。
// これが壊れると、設定タブで構成CSVのパスを変えても
// 「切り替えて読み直す」を押した後も古いファイルを読み続けます。
using Bui.Data;
using Bui.Data.Csv;

namespace Bui.Tests.Data_データ;

[TestClass]
public class SwitchingAssemblyRepositoryTests_構成窓口切替テスト
{
    [TestMethod]
    public async Task SwitchCsvPath_切替後は新しいCSVの構成を返す()
    {
        using var oldFiles = CsvAssemblyTestFiles.CreateEmpty();
        var oldRepo = new CsvAssemblyRepository(oldFiles.CsvPath);
        await oldRepo.AddAsync(AssemblyRepositoryTestFactory.Create("ASM-000001", ("Finish", 2m, "P-001", "材A")), "FP-OLD", default);

        using var newFiles = CsvAssemblyTestFiles.CreateEmpty();
        var newRepo = new CsvAssemblyRepository(newFiles.CsvPath);
        await newRepo.AddAsync(AssemblyRepositoryTestFactory.Create("ASM-000002", ("Structure", 150m, "P-105", "材B")), "FP-NEW", default);

        var sut = new SwitchingAssemblyRepository(oldRepo);

        sut.SwitchCsvPath(newFiles.CsvPath);
        var oldResult = await sut.FindByFingerprintAsync("FP-OLD", default);
        var newResult = await sut.FindByFingerprintAsync("FP-NEW", default);
        var ids = await sut.GetAllIdsAsync(default);

        Assert.IsNull(oldResult, "切り替え後は古いCSVの内容を持っていないこと。");
        Assert.IsNotNull(newResult);
        Assert.AreEqual("ASM-000002", newResult.AssemblyId);
        CollectionAssert.AreEqual(new[] { "ASM-000002" }, ids.ToArray());
    }

    [TestMethod]
    public async Task AddAsync_ICsvPathSwitchable経由で切り替えた後は新しいCSVへ書く()
    {
        using var oldFiles = CsvAssemblyTestFiles.CreateEmpty();
        using var newFiles = CsvAssemblyTestFiles.CreateEmpty();
        var sut = new SwitchingAssemblyRepository(new CsvAssemblyRepository(oldFiles.CsvPath));
        ICsvPathSwitchable switchable = sut;

        switchable.SwitchCsvPath(newFiles.CsvPath);
        await sut.AddAsync(AssemblyRepositoryTestFactory.Create("ASM-000003", ("Finish", 2m, "P-001", "材A")), "FP-3", default);

        Assert.AreEqual(4, File.ReadLines(newFiles.CsvPath).Count(), "新しいCSVへ書かれていること。");
        Assert.AreEqual(1, File.ReadLines(oldFiles.CsvPath).Count(), "古いCSVは変わっていないこと（見出し行のみ）。");
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~SwitchingAssemblyRepositoryTests" -v minimal`
Expected: ビルドエラー（`SwitchingAssemblyRepository` が存在しない）。

- [ ] **Step 3: 実装を書く**

`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingAssemblyRepository_構成窓口切替.cs` を新規作成する。

```csharp
// このファイルの目的:
//   構成CSV（Assemblies.csv）の読み込み先パスを、実体を作り直さずに
//   切り替えられるようにします。SwitchingPriceRepositoryの構成CSV版です。
//
// なぜ必要か:
//   2026-09-15までは、設定タブで構成CSVのパスを変えても、Revitを
//   再起動するまで反映されませんでした（BuiDockPaneComposition.Create
//   で起動時に1回だけCsvAssemblyRepositoryを作っていたため）。
//   SwitchCsvPathで「切り替えて読み直す」ボタンから即時反映できるように
//   します。
//
//   単価と違い、構成CSVはCSV/SQLの出どころ切替（DataSourceSwitch）の
//   対象ではありません。Assembly_IDの採番はSQL接続中でも常に構成CSVへ
//   書き込むためです。そのためDataSourceSwitchのような「出どころの種類」
//   ではなく、「パスだけを持つ薄い差し替え窓口」として作ります。
//
// 修正時の影響:
//   SwitchCsvPathはUIスレッドから呼ばれる想定です。差し替えは参照1つの
//   入れ替えに留め、ここでI/Oを行わないでください（DataSourceSwitchと
//   同じ制約）。

using Bui.Assemblies;
using Bui.Data.Csv;

namespace Bui.Data;

/// <summary>構成CSVの読み込み先を、実体を作り直さずに切り替えられる窓口です。</summary>
public sealed class SwitchingAssemblyRepository : IAssemblyRepository, ICsvPathSwitchable
{
    private IAssemblyRepository _current;

    public SwitchingAssemblyRepository(IAssemblyRepository initial) =>
        _current = initial ?? throw new ArgumentNullException(nameof(initial));

    /// <summary>読み込み先を新しいCSVパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    public void SwitchCsvPath(string csvPath) =>
        Volatile.Write(ref _current, new CsvAssemblyRepository(csvPath));

    public Task<AssemblyRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).FindByFingerprintAsync(fingerprint, cancellationToken);

    public Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).GetAllIdsAsync(cancellationToken);

    public Task AddAsync(AssemblyRecord record, string fingerprint, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).AddAsync(record, fingerprint, cancellationToken);

    public Task RemoveAsync(string assemblyId, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).RemoveAsync(assemblyId, cancellationToken);
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~SwitchingAssemblyRepositoryTests" -v minimal`
Expected: PASS（2件成功）。

- [ ] **Step 5: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingAssemblyRepository_構成窓口切替.cs tests_テスト/Bui.Tests/Data_データ/SwitchingAssemblyRepositoryTests_構成窓口切替テスト.cs
git commit -m "feat: SwitchingAssemblyRepositoryを追加し、構成CSVパスの実行時切替に対応する"
```

---

### Task 4: IDataSourceConnector拡張とSwitchDataSourceAsyncの配線

**Files:**
- Modify: `src_ソース/Bui.Shared_共有ロジック/UI_画面/IDataSourceConnector_データ元接続.cs`
- Modify: `src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs:928-958`
- Modify: `tests_テスト/Bui.Tests/UI_画面/DataSourceViewModelTests_データ元切替画面テスト.cs`（`FakeConnector` に新2メソッド追加＋新規テスト）

**Interfaces:**
- Consumes: なし（インターフェースそのものを拡張する）
- Produces: `IDataSourceConnector.UsePriceCsv(string configuredPath) : void`、
  `IDataSourceConnector.UseAssemblyCsv(string configuredPath) : void`
  （後続タスク5の `RevitDataSourceConnector` がこの2メソッドを実装する）

- [ ] **Step 1: IDataSourceConnectorへメソッドを追加する**

`src_ソース/Bui.Shared_共有ロジック/UI_画面/IDataSourceConnector_データ元接続.cs` の
`/// <summary>データ元をCSVフォルダへ戻します。</summary>` の行の直後に追加する。

```csharp
    /// <summary>データ元をCSVフォルダへ戻します。</summary>
    void UseCsvFolder(string folderPath);

    /// <summary>単価CSVの読み込み先を切り替えます。空欄ならBUI管理下の既定ファイルを使います。</summary>
    void UsePriceCsv(string configuredPath);

    /// <summary>構成CSVの読み込み先を切り替えます。空欄ならBUI管理下の既定ファイルを使います。</summary>
    void UseAssemblyCsv(string configuredPath);
```

この時点でBui.Shared全体がビルドできなくなる（`RevitDataSourceConnector` と `FakeConnector` が
新メソッドを実装していないため）。次のステップで両方直す。`RevitDataSourceConnector`
（Bui.Addin側）はTask 5で直すため、このタスクでは触らない。Bui.Sharedプロジェクト単体、
およびBui.Testsは、`FakeConnector` を直せばビルドが通るようになる。

- [ ] **Step 2: 失敗するテストを書く**

`tests_テスト/Bui.Tests/UI_画面/DataSourceViewModelTests_データ元切替画面テスト.cs` の
末尾の `FakeConnector` クラス（`public void UseCsvFolder(string folderPath)` の直後）に
次の3つを追加する。

```csharp
        public int PriceSwitches { get; private set; }
        public int AssemblySwitches { get; private set; }
        public string? LastPriceCsvPath { get; private set; }
        public string? LastAssemblyCsvPath { get; private set; }

        public void UsePriceCsv(string configuredPath)
        {
            PriceSwitches++;
            LastPriceCsvPath = configuredPath;
        }

        public void UseAssemblyCsv(string configuredPath)
        {
            AssemblySwitches++;
            LastAssemblyCsvPath = configuredPath;
        }
```

同じファイルの `[TestClass]` 内（既存のテストメソッドの末尾、`起動直後からいま読んでいるデータ元の説明を表示する`
などの近く）に、次の2つのテストメソッドを追加する。

```csharp
    [TestMethod]
    public async Task 切り替えて読み直すと入力欄の単価構成CSVパスで読み直す()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 3件"));
        var sut = DataSourceFactory.Create(connector);
        sut.PriceCsvPath = @"C:\NewData\Prices.csv";
        sut.AssemblyCsvPath = @"C:\NewData\Assemblies.csv";

        await sut.SwitchDataSourceAsync();

        Assert.AreEqual(1, connector.PriceSwitches);
        Assert.AreEqual(1, connector.AssemblySwitches);
        Assert.AreEqual(@"C:\NewData\Prices.csv", connector.LastPriceCsvPath);
        Assert.AreEqual(@"C:\NewData\Assemblies.csv", connector.LastAssemblyCsvPath);
    }

    [TestMethod]
    public async Task SQLへ切り替えるときも単価構成CSVパスを読み直す()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 3件"));
        var sut = DataSourceFactory.Create(connector);
        sut.PriceCsvPath = @"C:\NewData\Prices.csv";
        sut.AssemblyCsvPath = @"C:\NewData\Assemblies.csv";
        sut.UseSqlServer = true;
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.SwitchDataSourceAsync();

        Assert.AreEqual(1, connector.PriceSwitches);
        Assert.AreEqual(1, connector.AssemblySwitches);
    }

    [TestMethod]
    public async Task 切り替え完了メッセージに再起動の案内が出ない()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 3件"));
        var sut = DataSourceFactory.Create(connector);
        sut.PriceCsvPath = @"C:\NewData\Prices.csv";

        await sut.SwitchDataSourceAsync();

        StringAssert.DoesNotMatch(sut.SettingsStatusMessage, new System.Text.RegularExpressions.Regex("再起動"));
    }
```

- [ ] **Step 3: テストが失敗することを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter "FullyQualifiedName~DataSourceViewModelTests" -v minimal`
Expected: 新規3件が FAIL（`SwitchDataSourceAsync` がまだ `UsePriceCsv`/`UseAssemblyCsv` を呼んでいない、
完了メッセージにまだ「再起動」の文言が残っている）。既存テストはこの時点でも通ること。

- [ ] **Step 4: SwitchDataSourceAsyncを直す**

`src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs` の
`SwitchDataSourceAsync` メソッド（928行目付近）を次の内容に置き換える。

```csharp
    /// <summary>選んでいるデータ元へ実際に切り替え、読み直します。</summary>
    public async Task SwitchDataSourceAsync(CancellationToken cancellationToken = default)
    {
        if (_dataSourceConnector is null)
        {
            await SetSettingsStatusAsync("データ元を切り替える準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        // 単価CSV・構成CSVは、選んでいるデータ元（CSV/SQL）に関わらず、
        // その時点の入力欄の値で読み直します。構成CSVはSQL接続中でも
        // Assembly_IDの書き込み先として使われるため、製品データの
        // 切替が成功したかどうかに関係なく実行します。
        _dataSourceConnector.UsePriceCsv(PriceCsvPath ?? string.Empty);
        _dataSourceConnector.UseAssemblyCsv(AssemblyCsvPath ?? string.Empty);

        if (!UseSqlServer)
        {
            // 編集欄（DataFolderPath）は「保存」を押すまで設定へ反映されません。
            // ここで切り替える先も編集欄の値です。
            var adoptedFolder = DataFolderPath ?? string.Empty;
            _dataSourceConnector.UseCsvFolder(adoptedFolder);
            await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
            // 接続状態・製品カードを新しいデータ元の内容に更新します。ここを呼ばないと、
            // 要素をクリックし直すまで概要タブと接続状態が前のデータ元のまま残ります。
            await RefreshSelectionAsync(LastSnapshot, cancellationToken).ConfigureAwait(false);
            var message = $"CSVフォルダに切り替えました（{adoptedFolder}）。{DataSourceDescription}";
            await SetSettingsStatusAsync(message, cancellationToken).ConfigureAwait(false);
            await RefreshCandidatesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await RunSqlAsync(switchOver: true, cancellationToken).ConfigureAwait(false);
        if (result is null) return;

        if (!result.IsSuccess)
        {
            // 失敗したら切り替えません。中途半端な状態を作らないためです。
            await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
            await SetSettingsStatusAsync(
                "データ元を切り替えませんでした。" + Environment.NewLine + result.Error!.UserMessage,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
        // 接続状態・製品カードを新しいデータ元の内容に更新します（CSV分岐と同じ理由）。
        await RefreshSelectionAsync(LastSnapshot, cancellationToken).ConfigureAwait(false);
        await SetSettingsStatusAsync(
            $"SQL Serverに切り替えました。{result.Value!}", cancellationToken).ConfigureAwait(false);
        await RefreshCandidatesAsync(cancellationToken).ConfigureAwait(false);
    }
```

変更点のまとめ: メソッド冒頭（null チェックの直後）に `UsePriceCsv`/`UseAssemblyCsv` 呼び出しを追加。
CSV分岐から `savedFolder` の比較と、それに伴う「単価CSVと構成CSVは保存済みの設定のままです。
反映するには「保存」を押してからRevitを再起動してください。」という文言を削除。それ以外
（SQL分岐、エラー処理、既存のコメントの大半）は変更しない。

- [ ] **Step 5: 全テストが通ることを確認する**

Run: `.\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj -v minimal`
Expected: PASS（新規3件 + `DataSourceViewModelTests` の既存分 + Bui.Tests全体が成功）。

- [ ] **Step 6: コミットする**

```bash
git add src_ソース/Bui.Shared_共有ロジック/UI_画面/IDataSourceConnector_データ元接続.cs src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs tests_テスト/Bui.Tests/UI_画面/DataSourceViewModelTests_データ元切替画面テスト.cs
git commit -m "feat: 切り替えて読み直すで単価CSV・構成CSVも読み直す"
```

---

### Task 5: Bui.Addin配線（RevitDataSourceConnectorとComposition）

**このタスクはBui.Addin（Revit依存）に属し、私物PCではビルドできません
（[[bui-unverifiable-layer-needs-tested-limits]]、Revit APIがこのPCに無いため）。
判断ロジックは一切持たせず、Task 1〜4で作ったテスト済みの部品を配線するだけに
留めます。会社PCでのビルド確認が必須です。**

**Files:**
- Modify: `src_ソース/Bui.Addin/Data_データ/RevitDataSourceConnector_データ元接続.cs`
- Modify: `src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`

**Interfaces:**
- Consumes:
  - `Bui.Configuration.CsvPathResolver.Resolve(string?, string, string) : string`（Task 1）
  - `Bui.Data.ICsvPathSwitchable.SwitchCsvPath(string) : void`（Task 2）
  - `Bui.Data.SwitchingPriceRepository(IPriceRepository, DataSourceSwitch, PriceColumnMapping)`（Task 2）
  - `Bui.Data.SwitchingAssemblyRepository(IAssemblyRepository)`（Task 3）
  - `Bui.UI.IDataSourceConnector.UsePriceCsv(string)` / `.UseAssemblyCsv(string)`（Task 4）
- Produces: なし（最終タスク）

- [ ] **Step 1: RevitDataSourceConnectorを拡張する**

`src_ソース/Bui.Addin/Data_データ/RevitDataSourceConnector_データ元接続.cs` を次のとおり変更する。

コンストラクタとフィールドの変更前:
```csharp
internal sealed class RevitDataSourceConnector : IDataSourceConnector
{
    private readonly DataSourceSwitch _switch;

    public RevitDataSourceConnector(DataSourceSwitch dataSourceSwitch) =>
        _switch = dataSourceSwitch ?? throw new ArgumentNullException(nameof(dataSourceSwitch));
```

変更後:
```csharp
internal sealed class RevitDataSourceConnector : IDataSourceConnector
{
    private readonly DataSourceSwitch _switch;
    private readonly ICsvPathSwitchable _priceRepository;
    private readonly ICsvPathSwitchable _assemblyRepository;
    private readonly string _dataRoot;

    public RevitDataSourceConnector(
        DataSourceSwitch dataSourceSwitch,
        ICsvPathSwitchable priceRepository,
        ICsvPathSwitchable assemblyRepository,
        string dataRoot)
    {
        _switch = dataSourceSwitch ?? throw new ArgumentNullException(nameof(dataSourceSwitch));
        _priceRepository = priceRepository ?? throw new ArgumentNullException(nameof(priceRepository));
        _assemblyRepository = assemblyRepository ?? throw new ArgumentNullException(nameof(assemblyRepository));
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
    }
```

`public void UseCsvFolder(string folderPath) => _switch.SwitchTo(new CsvTableSource(folderPath));`
の行の直後に追加する。

```csharp
    public void UsePriceCsv(string configuredPath) =>
        _priceRepository.SwitchCsvPath(Bui.Configuration.CsvPathResolver.Resolve(configuredPath, _dataRoot, "Prices.csv"));

    public void UseAssemblyCsv(string configuredPath) =>
        _assemblyRepository.SwitchCsvPath(Bui.Configuration.CsvPathResolver.Resolve(configuredPath, _dataRoot, "Assemblies.csv"));
```

ファイル先頭の `using` 一覧に `Bui.Data;` が既にあるため `ICsvPathSwitchable` はそのまま使える
（`Bui.Data` 名前空間）。`Bui.Configuration.CsvPathResolver` は完全修飾で呼ぶ
（このファイルには `Bui.Configuration` の `using` が無いため）。

- [ ] **Step 2: BuiDockPaneComposition.Create()を配線する**

`src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs` を次のとおり変更する。

`pricePath` と `assemblyPath` の解決（49〜50行目付近）、変更前:
```csharp
        var pricePath = ResolveCsvPath(loaded.Settings.Data.PriceCsvPath, dataRoot, "Prices.csv");
        var assemblyPath = ResolveCsvPath(loaded.Settings.Data.AssemblyCsvPath, dataRoot, "Assemblies.csv");
```
変更後:
```csharp
        var pricePath = Bui.Configuration.CsvPathResolver.Resolve(loaded.Settings.Data.PriceCsvPath, dataRoot, "Prices.csv");
        var assemblyPath = Bui.Configuration.CsvPathResolver.Resolve(loaded.Settings.Data.AssemblyCsvPath, dataRoot, "Assemblies.csv");
```

`prices` と `assemblies` の組み立て（55, 59行目付近）、変更前:
```csharp
        var prices = new SwitchingPriceRepository(
            new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns), dataSource);
        var assemblies = new CsvAssemblyRepository(assemblyPath);
```
変更後:
```csharp
        var prices = new SwitchingPriceRepository(
            new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns), dataSource, loaded.Settings.Data.PriceColumns);
        var assemblies = new SwitchingAssemblyRepository(new CsvAssemblyRepository(assemblyPath));
```

`dataSourceConnector` の組み立て（105行目付近、`return new BuiDockPaneViewModel(...)` の中）、
変更前:
```csharp
            dataSourceConnector: new RevitDataSourceConnector(dataSource),
```
変更後:
```csharp
            dataSourceConnector: new RevitDataSourceConnector(dataSource, prices, assemblies, dataRoot),
```

ファイル末尾の private static メソッド `ResolveCsvPath`（165〜168行目付近）を削除する
（`CsvPathResolver` に置き換わったため、重複コードを残さない）。

```csharp
    private static string ResolveCsvPath(string configuredPath, string dataRoot, string fallbackFileName) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(dataRoot, fallbackFileName)
            : configuredPath;
```

- [ ] **Step 3: 目視で確認する**

このPCではBui.Addinをビルドできない。次を目視で確認する。

- `RevitDataSourceConnector` のコンストラクタ引数の順序（`DataSourceSwitch, ICsvPathSwitchable, ICsvPathSwitchable, string`）と、
  `BuiDockPaneComposition.Create()` の呼び出し側（`dataSource, prices, assemblies, dataRoot`）が一致していること。
- `prices`（`SwitchingPriceRepository`）と `assemblies`（`SwitchingAssemblyRepository`）は、
  どちらも `ICsvPathSwitchable` を実装しているため、そのまま渡せること（Task 2, 3で実装済み）。
- `ResolveCsvPath` の呼び出し元が1つも残っていないこと（`grep -rn "ResolveCsvPath" src_ソース/Bui.Addin` で確認）。

- [ ] **Step 4: コミットする**

```bash
git add src_ソース/Bui.Addin/Data_データ/RevitDataSourceConnector_データ元接続.cs src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs
git commit -m "feat: Bui.Addin側を配線し、単価CSV・構成CSVの即時読み直しを有効化する"
```

- [ ] **Step 5: 会社PCでの確認を依頼する**

次の内容を会社PCの担当者へ伝える（このタスクの完了はそちらでのビルド成功をもって確定する）。

1. `git pull` してからVisual Studioで「ソリューションのリビルド」（Debug構成で可）。
2. エラーなくビルドが通ること。
3. Revitを起動し、BUIパネルの設定タブで単価CSV・構成CSVのパスを別のフォルダへ変更し、
   「切り替えて読み直す」を押す。再起動せずに、次のことを確認する。
   - 完了メッセージに「再起動」という文言が出ないこと。
   - 変更後のフォルダの単価CSV・構成CSVの内容が、概要タブ・ID付与操作に反映されていること
     （再起動が不要になっていること）。

---

## 自己レビュー（実施済み）

- **スペック網羅性:** 設計スペックの8コンポーネント（`CsvPathResolver`, `ICsvPathSwitchable`,
  `SwitchingPriceRepository`, `SwitchingAssemblyRepository`, `IDataSourceConnector`,
  `RevitDataSourceConnector`, `BuiDockPaneComposition.Create()`, `SwitchDataSourceAsync`）
  すべてにタスクが対応している（Task 1〜5）。テストセクションの4項目もすべてタスクに含まれる。
- **プレースホルダー確認:** 「TBD」「後で実装」等の記載なし。すべてのコード片は完全な内容。
- **型の一貫性:** `ICsvPathSwitchable.SwitchCsvPath(string csvPath)` の名前・シグネチャは
  Task 2, 3, 5で一致。`SwitchingPriceRepository` の3引数コンストラクタはTask 2で定義し、
  Task 5で同じ順序（`csvPrices, source, mapping`）で呼んでいる。
  `RevitDataSourceConnector` の4引数コンストラクタの順序も、定義（Task 5 Step 1）と
  呼び出し（Task 5 Step 2）で一致している。
- **既存テストへの影響:** `SwitchingPriceRepository` のコンストラクタ変更（破壊的）が
  影響する既存の呼び出し箇所は2つのみ（`BuiDockPaneComposition.Create()` はTask 5で、
  `ReadOnlyDataSourceTests_読取専用テスト.cs` の2箇所はTask 2で）修正済み。他に
  `new SwitchingPriceRepository` を呼ぶ箇所が無いことは事前にリポジトリ全体を検索して確認した。
