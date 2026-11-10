# SQL Serverをデータ元にする 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BUIのデータ元をCSVフォルダとSQL Server（`localhost\sqlexpress` / `GoldStandardDB` / Windows認証）から選べるようにし、SQL接続中はCSVを読みに行かないようにする。

**Architecture:** 11枚の表を生の行のまま返す読み込み口 `IGoldStandardTableSource` を作り、CSV版とSQL版の2実装を用意する。`DataSourceSwitch` がいまの読み込み口を1つだけ持ち、既存のリポジトリはそこへ毎回聞く。平坦化から表示までの経路は1本のまま変えない。

**Tech Stack:** .NET 8 / C# 12 / WPF / MSTest / Microsoft.Data.SqlClient

## Global Constraints

- 設計書: `docs/superpowers/specs/2026-09-08-sqlserver-data-source-design.md`
- `Bui.Shared_共有ロジック` はRevit非依存を保つ。Revit/WPFの型を持ち込まない
- テストは `.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo` で実行し、**終了コード0**で判定する。`tail` などへパイプしない
- 利用者向けの日本語文言は、必ず**理由**と**次にどうすればよいか**を含める。件数や汎用文だけで済ませない
- SQL接続時の書き込みは実装しない（読み取り専用）。書き込み経路は書く前に断る
- 接続文字列は画面・ログへ出さない。サーバー名とDB名（利用者の入力値）は出してよい
- `Identifiers_固定GUID.cs` のGUIDは触らない
- Push前に生成物・一時ファイルを削除する

---

### Task 1: 表一式を出どころから切り離す

`GoldStandardCsvSet` は「CSVを読む」と「11枚の表を持つ」の2役を兼ねている。
後者だけを残し、読む側を差し替え可能にする。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardTableSource_表読込口.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardCsvSet_表一括読込.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardTableSourceTests_表読込口テスト.cs`

**Interfaces:**
- Consumes: `GoldStandardTables.All`、`GoldStandardTables.Products`、`CsvDocumentCodec.Read(string)`
- Produces:
  - `internal interface IGoldStandardTableSource { GoldStandardTableSet Load(); string Describe(); bool SupportsWrite { get; } bool IsCsvFolder { get; } long Version { get; } string DataFolderPath { get; } }`
  - `internal sealed class CsvTableSource : IGoldStandardTableSource` — コンストラクタ `CsvTableSource(string? folderPath)`
  - `internal sealed class GoldStandardTableSet` — コンストラクタ `GoldStandardTableSet(IReadOnlyDictionary<string, GoldStandardTable> tables, IReadOnlyList<string> problems)`、メンバー `Problems` / `HasProducts` / `Table(string)` / `Rows(string)`

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/GoldStandardTableSourceTests_表読込口テスト.cs`

```csharp
// このファイルの目的:
//   表の読み込み口が、出どころに関わらず同じ形の表一式を返すことを守ります。
//   ここが崩れると「CSVだと出るのにSQLだと出ない」という差が生まれます。
using System.Text;
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data_データ;

[TestClass]
public class GoldStandardTableSourceTests_表読込口テスト
{
    [TestMethod]
    public void CsvTableSource_フォルダのCSVを表一式として読む()
    {
        using var folder = TableSourceTestFolder.Create();
        folder.WriteTable("Products", "product_id,product_name", "P-001,タイガーボード");

        var set = new CsvTableSource(folder.Path).Load();

        Assert.IsTrue(set.HasProducts);
        Assert.AreEqual(1, set.Rows("Products").Count);
        Assert.AreEqual("タイガーボード", set.Rows("Products")[0]["product_name"]);
    }

    [TestMethod]
    public void CsvTableSource_フォルダが無ければ理由を返し例外にしない()
    {
        var set = new CsvTableSource(@"C:\存在しないフォルダ_BUI_TEST").Load();

        Assert.IsFalse(set.HasProducts);
        Assert.IsTrue(set.Problems.Any(p => p.Contains("データフォルダが見つかりません")));
    }

    [TestMethod]
    public void CsvTableSource_表が1枚欠けても空として続ける()
    {
        using var folder = TableSourceTestFolder.Create();
        folder.WriteTable("Products", "product_id", "P-001");

        var set = new CsvTableSource(folder.Path).Load();

        Assert.IsTrue(set.HasProducts);
        Assert.AreEqual(0, set.Rows("Manufacturers").Count);
    }

    [TestMethod]
    public void CsvTableSource_Versionはファイルを直すと変わる()
    {
        using var folder = TableSourceTestFolder.Create();
        folder.WriteTable("Products", "product_id", "P-001");
        var source = new CsvTableSource(folder.Path);
        var before = source.Version;

        Thread.Sleep(1100);
        folder.WriteTable("Products", "product_id", "P-002");

        Assert.AreNotEqual(before, source.Version);
    }

    [TestMethod]
    public void CsvTableSource_書き込みできる出どころである()
    {
        using var folder = TableSourceTestFolder.Create();

        var source = new CsvTableSource(folder.Path);

        Assert.IsTrue(source.SupportsWrite);
        Assert.IsTrue(source.IsCsvFolder);
        Assert.AreEqual(folder.Path, source.DataFolderPath);
        StringAssert.Contains(source.Describe(), "CSV");
    }
}

internal sealed class TableSourceTestFolder : IDisposable
{
    private TableSourceTestFolder(string path) => Path = path;

    public string Path { get; }

    public static TableSourceTestFolder Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BuiTableSource", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TableSourceTestFolder(path);
    }

    public void WriteTable(string table, string header, params string[] rows)
    {
        var text = string.Join("\r\n", new[] { header }.Concat(rows)) + "\r\n";
        File.WriteAllText(System.IO.Path.Combine(Path, table + ".csv"), text, new UTF8Encoding(true));
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: `CsvTableSource` が存在せずコンパイルエラー。

- [ ] **Step 3: 読み込み口を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardTableSource_表読込口.cs`

```csharp
// このファイルの目的:
//   11枚の表を「生の行のまま」返す口を1つに決めます。
//   CSVフォルダから読むか、SQL Serverから読むかは、この口の実装差だけにします。
//
// なぜこの形なのか:
//   結合・整形はGoldStandardFlattener以降が担っています。
//   出どころが違っても同じ形の表を渡せば、その先の経路は1本のままになり、
//   「SQLだと表示が違う」が構造的に起きません。
//
// 修正時の影響:
//   値は必ず文字列で持ってください。CSVの空欄とSQLのNULLを同じ「空文字」に
//   揃えることで、平坦化以降が出どころを気にせず済みます。

namespace Bui.Data.GoldStandard;

/// <summary>11枚の表を読む口です。出どころごとに実装します。</summary>
internal interface IGoldStandardTableSource
{
    /// <summary>表一式を読みます。例外は投げず、問題は Problems に集めます。</summary>
    GoldStandardTableSet Load();

    /// <summary>画面に出す出どころの説明です。接続文字列は含めません。</summary>
    string Describe();

    /// <summary>この出どころへ書き込めるかどうかです。SQLは読み取り専用なのでfalseです。</summary>
    bool SupportsWrite { get; }

    /// <summary>
    /// ローカルのCSVフォルダを読んでいるかどうかです。
    /// 単価CSVを読みに行ってよいかの判定に使います。SupportsWriteとは別に持ちます。
    /// 「書けるか」と「CSVを読んでいるか」は今は一致していますが、意味が違うためです。
    /// </summary>
    bool IsCsvFolder { get; }

    /// <summary>キャッシュの作り直し判定に使う版番号です。変わったら読み直します。</summary>
    long Version { get; }

    /// <summary>
    /// 製品画像の置き場です。CSVでは表の置き場でもあります。
    /// SQL接続中も画像はローカルのファイルとして扱うため、この値が必要です。
    /// </summary>
    string DataFolderPath { get; }
}

/// <summary>データフォルダのCSVから表一式を読みます。</summary>
internal sealed class CsvTableSource : IGoldStandardTableSource
{
    private readonly string _folderPath;

    public CsvTableSource(string? folderPath) => _folderPath = folderPath ?? string.Empty;

    public string DataFolderPath => _folderPath;

    public bool SupportsWrite => true;

    public bool IsCsvFolder => true;

    /// <summary>フォルダ内CSVの最終更新時刻の最大値です。ファイルを直すと変わります。</summary>
    public long Version => GoldStandardCsvReader.FolderTimestampUtc(_folderPath).Ticks;

    public GoldStandardTableSet Load() => GoldStandardCsvReader.Load(_folderPath);

    public string Describe() =>
        _folderPath.Length == 0 ? "CSVフォルダ（未設定）" : "CSVフォルダ: " + _folderPath;
}
```

- [ ] **Step 4: 表一式を出どころから切り離す**

`GoldStandardCsvSet_表一括読込.cs` を次の内容へ置き換える。
クラス名を `GoldStandardTableSet`（入れ物）と `GoldStandardCsvReader`（CSVを読む処理）へ分ける。
`FolderPath` / `TimestampUtc` プロパティは入れ物から外す。出どころの都合だからである。

```csharp
// このファイルの目的:
//   11枚の表の中身を、生の行のまま保持します。
//   結合や変換はここで行いません（GoldStandardFlattenerの仕事です）。
//   読む処理はGoldStandardCsvReaderに分けています。出どころがCSVだけではなくなったためです。
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

/// <summary>11枚の表一式です。出どころは問いません。</summary>
internal sealed class GoldStandardTableSet
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> NoRows =
        Array.Empty<IReadOnlyDictionary<string, string>>();

    private readonly IReadOnlyDictionary<string, GoldStandardTable> _tables;

    public GoldStandardTableSet(IReadOnlyDictionary<string, GoldStandardTable> tables, IReadOnlyList<string> problems)
    {
        _tables = tables;
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>Productsが読めたかどうかです。falseなら候補は0件になります。</summary>
    public bool HasProducts => _tables.ContainsKey(GoldStandardTables.Products);

    public GoldStandardTable? Table(string table) =>
        _tables.TryGetValue(table, out var value) ? value : null;

    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string table) =>
        _tables.TryGetValue(table, out var value) ? value.Rows : NoRows;
}

/// <summary>データフォルダのCSVを表一式として読みます。</summary>
internal static class GoldStandardCsvReader
{
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
    public static GoldStandardTableSet Load(string? folderPath)
    {
        var tables = new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            // 例外本文はパスを含むため画面に出せません。原因が分かる案内だけを返します。
            problems.Add("データフォルダが見つかりません。設定タブのパスを確認してください。");
            return new GoldStandardTableSet(tables, problems);
        }

        foreach (var table in GoldStandardTables.All)
        {
            var path = Path.Combine(folderPath, table + ".csv");
            if (!File.Exists(path))
            {
                problems.Add($"{table}.csv がありません。この表は空として扱います。");
                continue;
            }

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

        return new GoldStandardTableSet(tables, problems);
    }
}
```

- [ ] **Step 5: 既存の呼び出し元を新しい名前へ合わせる**

`GoldStandardCsvSet` を使っている箇所をすべて置き換える。

```bash
grep -rn "GoldStandardCsvSet" --include=*.cs "BUI_Addin_CSharpアドイン"
```

- `GoldStandardProductRepository_製品データ取得.cs`: `GoldStandardCsvSet.Load(_folderPath)` → `new CsvTableSource(_folderPath).Load()`、型 `GoldStandardCsvSet` → `GoldStandardTableSet`、`GoldStandardCsvSet.FolderTimestampUtc(_folderPath)` → `GoldStandardCsvReader.FolderTimestampUtc(_folderPath)`
- `GoldStandardDetailSource_製品詳細取得.cs`: 同じ置き換え
- `set.FolderPath` を使っている箇所があれば `_folderPath` へ置き換える

この段階では動作は変わらない。名前だけの整理である。

- [ ] **Step 6: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 既存テストも含めて全件合格（終了コード0）。

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "refactor: separate the table set from the CSV reader"
```

---

### Task 2: DataSourceSwitch でいまの出どころを1つに決める

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/DataSourceSwitch_データ元切替.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardProductRepository_製品データ取得.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardDetailSource_製品詳細取得.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/DataSourceSwitchTests_データ元切替テスト.cs`

**Interfaces:**
- Consumes: Task 1 の `IGoldStandardTableSource` / `GoldStandardTableSet` / `CsvTableSource`
- Produces:
  - `public sealed class DataSourceSwitch` — コンストラクタ `internal DataSourceSwitch(IGoldStandardTableSource initial)`、`internal IGoldStandardTableSource Current { get; }`、`internal void SwitchTo(IGoldStandardTableSource next)`、`public string Describe()`、`public bool SupportsWrite { get; }`
  - `public static DataSourceSwitch DataSourceSwitch.ForCsvFolder(string? folderPath)`
  - `GoldStandardProductRepository(DataSourceSwitch source, CsvFileWriter? writer = null)`
  - `GoldStandardDetailSource(DataSourceSwitch source)`

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/DataSourceSwitchTests_データ元切替テスト.cs`

```csharp
// このファイルの目的:
//   データ元の切り替えが、使わない側に一切触れないことを守ります。
//   「SQLに切り替えたのにCSVを読みに行っていた」を防ぐための唯一の砦です。
using Bui.Data;
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data_データ;

[TestClass]
public class DataSourceSwitchTests_データ元切替テスト
{
    [TestMethod]
    public void 切り替えると次の読み込みは新しい出どころを使う()
    {
        var first = new CountingTableSource("A");
        var second = new CountingTableSource("B");
        var sut = TestSwitch.Create(first);

        sut.Current.Load();
        TestSwitch.SwitchTo(sut, second);
        sut.Current.Load();

        Assert.AreEqual(1, first.LoadCalls);
        Assert.AreEqual(1, second.LoadCalls);
    }

    [TestMethod]
    public void 切り替えた後は古い出どころに一切触れない()
    {
        var csv = new CountingTableSource("CSV");
        var sql = new CountingTableSource("SQL");
        var sut = TestSwitch.Create(csv);
        TestSwitch.SwitchTo(sut, sql);
        var before = csv.LoadCalls;

        sut.Current.Load();
        sut.Current.Load();
        _ = sut.Describe();
        _ = sut.SupportsWrite;

        Assert.AreEqual(before, csv.LoadCalls, "古い出どころを読んではいけません。");
        Assert.AreEqual(2, sql.LoadCalls);
    }

    [TestMethod]
    public void Describeといまの書き込み可否は現在の出どころのものを返す()
    {
        var sut = TestSwitch.Create(new CountingTableSource("CSV", supportsWrite: true));

        Assert.IsTrue(sut.SupportsWrite);
        StringAssert.Contains(sut.Describe(), "CSV");

        TestSwitch.SwitchTo(sut, new CountingTableSource("SQL", supportsWrite: false));

        Assert.IsFalse(sut.SupportsWrite);
        StringAssert.Contains(sut.Describe(), "SQL");
    }

    [TestMethod]
    public void ForCsvFolderはCSVの出どころを作る()
    {
        var sut = DataSourceSwitch.ForCsvFolder(@"C:\BUI_PoCData");

        Assert.IsTrue(sut.SupportsWrite);
        StringAssert.Contains(sut.Describe(), "CSV");
    }

    /// <summary>読まれた回数を数える出どころです。</summary>
    private sealed class CountingTableSource : IGoldStandardTableSource
    {
        private readonly string _name;

        public CountingTableSource(string name, bool supportsWrite = true)
        {
            _name = name;
            SupportsWrite = supportsWrite;
        }

        public int LoadCalls { get; private set; }
        public bool SupportsWrite { get; }
        public bool IsCsvFolder => SupportsWrite;
        public long Version => LoadCalls;
        public string DataFolderPath => string.Empty;

        public GoldStandardTableSet Load()
        {
            LoadCalls++;
            return new GoldStandardTableSet(
                new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal),
                Array.Empty<string>());
        }

        public string Describe() => _name;
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: `DataSourceSwitch` と `TestSwitch` が無くコンパイルエラー。

- [ ] **Step 3: DataSourceSwitch を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/DataSourceSwitch_データ元切替.cs`

```csharp
// このファイルの目的:
//   いま使っているデータの出どころを1か所だけで持ちます。
//
// なぜ1か所なのか:
//   出どころの判断が各リポジトリに散ると、「SQLに切り替えたのに
//   ここだけCSVを読んでいた」が必ず起きます。判断はここだけにします。
//
// なぜリポジトリを作り直さないのか:
//   画面が持っているインターフェイスの実体を差し替えると、
//   検索結果・層の表・保留中のプレビューが消えます。
//   実体は据え置き、中身の出どころだけを入れ替えます。
//
// 修正時の影響:
//   SwitchToはUIスレッドから呼ばれます。差し替えは参照1つの入れ替えに留め、
//   ここでI/Oを行わないでください。

using Bui.Data.GoldStandard;

namespace Bui.Data;

/// <summary>いま使っているデータの出どころです。</summary>
public sealed class DataSourceSwitch
{
    private IGoldStandardTableSource _current;

    internal DataSourceSwitch(IGoldStandardTableSource initial) =>
        _current = initial ?? throw new ArgumentNullException(nameof(initial));

    /// <summary>CSVフォルダを出どころにした切替器を作ります。</summary>
    public static DataSourceSwitch ForCsvFolder(string? folderPath) =>
        new(new CsvTableSource(folderPath));

    internal IGoldStandardTableSource Current => Volatile.Read(ref _current);

    /// <summary>出どころを差し替えます。以降の読み込みは新しい方だけを使います。</summary>
    internal void SwitchTo(IGoldStandardTableSource next) =>
        Volatile.Write(ref _current, next ?? throw new ArgumentNullException(nameof(next)));

    /// <summary>画面に出す出どころの説明です。</summary>
    public string Describe() => Current.Describe();

    /// <summary>いまの出どころへ書き込めるかどうかです。</summary>
    public bool SupportsWrite => Current.SupportsWrite;

    /// <summary>いまローカルのCSVフォルダを読んでいるかどうかです。単価CSVの可否に使います。</summary>
    public bool IsCsvFolder => Current.IsCsvFolder;

    /// <summary>画像ファイルの置き場です。</summary>
    public string DataFolderPath => Current.DataFolderPath;
}
```

- [ ] **Step 4: テスト用の入口を作る**

`DataSourceSwitch` の `Current` と `SwitchTo` は `internal` である。
テストは同じアセンブリに入るため直接呼べるが、テスト側の意図を明確にするため
小さなヘルパーをテストファイルの末尾へ足す。

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/DataSourceSwitchTests_データ元切替テスト.cs` の
名前空間の末尾（クラスの外）へ追記する。

```csharp
/// <summary>internalな切替操作を、テストの意図が読める名前で包みます。</summary>
internal static class TestSwitch
{
    public static DataSourceSwitch Create(IGoldStandardTableSource initial) => new(initial);

    public static void SwitchTo(DataSourceSwitch target, IGoldStandardTableSource next) => target.SwitchTo(next);
}
```

- [ ] **Step 5: リポジトリを切替器へ付け替える**

`GoldStandardProductRepository_製品データ取得.cs`

```csharp
    private readonly DataSourceSwitch _source;
    private readonly CsvFileWriter _writer;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private Snapshot? _cache;

    public GoldStandardProductRepository(DataSourceSwitch source, CsvFileWriter? writer = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _writer = writer ?? new CsvFileWriter();
    }

    private string FolderPath => _source.DataFolderPath;

    private string ProductsPath => Path.Combine(FolderPath, GoldStandardTables.Products + ".csv");
```

`LoadAsync` のキャッシュ判定を、出どころの参照と版番号の両方で行うように変える。
出どころが切り替わったときに古い内容を使い続けないためである。

```csharp
    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = _source.Current;
            // 出どころそのものが入れ替わった場合も作り直します。
            // 版番号だけを見ると、別の出どころで偶然同じ番号のときに古い内容を返します。
            if (_cache is not null
                && ReferenceEquals(_cache.Source, source)
                && _cache.Version == source.Version)
            {
                return _cache;
            }

            var set = source.Load();
            var records = GoldStandardFlattener.Flatten(set, out var problems);
            var status = new DataSourceStatus(
                set.HasProducts,
                records.Count,
                null,
                set.Problems.Concat(problems).ToArray());
            var productsWriteTime = source.SupportsWrite && File.Exists(ProductsPath)
                ? File.GetLastWriteTimeUtc(ProductsPath)
                : DateTime.MinValue;

            _cache = new Snapshot(source, source.Version, set, records, status, productsWriteTime);
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private sealed record Snapshot(
        IGoldStandardTableSource Source,
        long Version,
        GoldStandardTableSet Set,
        IReadOnlyList<ProductRecord> Records,
        DataSourceStatus Status,
        DateTime ProductsWriteTimeUtc);
```

既存の `Snapshot` 定義と `LoadAsync` を上記へ置き換える。
`DataSourceStatus` の最終更新日時は出どころによって意味が変わるため `null` にする。
`set.TimestampUtc` を使っていた箇所は削除する。

`GoldStandardDetailSource_製品詳細取得.cs` も同様に付け替える。

```csharp
    private readonly DataSourceSwitch _source;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GoldStandardTableSet? _cache;
    private IGoldStandardTableSource? _cachedSource;
    private long _cachedVersion = -1;

    public GoldStandardDetailSource(DataSourceSwitch source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    private async Task<GoldStandardTableSet> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = _source.Current;
            if (_cache is not null && ReferenceEquals(_cachedSource, source) && _cachedVersion == source.Version)
            {
                return _cache;
            }

            _cache = source.Load();
            _cachedSource = source;
            _cachedVersion = source.Version;
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }
```

`LocalImagePath` の `_folderPath` は `_source.DataFolderPath` へ置き換える。

- [ ] **Step 6: 組み立てを合わせる**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`

```csharp
        var dataSource = DataSourceSwitch.ForCsvFolder(dataFolder);
        var products = new GoldStandardProductRepository(dataSource);
        var productDetails = new GoldStandardDetailSource(dataSource);
```

既存の `new GoldStandardProductRepository(dataFolder)` と
`new GoldStandardDetailSource(dataFolder)` をこれに置き換える。

テスト内の `new GoldStandardProductRepository(...)` / `new GoldStandardDetailSource(...)` も
`DataSourceSwitch.ForCsvFolder(folder)` を渡す形へ直す。

```bash
grep -rn "new GoldStandardProductRepository\|new GoldStandardDetailSource" --include=*.cs "BUI_Addin_CSharpアドイン"
```

- [ ] **Step 7: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。

- [ ] **Step 8: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: hold the active data source in one switch"
```

---

### Task 3: SQL接続中の振る舞い（書込み拒否と単価CSV）

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardProductRepository_製品データ取得.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingPriceRepository_単価窓口切替.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ReadOnlyDataSourceTests_読取専用テスト.cs`

**Interfaces:**
- Consumes: Task 2 の `DataSourceSwitch` / `IGoldStandardTableSource`
- Produces:
  - `GoldStandardProductRepository` の `UpsertAsync` / `RemoveAsync` / `AddValueAsync` / `RemoveValueAsync` が、`SupportsWrite == false` のとき `InvalidDataException` を投げる
  - `public sealed class SwitchingPriceRepository : IPriceRepository` — コンストラクタ `SwitchingPriceRepository(IPriceRepository csvPrices, DataSourceSwitch source)`

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ReadOnlyDataSourceTests_読取専用テスト.cs`

```csharp
// このファイルの目的:
//   SQL接続中（＝読み取り専用の出どころ）に、書き込みが「書く前に」断られることを守ります。
// なぜ必要か:
//   画面のボタンを無効にするだけでは、画面の不具合で押せてしまったときに
//   顧客の実データへ書きかけます。最後の砦はここに置きます。
using Bui.Data;
using Bui.Data.GoldStandard;
using Bui.Products;

namespace Bui.Tests.Data_データ;

[TestClass]
public class ReadOnlyDataSourceTests_読取専用テスト
{
    [TestMethod]
    public async Task 読取専用なら製品を登録しない()
    {
        var sut = new GoldStandardProductRepository(ReadOnlySwitch());

        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            sut.UpsertAsync(new ProductRecord("P-001", "名前", "メーカー", "型番", "枚", null,
                new Dictionary<string, string>()), default));

        StringAssert.Contains(error.Message, "SQL接続中");
        StringAssert.Contains(error.Message, "CSVフォルダ", "どうすれば書けるかを伝えること。");
    }

    [TestMethod]
    public async Task 読取専用ならマスタへ追加しない()
    {
        var sut = new GoldStandardProductRepository(ReadOnlySwitch());

        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            sut.AddValueAsync(ProductMasterKind.Manufacturer, "YLL", default));

        StringAssert.Contains(error.Message, "SQL接続中");
    }

    [TestMethod]
    public async Task 読取専用でも一覧の取得はできる()
    {
        var sut = new GoldStandardProductRepository(ReadOnlySwitch());

        var values = await sut.GetValuesAsync(ProductMasterKind.Manufacturer, default);

        Assert.AreEqual(0, values.Count);
    }

    [TestMethod]
    public async Task 読取専用なら単価CSVを読みに行かない()
    {
        // 単価はまだSQL側にありません。SQL接続中にCSVを読みに行くと、
        // 「SQLに切り替えたのにCSVを触っている」状態になります（設計 §3.3）。
        var csv = new CountingPriceRepository();
        var sut = new SwitchingPriceRepository(csv, ReadOnlySwitch());

        var prices = await sut.SearchAsync(new PriceQuery("P-001", null, null), default);
        var status = await sut.GetStatusAsync(default);

        Assert.AreEqual(0, prices.Count);
        Assert.AreEqual(0, csv.Calls, "CSVには一切触れないこと。");
        Assert.IsTrue(status.Problems.Any(p => p.Contains("SQL接続中")));
    }

    [TestMethod]
    public async Task CSVなら単価CSVをそのまま読む()
    {
        var csv = new CountingPriceRepository();
        var sut = new SwitchingPriceRepository(csv, DataSourceSwitch.ForCsvFolder(@"C:\BUI_PoCData"));

        await sut.SearchAsync(new PriceQuery("P-001", null, null), default);

        Assert.AreEqual(1, csv.Calls);
    }

    private static DataSourceSwitch ReadOnlySwitch() => TestSwitch.Create(new ReadOnlySource());

    private sealed class CountingPriceRepository : IPriceRepository
    {
        public int Calls { get; private set; }

        public Task<DataSourceStatus> GetStatusAsync(CancellationToken c) =>
            Task.FromResult(new DataSourceStatus(true, 0, null, Array.Empty<string>()));

        public Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery q, CancellationToken c)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PriceRecord>>(Array.Empty<PriceRecord>());
        }

        public Task UpsertAsync(PriceRecord r, CancellationToken c) => throw new NotSupportedException();
    }

    private sealed class ReadOnlySource : IGoldStandardTableSource
    {
        public bool SupportsWrite => false;
        public bool IsCsvFolder => false;
        public long Version => 1;
        public string DataFolderPath => string.Empty;
        public string Describe() => "SQL Server: localhost\\sqlexpress / GoldStandardDB";

        public GoldStandardTableSet Load() => new(
            new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal),
            Array.Empty<string>());
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 例外が投げられず `ThrowsExceptionAsync` が失敗する。

- [ ] **Step 3: 門番を足す**

`GoldStandardProductRepository_製品データ取得.cs` に次のメソッドを足す。

```csharp
    /// <summary>
    /// 読み取り専用の出どころ（SQL接続中）では、書き込みを始める前に断ります。
    /// 画面のボタンを無効にするだけでは、画面の不具合で押せてしまったときに
    /// 顧客の実データへ書きかけます。最後の砦をここに置きます。
    /// </summary>
    private void EnsureWritableSource()
    {
        if (_source.SupportsWrite) return;

        throw new InvalidDataException(
            "SQL接続中は製品やマスタを登録できません。"
            + "設定タブでデータ元を「CSVフォルダ」に切り替えてください。");
    }
```

`UpsertAsync` / `RemoveAsync` / `AddValueAsync` / `RemoveValueAsync` の先頭
（引数検査の直後、`LoadAsync` の前）で `EnsureWritableSource();` を呼ぶ。

- [ ] **Step 4: 単価窓口の切替を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SwitchingPriceRepository_単価窓口切替.cs`

```csharp
// このファイルの目的:
//   SQL接続中に単価CSVを読みに行かないようにします。
//
// なぜ必要か:
//   単価情報はまだSQL側にありません（GoldStandardDBに単価テーブルがない）。
//   何もしないと、SQLへ切り替えた後も選択のたびにPrices.csvを開きに行き、
//   「SQLに切り替えたのにCSVを触っている」状態になります。
//
// 修正時の影響:
//   読めないことを黙って隠さず、状態表示に理由を出してください。
//   単価が空になった理由が分からないと、原因の切り分けができません。

namespace Bui.Data;

/// <summary>データ元がCSVのときだけ単価CSVを読みます。</summary>
public sealed class SwitchingPriceRepository : IPriceRepository
{
    private readonly IPriceRepository _csvPrices;
    private readonly DataSourceSwitch _source;

    public SwitchingPriceRepository(IPriceRepository csvPrices, DataSourceSwitch source)
    {
        _csvPrices = csvPrices ?? throw new ArgumentNullException(nameof(csvPrices));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? _csvPrices.GetStatusAsync(cancellationToken)
            : Task.FromResult(new DataSourceStatus(
                false, 0, null,
                new[] { "SQL接続中は単価を表示しません。単価情報はまだSQL側にありません。" }));

    public Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery query, CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? _csvPrices.SearchAsync(query, cancellationToken)
            : Task.FromResult<IReadOnlyList<PriceRecord>>(Array.Empty<PriceRecord>());

    public Task UpsertAsync(PriceRecord record, CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? _csvPrices.UpsertAsync(record, cancellationToken)
            : throw new InvalidDataException(
                "SQL接続中は単価を更新できません。設定タブでデータ元を「CSVフォルダ」に切り替えてください。");
}
```

`using System.IO;` をファイル冒頭へ足す。

- [ ] **Step 5: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。

- [ ] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: keep SQL sessions from touching the CSV files"
```

---

### Task 4: SQL接続の設定項目と接続文字列

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SqlConnectionStringFactory_接続文字列組立.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/BuiSettings_BUI設定.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SettingsMerger_設定統合.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SettingsService_設定管理.cs`
- Modify: `BUI_Addin_CSharpアドイン/config_設定例/Settings.Default_共通設定.json`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Configuration_設定/SqlConnectionStringFactoryTests_接続文字列テスト.cs`

**Interfaces:**
- Consumes: `DataSettings`、`DataProviderPolicy`
- Produces:
  - `DataSettings` の末尾に `string SqlServerName, string SqlDatabaseName` が増える
  - `public static class SqlConnectionStringFactory` — `public static OperationResult<string> Build(string? serverName, string? databaseName)`
  - `DataProviderPolicy.Validate("SqlServer")` が成功する

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Configuration_設定/SqlConnectionStringFactoryTests_接続文字列テスト.cs`

```csharp
// このファイルの目的:
//   接続文字列を利用者に書かせず、サーバー名とDB名から組み立てることを守ります。
// なぜ必要か:
//   接続文字列を直接入力させると、書式の誤りが「接続できません」としか出ず、
//   会社PCでしか再現できない往復が増えます。入力は2つだけにします。
using Bui.Configuration;

namespace Bui.Tests.Configuration_設定;

[TestClass]
public class SqlConnectionStringFactoryTests_接続文字列テスト
{
    [TestMethod]
    public void サーバー名とDB名から組み立てる()
    {
        var result = SqlConnectionStringFactory.Build(@"localhost\sqlexpress", "GoldStandardDB");

        Assert.IsTrue(result.IsSuccess);
        StringAssert.Contains(result.Value!, @"Data Source=localhost\sqlexpress");
        StringAssert.Contains(result.Value!, "Initial Catalog=GoldStandardDB");
        StringAssert.Contains(result.Value!, "Integrated Security=True");
    }

    [TestMethod]
    public void サーバー名が空なら断る()
    {
        var result = SqlConnectionStringFactory.Build("   ", "GoldStandardDB");

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error!.UserMessage, "サーバー名");
    }

    [TestMethod]
    public void データベース名が空なら断る()
    {
        var result = SqlConnectionStringFactory.Build(@"localhost\sqlexpress", "");

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error!.UserMessage, "データベース名");
    }

    [TestMethod]
    public void 接続文字列を壊す文字は断る()
    {
        // セミコロンを通すと、利用者の入力で他の設定を上書きできてしまいます。
        var result = SqlConnectionStringFactory.Build(@"localhost;Password=x", "GoldStandardDB");

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error!.UserMessage, "使えない文字");
    }

    [TestMethod]
    public void SqlServerもデータ方式として通す()
    {
        Assert.IsTrue(DataProviderPolicy.Validate("SqlServer").IsSuccess);
        Assert.IsTrue(DataProviderPolicy.Validate("GoldStandard").IsSuccess);
        Assert.IsFalse(DataProviderPolicy.Validate("Oracle").IsSuccess);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: `SqlConnectionStringFactory` が無くコンパイルエラー。

- [ ] **Step 3: 接続文字列の組み立てを作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/SqlConnectionStringFactory_接続文字列組立.cs`

```csharp
// このファイルの目的:
//   サーバー名とデータベース名から、SQL Serverの接続文字列を組み立てます。
//
// なぜ利用者に接続文字列を書かせないのか:
//   書式の誤りは「接続できません」としか出ず、会社PCでしか再現できません。
//   入力を2つに絞れば、間違いようがある箇所も2つに絞れます。
//
// 修正時の影響:
//   セミコロンなどを弾いています。通すと、利用者の入力で
//   Integrated Security を上書きできてしまいます。

using Bui.Common;

namespace Bui.Configuration;

/// <summary>Windows認証でSQL Serverへ接続する文字列を組み立てます。</summary>
public static class SqlConnectionStringFactory
{
    /// <summary>接続文字列の値として使えない文字です。</summary>
    private static readonly char[] Forbidden = { ';', '=', '\'', '"', '\r', '\n' };

    public static OperationResult<string> Build(string? serverName, string? databaseName)
    {
        var server = (serverName ?? string.Empty).Trim();
        var database = (databaseName ?? string.Empty).Trim();

        if (server.Length == 0)
        {
            return OperationResult<string>.Failure(
                "SQL_SERVER_NAME_REQUIRED",
                "サーバー名を入力してください。例: localhost\\sqlexpress",
                "server name is empty");
        }

        if (database.Length == 0)
        {
            return OperationResult<string>.Failure(
                "SQL_DATABASE_NAME_REQUIRED",
                "データベース名を入力してください。例: GoldStandardDB",
                "database name is empty");
        }

        if (server.IndexOfAny(Forbidden) >= 0 || database.IndexOfAny(Forbidden) >= 0)
        {
            return OperationResult<string>.Failure(
                "SQL_NAME_HAS_FORBIDDEN_CHARACTER",
                "サーバー名またはデータベース名に使えない文字が含まれています。記号を外してください。",
                "forbidden character in server or database name");
        }

        // Windows認証なので、保存も送信もするパスワードはありません。
        // TrustServerCertificateは、既定の自己署名証明書のままのSQL Server Expressへ
        // 社内で接続するための指定です。
        var connectionString =
            $"Data Source={server};Initial Catalog={database};"
            + "Integrated Security=True;TrustServerCertificate=True;"
            + "Connect Timeout=5;Application Name=BUI";

        return OperationResult<string>.Success(connectionString);
    }
}
```

- [ ] **Step 4: 設定にSQLの2項目を足す**

`BuiSettings_BUI設定.cs` の `DataSettings` の末尾へ追加する。

```csharp
    string NewProductIdPrefix,
    int NewProductIdDigits,
    // SQL Serverをデータ元にするときの接続先です。Windows認証なので秘密値はありません。
    string SqlServerName = "",
    string SqlDatabaseName = "");
```

既定値付きで末尾へ足すことで、既存の呼び出し（テストを含む）は変更不要になる。

`SettingsMerger_設定統合.cs` の `FillMissingData` へ追加する。

```csharp
        NewProductIdDigits = local.NewProductIdDigits < 1 ? defaults.NewProductIdDigits : local.NewProductIdDigits,
        // 更新前の設定ファイルにはこの2項目がありません。空のまま扱い、設定画面で入力させます。
        SqlServerName = string.IsNullOrWhiteSpace(local.SqlServerName)
            ? defaults.SqlServerName ?? string.Empty
            : local.SqlServerName,
        SqlDatabaseName = string.IsNullOrWhiteSpace(local.SqlDatabaseName)
            ? defaults.SqlDatabaseName ?? string.Empty
            : local.SqlDatabaseName
```

`SettingsService_設定管理.cs` の `SaveLocalAsync` は `CsvPathNormalizer.Normalize(settings.Data)` の
戻り値をそのまま書き出しているため、`DataSettings` へ足した2項目は自動的に保存される。
変更は不要である。保存されることを Step 6 のテストで確かめる。

`DataProviderPolicy.Validate` を書き換える。

```csharp
    /// <summary>
    /// PoCで実装済みのデータ方式です。
    /// 旧設定の "Csv" はSettingsMergerが "GoldStandard" へ読み替えるため、ここには到達しません。
    /// </summary>
    public static OperationResult<string> Validate(string provider)
    {
        if (string.Equals(provider, "GoldStandard", StringComparison.OrdinalIgnoreCase))
            return OperationResult<string>.Success("GoldStandard");
        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            return OperationResult<string>.Success("SqlServer");

        return OperationResult<string>.Failure("DATA_PROVIDER_NOT_IMPLEMENTED",
            $"データ方式「{provider}」はまだ利用できません。GoldStandard（CSV）かSqlServerを選択してください。",
            "Only GoldStandard and SqlServer are implemented.");
    }
```

- [ ] **Step 5: 出荷設定へ空欄を足す**

`BUI_Addin_CSharpアドイン/config_設定例/Settings.Default_共通設定.json` の `data` へ追加する。

```json
    "sqlServerName": "",
    "sqlDatabaseName": ""
```

出荷時は空欄にする。データフォルダと同じ考え方で、利用者の環境の値を勝手に埋めない。

- [ ] **Step 6: 保存される項目のテストを足す**

`SqlConnectionStringFactoryTests_接続文字列テスト.cs` へ追記する。

```csharp
    [TestMethod]
    public void 設定に接続先の2項目が入る()
    {
        var data = new DataSettings(
            "SqlServer", "", "", "", null,
            CsvColumnMapping.GoldStandardDefault, PriceColumnMapping.PocDefault,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            "BM_NEW-", 4,
            SqlServerName: @"localhost\sqlexpress",
            SqlDatabaseName: "GoldStandardDB");

        var normalized = CsvPathNormalizer.Normalize(data);

        Assert.AreEqual(@"localhost\sqlexpress", normalized.SqlServerName);
        Assert.AreEqual("GoldStandardDB", normalized.SqlDatabaseName);
    }
```

`using Bui.Data;` をファイル冒頭へ足す。

- [ ] **Step 7: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。

- [ ] **Step 8: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add SQL Server connection settings and connection string builder"
```

---

### Task 5: SQL Serverのエラーを日本語の対処文にする

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SqlErrorMessages_SQLエラー文言.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SqlErrorMessagesTests_SQLエラー文言テスト.cs`

**Interfaces:**
- Consumes: なし
- Produces: `public static class SqlErrorMessages` — `public static string Describe(int errorNumber, string databaseName, string originalMessage)`

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SqlErrorMessagesTests_SQLエラー文言テスト.cs`

```csharp
// このファイルの目的:
//   SQL Serverのエラー番号を、利用者が次に何をすればよいか分かる日本語にします。
// なぜ必要か:
//   接続まわりは会社PCでしか確かめられません。画面に理由が出ていないと、
//   1往復で1つしか原因を潰せず、往復が積み上がります。
using Bui.Data;

namespace Bui.Tests.Data_データ;

[TestClass]
public class SqlErrorMessagesTests_SQLエラー文言テスト
{
    [TestMethod]
    public void 到達できない場合はインスタンス名と起動を確認させる()
    {
        var text = SqlErrorMessages.Describe(53, "GoldStandardDB", "A network-related error");

        StringAssert.Contains(text, "インスタンス名");
        StringAssert.Contains(text, "SQL Server Express");
        StringAssert.Contains(text, "A network-related error", "原文も併記すること。");
    }

    [TestMethod]
    public void データベースを開けない場合は名前と権限を確認させる()
    {
        var text = SqlErrorMessages.Describe(4060, "GoldStandardDB", "Cannot open database");

        StringAssert.Contains(text, "GoldStandardDB");
        StringAssert.Contains(text, "参照権限");
    }

    [TestMethod]
    public void ログインできない場合はWindowsユーザーの権限を確認させる()
    {
        var text = SqlErrorMessages.Describe(18456, "GoldStandardDB", "Login failed");

        StringAssert.Contains(text, "Windowsユーザー");
    }

    [TestMethod]
    public void 知らない番号でも原文を必ず添える()
    {
        var text = SqlErrorMessages.Describe(99999, "GoldStandardDB", "Something specific happened");

        StringAssert.Contains(text, "Something specific happened");
    }

    [TestMethod]
    public void 原文が空でも文言は成立する()
    {
        var text = SqlErrorMessages.Describe(53, "GoldStandardDB", "");

        Assert.IsTrue(text.Length > 0);
        Assert.IsFalse(text.EndsWith("（）", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: `SqlErrorMessages` が無くコンパイルエラー。

- [ ] **Step 3: 文言を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SqlErrorMessages_SQLエラー文言.cs`

```csharp
// このファイルの目的:
//   SQL Serverのエラー番号を、利用者が次に何をすればよいか分かる日本語にします。
//
// なぜ原文を併記するのか:
//   接続まわりは会社PCでしか確かめられません。言い換えた文だけを出すと、
//   言い換えが外れていたときに原因へ辿り着けなくなります。
//   言い換えと原文の両方を出せば、どちらかで届きます。
//
// 修正時の影響:
//   接続文字列は絶対に出さないでください。ここへ渡してよいのは
//   利用者が入力したデータベース名までです。

namespace Bui.Data;

/// <summary>SQL Serverのエラーを利用者向けの日本語にします。</summary>
public static class SqlErrorMessages
{
    public static string Describe(int errorNumber, string databaseName, string originalMessage)
    {
        var database = (databaseName ?? string.Empty).Trim();
        var advice = errorNumber switch
        {
            2 or 53 or 258 or -2 =>
                "SQL Serverに接続できませんでした。"
                + "インスタンス名が正しいか、SQL Server Express が起動しているかを確認してください。",
            4060 =>
                $"データベース「{database}」を開けません。"
                + "名前が正しいか、このWindowsユーザーに参照権限があるかを確認してください。",
            18456 =>
                "このWindowsユーザーではログインできません。"
                + "データベースへの参照権限を確認してください。",
            _ => "SQL Serverへの接続に失敗しました。"
        };

        var original = (originalMessage ?? string.Empty).Trim();
        return original.Length == 0
            ? advice
            : advice + Environment.NewLine + "SQL Serverからの応答: " + original;
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。

- [ ] **Step 5: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: turn SQL Server error numbers into actionable Japanese"
```

---

### Task 6: SQL Serverから表一式を読む

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/SqlServerTableSource_SQL読込口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Data_データ/SqlClientTableReader_SQL接続.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SqlServerTableSourceTests_SQL読込口テスト.cs`

**Interfaces:**
- Consumes: Task 1 の `IGoldStandardTableSource` / `GoldStandardTableSet` / `GoldStandardTable`、Task 5 の `SqlErrorMessages`
- Produces:
  - `internal interface ISqlTableReader : IDisposable` — `GoldStandardTable? Read(string table)`
  - `internal sealed class SqlServerTableSource : IGoldStandardTableSource` — コンストラクタ `SqlServerTableSource(Func<ISqlTableReader> openReader, string dataFolderPath, string serverName, string databaseName)`、メソッド `void Invalidate()`
  - `internal sealed class SqlClientTableReader : ISqlTableReader` — `public static ISqlTableReader Open(string connectionString)`

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SqlServerTableSourceTests_SQL読込口テスト.cs`

```csharp
// このファイルの目的:
//   SQLの行が、CSVと同じ形の表一式になることを守ります。
//   NULLを空文字に揃えないと、平坦化以降で「CSVだと出るのにSQLだと出ない」が起きます。
// 接続そのものはここでは試しません（このPCにSQL Serverがありません）。
//   偽の読み取り器を差して、変換の規則だけを固定します。
using Bui.Data.GoldStandard;

namespace Bui.Tests.Data_データ;

[TestClass]
public class SqlServerTableSourceTests_SQL読込口テスト
{
    [TestMethod]
    public void 行を表一式として読む()
    {
        var reader = new FakeSqlTableReader();
        reader.Add("Products", new[] { "product_id", "product_name" }, new[] { "P-001", "タイガーボード" });

        var set = Create(reader).Load();

        Assert.IsTrue(set.HasProducts);
        Assert.AreEqual("タイガーボード", set.Rows("Products")[0]["product_name"]);
    }

    [TestMethod]
    public void NULLは空文字にする()
    {
        var reader = new FakeSqlTableReader();
        reader.Add("Products", new[] { "product_id", "note" }, new[] { "P-001", null });

        var set = Create(reader).Load();

        Assert.AreEqual(string.Empty, set.Rows("Products")[0]["note"]);
    }

    [TestMethod]
    public void 表が無ければ空として続ける()
    {
        var reader = new FakeSqlTableReader();
        reader.Add("Products", new[] { "product_id" }, new[] { "P-001" });

        var set = Create(reader).Load();

        Assert.AreEqual(0, set.Rows("Manufacturers").Count);
        Assert.IsTrue(set.Problems.Any(p => p.Contains("Manufacturers")));
    }

    [TestMethod]
    public void Productsが読めなければ理由を先頭に置く()
    {
        var set = Create(new FakeSqlTableReader()).Load();

        Assert.IsFalse(set.HasProducts);
        StringAssert.Contains(set.Problems[0], "Products");
    }

    [TestMethod]
    public void 接続に失敗しても例外にせず理由を返す()
    {
        var source = new SqlServerTableSource(
            () => throw new InvalidOperationException("接続できません"),
            @"C:\BUI_PoCData", @"localhost\sqlexpress", "GoldStandardDB");

        var set = source.Load();

        Assert.IsFalse(set.HasProducts);
        Assert.IsTrue(set.Problems.Any(p => p.Contains("接続できません")));
    }

    [TestMethod]
    public void 書き込みできない出どころである()
    {
        var source = Create(new FakeSqlTableReader());

        Assert.IsFalse(source.SupportsWrite);
        Assert.IsFalse(source.IsCsvFolder);
        Assert.AreEqual(@"C:\BUI_PoCData", source.DataFolderPath);
        StringAssert.Contains(source.Describe(), "GoldStandardDB");
        StringAssert.Contains(source.Describe(), @"localhost\sqlexpress");
    }

    [TestMethod]
    public void Invalidateで版番号が変わる()
    {
        var source = Create(new FakeSqlTableReader());
        var before = source.Version;

        source.Invalidate();

        Assert.AreNotEqual(before, source.Version);
    }

    private static SqlServerTableSource Create(FakeSqlTableReader reader) =>
        new(() => reader, @"C:\BUI_PoCData", @"localhost\sqlexpress", "GoldStandardDB");

    private sealed class FakeSqlTableReader : ISqlTableReader
    {
        private readonly Dictionary<string, GoldStandardTable> _tables = new(StringComparer.Ordinal);

        public void Add(string table, string[] headers, string?[] row)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < headers.Length; index++)
            {
                values[headers[index]] = row[index] ?? string.Empty;
            }

            _tables[table] = new GoldStandardTable(headers, new[] { values }, HasUtf8Bom: false);
        }

        public GoldStandardTable? Read(string table) =>
            _tables.TryGetValue(table, out var value) ? value : null;

        public void Dispose() { }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: `SqlServerTableSource` と `ISqlTableReader` が無くコンパイルエラー。

- [ ] **Step 3: SQL読み込み口を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/SqlServerTableSource_SQL読込口.cs`

```csharp
// このファイルの目的:
//   SQL Serverから11枚の表を読み、CSVと同じ形の表一式にします。
//
// なぜここにSQLの型が出てこないのか:
//   Bui.SharedはRevitにもSQLのパッケージにも依存させません。
//   実際の接続はISqlTableReaderの実装（Bui.Addin側）が担い、
//   ここは「表名を渡すと表が返る」という細い境界だけを使います。
//   おかげでSQL Serverの無いこのPCでも変換規則を単体テストできます。
//
// 修正時の影響:
//   NULLは必ず空文字にしてください。CSVの空欄と揃えることで、
//   平坦化以降が出どころを気にせず済みます。
//   接続に失敗しても例外を投げず、Problemsへ理由を入れてください。
//   投げるとBUIパネルごと止まります。

namespace Bui.Data.GoldStandard;

/// <summary>表名を渡すと1枚の表を返す、SQL接続の細い境界です。</summary>
internal interface ISqlTableReader : IDisposable
{
    /// <summary>表が無ければnullを返します。例外は投げません。</summary>
    GoldStandardTable? Read(string table);
}

/// <summary>SQL Serverから表一式を読みます。読み取り専用です。</summary>
internal sealed class SqlServerTableSource : IGoldStandardTableSource
{
    private readonly Func<ISqlTableReader> _openReader;
    private readonly string _serverName;
    private readonly string _databaseName;
    private long _version;

    public SqlServerTableSource(
        Func<ISqlTableReader> openReader,
        string dataFolderPath,
        string serverName,
        string databaseName)
    {
        _openReader = openReader ?? throw new ArgumentNullException(nameof(openReader));
        DataFolderPath = dataFolderPath ?? string.Empty;
        _serverName = serverName ?? string.Empty;
        _databaseName = databaseName ?? string.Empty;
    }

    /// <summary>製品画像の置き場です。SQL接続中も画像はローカルのファイルとして扱います。</summary>
    public string DataFolderPath { get; }

    /// <summary>SQLは読み取り専用です（設計 §1）。</summary>
    public bool SupportsWrite => false;

    /// <summary>CSVフォルダではありません。単価CSVを読みに行かせないための印です。</summary>
    public bool IsCsvFolder => false;

    public long Version => Volatile.Read(ref _version);

    /// <summary>「切り替えて読み直す」で呼びます。次のLoadで必ず読み直されます。</summary>
    public void Invalidate() => Interlocked.Increment(ref _version);

    public string Describe() => $"SQL Server: {_serverName} / {_databaseName}";

    public GoldStandardTableSet Load()
    {
        var tables = new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal);
        var problems = new List<string>();

        try
        {
            using var reader = _openReader();
            foreach (var table in GoldStandardTables.All)
            {
                try
                {
                    var value = reader.Read(table);
                    if (value is null)
                    {
                        problems.Add($"{table} テーブルがありません。この表は空として扱います。");
                        continue;
                    }

                    tables[table] = value;
                }
                catch (Exception exception)
                {
                    problems.Add($"{table} テーブルを読み取れません。{exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            // 接続そのものに失敗した場合です。ここで投げるとBUIパネルごと止まります。
            problems.Add(exception.Message);
        }

        if (!tables.ContainsKey(GoldStandardTables.Products))
        {
            problems.Insert(0, $"{GoldStandardTables.Products} テーブルを読み取れないため、製品候補を表示できません。");
        }

        return new GoldStandardTableSet(tables, problems);
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。

- [ ] **Step 5: 実際のSQL接続を作る**

パッケージ参照を `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj` の
`ItemGroup`（`Reference Include="RevitAPI"` があるものとは別の新しい `ItemGroup`）へ足す。

```xml
  <!--
    SQL Serverへの接続に使います。Revitのプロセスへ読み込まれるため、
    Revitが同梱する別バージョンとぶつかる可能性があります。
    ぶつかっても起動が止まらないよう、この参照に触れるのは
    利用者が設定タブでSQL Serverへ切り替えたときだけにしています。
    起動経路（BuiDockPaneComposition）からは呼びません。
  -->
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />
  </ItemGroup>
```

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Data_データ/SqlClientTableReader_SQL接続.cs`

```csharp
// このファイルの目的:
//   Microsoft.Data.SqlClient を使って、1枚の表を読み出します。
//   SQLのパッケージに触れるのはこのファイルだけです。
//
// なぜBui.Addin側に置くのか:
//   Bui.Sharedはパッケージ依存を持たせません。持たせると、
//   SQL Serverの無い環境でも単体テストがパッケージを要求します。
//
// 修正時の影響:
//   このクラスに触れるのは、利用者が設定タブでSQL Serverへ切り替えたときだけです。
//   起動経路から呼ばないでください。呼ぶと、アセンブリの読み込みに失敗したときに
//   BUIパネルごと開かなくなります。

using System.Data;
using Bui.Data;
using Bui.Data.GoldStandard;
using Microsoft.Data.SqlClient;

namespace Bui.Data.SqlServer;

/// <summary>SQL Serverの1接続で、表を順に読み出します。</summary>
internal sealed class SqlClientTableReader : ISqlTableReader
{
    private readonly SqlConnection _connection;
    private readonly string _databaseName;

    private SqlClientTableReader(SqlConnection connection, string databaseName)
    {
        _connection = connection;
        _databaseName = databaseName;
    }

    /// <summary>接続を開きます。失敗したら日本語の理由付きで投げます。</summary>
    public static ISqlTableReader Open(string connectionString, string databaseName)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            return new SqlClientTableReader(connection, databaseName);
        }
        catch (SqlException exception)
        {
            connection.Dispose();
            throw new InvalidOperationException(
                SqlErrorMessages.Describe(exception.Number, databaseName, exception.Message), exception);
        }
        catch (Exception exception)
        {
            connection.Dispose();
            // アセンブリの読み込み失敗はここに来ます。落ちる代わりに読める文言にします。
            throw new InvalidOperationException(
                "SQL接続に必要な部品を読み込めませんでした。"
                + "配置先にSQL用のDLLが届いているかを配置診断で確認してください。"
                + Environment.NewLine + exception.Message, exception);
        }
    }

    public GoldStandardTable? Read(string table)
    {
        // 表名はGoldStandardTables.Allの固定値だけです。外部入力は入りません。
        using var command = new SqlCommand($"SELECT * FROM dbo.[{table}]", _connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 30
        };

        try
        {
            using var reader = command.ExecuteReader();
            var headers = new List<string>(reader.FieldCount);
            for (var index = 0; index < reader.FieldCount; index++) headers.Add(reader.GetName(index));

            var rows = new List<Dictionary<string, string>>();
            while (reader.Read())
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    // NULLはCSVの空欄と同じ「空文字」に揃えます。
                    row[headers[index]] = reader.IsDBNull(index)
                        ? string.Empty
                        : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }

                rows.Add(row);
            }

            return new GoldStandardTable(headers, rows, HasUtf8Bom: false);
        }
        catch (SqlException exception) when (exception.Number is 208)
        {
            // 208 = テーブルが無い。CSVの「ファイルが無い」と同じ扱いにします。
            return null;
        }
    }

    public void Dispose() => _connection.Dispose();
}
```

- [ ] **Step 6: 非Revitのビルドが壊れていないことを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。
`SqlClientTableReader` は `Bui.Addin` 側なのでテスト対象には入らない。
Revit依存アセンブリを必要とするため、このPCではコンパイル確認までできない。
**目視確認どまりであることを、この後の報告に必ず明記する。**

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: read the GoldStandard tables from SQL Server"
```

---

### Task 7: 接続確認と切り替えの操作

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/IDataSourceConnector_データ元接続.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/DataSourceViewModelTests_データ元切替画面テスト.cs`

**Interfaces:**
- Consumes: Task 2 の `DataSourceSwitch`、Task 4 の `SqlConnectionStringFactory`
- Produces:
  - `public interface IDataSourceConnector { Task<OperationResult<string>> TestSqlServerAsync(string serverName, string databaseName, CancellationToken cancellationToken); Task<OperationResult<string>> UseSqlServerAsync(string serverName, string databaseName, CancellationToken cancellationToken); void UseCsvFolder(string folderPath); bool SupportsWrite { get; } }`
  - `BuiDockPaneViewModel` の新しいメンバー: `bool UseSqlServer`、`string SqlServerName`、`string SqlDatabaseName`、`bool IsDataSourceReadOnly`、`string DataSourceDescription`、`AsyncCommand TestSqlConnectionCommand`、`AsyncCommand SwitchDataSourceCommand`、`Task TestSqlConnectionAsync(CancellationToken)`、`Task SwitchDataSourceAsync(CancellationToken)`
  - コンストラクタ引数 `IDataSourceConnector? dataSourceConnector = null` が末尾に増える

- [ ] **Step 1: 失敗するテストを書く**

`BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/DataSourceViewModelTests_データ元切替画面テスト.cs`

```csharp
// このファイルの目的:
//   データ元の切り替えが、失敗したときに切り替わらないこと、
//   成功したときに画面の状態を壊さないことを守ります。
// なぜ必要か:
//   切り替えの途中で失敗して中途半端な状態になると、
//   「どちらを読んでいるのか分からない」という最悪の状態になります。
using Bui.Assemblies;
using Bui.Common;
using Bui.Configuration;
using Bui.Data;
using Bui.Products;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class DataSourceViewModelTests_データ元切替画面テスト
{
    [TestMethod]
    public async Task 接続確認に成功したら件数を出す()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 1,204件"));
        var sut = DataSourceFactory.Create(connector);
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.TestSqlConnectionAsync();

        StringAssert.Contains(sut.SettingsStatusMessage, "接続できました");
        Assert.AreEqual(0, connector.SqlSwitches, "確認だけでは切り替えないこと。");
        Assert.IsFalse(sut.IsDataSourceReadOnly, "確認だけではデータ元を変えないこと。");
    }

    [TestMethod]
    public async Task 接続確認に失敗したら理由を出す()
    {
        var connector = new FakeConnector(OperationResult<string>.Failure(
            "SQL_CONNECT_FAILED", "SQL Serverに接続できませんでした。インスタンス名が正しいか…", "detail"));
        var sut = DataSourceFactory.Create(connector);
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.TestSqlConnectionAsync();

        StringAssert.Contains(sut.SettingsStatusMessage, "インスタンス名");
    }

    [TestMethod]
    public async Task サーバー名が空なら接続を試みない()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました"));
        var sut = DataSourceFactory.Create(connector);
        sut.SqlServerName = "";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.TestSqlConnectionAsync();

        Assert.AreEqual(0, connector.SqlAttempts);
        StringAssert.Contains(sut.SettingsStatusMessage, "サーバー名");
    }

    [TestMethod]
    public async Task 切り替えに失敗したらデータ元は変わらない()
    {
        var connector = new FakeConnector(OperationResult<string>.Failure(
            "SQL_CONNECT_FAILED", "SQL Serverに接続できませんでした。", "detail"));
        var sut = DataSourceFactory.Create(connector);
        sut.UseSqlServer = true;
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.SwitchDataSourceAsync();

        Assert.IsFalse(sut.IsDataSourceReadOnly, "切り替わっていないこと。");
        StringAssert.Contains(sut.SettingsStatusMessage, "切り替えませんでした");
    }

    [TestMethod]
    public async Task 切り替えに成功したら読み取り専用になる()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 3件"));
        var sut = DataSourceFactory.Create(connector);
        sut.UseSqlServer = true;
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.SwitchDataSourceAsync();

        Assert.IsTrue(sut.IsDataSourceReadOnly);
        Assert.AreEqual(1, connector.SqlSwitches);
    }

    [TestMethod]
    public async Task CSVへ戻すと書き込みできる状態になる()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 3件"));
        var sut = DataSourceFactory.Create(connector);
        sut.UseSqlServer = true;
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";
        await sut.SwitchDataSourceAsync();

        sut.UseSqlServer = false;
        await sut.SwitchDataSourceAsync();

        Assert.IsFalse(sut.IsDataSourceReadOnly);
        Assert.AreEqual(1, connector.CsvSwitches);
    }

    [TestMethod]
    public async Task 切り替えても画面の状態は消えない()
    {
        var connector = new FakeConnector(OperationResult<string>.Success("接続できました。製品 3件"));
        var sut = DataSourceFactory.Create(connector);
        sut.SearchText = "せっこう";
        sut.UseSqlServer = true;
        sut.SqlServerName = @"localhost\sqlexpress";
        sut.SqlDatabaseName = "GoldStandardDB";

        await sut.SwitchDataSourceAsync();

        Assert.AreEqual("せっこう", sut.SearchText);
    }

    private sealed class FakeConnector : IDataSourceConnector
    {
        private readonly OperationResult<string> _result;

        public FakeConnector(OperationResult<string> result) => _result = result;

        public int SqlAttempts { get; private set; }
        public int SqlSwitches { get; private set; }
        public int CsvSwitches { get; private set; }
        public bool IsReadOnly { get; private set; }

        public Task<OperationResult<string>> TestSqlServerAsync(
            string serverName, string databaseName, CancellationToken cancellationToken)
        {
            SqlAttempts++;
            return Task.FromResult(_result);
        }

        public Task<OperationResult<string>> UseSqlServerAsync(
            string serverName, string databaseName, CancellationToken cancellationToken)
        {
            SqlAttempts++;
            if (_result.IsSuccess)
            {
                SqlSwitches++;
                IsReadOnly = true;
            }

            return Task.FromResult(_result);
        }

        public void UseCsvFolder(string folderPath)
        {
            CsvSwitches++;
            IsReadOnly = false;
        }

        public bool SupportsWrite => !IsReadOnly;
    }
}
```

**注意:** 「接続を確認」と「切り替えて読み直す」は別のメソッドを呼ぶ。
`TestSqlServerAsync` は接続を試すだけで切り替えない。
`UseSqlServerAsync` は接続に成功した場合だけ切り替える。
1つのメソッドにまとめると、確認を押しただけでデータ元が変わり、
利用者の意図と画面が食い違う。

- [ ] **Step 2: 接続窓口を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/IDataSourceConnector_データ元接続.cs`

```csharp
// このファイルの目的:
//   画面から見た「データ元をつなぎ替える操作」を定義します。
//
// なぜインターフェイスにするのか:
//   実際の接続はMicrosoft.Data.SqlClientを使うため、Bui.Addin側にしか置けません。
//   画面側はこの契約だけを知っていれば、SQL Serverの無いこのPCでも単体テストできます。
//
// 修正時の影響:
//   ConnectSqlServerAsyncは、接続に成功した場合だけデータ元を切り替えてください。
//   失敗したのに切り替えると「どちらを読んでいるか分からない」状態になります。

using Bui.Common;

namespace Bui.UI;

/// <summary>データ元をつなぎ替える操作です。</summary>
public interface IDataSourceConnector
{
    /// <summary>
    /// SQL Serverへ接続できるかだけを確かめます。**データ元は切り替えません。**
    /// 成功時の値は画面へそのまま出す件数の要約です。
    /// 確認と切り替えを同じメソッドにすると、「接続を確認」を押しただけで
    /// データ元が変わり、利用者の意図と画面が食い違います。
    /// </summary>
    Task<OperationResult<string>> TestSqlServerAsync(
        string serverName, string databaseName, CancellationToken cancellationToken);

    /// <summary>
    /// SQL Serverへ接続し、成功したらデータ元をそちらへ切り替えます。
    /// 失敗時は理由を返し、データ元は変えません。
    /// </summary>
    Task<OperationResult<string>> UseSqlServerAsync(
        string serverName, string databaseName, CancellationToken cancellationToken);

    /// <summary>データ元をCSVフォルダへ戻します。</summary>
    void UseCsvFolder(string folderPath);

    /// <summary>いまのデータ元へ書き込めるかどうかです。</summary>
    bool SupportsWrite { get; }
}
```

- [ ] **Step 3: テストが失敗することを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: `DataSourceFactory` と各プロパティが無くコンパイルエラー。

- [ ] **Step 4: 画面の状態と操作を足す**

`BuiDockPaneViewModel_パネル状態.cs` へ追加する。

フィールドとコンストラクタ引数:

```csharp
    private readonly IDataSourceConnector? _dataSourceConnector;
    private bool _useSqlServer;
    private string _sqlServerName = string.Empty;
    private string _sqlDatabaseName = string.Empty;
    private bool _isDataSourceReadOnly;
    private string _dataSourceDescription = string.Empty;
```

コンストラクタの引数末尾へ `IDataSourceConnector? dataSourceConnector = null` を足し、
本体で `_dataSourceConnector = dataSourceConnector;` を代入する。
コマンドの生成は他のコマンドの並びへ足す。

```csharp
        TestSqlConnectionCommand = new AsyncCommand(TestSqlConnectionAsync, commandErrorHandler);
        SwitchDataSourceCommand = new AsyncCommand(SwitchDataSourceAsync, commandErrorHandler);
```

公開メンバー:

```csharp
    /// <summary>設定タブで「SQL Server」を選んでいるかどうかです。まだ切り替えてはいません。</summary>
    public bool UseSqlServer { get => _useSqlServer; set => SetProperty(ref _useSqlServer, value); }

    public string SqlServerName { get => _sqlServerName; set => SetProperty(ref _sqlServerName, value); }

    public string SqlDatabaseName { get => _sqlDatabaseName; set => SetProperty(ref _sqlDatabaseName, value); }

    /// <summary>
    /// いま読んでいるデータ元が読み取り専用かどうかです。
    /// 真のとき、新規登録とマスタ追加の操作を画面から止めます。
    /// </summary>
    public bool IsDataSourceReadOnly
    {
        get => _isDataSourceReadOnly;
        private set
        {
            if (!SetProperty(ref _isDataSourceReadOnly, value)) return;
            RaisePropertyChanged(nameof(CanRegisterNewProductHere));
            RaisePropertyChanged(nameof(DataSourceReadOnlyNotice));
        }
    }

    /// <summary>新規登録の操作を出せるかどうかです。</summary>
    public bool CanRegisterNewProductHere => !IsDataSourceReadOnly;

    /// <summary>読み取り専用のときに新規登録の枠へ出す案内です。</summary>
    public string DataSourceReadOnlyNotice => IsDataSourceReadOnly
        ? "SQL接続中は製品を登録できません。設定タブでデータ元を「CSVフォルダ」に切り替えてください。"
        : string.Empty;

    /// <summary>いま読んでいるデータ元の説明です。</summary>
    public string DataSourceDescription
    {
        get => _dataSourceDescription;
        private set => SetProperty(ref _dataSourceDescription, value);
    }

    public AsyncCommand TestSqlConnectionCommand { get; }
    public AsyncCommand SwitchDataSourceCommand { get; }
```

操作:

```csharp
    /// <summary>
    /// SQL Serverへ接続できるかだけを確かめます。データ元は切り替えません。
    /// 切り替えは「切り替えて読み直す」（SwitchDataSourceAsync）の役目です。
    /// </summary>
    public async Task TestSqlConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunSqlAsync(switchOver: false, cancellationToken).ConfigureAwait(false);
        if (result is null) return;

        await SetSettingsStatusAsync(
            result.IsSuccess ? result.Value! : result.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
        // 確認だけなので、成功していてもCSVへは戻しません。切替器の状態は変えません。
        await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>選んでいるデータ元へ実際に切り替え、読み直します。</summary>
    public async Task SwitchDataSourceAsync(CancellationToken cancellationToken = default)
    {
        if (_dataSourceConnector is null)
        {
            await SetSettingsStatusAsync("データ元を切り替える準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!UseSqlServer)
        {
            _dataSourceConnector.UseCsvFolder(DataFolderPath ?? string.Empty);
            await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
            await SetSettingsStatusAsync(
                $"CSVフォルダに切り替えました。{DataSourceDescription}", cancellationToken).ConfigureAwait(false);
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
        await SetSettingsStatusAsync(
            $"SQL Serverに切り替えました。{result.Value!}", cancellationToken).ConfigureAwait(false);
        await RefreshCandidatesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 入力を検査してからSQLへ接続します。入力が不足していればnullを返します。
    /// switchOverがfalseなら接続を確かめるだけで、データ元は変えません。
    /// </summary>
    private async Task<OperationResult<string>?> RunSqlAsync(bool switchOver, CancellationToken cancellationToken)
    {
        if (_dataSourceConnector is null)
        {
            await SetSettingsStatusAsync("データ元を切り替える準備ができていません。", cancellationToken).ConfigureAwait(false);
            return null;
        }

        // 接続文字列の組み立てと同じ検査をここでも通し、
        // 空欄のまま接続を試みて分かりにくい失敗になるのを防ぎます。
        var connectionString = SqlConnectionStringFactory.Build(SqlServerName, SqlDatabaseName);
        if (!connectionString.IsSuccess)
        {
            await SetSettingsStatusAsync(connectionString.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var server = SqlServerName.Trim();
        var database = SqlDatabaseName.Trim();
        return switchOver
            ? await _dataSourceConnector.UseSqlServerAsync(server, database, cancellationToken).ConfigureAwait(false)
            : await _dataSourceConnector.TestSqlServerAsync(server, database, cancellationToken).ConfigureAwait(false);
    }

    private Task RefreshDataSourceStateAsync(CancellationToken cancellationToken) =>
        _ui.InvokeAsync(() =>
        {
            IsDataSourceReadOnly = _dataSourceConnector is not null && !_dataSourceConnector.SupportsWrite;
        }, cancellationToken);
```

`ApplySettings` へ追加する。

```csharp
        SqlServerName = settings.Data.SqlServerName ?? string.Empty;
        SqlDatabaseName = settings.Data.SqlDatabaseName ?? string.Empty;
        UseSqlServer = string.Equals(settings.Data.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);
```

`BuildSettingsFromEditor` の `DataSettings` を書き換える。

```csharp
        return new BuiSettings(
            new DataSettings(UseSqlServer ? "SqlServer" : "GoldStandard",
                DataFolderPath ?? string.Empty, PriceCsvPath ?? string.Empty,
                AssemblyCsvPath ?? string.Empty,
                priorData?.IdAliasCsvPath, priorData?.ProductColumns ?? CsvColumnMapping.GoldStandardDefault,
                priorData?.PriceColumns ?? PriceColumnMapping.PocDefault,
                priorData?.CandidateFilterColumns ?? Array.Empty<string>(),
                priorData?.NewProductColumns ?? Array.Empty<string>(),
                priorData?.NewProductRequiredColumns ?? Array.Empty<string>(),
                priorData?.DuplicateCompareColumns ?? Array.Empty<string>(),
                priorData?.NewProductIdPrefix ?? string.Empty,
                priorData?.NewProductIdDigits ?? 0,
                SqlServerName ?? string.Empty,
                SqlDatabaseName ?? string.Empty),
```

- [ ] **Step 5: テスト用の組み立てを足す**

`DataSourceViewModelTests_データ元切替画面テスト.cs` の名前空間末尾へ追記する。

```csharp
internal static class DataSourceFactory
{
    public static BuiDockPaneViewModel Create(IDataSourceConnector connector) => new(
        new NoProducts(),
        new NoPrices(),
        new NoSelection(),
        new NoAudit(),
        new Immediate(),
        new Bui.Llm.DisabledLlmProvider(),
        new Bui.Metadata.AddinMetadata("BUI", "Building Unit Intelligence", "説明", "作成者", "部署", "会社", "2025"),
        "0.1.0", "2026-09-08 UTC",
        dataSourceConnector: connector);

    private sealed class NoProducts : IProductRepository
    {
        public Task<DataSourceStatus> GetStatusAsync(CancellationToken c) =>
            Task.FromResult(new DataSourceStatus(true, 0, null, Array.Empty<string>()));
        public Task<ProductRecord?> GetByIdAsync(string id, CancellationToken c) => Task.FromResult<ProductRecord?>(null);
        public Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery q, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<ProductRecord>>(Array.Empty<ProductRecord>());
        public Task UpsertAsync(ProductRecord r, CancellationToken c) => Task.CompletedTask;
        public Task RemoveAsync(string id, CancellationToken c) => Task.CompletedTask;
    }

    private sealed class NoPrices : IPriceRepository
    {
        public Task<DataSourceStatus> GetStatusAsync(CancellationToken c) =>
            Task.FromResult(new DataSourceStatus(true, 0, null, Array.Empty<string>()));
        public Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery q, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<PriceRecord>>(Array.Empty<PriceRecord>());
        public Task UpsertAsync(PriceRecord r, CancellationToken c) => throw new NotSupportedException();
    }

    private sealed class NoSelection : ISelectionDetailsService
    {
        public Task<SelectionDetails> LoadAsync(SelectionSnapshot s, CancellationToken c) =>
            Task.FromResult(new SelectionDetails(s, null, null, Array.Empty<AssemblyLayerRecord>(), Array.Empty<string>()));
    }

    private sealed class NoAudit : IAuditQueryService
    {
        public Task<IReadOnlyList<AuditIssue>> AuditAsync(CancellationToken c) =>
            Task.FromResult<IReadOnlyList<AuditIssue>>(Array.Empty<AuditIssue>());
    }

    private sealed class Immediate : IUiDispatcher
    {
        public Task InvokeAsync(Action action, CancellationToken c) { action(); return Task.CompletedTask; }
    }
}
```

- [ ] **Step 6: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add data source switching commands to the pane"
```

---

### Task 8: 画面（設定タブとSQL接続中の新規登録）

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SettingsView_設定.xaml`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml`

**Interfaces:**
- Consumes: Task 7 の `UseSqlServer` / `SqlServerName` / `SqlDatabaseName` / `IsDataSourceReadOnly` / `CanRegisterNewProductHere` / `DataSourceReadOnlyNotice` / `DataSourceDescription` / `TestSqlConnectionCommand` / `SwitchDataSourceCommand`
- Produces: 画面のみ。他タスクは依存しない

- [ ] **Step 1: 設定タブへデータ元の枠を足す**

`SettingsView_設定.xaml` の「データ設定」の見出しの直後、
データフォルダの入力の**前**へ挿入する。

```xml
   <TextBlock Text="データ元" FontWeight="SemiBold" Margin="0,6,0,2"/>
   <!--
     CSVとSQLを何度も行き来して検証するため、Revitを再起動せずに切り替わります。
     切り替えは「切り替えて読み直す」を押したときだけ起こります。
     ラジオを選んだだけでは変わりません。押し忘れに気づけるよう、
     いま読んでいる出どころを下に必ず表示します。
   -->
   <RadioButton Content="CSVフォルダ" GroupName="DataSourceKind"
                IsChecked="{Binding UseSqlServer, Converter={StaticResource InverseBool}}"/>
   <RadioButton Content="SQL Server" GroupName="DataSourceKind"
                IsChecked="{Binding UseSqlServer}" Margin="0,2,0,0"/>
   <Grid Margin="16,4,0,0" IsEnabled="{Binding UseSqlServer}">
    <Grid.ColumnDefinitions>
     <ColumnDefinition Width="120"/>
     <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
     <RowDefinition Height="Auto"/>
     <RowDefinition Height="Auto"/>
     <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <TextBlock Grid.Row="0" Grid.Column="0" Text="サーバー名" VerticalAlignment="Center"/>
    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding SqlServerName, UpdateSourceTrigger=PropertyChanged}"/>
    <TextBlock Grid.Row="1" Grid.Column="0" Text="データベース名" VerticalAlignment="Center" Margin="0,4,0,0"/>
    <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding SqlDatabaseName, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,0"/>
    <TextBlock Grid.Row="2" Grid.Column="0" Text="認証" VerticalAlignment="Center" Margin="0,4,0,0"/>
    <TextBlock Grid.Row="2" Grid.Column="1" Text="Windows認証（固定）" VerticalAlignment="Center" Margin="0,4,0,0"/>
   </Grid>
   <WrapPanel Margin="16,6,0,0">
    <Button Content="接続を確認" Command="{Binding TestSqlConnectionCommand}" Margin="0,0,6,0"
            IsEnabled="{Binding UseSqlServer}"
            ToolTip="接続できるかだけを試します。データ元は切り替えません。"/>
    <Button Content="切り替えて読み直す" Command="{Binding SwitchDataSourceCommand}"
            ToolTip="選んでいるデータ元へ実際に切り替えます。Revitの再起動は不要です。"/>
   </WrapPanel>
   <TextBlock Text="{Binding DataSourceDescription, StringFormat=いま読んでいるデータ元: {0}}"
              TextWrapping="Wrap" Margin="16,4,0,8" FontWeight="SemiBold"/>
   <Separator Margin="0,4,0,8"/>
```

`InverseBool` コンバーターが必要なので、`SettingsView_設定.xaml` の
`UserControl.Resources` へ足す。存在しなければ `UserControl.Resources` ごと作る。

```xml
 <UserControl.Resources>
  <ui:InverseBooleanConverter x:Key="InverseBool"/>
 </UserControl.Resources>
```

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/InverseBooleanConverter_真偽反転.cs` を作る。

```csharp
// このファイルの目的:
//   「SQL Serverを使うか」1つの値で、2つのラジオボタンを排他にします。
//   専用の列挙を足すより、既存のbool 1つで済ませます。

using System.Globalization;
using System.Windows.Data;

namespace Bui.UI;

/// <summary>真偽を反転します。ラジオボタンの排他に使います。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}
```

`SettingsView_設定.xaml` の `UserControl` タグに `xmlns:ui="clr-namespace:Bui.UI"` が
無ければ足す。

- [ ] **Step 2: 検索タブの新規登録をSQL接続中は止める**

`SearchView_検索.xaml` の `<Expander Header="候補が無いので新規登録" ...>` の
中身の先頭（`<StackPanel Margin="0,6,0,0">` の直後）へ挿入する。

```xml
     <!--
       読み取り専用のデータ元（SQL接続中）では、押せないボタンを黙って置きません。
       なぜ押せないかと、どうすれば押せるかを必ず添えます。
     -->
     <TextBlock Text="{Binding DataSourceReadOnlyNotice}" TextWrapping="Wrap"
                Foreground="#FFB00020" FontWeight="SemiBold" Margin="0,0,0,6"
                Visibility="{Binding IsDataSourceReadOnly, Converter={StaticResource BoolToVisibility}}"/>
```

同じ `Expander` の中の入力欄とボタンをまとめて無効化する。
`<ItemsControl ItemsSource="{Binding NewProductFields}">` から
最後の注意書き `TextBlock` までを `<StackPanel IsEnabled="{Binding CanRegisterNewProductHere}">` で囲む。

- [ ] **Step 3: テストが通ることを確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
```

期待: 全件合格（終了コード0）。XAMLはテスト対象外だが、退行が無いことを確かめる。

- [ ] **Step 4: XAMLに壊れが無いか目視で確認する**

```bash
grep -c "UserControl" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SettingsView_設定.xaml"
grep -c "StackPanel" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml"
```

開始タグと終了タグの数が合っていることを確認する。
**WPFのコンパイルはこのPCでできない。目視確認どまりであることを報告に明記する。**

- [ ] **Step 5: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: add the data source switch to the settings tab"
```

---

### Task 9: 組み立て・配置・手順書

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Data_データ/RevitDataSourceConnector_データ元接続.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`
- Modify: `BUI_Addin_CSharpアドイン/scripts_開発補助/DeployDev_開発用配置.ps1`
- Modify: `BUI_Addin_CSharpアドイン/scripts_開発補助/CheckDeploy_配置診断.ps1`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`

**Interfaces:**
- Consumes: Task 2 の `DataSourceSwitch`、Task 4 の `SqlConnectionStringFactory`、Task 6 の `SqlClientTableReader` / `SqlServerTableSource`、Task 7 の `IDataSourceConnector`
- Produces: なし（最終タスク）

- [ ] **Step 1: 実際の接続窓口を作る**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Data_データ/RevitDataSourceConnector_データ元接続.cs`

```csharp
// このファイルの目的:
//   設定タブの操作を、実際のデータ元の差し替えにつなげます。
//
// なぜ起動経路から呼ばないのか:
//   Microsoft.Data.SqlClient はRevitのプロセスへ読み込まれます。
//   Revitが同梱する別バージョンとぶつかる可能性があり、こちらでは確かめられません。
//   利用者がSQL Serverへ切り替えたときにだけ触れる置き方にしておけば、
//   読み込みに失敗してもBUIパネル自体は今までどおり開きます。
//
// 修正時の影響:
//   接続に失敗したらデータ元を変えないでください。
//   変えると「どちらを読んでいるか分からない」状態になります。

using Bui.Common;
using Bui.Configuration;
using Bui.Data;
using Bui.Data.GoldStandard;
using Bui.Data.SqlServer;
using Bui.UI;

namespace Bui.Revit.Data;

/// <summary>設定タブからデータ元を差し替えます。</summary>
internal sealed class RevitDataSourceConnector : IDataSourceConnector
{
    private readonly DataSourceSwitch _switch;

    public RevitDataSourceConnector(DataSourceSwitch dataSourceSwitch) =>
        _switch = dataSourceSwitch ?? throw new ArgumentNullException(nameof(dataSourceSwitch));

    public bool SupportsWrite => _switch.SupportsWrite;

    /// <summary>接続して件数を数えるだけです。データ元は変えません。</summary>
    public Task<OperationResult<string>> TestSqlServerAsync(
        string serverName, string databaseName, CancellationToken cancellationToken) =>
        Task.FromResult(TryRead(serverName, databaseName, out _));

    /// <summary>接続に成功した場合だけデータ元を差し替えます。</summary>
    public Task<OperationResult<string>> UseSqlServerAsync(
        string serverName, string databaseName, CancellationToken cancellationToken)
    {
        var result = TryRead(serverName, databaseName, out var source);
        // 失敗したら差し替えません。中途半端な状態を作らないためです。
        if (result.IsSuccess && source is not null) _switch.SwitchTo(source);
        return Task.FromResult(result);
    }

    public void UseCsvFolder(string folderPath) => _switch.SwitchTo(new CsvTableSource(folderPath));

    /// <summary>実際に読んでみて、成功なら件数の要約を返します。</summary>
    private OperationResult<string> TryRead(
        string serverName, string databaseName, out SqlServerTableSource? source)
    {
        source = null;
        var connectionString = SqlConnectionStringFactory.Build(serverName, databaseName);
        if (!connectionString.IsSuccess)
        {
            return OperationResult<string>.Failure(
                connectionString.Error!.Code, connectionString.Error.UserMessage, connectionString.Error.TechnicalDetail);
        }

        // 画像はSQL接続中もローカルのファイルとして扱うため、いまのデータフォルダを引き継ぎます。
        var candidate = new SqlServerTableSource(
            () => SqlClientTableReader.Open(connectionString.Value!, databaseName),
            _switch.DataFolderPath, serverName, databaseName);

        var set = candidate.Load();
        if (!set.HasProducts)
        {
            var reason = set.Problems.Count > 0
                ? string.Join(Environment.NewLine, set.Problems.Take(3))
                : "Products テーブルを読み取れませんでした。";
            return OperationResult<string>.Failure("SQL_CONNECT_FAILED", reason, "HasProducts is false");
        }

        source = candidate;
        return OperationResult<string>.Success(
            $"接続できました。製品 {set.Rows(GoldStandardTables.Products).Count}件 / "
            + $"メーカー {set.Rows(GoldStandardTables.Manufacturers).Count}件 / "
            + $"属性 {set.Rows(GoldStandardTables.ProductSpecs).Count}件");
    }
}
```

`GoldStandardTables` は `internal` なのでアセンブリ内から参照できる。

- [ ] **Step 2: 組み立てへつなぐ**

`BuiDockPaneComposition_画面構成.cs` を変更する。

起動時のデータ元は、保存されている `Provider` に関わらず**必ずCSVから始める**。
SQLのアセンブリ読み込みに失敗しても、BUIパネルが開かなくなることを避けるためである。
利用者は設定タブで「切り替えて読み直す」を押してSQLへ移る。

```csharp
        // 起動時は必ずCSVから始めます。SQL用のアセンブリが読めない環境でも
        // BUIパネルが開かなくなることを避けるためです（設計 §5）。
        // 保存された Provider が "SqlServer" の場合は、その旨を起動時の警告に出します。
        var dataSource = DataSourceSwitch.ForCsvFolder(dataFolder);
        var products = new GoldStandardProductRepository(dataSource);
        var productDetails = new GoldStandardDetailSource(dataSource);
```

`startupWarnings` はこの時点ではまだ宣言されていない。
`var startupWarnings = new List<string>(loaded.Warnings);` の**直後**へ次を足す。

```csharp
        if (string.Equals(loaded.Settings.Data.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            startupWarnings.Add(
                "前回はSQL Serverを使う設定でした。起動時はCSVフォルダから始めます。"
                + "設定タブで「切り替えて読み直す」を押してください。");
        }
```

既存の `var prices = new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns);` を
次へ置き換え、SQL接続中に単価CSVを読みに行かないようにする。

```csharp
        var prices = new SwitchingPriceRepository(
            new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns), dataSource);
```

`new BuiDockPaneViewModel(...)` の引数末尾へ足す。

```csharp
            layerAssignment: identifierWorkflow,
            dataSourceConnector: new RevitDataSourceConnector(dataSource));
```

`using Bui.Revit.Data;` をファイル冒頭へ足す。

- [ ] **Step 3: 配置の許可一覧へSQLのDLLを足す**

`DeployDev_開発用配置.ps1` の `Get-BuiDeployAllowlist` は6ファイルだけを配置している。
このままではSQL用のDLLが配置先へ届かず、SQL接続が必ず失敗する。

```powershell
function Get-BuiDeployAllowlist {
    # ローカル設定、認証情報、CSV、RVT、ログ、バックアップ、一時ファイルはこの一覧に入れません。
    return @(
        'Bui.dll',
        'Bui.pdb',
        'Bui.deps.json',
        'Config\AddinMetadata_アドイン情報.json',
        'Config\Settings.Default_共通設定.json',
        'Resources_画像と定義\SharedParameters_共有パラメータ.txt'
    )
}

function Get-BuiSqlDeployPattern {
    # SQL接続用のDLLです。Microsoft.Data.SqlClientは依存DLLを連れてくるため、
    # 個別に列挙せずパターンで拾います。これらが配置先に無いとSQL接続が必ず失敗します。
    return @(
        'Microsoft.Data.SqlClient*.dll',
        'Microsoft.Identity.Client*.dll',
        'Microsoft.IdentityModel*.dll',
        'System.Configuration.ConfigurationManager.dll',
        'System.Security.Cryptography.ProtectedData.dll',
        'System.Runtime.Caching.dll',
        'System.Diagnostics.EventLog.dll',
        'Azure.Core.dll',
        'Azure.Identity.dll',
        'runtimes\win-x64\native\*.dll'
    )
}
```

配置処理は許可一覧を2回まわしている（ステージへのコピーと、配置先へのコピー）。
一覧を1つの変数へまとめ、両方のループがそれを使うようにする。

`New-Item -ItemType Directory -Force -Path $stagedAddin -WhatIf:$false | Out-Null` の直後へ足す。

```powershell
        # 固定の許可一覧に、ビルド出力にあるSQL用DLLを足した一覧を1回だけ作ります。
        # 2つのループが同じ一覧を使わないと、ステージには入ったのに配置先へ届かない、
        # という分かりにくい状態になります。
        $deployFiles = @(Get-BuiDeployAllowlist) + @(Get-BuiSqlDeployRelativePaths -OutputPath $output)
```

そのうえで、`foreach ($relativePath in Get-BuiDeployAllowlist) {` の**2か所**を
`foreach ($relativePath in $deployFiles) {` へ置き換える。

先に定義した `Get-BuiSqlDeployPattern` は使わないので削除し、代わりに相対パスを集める
関数を定義する。

```powershell
function Get-BuiSqlDeployRelativePaths {
    param([Parameter(Mandatory)][string]$OutputPath)

    # SQL接続用のDLLです。Microsoft.Data.SqlClientは依存DLLを連れてくるため、
    # 個別に列挙せずパターンで拾います。
    # これらが配置先に無いと、SQL Serverへの切り替えが必ず失敗します。
    # CSVだけで使う場合は無くても動くため、見つからなくてもエラーにしません。
    $patterns = @(
        'Microsoft.Data.SqlClient*.dll',
        'Microsoft.Identity.Client*.dll',
        'Microsoft.IdentityModel*.dll',
        'Microsoft.SqlServer.Server.dll',
        'System.Configuration.ConfigurationManager.dll',
        'System.Security.Cryptography.ProtectedData.dll',
        'System.Runtime.Caching.dll',
        'System.Diagnostics.EventLog.dll',
        'System.Text.Encodings.Web.dll',
        'Azure.Core.dll',
        'Azure.Identity.dll'
    )

    $found = New-Object System.Collections.Generic.List[string]
    foreach ($pattern in $patterns) {
        foreach ($file in Get-ChildItem -LiteralPath $OutputPath -Filter $pattern -File -ErrorAction SilentlyContinue) {
            $found.Add($file.Name)
        }
    }

    # ネイティブのSNI DLLはフォルダ構造ごと必要です。
    $nativeRelative = 'runtimes\win-x64\native'
    $nativeRoot = Join-Path $OutputPath $nativeRelative
    if (Test-Path -LiteralPath $nativeRoot -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $nativeRoot -Filter '*.dll' -File -ErrorAction SilentlyContinue) {
            $found.Add((Join-Path $nativeRelative $file.Name))
        }
    }

    return $found.ToArray()
}
```

既存の必須ファイル判定（`if ($relativePath -eq 'Bui.dll' -or ...)`）は変更しない。
SQL用DLLはこの条件に当たらないため、見つからなければ静かに飛ばされる。

`BUI_Addin_CSharpアドイン/tests_テスト/PowerShell/DeployDev.Tests.ps1` が
許可一覧を前提にしている場合は、SQL用DLLが無い出力でも従来どおり通ることを確認する。

```bash
grep -n "Allowlist" "BUI_Addin_CSharpアドイン/tests_テスト/PowerShell/DeployDev.Tests.ps1"
```

- [ ] **Step 4: 配置診断へSQLのDLLの確認を足す**

`CheckDeploy_配置診断.ps1` に、配置先へ `Microsoft.Data.SqlClient.dll` があるかの
確認を足す。無い場合は次の一文を出す。

```
[NG] SQL接続用のDLLが配置されていません。SQL Serverへは接続できません。
     DeployDev_開発用配置.ps1 を実行し直してください。
```

CSVだけで使う場合は問題ないため、`[NG]` ではなく `[情報]` として扱い、
診断全体は失敗させない。

- [ ] **Step 5: 手順書へ段階11を足す**

`操作手順_会社PC_次にやること.txt` の末尾へ、次の段階を追記する。
既存の書式（`====` の区切り、`■ここまでの確認`）に合わせる。

```
================================================================================
 段階11 : SQL Serverへ切り替えて、同じ表示が出るか確かめる
================================================================================

 CSVでの確認が終わってから実施してください。
 SQL接続中は読み取り専用です。新規登録とマスタ追加はできません。

11-1. SQL Server Management Studio などで、GoldStandardDB に
      GoldStandard_SQLServer_FullBuild_v2.sql を流し、データが入っている
      ことを確認します。

11-2. Revit で BUI パネルを開き、【設定】タブを開きます。

■ここまでの確認
   「データ元」の欄があり、「CSVフォルダ」が選ばれていること。
   その下に「いま読んでいるデータ元: CSVフォルダ: C:\BUI_PoCData」と
   出ていること。

11-3.「SQL Server」を選び、次を入力します。

      サーバー名        localhost\sqlexpress
      データベース名    GoldStandardDB

11-4.「接続を確認」を押します。

■ここまでの確認
   「接続できました。製品 ○件 / メーカー ○件 / 属性 ○件」と出ること。
   件数がCSVと同じであること。

   失敗した場合は、表示された文章をそのまま全部貼り付けてください。
   SQL Server からの応答も一緒に出ています。

11-5.「切り替えて読み直す」を押します。

■ここまでの確認
   「SQL Serverに切り替えました。製品 ○件…」と出ること。
   「いま読んでいるデータ元」が
   「SQL Server: localhost\sqlexpress / GoldStandardDB」に変わること。

11-6.【検索】タブで段階8と同じ絞り込みを行い、同じ候補が出ることを
      確認します。

■ここまでの確認
   候補の件数と並び順がCSVのときと同じであること。
   違う場合は、CSVとSQLそれぞれの画面を記録してください。

11-7.【概要】タブで、IDが入っている要素を選びます。

■ここまでの確認
   製品カードの内容がCSVのときと同じであること。
   複合レイヤーの行をクリックして、層別のカードも出ること。

11-8.【検索】タブの「候補が無いので新規登録」を開きます。

■ここまでの確認
   赤字で「SQL接続中は製品を登録できません。設定タブでデータ元を
   「CSVフォルダ」に切り替えてください。」と出ていること。
   入力欄とボタンが操作できないこと。

11-9. 層にProduct_IDを付け、手順1→手順2を実行します。

■ここまでの確認
   Revitのパラメータに値が入ること。
   Assembly_ID も採番され、構成CSVに行が増えること
   （SQL接続中でもAssembly_IDは構成CSVへ書きます）。

11-10.【設定】タブで「CSVフォルダ」を選び、「切り替えて読み直す」を
      押します。

■ここまでの確認
   「CSVフォルダに切り替えました」と出ること。
   新規登録の赤字が消え、入力欄が操作できるようになること。
```

- [ ] **Step 6: Troubleshooting へSQLの節を足す**

`Troubleshooting_問題解決.md` へ次の節を足す。挿入位置は
`## 共有項目の競合が出る` の直前とする。

```markdown
## SQL Serverに接続できない

「接続を確認」の結果に**理由が出ています**。まずそこを読んでください。
SQL Server からの応答も一緒に表示されます。

| 出ている文 | 原因 | 対処 |
| --- | --- | --- |
| インスタンス名が正しいか、SQL Server Express が起動しているか | サーバーに到達できない | サービスで `SQL Server (SQLEXPRESS)` が実行中か確認。サーバー名は `localhost\sqlexpress` |
| データベース「…」を開けません | DB名が違う、または参照権限が無い | 名前を確認。管理者にこのWindowsユーザーへの参照権限を依頼 |
| このWindowsユーザーではログインできません | ログイン自体が拒否された | 同上 |
| SQL接続に必要な部品を読み込めませんでした | 配置先にSQL用のDLLが無い | 配置診断を実行し、`DeployDev_開発用配置.ps1` をやり直す |

## SQL接続中に新規登録できない

**仕様です。**SQL接続中は読み取り専用にしています。
設定タブでデータ元を「CSVフォルダ」に切り替えると登録できます。

Assembly_ID の採番だけは SQL接続中でも動きます。
書き先が構成CSVで、GoldStandardDB には構成テーブルが無いためです。

## 切り替えたのに古いデータが出る

「切り替えて読み直す」を押していない可能性があります。
**ラジオボタンを選んだだけでは切り替わりません。**
設定タブの「いま読んでいるデータ元」が、選んだ方になっているか確認してください。
```

- [ ] **Step 7: 全体を確認する**

```bash
.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests" -v q --nologo
grep -rn "^<<<<<<<\|^>>>>>>>" --include=*.cs --include=*.md --include=*.xaml --include=*.txt .
grep -rn "GoldStandardCsvSet" --include=*.cs "BUI_Addin_CSharpアドイン"
```

期待: テスト全件合格（終了コード0）、競合マーカーなし、古い名前の残りなし。

- [ ] **Step 8: コミット**

```bash
git add "BUI_Addin_CSharpアドイン"
git commit -m "feat: wire the SQL Server data source into the pane and deployment"
```

---

## 実装後に必ず報告すること

- **Revit依存とWPFのコードはこのPCでコンパイルできない。**Task 6 の `SqlClientTableReader`、
  Task 8 のXAML、Task 9 の `RevitDataSourceConnector` と組み立ては目視確認どまりである
- `Microsoft.Data.SqlClient` がRevitの中で動くかは**会社PCでしか分からない**
- 会社PCでの手順は段階11
