# BUI Product_ID 候補選択・付与 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Revitの要素に対し、製品データをカスケード絞り込みして候補から選び、同一タイプ全体へProduct_IDを付与できるようにする。候補に無い製品はCSVへ新規登録してから付与できる。

**Architecture:** 絞り込みはRevit非依存の純粋関数として共有ロジックへ置き、`ICandidateSource` の背後に隠す。画面は実装を知らないため、将来のSQL Server実装へ差し替えても変更が要らない。Revitへの書き込みは既存の `IIdentifierWorkflowService`（プレビュー → 使い捨てトークン → External Event → 単一トランザクション）をそのまま使い、新しい書込み経路は作らない。

**Tech Stack:** C# 12、.NET 8 (`net8.0-windows`)、WPF、Autodesk Revit 2025 API、MSTest

**Spec:** `docs/superpowers/specs/2026-09-04-bui-product-id-selection-design.md`

## Global Constraints

- 既存の `建材・単価情報.extension`、`維持管理情報.extension`、元CSVを変更・削除しない。
- 新規ファイルは `BUI_Addin_CSharpアドイン` 配下だけに作成する。
- C#ファイル名は `English_日本語.cs`、主要クラスは原則1ファイル1つとする。
- 名前空間ルートは `Bui`。合意なく既存の公開型名・メソッド名を変更しない。
- 各ファイル冒頭と主要処理内に、目的・理由・修正時の影響が分かる日本語コメントを記載する。
- Revitへの書き込みはExternal Eventだけで行う。共有ロジックは `Autodesk.Revit.*` と `System.Windows.*` を参照しない。
- 実装はTDDで進め、各タスク終了時に全自動テストを実行して小さくコミットする。
- テスト実行は私用PCで可能。`./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
- コミット前に、テストの終了コードで合否を判定する。パイプで繋いで見落とさない。
- コミット前に、追跡ファイルへ競合マーカーと制御文字が混入していないことを走査する。
- Windowsパスをスクリプト経由で書くときは必ずraw文字列を使う。`\2025` は8進エスケープとして壊れる。

---

## File Map

```text
BUI_Addin_CSharpアドイン/
├─ src_ソース/
│  ├─ Bui.Shared_共有ロジック/
│  │  ├─ Data_データ窓口/
│  │  │  ├─ ProductFieldAccessor_製品列読出.cs        新規
│  │  │  ├─ CandidateModels_候補データ.cs             新規
│  │  │  ├─ ProductCandidateFilter_候補絞り込み.cs    新規
│  │  │  ├─ ICandidateSource_候補データ窓口.cs        新規
│  │  │  └─ InMemoryCandidateSource_全件絞り込み.cs   新規
│  │  ├─ Products_製品/
│  │  │  ├─ ProductIdAllocator_製品ID採番.cs          新規
│  │  │  ├─ ProductDuplicateFinder_重複候補検出.cs    新規
│  │  │  ├─ NewProductDraft_新規登録入力.cs           新規
│  │  │  └─ ProductRegistrationCoordinator_新規登録調整.cs 新規
│  │  ├─ Configuration_設定/BuiSettings_BUI設定.cs    変更
│  │  └─ UI_画面/BuiDockPaneViewModel_パネル状態.cs   変更
│  └─ Bui.Addin/
│     ├─ UI_画面/Tabs/SearchView_検索.xaml            変更
│     └─ UI_画面/BuiDockPaneComposition_画面構成.cs   変更
├─ config_設定例/Settings.Default_共通設定.json        変更
└─ tests_テスト/Bui.Tests/                            各タスクで追加
```

責務の分け方。`ProductFieldAccessor` は「列名から値を引く」だけ。`ProductCandidateFilter` は「絞り込みと選択肢の算出」だけ。`InMemoryCandidateSource` は「リポジトリから全件を取り、フィルターへ渡す」だけ。この3つを分けることで、カスケードの仕様変更がリポジトリに波及しない。

---

### Task 1: 列名から値を引く仕組み

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductFieldAccessor_製品列読出.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductFieldAccessorTests_製品列読出テスト.cs`

**Interfaces:**
- Consumes: 既存の `Bui.Products.ProductRecord`、`Bui.Data.CsvColumnMapping`。
- Produces:
  - `Bui.Data.ProductFieldAccessor.Read(ProductRecord record, CsvColumnMapping mapping, string column) -> string`
  - `Bui.Data.ProductFieldAccessor.Fields(ProductRecord record, CsvColumnMapping mapping) -> IReadOnlyDictionary<string, string>`

`Fields` は既存の `CsvProductRepository` が内部に持つ同名の処理と同じものである。文字列検索の対象範囲を決める重要な処理が2箇所にあると、片方だけ直して食い違う。このタスクで共通化し、リポジトリ側もこれを使うように差し替える。

`ProductRecord` は対応付け済みの項目（`ProductName` など）と `AdditionalAttributes` に分かれている。絞り込み項目の「カテゴリー」「用途」「構造」は追加属性側、「製品名」「メーカー名」「型番」は対応付け側にある。列名だけを受け取って両方から引ける窓口が要る。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは、絞り込みが列名だけで値を引けることを保証します。
// 対応付け済みの項目と追加属性のどちらにあっても同じように読めないと、
// カスケード絞り込みが一部の項目でだけ動かないという分かりにくい不具合になります。
using Bui.Data;
using Bui.Products;

namespace Bui.Tests.Data_データ;

[TestClass]
public class ProductFieldAccessorTests_製品列読出テスト
{
    private static ProductRecord Record() => new(
        "BM-0001", "床タイル", "メーカーA", "VT-100", "枚", null,
        new Dictionary<string, string> { ["カテゴリー"] = "内装", ["構造"] = " 単層 " });

    [TestMethod]
    public void Read_対応付け済みの列を引ける()
    {
        var mapping = CsvColumnMapping.PocDefault;

        Assert.AreEqual("BM-0001", ProductFieldAccessor.Read(Record(), mapping, mapping.ProductIdColumn));
        Assert.AreEqual("床タイル", ProductFieldAccessor.Read(Record(), mapping, mapping.ProductNameColumn));
        Assert.AreEqual("メーカーA", ProductFieldAccessor.Read(Record(), mapping, mapping.ManufacturerColumn));
        Assert.AreEqual("VT-100", ProductFieldAccessor.Read(Record(), mapping, mapping.ModelNumberColumn));
    }

    [TestMethod]
    public void Read_追加属性の列を引ける()
    {
        Assert.AreEqual("内装", ProductFieldAccessor.Read(Record(), CsvColumnMapping.PocDefault, "カテゴリー"));
    }

    [TestMethod]
    public void Read_前後の空白を取り除く()
    {
        Assert.AreEqual("単層", ProductFieldAccessor.Read(Record(), CsvColumnMapping.PocDefault, "構造"));
    }

    [TestMethod]
    public void Read_存在しない列は空文字を返す()
    {
        // CSVに無い列が設定されても例外にせず、その項目だけ空にします。
        Assert.AreEqual(string.Empty, ProductFieldAccessor.Read(Record(), CsvColumnMapping.PocDefault, "存在しない列"));
    }

    [TestMethod]
    public void Fields_対応付け済みと追加属性をまとめて返す()
    {
        // 文字列検索の対象範囲を決める処理です。ここから漏れた列は検索に掛かりません。
        // PocDefault.ProductIdColumn は "db_key" です。実データの列名（建材コード）は
        // 出荷設定JSON側の値なので、テストでは列名を直書きせず対応表から引きます。
        var mapping = CsvColumnMapping.PocDefault;
        var fields = ProductFieldAccessor.Fields(Record(), mapping);

        Assert.AreEqual("BM-0001", fields[mapping.ProductIdColumn]);
        Assert.AreEqual("床タイル", fields[mapping.ProductNameColumn]);
        Assert.AreEqual("内装", fields["カテゴリー"]);
        Assert.IsTrue(fields.ContainsKey(mapping.UpdatedAtColumn), "更新日を含めないと既存の検索と挙動が食い違います。");
    }

    [TestMethod]
    public void Read_列名がnullや空なら空文字を返す()
    {
        Assert.AreEqual(string.Empty, ProductFieldAccessor.Read(Record(), CsvColumnMapping.PocDefault, ""));
        Assert.AreEqual(string.Empty, ProductFieldAccessor.Read(Record(), CsvColumnMapping.PocDefault, null!));
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~ProductFieldAccessorTests"
```

Expected: `ProductFieldAccessor` が存在せずコンパイルエラー。

- [ ] **Step 3: 実装する**

```csharp
// このファイルは、列名だけを頼りに製品レコードから値を取り出すために存在します。
// ProductRecordは対応付け済みの項目と追加属性に分かれているため、
// 絞り込み側が両者の違いを意識しなくて済むよう、ここで吸収します。
// 修正時の影響: 対応付け項目を増やした場合、ここにも追加しないと
// その列だけ絞り込みで空扱いになります。
using Bui.Products;

namespace Bui.Data;

/// <summary>製品レコードから、列名を指定して値を取り出します。</summary>
public static class ProductFieldAccessor
{
    /// <summary>見つからない列は空文字を返します。設定に無い列でも機能を止めないためです。</summary>
    public static string Read(ProductRecord record, CsvColumnMapping mapping, string column)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(mapping);
        if (string.IsNullOrWhiteSpace(column)) return string.Empty;

        var value = ReadMapped(record, mapping, column)
            ?? (record.AdditionalAttributes.TryGetValue(column, out var extra) ? extra : null);

        return value?.Trim() ?? string.Empty;
    }

    private static string? ReadMapped(ProductRecord record, CsvColumnMapping mapping, string column)
    {
        if (Same(column, mapping.ProductIdColumn)) return record.ProductId;
        if (Same(column, mapping.ProductNameColumn)) return record.ProductName;
        if (Same(column, mapping.ManufacturerColumn)) return record.Manufacturer;
        if (Same(column, mapping.ModelNumberColumn)) return record.ModelNumber;
        if (Same(column, mapping.UnitColumn)) return record.Unit;
        return null;
    }

    /// <summary>
    /// 対応付け済みの項目と追加属性を、列名をキーにした1つの表へまとめます。
    /// 文字列検索の対象範囲はこの表が決めます。CsvProductRepositoryもこれを使います。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Fields(ProductRecord record, CsvColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(mapping);

        return new Dictionary<string, string>(record.AdditionalAttributes, StringComparer.Ordinal)
        {
            [mapping.ProductIdColumn] = record.ProductId,
            [mapping.ProductNameColumn] = record.ProductName,
            [mapping.ManufacturerColumn] = record.Manufacturer,
            [mapping.ModelNumberColumn] = record.ModelNumber,
            [mapping.UnitColumn] = record.Unit,
            [mapping.UpdatedAtColumn] =
                record.SourceUpdatedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
```

続けて、既存の `CsvProductRepository_CSV製品データ取得.cs` の `Fields` を削除し、呼び出しを差し替える。

```csharp
    private IEnumerable<string> Values(ProductRecord record) =>
        ProductFieldAccessor.Fields(record, mapping).Values;
```

`private Dictionary<string, string> Fields(ProductRecord record)` の定義ごと削除する。既存のテストが挙動を保証しているため、差し替え後に全テストが合格すれば同等である。

- [ ] **Step 4: テストを実行して合格を確認する**

Run: 上と同じコマンド。Expected: PASS。

- [ ] **Step 5: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductFieldAccessor_製品列読出.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductFieldAccessorTests_製品列読出テスト.cs"
git commit -m "feat: read product values by column name"
```

---

### Task 2: カスケード絞り込みの中核

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/CandidateModels_候補データ.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductCandidateFilter_候補絞り込み.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductCandidateFilterTests_候補絞り込みテスト.cs`

**Interfaces:**
- Consumes: Task 1の `ProductFieldAccessor.Read`。
- Produces:
  - `Bui.Data.CandidateQuery(IReadOnlyDictionary<string,string> Selections, string Text, int Limit = 200)`
  - `Bui.Data.CandidateFilterOptions(string Column, string Selected, IReadOnlyList<string> Values)`
  - `Bui.Data.CandidateView(IReadOnlyList<CandidateFilterOptions> Filters, IReadOnlyList<ProductRecord> Candidates, int TotalCount, bool IsTruncated)`
  - `Bui.Data.ProductCandidateFilter.Apply(IReadOnlyList<ProductRecord> products, CsvColumnMapping mapping, IReadOnlyList<string> filterColumns, CandidateQuery query) -> CandidateView`

仕様の中心。**項目Fの選択肢は、F以外のすべての絞り込みと文字列検索を適用した結果から作る。** 自分の選択は無視する。これが無いと、一度選んだ項目を別の値へ切り替えられなくなる。

`Selections` の値が空文字なら「(すべて)」を意味する。`(すべて)` という表示文字列は画面側が付ける。ここでは扱わない。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルはカスケード絞り込みの仕様を固定します。
// 特に「自分自身の選択は自分の選択肢に影響しない」は、
// 壊れると一度選んだ項目を選び直せなくなる重要な性質です。
using Bui.Data;
using Bui.Products;

namespace Bui.Tests.Data_データ;

[TestClass]
public class ProductCandidateFilterTests_候補絞り込みテスト
{
    private static readonly string[] Columns = { "カテゴリー", "メーカー名", "製品名" };

    private static ProductRecord P(string id, string category, string maker, string name) =>
        new(id, name, maker, string.Empty, string.Empty, null,
            new Dictionary<string, string> { ["カテゴリー"] = category });

    private static IReadOnlyList<ProductRecord> Products() => new[]
    {
        P("BM-0001", "内装", "メーカーA", "床タイル"),
        P("BM-0002", "内装", "メーカーB", "床タイル"),
        P("BM-0003", "外装", "メーカーA", "外壁パネル"),
    };

    private static CandidateView Apply(Dictionary<string, string>? selections = null, string text = "", int limit = 200) =>
        ProductCandidateFilter.Apply(Products(), CsvColumnMapping.PocDefault, Columns,
            new CandidateQuery(selections ?? new Dictionary<string, string>(), text, limit));

    private static IReadOnlyList<string> Values(CandidateView view, string column) =>
        view.Filters.Single(f => f.Column == column).Values;

    [TestMethod]
    public void Apply_未選択なら全件と全選択肢を返す()
    {
        var view = Apply();

        Assert.AreEqual(3, view.Candidates.Count);
        Assert.AreEqual(3, view.TotalCount);
        Assert.IsFalse(view.IsTruncated);
        CollectionAssert.AreEqual(new[] { "内装", "外装" }, Values(view, "カテゴリー").ToArray());
        CollectionAssert.AreEqual(new[] { "メーカーA", "メーカーB" }, Values(view, "メーカー名").ToArray());
    }

    [TestMethod]
    public void Apply_選択すると他項目の選択肢が狭まる()
    {
        var view = Apply(new Dictionary<string, string> { ["カテゴリー"] = "外装" });

        Assert.AreEqual(1, view.Candidates.Count);
        CollectionAssert.AreEqual(new[] { "メーカーA" }, Values(view, "メーカー名").ToArray());
    }

    [TestMethod]
    public void Apply_自分自身の選択は自分の選択肢を狭めない()
    {
        // これが崩れると、一度「外装」を選んだあとに「内装」へ戻せなくなります。
        var view = Apply(new Dictionary<string, string> { ["カテゴリー"] = "外装" });

        CollectionAssert.AreEqual(new[] { "内装", "外装" }, Values(view, "カテゴリー").ToArray());
        Assert.AreEqual("外装", view.Filters.Single(f => f.Column == "カテゴリー").Selected);
    }

    [TestMethod]
    public void Apply_選択値が候補から消えたら未選択へ戻す()
    {
        // 「外装」かつ「メーカーB」は存在しません。矛盾した状態を残さず、片方を解除します。
        var view = Apply(new Dictionary<string, string> { ["カテゴリー"] = "外装", ["メーカー名"] = "メーカーB" });

        Assert.AreEqual(string.Empty, view.Filters.Single(f => f.Column == "メーカー名").Selected);
        Assert.AreEqual(1, view.Candidates.Count);
    }

    [TestMethod]
    public void Apply_前後空白を無視して突き合わせる()
    {
        var view = Apply(new Dictionary<string, string> { ["メーカー名"] = "  メーカーA  " });

        Assert.AreEqual(2, view.Candidates.Count);
    }

    [TestMethod]
    public void Apply_英字は大文字小文字を無視して突き合わせる()
    {
        var products = new[] { P("BM-0001", "内装", "ABC", "床タイル") };
        var view = ProductCandidateFilter.Apply(products, CsvColumnMapping.PocDefault, Columns,
            new CandidateQuery(new Dictionary<string, string> { ["メーカー名"] = "abc" }, string.Empty));

        Assert.AreEqual(1, view.Candidates.Count);
    }

    [TestMethod]
    public void Apply_空文字は選択肢に含めない()
    {
        var products = new[] { P("BM-0001", "", "メーカーA", "床タイル") };
        var view = ProductCandidateFilter.Apply(products, CsvColumnMapping.PocDefault, Columns,
            new CandidateQuery(new Dictionary<string, string>(), string.Empty));

        Assert.AreEqual(0, Values(view, "カテゴリー").Count);
    }

    [TestMethod]
    public void Apply_文字列検索と併用できる()
    {
        var view = Apply(new Dictionary<string, string> { ["カテゴリー"] = "内装" }, text: "メーカーB");

        Assert.AreEqual("BM-0002", view.Candidates.Single().ProductId);
    }

    [TestMethod]
    public void Apply_上限を超えると打ち切り総数は保つ()
    {
        var many = Enumerable.Range(1, 250)
            .Select(i => P($"BM-{i:0000}", "内装", "メーカーA", $"製品{i}")).ToArray();

        var view = ProductCandidateFilter.Apply(many, CsvColumnMapping.PocDefault, Columns,
            new CandidateQuery(new Dictionary<string, string>(), string.Empty, 200));

        Assert.AreEqual(200, view.Candidates.Count);
        Assert.AreEqual(250, view.TotalCount);
        Assert.IsTrue(view.IsTruncated);
    }

    [TestMethod]
    public void Apply_CSVに無い列を指定しても止まらない()
    {
        var view = ProductCandidateFilter.Apply(Products(), CsvColumnMapping.PocDefault,
            new[] { "存在しない列" }, new CandidateQuery(new Dictionary<string, string>(), string.Empty));

        Assert.AreEqual(0, Values(view, "存在しない列").Count);
        Assert.AreEqual(3, view.Candidates.Count);
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~ProductCandidateFilterTests"
```

Expected: 型が存在せずコンパイルエラー。

- [ ] **Step 3: データ型を実装する**

`CandidateModels_候補データ.cs`:

```csharp
// このファイルは、画面と絞り込みの間でやり取りする形を定めます。
// 画面はこの3つの型だけを見て表示を組み立てるため、
// 絞り込みの実装をCSVからSQLへ差し替えても画面は変わりません。
// 修正時の影響: ここを変えると画面とSQL実装の両方に波及します。
using Bui.Products;

namespace Bui.Data;

/// <summary>選択中の絞り込み条件。値が空文字なら未選択（すべて）を意味します。</summary>
public sealed record CandidateQuery(
    IReadOnlyDictionary<string, string> Selections,
    string Text,
    int Limit = 200);

/// <summary>1つの絞り込み項目と、その時点で選べる値。表示用の「(すべて)」は画面側が付けます。</summary>
public sealed record CandidateFilterOptions(
    string Column,
    string Selected,
    IReadOnlyList<string> Values);

/// <summary>絞り込み結果。TotalCountは打ち切り前の総数です。</summary>
public sealed record CandidateView(
    IReadOnlyList<CandidateFilterOptions> Filters,
    IReadOnlyList<ProductRecord> Candidates,
    int TotalCount,
    bool IsTruncated);
```

- [ ] **Step 4: 絞り込みを実装する**

`ProductCandidateFilter_候補絞り込み.cs`:

```csharp
// このファイルはカスケード絞り込みの仕様そのものです。
// 項目Fの選択肢は「F以外のすべての条件を適用した結果」から作ります。
// 自分の選択を除外しないと、一度選んだ項目を選び直せなくなります。
// 修正時の影響: ここを変えると絞り込みの挙動が全面的に変わります。
// 純粋関数として保ち、ファイル入出力やRevit参照を持ち込まないでください。
using Bui.Products;

namespace Bui.Data;

/// <summary>製品一覧を絞り込み、各項目の選択肢を算出します。</summary>
public static class ProductCandidateFilter
{
    public static CandidateView Apply(
        IReadOnlyList<ProductRecord> products,
        CsvColumnMapping mapping,
        IReadOnlyList<string> filterColumns,
        CandidateQuery query)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(filterColumns);
        ArgumentNullException.ThrowIfNull(query);

        var text = (query.Text ?? string.Empty).Trim();
        var limit = Math.Clamp(query.Limit, 1, 1000);

        // 「選択したが候補に存在しない」矛盾を先に解消します。
        // 解消しないと、候補0件のまま利用者が原因に気づけません。
        var effective = ResolveSelections(products, mapping, filterColumns, query, text);

        var filters = filterColumns
            .Select(column => new CandidateFilterOptions(
                column,
                effective.TryGetValue(column, out var selected) ? selected : string.Empty,
                DistinctValues(products, mapping, filterColumns, effective, text, ignoreColumn: column)))
            .ToArray();

        var matched = products
            .Where(record => Matches(record, mapping, filterColumns, effective, text, ignoreColumn: null))
            .ToArray();

        return new CandidateView(filters, matched.Take(limit).ToArray(), matched.Length, matched.Length > limit);
    }

    /// <summary>候補に存在しなくなった選択を未選択へ戻します。前の項目から順に確定させます。</summary>
    private static Dictionary<string, string> ResolveSelections(
        IReadOnlyList<ProductRecord> products,
        CsvColumnMapping mapping,
        IReadOnlyList<string> filterColumns,
        CandidateQuery query,
        string text)
    {
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in filterColumns)
        {
            var requested = query.Selections.TryGetValue(column, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
            if (requested.Length == 0) continue;

            var available = DistinctValues(products, mapping, filterColumns, effective, text, ignoreColumn: column);
            if (available.Any(item => string.Equals(item, requested, StringComparison.OrdinalIgnoreCase)))
            {
                effective[column] = requested;
            }
        }

        return effective;
    }

    private static IReadOnlyList<string> DistinctValues(
        IReadOnlyList<ProductRecord> products,
        CsvColumnMapping mapping,
        IReadOnlyList<string> filterColumns,
        IReadOnlyDictionary<string, string> selections,
        string text,
        string ignoreColumn) =>
        products
            .Where(record => Matches(record, mapping, filterColumns, selections, text, ignoreColumn))
            .Select(record => ProductFieldAccessor.Read(record, mapping, ignoreColumn))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool Matches(
        ProductRecord record,
        CsvColumnMapping mapping,
        IReadOnlyList<string> filterColumns,
        IReadOnlyDictionary<string, string> selections,
        string text,
        string? ignoreColumn)
    {
        foreach (var column in filterColumns)
        {
            if (string.Equals(column, ignoreColumn, StringComparison.Ordinal)) continue;
            if (!selections.TryGetValue(column, out var selected) || selected.Length == 0) continue;

            var actual = ProductFieldAccessor.Read(record, mapping, column);
            if (!string.Equals(actual, selected, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return text.Length == 0 || ContainsText(record, mapping, text);
    }

    /// <summary>
    /// 対応付け済みの値と追加属性の全体を対象にした部分一致です。
    /// 対象範囲の定義はProductFieldAccessor.Fieldsに一本化しています。
    /// ここで独自に列挙し直すと、既存の製品検索と挙動が食い違います。
    /// </summary>
    private static bool ContainsText(ProductRecord record, CsvColumnMapping mapping, string text) =>
        ProductFieldAccessor.Fields(record, mapping).Values
            .Any(value => value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 5: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。

- [ ] **Step 6: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ"
git commit -m "feat: add cascading candidate filter"
```

---

### Task 3: 候補データ窓口とCSV実装

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ICandidateSource_候補データ窓口.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/InMemoryCandidateSource_全件絞り込み.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/InMemoryCandidateSourceTests_全件絞り込みテスト.cs`

**Interfaces:**
- Consumes: Task 2の `ProductCandidateFilter.Apply`、既存の `IProductRepository.SearchAsync`。
- Produces:
  - `Bui.Data.ICandidateSource.QueryAsync(CandidateQuery query, CancellationToken cancellationToken) -> Task<CandidateView>`
  - `Bui.Data.InMemoryCandidateSource(IProductRepository products, CsvColumnMapping mapping, IReadOnlyList<string> filterColumns)`

画面はこの契約だけを見る。SQL実装を足すとき、画面とViewModelのテストは変更しない。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは、画面が見る契約がリポジトリの全件取得を隠せていることを保証します。
// ここが崩れると、SQL実装へ差し替える際に画面まで変更が必要になります。
using Bui.Data;
using Bui.Products;

namespace Bui.Tests.Data_データ;

[TestClass]
public class InMemoryCandidateSourceTests_全件絞り込みテスト
{
    [TestMethod]
    public async Task QueryAsync_リポジトリの全件から絞り込む()
    {
        var repository = new StubProductRepository(
            Product("BM-0001", "内装", "メーカーA"),
            Product("BM-0002", "外装", "メーカーB"));
        var sut = new InMemoryCandidateSource(repository, CsvColumnMapping.PocDefault, new[] { "カテゴリー" });

        var view = await sut.QueryAsync(
            new CandidateQuery(new Dictionary<string, string> { ["カテゴリー"] = "内装" }, string.Empty), default);

        Assert.AreEqual("BM-0001", view.Candidates.Single().ProductId);
        CollectionAssert.AreEqual(new[] { "内装", "外装" }, view.Filters.Single().Values.ToArray());
    }

    [TestMethod]
    public async Task QueryAsync_全件取得は1回だけ行う()
    {
        // 選択肢の算出と候補の抽出で二重に取りに行くと、件数が増えたとき無駄が大きくなります。
        var repository = new StubProductRepository(Product("BM-0001", "内装", "メーカーA"));
        var sut = new InMemoryCandidateSource(repository, CsvColumnMapping.PocDefault, new[] { "カテゴリー", "メーカー名" });

        await sut.QueryAsync(new CandidateQuery(new Dictionary<string, string>(), string.Empty), default);

        Assert.AreEqual(1, repository.SearchCalls);
    }

    [TestMethod]
    public async Task QueryAsync_CSVが読めないときは空を返す()
    {
        var sut = new InMemoryCandidateSource(new StubProductRepository(), CsvColumnMapping.PocDefault, new[] { "カテゴリー" });

        var view = await sut.QueryAsync(new CandidateQuery(new Dictionary<string, string>(), string.Empty), default);

        Assert.AreEqual(0, view.Candidates.Count);
        Assert.AreEqual(0, view.TotalCount);
        Assert.AreEqual(1, view.Filters.Count);
    }

    private static ProductRecord Product(string id, string category, string maker) =>
        new(id, "製品", maker, string.Empty, string.Empty, null,
            new Dictionary<string, string> { ["カテゴリー"] = category });

    private sealed class StubProductRepository(params ProductRecord[] records) : IProductRepository
    {
        public int SearchCalls { get; private set; }

        public Task<DataSourceStatus> GetStatusAsync(CancellationToken c) =>
            Task.FromResult(new DataSourceStatus(true, records.Length, null, Array.Empty<string>()));

        public Task<ProductRecord?> GetByIdAsync(string id, CancellationToken c) =>
            Task.FromResult(records.FirstOrDefault(r => r.ProductId == id));

        public Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery q, CancellationToken c)
        {
            SearchCalls++;
            return Task.FromResult<IReadOnlyList<ProductRecord>>(records);
        }

        public Task UpsertAsync(ProductRecord r, CancellationToken c) => Task.CompletedTask;
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~InMemoryCandidateSourceTests"
```

Expected: 型が存在せずコンパイルエラー。

- [ ] **Step 3: 契約を実装する**

`ICandidateSource_候補データ窓口.cs`:

```csharp
// このファイルは画面と絞り込み実装の唯一の境界です。
// 画面はこの契約だけを見るため、CSVからSQL Serverへ実装を差し替えても
// 画面とそのテストは変更が要りません。
// 修正時の影響: 署名を変えると、将来のSQL実装と画面の両方に波及します。
namespace Bui.Data;

/// <summary>現在の絞り込み条件から、各項目の選択肢と該当候補を返します。</summary>
public interface ICandidateSource
{
    Task<CandidateView> QueryAsync(CandidateQuery query, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: CSV向け実装を書く**

`InMemoryCandidateSource_全件絞り込み.cs`:

```csharp
// このファイルは、製品リポジトリの全件をメモリ上で絞り込む実装です。
// PoCのCSVは数百件規模のため、全件を1回取得して純粋関数へ渡すのが最も単純で速い方法です。
// 修正時の影響: 件数が数十万規模になったら、この実装を
// サーバー側で絞るSQL実装へ差し替えます。画面は変更不要です。
using Bui.Products;

namespace Bui.Data;

/// <summary>全件を取得し、カスケード絞り込みをメモリ上で適用します。</summary>
public sealed class InMemoryCandidateSource : ICandidateSource
{
    private const int FetchLimit = 200;

    private readonly IProductRepository _products;
    private readonly CsvColumnMapping _mapping;
    private readonly IReadOnlyList<string> _filterColumns;

    public InMemoryCandidateSource(
        IProductRepository products, CsvColumnMapping mapping, IReadOnlyList<string> filterColumns)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _filterColumns = filterColumns ?? throw new ArgumentNullException(nameof(filterColumns));
    }

    public async Task<CandidateView> QueryAsync(CandidateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // 絞り込みと選択肢の算出は全件が揃っていないと成り立たないため、ここで1回だけ取得します。
        // 上限は既存の検索APIの最大値に合わせます。
        var all = await _products
            .SearchAsync(new ProductQuery(string.Empty, new Dictionary<string, string>(), FetchLimit), cancellationToken)
            .ConfigureAwait(false);

        return ProductCandidateFilter.Apply(all, _mapping, _filterColumns, query);
    }
}
```

- [ ] **Step 5: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。

- [ ] **Step 6: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ"
git commit -m "feat: add candidate source boundary for future SQL"
```

---

### Task 4: 設定項目の追加

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/BuiSettings_BUI設定.cs`
- Modify: `BUI_Addin_CSharpアドイン/config_設定例/Settings.Default_共通設定.json`
- Modify: `BUI_Addin_CSharpアドイン/config_設定例/Settings.Local.example_個別設定例.json`
- Modify: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Configuration_設定/SettingsMergerTests_設定統合テスト.cs`

**Interfaces:**
- Produces: `DataSettings` に6項目を追加。`CandidateFilterColumns`, `NewProductColumns`, `NewProductRequiredColumns`, `DuplicateCompareColumns`, `NewProductIdPrefix`, `NewProductIdDigits`。

絞り込み項目は実データの列名に依存するため、CSV列対応と同じく設定へ出す。

- [ ] **Step 1: 出荷設定の期待値を先に更新する**

`SettingsMergerTests_設定統合テスト.cs` の `DefaultJson` 定数の `data` に、`priceColumns` の直後へ次を追加する。

```json
    ,
    "candidateFilterColumns": ["カテゴリー", "用途", "メーカー名", "製品名", "型番", "構造"],
    "newProductColumns": ["カテゴリー", "用途", "メーカー名", "製品名", "型番", "構造", "製品画像", "商品コード"],
    "newProductRequiredColumns": ["カテゴリー", "メーカー名", "製品名"],
    "duplicateCompareColumns": ["カテゴリー", "用途", "メーカー名", "製品名", "型番", "構造"],
    "newProductIdPrefix": "BM_NEW-",
    "newProductIdDigits": 4
```

同じファイルへ次のテストを追加する。

```csharp
    [TestMethod]
    public async Task LoadAsync_候補選択の既定値を読み込む()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(
            folder.Write("default.json", DefaultJson), folder.PathOf("local.json"), folder.PathOf("secrets.json"));

        var result = await service.LoadAsync();

        var data = result.Value!.Settings.Data;
        CollectionAssert.AreEqual(
            new[] { "カテゴリー", "用途", "メーカー名", "製品名", "型番", "構造" },
            data.CandidateFilterColumns.ToArray());
        CollectionAssert.AreEqual(new[] { "カテゴリー", "メーカー名", "製品名" }, data.NewProductRequiredColumns.ToArray());
        Assert.AreEqual("BM_NEW-", data.NewProductIdPrefix);
        Assert.AreEqual(4, data.NewProductIdDigits);
    }
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~SettingsMergerTests"
```

Expected: `CandidateFilterColumns` が存在せずコンパイルエラー。

- [ ] **Step 3: 設定の型を拡張する**

`BuiSettings_BUI設定.cs` の `DataSettings` を次へ置き換える。既存6項目の順序と名前は変えない。

```csharp
public sealed record DataSettings(
    string Provider,
    string ProductCsvPath,
    string PriceCsvPath,
    string AssemblyCsvPath,
    string? IdAliasCsvPath,
    CsvColumnMapping ProductColumns,
    PriceColumnMapping PriceColumns,
    // 以下は候補選択・新規登録用。実データの列名に依存するため設定へ出します。
    IReadOnlyList<string> CandidateFilterColumns,
    IReadOnlyList<string> NewProductColumns,
    IReadOnlyList<string> NewProductRequiredColumns,
    IReadOnlyList<string> DuplicateCompareColumns,
    string NewProductIdPrefix,
    int NewProductIdDigits);
```

- [ ] **Step 4: 出荷設定JSONへ追加する**

`config_設定例/Settings.Default_共通設定.json` と `Settings.Local.example_個別設定例.json` の `data` へ、Step 1と同じ6項目を追加する。JSONとして妥当であることを確認する。

```bash
python -c "import json,io; json.load(io.open('BUI_Addin_CSharpアドイン/config_設定例/Settings.Default_共通設定.json',encoding='utf-8')); print('OK')"
```

- [ ] **Step 5: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。既存の `ShippedDefaults_合意済みの構造と値に一致する` も合格すること。

- [ ] **Step 6: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定" "BUI_Addin_CSharpアドイン/config_設定例" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Configuration_設定"
git commit -m "feat: configure candidate filter and new product columns"
```

---

### Task 5: 製品IDの採番

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ProductIdAllocator_製品ID採番.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/ProductIdAllocatorTests_製品ID採番テスト.cs`

**Interfaces:**
- Produces: `Bui.Products.ProductIdAllocator.Next(IEnumerable<string> existingIds, string prefix, int digits) -> Bui.Common.OperationResult<string>`

`AssemblyIdAllocator` は桁あふれで例外を投げるが、こちらは画面から直接呼ぶため `OperationResult` を返す。例外を画面まで通さない既存方針に合わせる。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは新規製品IDの採番規則を固定します。
// 既存の建材コード（BM-0012 など）を誤って連番の対象に含めると、
// 実在するコードと衝突するIDを発行してしまいます。
using Bui.Products;

namespace Bui.Tests.Products_製品;

[TestClass]
public class ProductIdAllocatorTests_製品ID採番テスト
{
    [TestMethod]
    public void Next_空なら先頭番号を返す()
    {
        var actual = ProductIdAllocator.Next(Array.Empty<string>(), "BM_NEW-", 4);

        Assert.IsTrue(actual.IsSuccess);
        Assert.AreEqual("BM_NEW-0001", actual.Value);
    }

    [TestMethod]
    public void Next_最大値の次を返す()
    {
        var actual = ProductIdAllocator.Next(new[] { "BM_NEW-0001", "BM_NEW-0009" }, "BM_NEW-", 4);

        Assert.AreEqual("BM_NEW-0010", actual.Value);
    }

    [TestMethod]
    public void Next_形式の違う既存コードは無視する()
    {
        // 実データの建材コードを連番に混ぜてはいけません。
        var actual = ProductIdAllocator.Next(new[] { "BM-0500", "BM_NEW-3", "bm_new-0007" }, "BM_NEW-", 4);

        Assert.AreEqual("BM_NEW-0001", actual.Value);
    }

    [TestMethod]
    public void Next_接頭辞と桁数を設定で変えられる()
    {
        var actual = ProductIdAllocator.Next(new[] { "P-00042" }, "P-", 5);

        Assert.AreEqual("P-00043", actual.Value);
    }

    [TestMethod]
    public void Next_桁があふれたら失敗を返す()
    {
        var actual = ProductIdAllocator.Next(new[] { "BM_NEW-9999" }, "BM_NEW-", 4);

        Assert.IsFalse(actual.IsSuccess);
        Assert.AreEqual("PRODUCT_ID_RANGE_EXHAUSTED", actual.Error!.Code);
        StringAssert.Contains(actual.Error.UserMessage, "上限");
    }

    [TestMethod]
    public void Next_接頭辞が空なら失敗を返す()
    {
        var actual = ProductIdAllocator.Next(Array.Empty<string>(), "  ", 4);

        Assert.IsFalse(actual.IsSuccess);
        Assert.AreEqual("PRODUCT_ID_PREFIX_INVALID", actual.Error!.Code);
    }

    [TestMethod]
    public void Next_桁数が範囲外なら失敗を返す()
    {
        Assert.AreEqual("PRODUCT_ID_DIGITS_INVALID", ProductIdAllocator.Next(Array.Empty<string>(), "BM_NEW-", 0).Error!.Code);
        Assert.AreEqual("PRODUCT_ID_DIGITS_INVALID", ProductIdAllocator.Next(Array.Empty<string>(), "BM_NEW-", 19).Error!.Code);
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~ProductIdAllocatorTests"
```

Expected: `ProductIdAllocator` が存在せずコンパイルエラー。

- [ ] **Step 3: 実装する**

```csharp
// このファイルは新規登録時のProduct_IDを採番します。
// AssemblyIdAllocatorと同じ考え方ですが、画面から直接呼ぶため
// 例外ではなくOperationResultで失敗を返します。
// 修正時の影響: 接頭辞の比較を緩めると、実在する建材コードを
// 連番の対象に含めてしまい、衝突するIDを発行します。
using System.Globalization;
using Bui.Common;

namespace Bui.Products;

/// <summary>既存IDと衝突しない、連番の新規Product_ID候補を作成します。</summary>
public static class ProductIdAllocator
{
    public static OperationResult<string> Next(IEnumerable<string> existingIds, string prefix, int digits)
    {
        ArgumentNullException.ThrowIfNull(existingIds);

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return OperationResult<string>.Failure(
                "PRODUCT_ID_PREFIX_INVALID", "新規IDの接頭辞が設定されていません。設定を確認してください。", "Prefix is blank.");
        }

        if (digits is < 1 or > 18)
        {
            return OperationResult<string>.Failure(
                "PRODUCT_ID_DIGITS_INVALID", "新規IDの桁数は1から18の範囲で設定してください。", $"digits={digits}");
        }

        long maximum = 0;
        foreach (var existingId in existingIds)
        {
            // 大文字小文字も含めた完全一致にします。緩めると実在コードを巻き込みます。
            if (existingId is null || !existingId.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var numericPart = existingId[prefix.Length..];
            if (numericPart.Length != digits || numericPart.Any(c => c is < '0' or > '9')) continue;

            if (long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                maximum = Math.Max(maximum, value);
            }
        }

        var next = maximum + 1;
        var limit = Pow10(digits) - 1;
        if (next > limit)
        {
            return OperationResult<string>.Failure(
                "PRODUCT_ID_RANGE_EXHAUSTED",
                "新規IDの採番が上限に達しました。設定の桁数を見直してください。",
                $"next={next}; limit={limit}");
        }

        return OperationResult<string>.Success(prefix + next.ToString($"D{digits}", CultureInfo.InvariantCulture));
    }

    private static long Pow10(int digits)
    {
        long value = 1;
        for (var index = 0; index < digits; index++) value = checked(value * 10);
        return value;
    }
}
```

- [ ] **Step 4: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。

- [ ] **Step 5: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ProductIdAllocator_製品ID採番.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/ProductIdAllocatorTests_製品ID採番テスト.cs"
git commit -m "feat: allocate new product identifiers"
```

---

### Task 6: 重複候補の検出

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/NewProductDraft_新規登録入力.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ProductDuplicateFinder_重複候補検出.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/ProductDuplicateFinderTests_重複候補検出テスト.cs`

**Interfaces:**
- Consumes: Task 1の `ProductFieldAccessor.Read`。
- Produces:
  - `Bui.Products.NewProductDraft(IReadOnlyDictionary<string,string> Values)` と `IsMissingRequired(IReadOnlyList<string> requiredColumns) -> IReadOnlyList<string>`
  - `Bui.Products.DuplicateCandidate(ProductRecord Product, int MatchedCount, IReadOnlyList<string> MatchedColumns)`
  - `Bui.Products.ProductDuplicateFinder.Find(IReadOnlyList<ProductRecord> products, CsvColumnMapping mapping, NewProductDraft draft, IReadOnlyList<string> compareColumns, int limit = 20) -> IReadOnlyList<DuplicateCandidate>`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは、新規登録の前に「同じ製品が既にあるか」を示す仕組みを固定します。
// 見落とすと同じ製品が二重に登録され、どちらのIDが正しいか分からなくなります。
using Bui.Data;
using Bui.Products;

namespace Bui.Tests.Products_製品;

[TestClass]
public class ProductDuplicateFinderTests_重複候補検出テスト
{
    private static readonly string[] Compare = { "カテゴリー", "メーカー名", "製品名", "型番" };

    private static ProductRecord P(string id, string category, string maker, string name, string model) =>
        new(id, name, maker, model, string.Empty, null,
            new Dictionary<string, string> { ["カテゴリー"] = category });

    private static IReadOnlyList<ProductRecord> Products() => new[]
    {
        P("BM-0001", "内装", "メーカーA", "床タイル", "VT-100"),
        P("BM-0002", "内装", "メーカーA", "床タイル", "VT-200"),
        P("BM-0003", "外装", "メーカーB", "外壁パネル", "WP-1"),
    };

    private static NewProductDraft Draft(params (string Column, string Value)[] values) =>
        new(values.ToDictionary(v => v.Column, v => v.Value, StringComparer.Ordinal));

    [TestMethod]
    public void Find_完全一致を先頭に返す()
    {
        var draft = Draft(("カテゴリー", "内装"), ("メーカー名", "メーカーA"), ("製品名", "床タイル"), ("型番", "VT-200"));

        var actual = ProductDuplicateFinder.Find(Products(), CsvColumnMapping.PocDefault, draft, Compare);

        Assert.AreEqual("BM-0002", actual[0].Product.ProductId);
        Assert.AreEqual(4, actual[0].MatchedCount);
    }

    [TestMethod]
    public void Find_一致数の多い順に並べる()
    {
        var draft = Draft(("カテゴリー", "内装"), ("メーカー名", "メーカーA"), ("製品名", "床タイル"), ("型番", "VT-999"));

        var actual = ProductDuplicateFinder.Find(Products(), CsvColumnMapping.PocDefault, draft, Compare);

        Assert.AreEqual(3, actual[0].MatchedCount);
        Assert.IsTrue(actual.All(c => c.MatchedCount >= 3));
        Assert.AreEqual(2, actual.Count);
    }

    [TestMethod]
    public void Find_入力が空の項目は比較に含めない()
    {
        var draft = Draft(("カテゴリー", "内装"), ("メーカー名", ""), ("製品名", "床タイル"), ("型番", ""));

        var actual = ProductDuplicateFinder.Find(Products(), CsvColumnMapping.PocDefault, draft, Compare);

        Assert.AreEqual(2, actual.Count);
        CollectionAssert.AreEqual(new[] { "カテゴリー", "製品名" }, actual[0].MatchedColumns.ToArray());
    }

    [TestMethod]
    public void Find_一致が無ければ空を返す()
    {
        var draft = Draft(("カテゴリー", "設備"), ("製品名", "配管"));

        Assert.AreEqual(0, ProductDuplicateFinder.Find(Products(), CsvColumnMapping.PocDefault, draft, Compare).Count);
    }

    [TestMethod]
    public void Find_上限で打ち切る()
    {
        var many = Enumerable.Range(1, 30)
            .Select(i => P($"BM-{i:0000}", "内装", "メーカーA", "床タイル", $"VT-{i}")).ToArray();
        var draft = Draft(("カテゴリー", "内装"));

        Assert.AreEqual(20, ProductDuplicateFinder.Find(many, CsvColumnMapping.PocDefault, draft, Compare).Count);
    }

    [TestMethod]
    public void IsMissingRequired_未入力の必須項目を返す()
    {
        var draft = Draft(("カテゴリー", "内装"), ("メーカー名", "  "), ("製品名", "床タイル"));

        var missing = draft.IsMissingRequired(new[] { "カテゴリー", "メーカー名", "製品名" });

        CollectionAssert.AreEqual(new[] { "メーカー名" }, missing.ToArray());
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~ProductDuplicateFinderTests"
```

Expected: 型が存在せずコンパイルエラー。

- [ ] **Step 3: 入力の型を実装する**

`NewProductDraft_新規登録入力.cs`:

```csharp
// このファイルは新規登録の入力値を保持します。
// 列名から値へのただの対応表ですが、必須チェックをここに置くことで
// 画面と登録処理の双方が同じ判断規則を使えます。
namespace Bui.Products;

/// <summary>新規登録の入力内容。キーはCSVの列名です。</summary>
public sealed record NewProductDraft(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>値を取り出します。未入力の項目は空文字を返します。</summary>
    public string Read(string column) =>
        Values.TryGetValue(column, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    /// <summary>未入力の必須項目を、指定された順序で返します。</summary>
    public IReadOnlyList<string> IsMissingRequired(IReadOnlyList<string> requiredColumns)
    {
        ArgumentNullException.ThrowIfNull(requiredColumns);
        return requiredColumns.Where(column => Read(column).Length == 0).ToArray();
    }
}
```

- [ ] **Step 4: 重複検出を実装する**

`ProductDuplicateFinder_重複候補検出.cs`:

```csharp
// このファイルは、新規登録しようとしている内容に近い既存製品を探します。
// 登録は止めません。判断は利用者に委ねる方針です。
// 修正時の影響: 比較列は設定から渡されます。ここで列を固定しないでください。
using Bui.Data;

namespace Bui.Products;

/// <summary>入力内容と一致した既存製品。一致数が多いほど重複の疑いが強くなります。</summary>
public sealed record DuplicateCandidate(
    ProductRecord Product,
    int MatchedCount,
    IReadOnlyList<string> MatchedColumns);

/// <summary>新規登録の前に、重複の疑いがある既存製品を提示します。</summary>
public static class ProductDuplicateFinder
{
    public static IReadOnlyList<DuplicateCandidate> Find(
        IReadOnlyList<ProductRecord> products,
        CsvColumnMapping mapping,
        NewProductDraft draft,
        IReadOnlyList<string> compareColumns,
        int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(compareColumns);

        // 入力が空の項目は比較しません。空欄同士を一致と数えると、
        // ほとんど入力していない段階で無関係な製品が大量に並びます。
        var targets = compareColumns.Where(column => draft.Read(column).Length > 0).ToArray();
        if (targets.Length == 0) return Array.Empty<DuplicateCandidate>();

        return products
            .Select(product => Compare(product, mapping, draft, targets))
            .Where(candidate => candidate.MatchedCount > 0)
            .OrderByDescending(candidate => candidate.MatchedCount)
            .ThenBy(candidate => candidate.Product.ProductId, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static DuplicateCandidate Compare(
        ProductRecord product, CsvColumnMapping mapping, NewProductDraft draft, IReadOnlyList<string> targets)
    {
        var matched = targets
            .Where(column => string.Equals(
                ProductFieldAccessor.Read(product, mapping, column),
                draft.Read(column),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new DuplicateCandidate(product, matched.Length, matched);
    }
}
```

- [ ] **Step 5: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。

- [ ] **Step 6: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品"
git commit -m "feat: detect duplicate products before registration"
```

---

### Task 7: 新規登録の調整と補償

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Products_製品/ProductRegistrationCoordinator_新規登録調整.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Products_製品/ProductRegistrationCoordinatorTests_新規登録調整テスト.cs`

**Interfaces:**
- Consumes: Task 5の `ProductIdAllocator.Next`、Task 6の `NewProductDraft`、既存の `IProductRepository`。
- Produces: `Bui.Products.ProductRegistrationCoordinator.RegisterAsync(NewProductDraft draft, DataSettings settings, Func<string, CancellationToken, Task<OperationResult<string>>> applyAsync, CancellationToken cancellationToken) -> Task<OperationResult<string>>`

CSVへ追記したあとRevitへの書込みが失敗したら、追加した行を削除して元に戻す。CSVだけに幽霊レコードが残る状態を作らない。

削除は既存の `IProductRepository` に無いため、このタスクで `IProductRepository` へ `RemoveAsync(string productId, CancellationToken)` を追加する。CSV実装は既存の安全な置換（バックアップ + 一時ファイル）を使う。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは「CSVへ追記した後にRevit書込が失敗したら元に戻す」ことを保証します。
// ここが壊れると、実体の無いProduct_IDがCSVに残り続けます。
using Bui.Common;
using Bui.Configuration;
using Bui.Data;
using Bui.Products;

namespace Bui.Tests.Products_製品;

[TestClass]
public class ProductRegistrationCoordinatorTests_新規登録調整テスト
{
    private static DataSettings Settings() => new(
        "Csv", "p.csv", "pr.csv", "a.csv", null,
        CsvColumnMapping.PocDefault, PriceColumnMapping.PocDefault,
        new[] { "カテゴリー" }, new[] { "カテゴリー", "メーカー名", "製品名" },
        new[] { "カテゴリー", "メーカー名", "製品名" }, new[] { "カテゴリー" }, "BM_NEW-", 4);

    private static NewProductDraft Draft() => new(new Dictionary<string, string>
    {
        ["カテゴリー"] = "内装", ["メーカー名"] = "メーカーA", ["製品名"] = "床タイル"
    });

    [TestMethod]
    public async Task RegisterAsync_採番して追記し付与まで通す()
    {
        var repository = new RecordingProductRepository();
        var sut = new ProductRegistrationCoordinator(repository);

        var actual = await sut.RegisterAsync(Draft(), Settings(),
            (id, c) => Task.FromResult(OperationResult<string>.Success($"{id} を付与しました。")), default);

        Assert.IsTrue(actual.IsSuccess);
        Assert.AreEqual("BM_NEW-0001", repository.Upserted.Single().ProductId);
        Assert.AreEqual(0, repository.Removed.Count);
    }

    [TestMethod]
    public async Task RegisterAsync_必須未入力なら追記しない()
    {
        var repository = new RecordingProductRepository();
        var sut = new ProductRegistrationCoordinator(repository);
        var draft = new NewProductDraft(new Dictionary<string, string> { ["カテゴリー"] = "内装" });

        var actual = await sut.RegisterAsync(draft, Settings(),
            (id, c) => Task.FromResult(OperationResult<string>.Success("")), default);

        Assert.IsFalse(actual.IsSuccess);
        Assert.AreEqual("NEW_PRODUCT_REQUIRED_MISSING", actual.Error!.Code);
        StringAssert.Contains(actual.Error.UserMessage, "メーカー名");
        Assert.AreEqual(0, repository.Upserted.Count);
    }

    [TestMethod]
    public async Task RegisterAsync_付与が失敗したら追加行を削除して戻す()
    {
        var repository = new RecordingProductRepository();
        var sut = new ProductRegistrationCoordinator(repository);

        var actual = await sut.RegisterAsync(Draft(), Settings(),
            (id, c) => Task.FromResult(OperationResult<string>.Failure("REVIT_WRITE_FAILED", "書き込めませんでした。", "detail")),
            default);

        Assert.IsFalse(actual.IsSuccess);
        CollectionAssert.AreEqual(new[] { "BM_NEW-0001" }, repository.Removed.ToArray());
        StringAssert.Contains(actual.Error!.UserMessage, "書き込めませんでした");
    }

    [TestMethod]
    public async Task RegisterAsync_補償も失敗したら両方を伝える()
    {
        // 手作業で確認できるよう、残ったIDを利用者向け文言に必ず含めます。
        var repository = new RecordingProductRepository { RemoveThrows = true };
        var sut = new ProductRegistrationCoordinator(repository);

        var actual = await sut.RegisterAsync(Draft(), Settings(),
            (id, c) => Task.FromResult(OperationResult<string>.Failure("REVIT_WRITE_FAILED", "書き込めませんでした。", "detail")),
            default);

        Assert.IsFalse(actual.IsSuccess);
        Assert.AreEqual("NEW_PRODUCT_ROLLBACK_FAILED", actual.Error!.Code);
        StringAssert.Contains(actual.Error.UserMessage, "BM_NEW-0001");
    }

    private sealed class RecordingProductRepository : IProductRepository
    {
        public List<ProductRecord> Upserted { get; } = new();
        public List<string> Removed { get; } = new();
        public bool RemoveThrows { get; init; }

        public Task<DataSourceStatus> GetStatusAsync(CancellationToken c) =>
            Task.FromResult(new DataSourceStatus(true, Upserted.Count, null, Array.Empty<string>()));
        public Task<ProductRecord?> GetByIdAsync(string id, CancellationToken c) =>
            Task.FromResult(Upserted.FirstOrDefault(r => r.ProductId == id));
        public Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery q, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<ProductRecord>>(Upserted.ToArray());
        public Task UpsertAsync(ProductRecord r, CancellationToken c) { Upserted.Add(r); return Task.CompletedTask; }
        public Task RemoveAsync(string productId, CancellationToken c)
        {
            if (RemoveThrows) throw new IOException("削除できません。");
            Removed.Add(productId);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~ProductRegistrationCoordinatorTests"
```

Expected: `RemoveAsync` と `ProductRegistrationCoordinator` が存在せずコンパイルエラー。

- [ ] **Step 3: リポジトリへ削除を追加する**

`IProductRepository_製品データ窓口.cs` へ次を追加する。

```csharp
    /// <summary>指定したProduct_IDの行を削除します。新規登録の取り消しに使います。</summary>
    Task RemoveAsync(string productId, CancellationToken cancellationToken);
```

`CsvProductRepository_CSV製品データ取得.cs` へ実装を追加する。既存の `UpsertAsync` と同じく、解析済みの生の行から書き戻す。

```csharp
    public async Task RemoveAsync(string productId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var headers = snapshot.Document.Headers.ToList();
        // 読み飛ばした行も含めて生の行から作り直すため、対象以外は内容が保たれます。
        var rows = snapshot.Document.Rows
            .Where(row => !string.Equals(
                row.GetValueOrDefault(mapping.ProductIdColumn)?.Trim(), productId.Trim(), StringComparison.Ordinal))
            .Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal))
            .ToList();

        await writer.ReplaceWithBackupAsync(
            csvPath, CsvDocumentCodec.Write(headers, rows),
            snapshot.TimestampUtc, snapshot.Document.HasUtf8Bom, cancellationToken).ConfigureAwait(false);

        cache = null;
    }
```

既存のテスト用スタブリポジトリ（`Bui.Tests` 内の `IProductRepository` 実装すべて）へ `RemoveAsync` を追加する。対象は次のファイル。

- `tests_テスト/Bui.Tests/UI_画面/BuiDockPaneViewModelTests_パネル状態テスト.cs`
- `tests_テスト/Bui.Tests/UI_画面/FilePickerCommandTests_CSV参照ボタンテスト.cs`
- `tests_テスト/Bui.Tests/UI_画面/IdentifierWorkflowViewModelTests_ID付与画面テスト.cs`

追加する内容は次のとおり。

```csharp
        public Task RemoveAsync(string productId, CancellationToken c) => Task.CompletedTask;
```

- [ ] **Step 4: 調整役を実装する**

```csharp
// このファイルは新規登録の一連の流れをまとめます。
// 採番 → CSVへ追記 → Revitへ付与、の途中で失敗したときに
// CSVだけへ幽霊レコードが残らないよう、追加した行を取り消します。
// 修正時の影響: 取り消しを外すと、実体の無いProduct_IDがCSVへ蓄積します。
using Bui.Common;
using Bui.Configuration;
using Bui.Data;

namespace Bui.Products;

/// <summary>新規製品の採番・追記・付与を、失敗時の取り消し込みで実行します。</summary>
public sealed class ProductRegistrationCoordinator
{
    private readonly IProductRepository _products;

    public ProductRegistrationCoordinator(IProductRepository products) =>
        _products = products ?? throw new ArgumentNullException(nameof(products));

    /// <param name="applyAsync">採番したIDをRevitへ付与する処理。失敗したら追記を取り消します。</param>
    public async Task<OperationResult<string>> RegisterAsync(
        NewProductDraft draft,
        DataSettings settings,
        Func<string, CancellationToken, Task<OperationResult<string>>> applyAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applyAsync);

        var missing = draft.IsMissingRequired(settings.NewProductRequiredColumns);
        if (missing.Count > 0)
        {
            return OperationResult<string>.Failure(
                "NEW_PRODUCT_REQUIRED_MISSING",
                $"必須項目（{string.Join("、", missing)}）を入力してください。",
                $"missing={string.Join(",", missing)}");
        }

        var existing = await _products
            .SearchAsync(new ProductQuery(string.Empty, new Dictionary<string, string>(), 200), cancellationToken)
            .ConfigureAwait(false);

        var allocated = ProductIdAllocator.Next(
            existing.Select(record => record.ProductId), settings.NewProductIdPrefix, settings.NewProductIdDigits);
        if (!allocated.IsSuccess) return allocated;

        var newId = allocated.Value!;
        await _products.UpsertAsync(Build(newId, draft, settings), cancellationToken).ConfigureAwait(false);

        var applied = await applyAsync(newId, cancellationToken).ConfigureAwait(false);
        if (applied.IsSuccess) return applied;

        // ここから先は取り消しです。取り消し自体の失敗も利用者へ必ず伝えます。
        try
        {
            await _products.RemoveAsync(newId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return OperationResult<string>.Failure(
                "NEW_PRODUCT_ROLLBACK_FAILED",
                $"付与に失敗し、追加した「{newId}」の取り消しもできませんでした。製品CSVを確認してください。",
                exception.GetType().FullName ?? "Unknown");
        }

        return OperationResult<string>.Failure(
            applied.Error!.Code,
            $"{applied.Error.UserMessage}追加した製品は取り消しました。",
            applied.Error.TechnicalDetail);
    }

    /// <summary>入力値を製品レコードへ移します。対応付け済みの列は専用項目へ、それ以外は追加属性へ入れます。</summary>
    private static ProductRecord Build(string productId, NewProductDraft draft, DataSettings settings)
    {
        var mapping = settings.ProductColumns;
        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in settings.NewProductColumns)
        {
            if (IsMapped(column, mapping)) continue;
            var value = draft.Read(column);
            if (value.Length > 0) extras[column] = value;
        }

        return new ProductRecord(
            productId,
            draft.Read(mapping.ProductNameColumn),
            draft.Read(mapping.ManufacturerColumn),
            draft.Read(mapping.ModelNumberColumn),
            draft.Read(mapping.UnitColumn),
            null,
            extras);
    }

    private static bool IsMapped(string column, CsvColumnMapping mapping) =>
        string.Equals(column, mapping.ProductIdColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.ProductNameColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.ManufacturerColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.ModelNumberColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.UnitColumn, StringComparison.Ordinal);
}
```

- [ ] **Step 5: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。既存の全テストも合格すること。

- [ ] **Step 6: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト"
git commit -m "feat: register new products with rollback on failure"
```

---

### Task 8: 画面状態への候補選択の追加

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/CandidateSelectionViewModelTests_候補選択画面テスト.cs`

**Interfaces:**
- Consumes: Task 3の `ICandidateSource`、既存の `IIdentifierWorkflowService`。
- Produces: ViewModelへ次を追加。
  - `ObservableCollection<CandidateFilterViewModel> CandidateFilters`
  - `ObservableCollection<ProductRecord> Candidates`
  - `ProductRecord? SelectedCandidate { get; set; }`
  - `string CandidateSummary { get; }`
  - `AsyncCommand RefreshCandidatesCommand`
  - `AsyncCommand ClearCandidateFiltersCommand`
  - `AsyncCommand AssignSelectedCandidateCommand`
  - `sealed class CandidateFilterViewModel { string Column; string Selected; ObservableCollection<string> Values; }`

`CandidateFilterViewModel.Values` の先頭に `(すべて)` を入れる。これが未選択を表す。ViewModelは `Selected` が `(すべて)` のとき空文字として `CandidateQuery` へ渡す。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは候補選択の画面状態を固定します。
// 特に「既にIDが付いた要素を選ぶと絞り込みが初期化される」挙動は、
// 再選定のモードを持たない設計の要です。
using Bui.Assemblies;
using Bui.Data;
using Bui.Products;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class CandidateSelectionViewModelTests_候補選択画面テスト
{
    [TestMethod]
    public async Task RefreshCandidatesAsync_選択肢の先頭にすべてを置く()
    {
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装")));

        await sut.RefreshCandidatesAsync();

        var filter = sut.CandidateFilters.Single(f => f.Column == "カテゴリー");
        Assert.AreEqual("(すべて)", filter.Values[0]);
        Assert.AreEqual("(すべて)", filter.Selected);
    }

    [TestMethod]
    public async Task RefreshCandidatesAsync_件数を要約に出す()
    {
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装"), Factory.Product("BM-0002", "外装")));

        await sut.RefreshCandidatesAsync();

        StringAssert.Contains(sut.CandidateSummary, "2");
    }

    [TestMethod]
    public async Task ClearCandidateFiltersAsync_すべて未選択に戻す()
    {
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装")));
        await sut.RefreshCandidatesAsync();
        sut.CandidateFilters.Single().Selected = "内装";

        await sut.ClearCandidateFiltersAsync();

        Assert.AreEqual("(すべて)", sut.CandidateFilters.Single().Selected);
    }

    [TestMethod]
    public async Task RefreshSelectionAsync_既存IDがあれば絞り込みを初期化する()
    {
        var product = Factory.Product("BM-0001", "内装");
        var sut = Factory.Create(Factory.Source(product), product, currentProductId: "BM-0001");

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.AreEqual("内装", sut.CandidateFilters.Single(f => f.Column == "カテゴリー").Selected);
        StringAssert.Contains(sut.StatusMessage, "絞り込み");
    }

    [TestMethod]
    public async Task RefreshSelectionAsync_既存IDがCSVに無ければ初期化せず知らせる()
    {
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装")), currentProductId: "旧-999");

        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        Assert.AreEqual("(すべて)", sut.CandidateFilters.Single(f => f.Column == "カテゴリー").Selected);
        StringAssert.Contains(sut.StatusMessage, "製品データにありません");
    }

    [TestMethod]
    public async Task RefreshCandidatesAsync_選択肢が空の項目を知らせる()
    {
        // 設定の列名と実データがずれていると候補が出ません。
        // 原因を利用者が特定できるよう、どの項目が空かを伝えます。
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装"), filterColumns: new[] { "存在しない列" }));

        await sut.RefreshCandidatesAsync();

        StringAssert.Contains(sut.StatusMessage, "存在しない列");
        StringAssert.Contains(sut.StatusMessage, "CSVにありません");
    }

    [TestMethod]
    public async Task AssignSelectedCandidateAsync_要素未選択なら実行しない()
    {
        var workflow = new Factory.RecordingWorkflow();
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装")), workflow: workflow);
        await sut.RefreshCandidatesAsync();
        sut.SelectedCandidate = sut.Candidates[0];

        await sut.AssignSelectedCandidateAsync();

        Assert.AreEqual(0, workflow.PreviewCalls);
        StringAssert.Contains(sut.StatusMessage, "要素を選択");
    }

    [TestMethod]
    public async Task AssignSelectedCandidateAsync_候補未選択なら実行しない()
    {
        var workflow = new Factory.RecordingWorkflow();
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装")), workflow: workflow);
        await sut.RefreshSelectionAsync(Factory.Snapshot(10));

        await sut.AssignSelectedCandidateAsync();

        Assert.AreEqual(0, workflow.PreviewCalls);
        StringAssert.Contains(sut.StatusMessage, "候補を選択");
    }

    [TestMethod]
    public async Task AssignSelectedCandidateAsync_同一タイプ限定でプレビューを求める()
    {
        var workflow = new Factory.RecordingWorkflow();
        var sut = Factory.Create(Factory.Source(Factory.Product("BM-0001", "内装")), workflow: workflow);
        await sut.RefreshSelectionAsync(Factory.Snapshot(10));
        await sut.RefreshCandidatesAsync();
        sut.SelectedCandidate = sut.Candidates[0];

        await sut.AssignSelectedCandidateAsync();

        Assert.AreEqual(1, workflow.PreviewCalls);
        Assert.AreEqual("BM-0001", workflow.LastProductId);
        Assert.AreEqual(ProductAssignmentScope.Selection, workflow.LastScope);
        Assert.IsTrue(workflow.LastSameTypeOnly);
    }
}
```

`Factory` は同ファイル内に置くテスト補助クラスとする。`Create` は既存の `BuiDockPaneViewModel` を、`ICandidateSource`・`IIdentifierWorkflowService`・`ISelectionDetailsService` の差し替え可能な実装で組み立てる。`Factory.Source` は与えた製品を返す `ICandidateSource` の偽実装（`filterColumns` を省略した場合は `カテゴリー` の1項目とする）、`Factory.RecordingWorkflow` は `IIdentifierWorkflowService` の記録用実装で、`PreviewCalls` / `LastProductId` / `LastScope` / `LastSameTypeOnly` を公開する。既存の `IdentifierWorkflowViewModelTests_ID付与画面テスト.cs` の補助実装を参考にし、同じ命名規則に合わせること。

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~CandidateSelectionViewModelTests"
```

Expected: プロパティとコマンドが存在せずコンパイルエラー。

- [ ] **Step 3: ViewModelへ状態とコマンドを追加する**

`BuiDockPaneViewModel_パネル状態.cs` のコンストラクタ末尾へ `ICandidateSource? candidateSource = null` を追加し、フィールドへ保持する。次を追加する。

```csharp
    /// <summary>1つの絞り込み項目の画面状態です。先頭の「(すべて)」が未選択を表します。</summary>
    public sealed class CandidateFilterViewModel : ObservableObject
    {
        private string _selected = AllValuesLabel;

        public CandidateFilterViewModel(string column) => Column = column;

        public string Column { get; }
        public ObservableCollection<string> Values { get; } = new();
        public string Selected { get => _selected; set => SetProperty(ref _selected, value); }
    }

    /// <summary>未選択を表す表示文字列。絞り込みへ渡すときは空文字に変換します。</summary>
    public const string AllValuesLabel = "(すべて)";

    public ObservableCollection<CandidateFilterViewModel> CandidateFilters { get; } = new();
    public ObservableCollection<ProductRecord> Candidates { get; } = new();
    public ProductRecord? SelectedCandidate { get => _selectedCandidate; set => SetProperty(ref _selectedCandidate, value); }
    public string CandidateSummary { get => _candidateSummary; private set => SetProperty(ref _candidateSummary, value); }
```

`RefreshCandidatesAsync` は現在の選択を `CandidateQuery` へ組み立てて `ICandidateSource.QueryAsync` を呼び、結果で `CandidateFilters` と `Candidates` を作り直す。`Selected` は結果の `CandidateFilterOptions.Selected` で上書きする。空文字なら `(すべて)` に戻す。

`CandidateSummary` は `IsTruncated` のとき「○件中200件を表示しています。絞り込んでください。」、そうでなければ「候補○件」とする。

選択肢が空の絞り込み項目があれば、状態表示に「絞り込み項目『○○』はCSVにありません。」を出す。設定の列名と実データがずれていることに気づけるようにする。候補が0件になった原因を利用者が特定できないと、設定を疑う手がかりが無くなる。

`RefreshSelectionAsync` の末尾で、現在のProduct_IDがある場合に候補側の初期化を行う。CSVに存在すればその製品の値で各項目を選択し、存在しなければ初期化せず状態表示に「現在のProduct_ID『○○』は製品データにありません。」を出す。

`AssignSelectedCandidateAsync` は既存の `PreviewProductIdAsync` と同じ経路を使い、`ProductAssignmentScope.Selection` と `sameTypeOnly: true` で `PreviewProductAsync` を呼ぶ。要素未選択・候補未選択は事前に弾き、状態表示で理由を伝える。

- [ ] **Step 4: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。

- [ ] **Step 5: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面"
git commit -m "feat: add candidate selection state to the pane"
```

---

### Task 9: 新規登録の画面状態

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/NewProductViewModelTests_新規登録画面テスト.cs`

**Interfaces:**
- Consumes: Task 6の `NewProductDraft`・`ProductDuplicateFinder`、Task 7の `ProductRegistrationCoordinator`。
- Produces: ViewModelへ次を追加。
  - `ObservableCollection<NewProductFieldViewModel> NewProductFields`
  - `ObservableCollection<DuplicateCandidate> DuplicateCandidates`
  - `string NewProductIdPreview { get; }`
  - `bool CanRegisterNewProduct { get; }`
  - `AsyncCommand CheckNewProductCommand`
  - `AsyncCommand RegisterNewProductCommand`
  - `sealed class NewProductFieldViewModel { string Column; string Value; bool IsRequired; }`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// このファイルは新規登録の画面状態を固定します。
// 必須未入力で登録できてしまうと、名前の無い製品がCSVへ入ります。
using Bui.Products;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class NewProductViewModelTests_新規登録画面テスト
{
    [TestMethod]
    public async Task CheckNewProductAsync_必須未入力なら登録できない()
    {
        var sut = NewProductFactory.Create();
        sut.NewProductFields.Single(f => f.Column == "カテゴリー").Value = "内装";

        await sut.CheckNewProductAsync();

        Assert.IsFalse(sut.CanRegisterNewProduct);
        StringAssert.Contains(sut.StatusMessage, "メーカー名");
    }

    [TestMethod]
    public async Task CheckNewProductAsync_必須が揃えば登録できる()
    {
        var sut = NewProductFactory.Create();
        NewProductFactory.FillRequired(sut);

        await sut.CheckNewProductAsync();

        Assert.IsTrue(sut.CanRegisterNewProduct);
    }

    [TestMethod]
    public async Task CheckNewProductAsync_重複候補を提示するが登録は止めない()
    {
        var sut = NewProductFactory.Create(NewProductFactory.Product("BM-0001", "内装", "メーカーA", "床タイル"));
        NewProductFactory.FillRequired(sut);

        await sut.CheckNewProductAsync();

        Assert.AreEqual(1, sut.DuplicateCandidates.Count);
        Assert.IsTrue(sut.CanRegisterNewProduct);
        StringAssert.Contains(sut.StatusMessage, "重複");
    }

    [TestMethod]
    public async Task CheckNewProductAsync_採番予定のIDを表示する()
    {
        var sut = NewProductFactory.Create(NewProductFactory.Product("BM_NEW-0003", "内装", "メーカーA", "床タイル"));

        await sut.CheckNewProductAsync();

        Assert.AreEqual("BM_NEW-0004", sut.NewProductIdPreview);
    }

    [TestMethod]
    public async Task CheckNewProductAsync_CSVへ書けないなら開始しない()
    {
        // 入力させてから失敗させると、書いた内容が無駄になります。
        var sut = NewProductFactory.CreateUnwritable();
        NewProductFactory.FillRequired(sut);

        await sut.CheckNewProductAsync();

        Assert.IsFalse(sut.CanRegisterNewProduct);
        StringAssert.Contains(sut.StatusMessage, "書き込めない");
    }

    [TestMethod]
    public async Task RegisterNewProductAsync_確認していなければ実行しない()
    {
        var sut = NewProductFactory.Create();
        NewProductFactory.FillRequired(sut);

        await sut.RegisterNewProductAsync();

        StringAssert.Contains(sut.StatusMessage, "内容を確認");
    }
}
```

`NewProductFactory` は同ファイル内のテスト補助クラス。`Create` は `ICandidateSource`・`IProductRepository`・`ProductRegistrationCoordinator` を差し替えた ViewModel を返す。`CreateUnwritable` は `GetStatusAsync` が `IsAvailable = false` を返すリポジトリで組み立てる。`FillRequired` はカテゴリー・メーカー名・製品名へ値を入れる。

- [ ] **Step 2: テストを実行して失敗を確認する**

Run:

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~NewProductViewModelTests"
```

Expected: プロパティとコマンドが存在せずコンパイルエラー。

- [ ] **Step 3: 実装する**

`NewProductFields` は設定の `NewProductColumns` から作る。`IsRequired` は `NewProductRequiredColumns` に含まれるかで決める。

`CheckNewProductAsync` は次を行う。

1. 入力から `NewProductDraft` を作る
2. `ProductDuplicateFinder.Find` で重複候補を求め、`DuplicateCandidates` を差し替える
3. `ProductIdAllocator.Next` で採番予定のIDを求め、`NewProductIdPreview` へ入れる
4. 必須の未入力があれば `CanRegisterNewProduct = false` とし、未入力の項目名を状態表示に出す
5. 重複候補があれば状態表示で注意を促す。ただし登録は止めない

`CheckNewProductAsync` の冒頭で `IProductRepository.GetStatusAsync` を呼び、`IsAvailable` が偽なら以降を行わず「製品CSVに書き込めないため、新規登録を開始できません。」と表示する。入力させてから失敗させない。

`RegisterNewProductAsync` は `CheckNewProductAsync` を通っていなければ実行せず、「先に内容を確認してください。」と表示する。通っていれば `ProductRegistrationCoordinator.RegisterAsync` を呼び、付与処理として既存のプレビュー経路を渡す。

- [ ] **Step 4: テストを実行して合格を確認する**

Run: Step 2と同じコマンド。Expected: PASS。

- [ ] **Step 5: 全テストを実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面"
git commit -m "feat: add new product registration state"
```

---

### Task 10: 検索タブの画面と合成

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`

**Interfaces:**
- Consumes: Task 8とTask 9のViewModelプロパティとコマンド、Task 3の `InMemoryCandidateSource`。
- Produces: 動作する検索タブ。

このタスクにはRevit APIが必要なため、私用PCではコンパイル確認までしかできない。共有ロジックとWPFアダプターだけを切り出した一時プロジェクトでコンパイルを確認する。

- [ ] **Step 1: 合成へ候補データ窓口を組み込む**

`BuiDockPaneComposition_画面構成.cs` の ViewModel 生成箇所へ次を追加する。

```csharp
            candidateSource: new InMemoryCandidateSource(
                products, loaded.Settings.Data.ProductColumns, loaded.Settings.Data.CandidateFilterColumns),
```

- [ ] **Step 2: 検索タブへ対象要素と絞り込みを追加する**

`SearchView_検索.xaml` の先頭へ、対象要素の表示と絞り込みを置く。既存の文字列検索と検索結果は残す。

```xml
  <TextBlock TextWrapping="Wrap" FontWeight="SemiBold">
    <Run Text="対象: "/><Run Text="{Binding Selection.CategoryName, Mode=OneWay}"/>
    <Run Text=" / "/><Run Text="{Binding Selection.TypeName, Mode=OneWay}"/>
    <Run Text=" / 同一タイプ "/><Run Text="{Binding Selection.SameTypeCount, Mode=OneWay}"/><Run Text="件"/>
  </TextBlock>
  <TextBlock Text="{Binding SelectedProductId, StringFormat=現在のProduct_ID: {0}}" TextWrapping="Wrap" Margin="0,2,0,6"/>
  <ItemsControl ItemsSource="{Binding CandidateFilters}">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <DockPanel Margin="0,0,0,4">
          <TextBlock DockPanel.Dock="Left" Text="{Binding Column}" Width="90" VerticalAlignment="Center"/>
          <ComboBox ItemsSource="{Binding Values}" SelectedItem="{Binding Selected, Mode=TwoWay}"/>
        </DockPanel>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
  <WrapPanel Margin="0,4,0,0">
    <Button Content="絞り込む" Command="{Binding RefreshCandidatesCommand}" Margin="0,0,6,0"/>
    <Button Content="絞り込みを解除" Command="{Binding ClearCandidateFiltersCommand}"/>
  </WrapPanel>
  <TextBlock Text="{Binding CandidateSummary}" TextWrapping="Wrap" Margin="0,6,0,0"/>
  <ListBox ItemsSource="{Binding Candidates}" SelectedItem="{Binding SelectedCandidate, Mode=TwoWay}" MaxHeight="180">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <TextBlock TextWrapping="Wrap">
          <Run Text="{Binding ProductName, Mode=OneWay}"/>
          <Run Text=" / "/><Run Text="{Binding Manufacturer, Mode=OneWay}"/>
          <Run Text=" / "/><Run Text="{Binding ModelNumber, Mode=OneWay}"/>
        </TextBlock>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
  <Button Content="選択要素に付与" Command="{Binding AssignSelectedCandidateCommand}" Margin="0,6,0,0"/>
```

- [ ] **Step 3: 新規登録の折りたたみ領域を追加する**

同じファイルの末尾へ置く。

```xml
  <Expander Header="候補が無いので新規登録" Margin="0,10,0,0">
    <StackPanel Margin="0,6,0,0">
      <ItemsControl ItemsSource="{Binding NewProductFields}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <DockPanel Margin="0,0,0,4">
              <TextBlock DockPanel.Dock="Left" Text="{Binding Column}" Width="90" VerticalAlignment="Center"/>
              <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}"/>
            </DockPanel>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
      <TextBlock Text="{Binding NewProductIdPreview, StringFormat=採番予定: {0}}" Margin="0,4,0,0"/>
      <TextBlock Text="重複候補" Margin="0,6,0,0"/>
      <ListBox ItemsSource="{Binding DuplicateCandidates}" MaxHeight="100" DisplayMemberPath="Product.ProductName"/>
      <WrapPanel Margin="0,6,0,0">
        <Button Content="内容を確認" Command="{Binding CheckNewProductCommand}" Margin="0,0,6,0"/>
        <Button Content="新規登録して付与" Command="{Binding RegisterNewProductCommand}"
                IsEnabled="{Binding CanRegisterNewProduct}"/>
      </WrapPanel>
    </StackPanel>
  </Expander>
```

- [ ] **Step 4: コンパイルとXAMLの妥当性を確認する**

共有ロジックとWPFアダプターを切り出してコンパイルする。

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
python -c "import xml.dom.minidom as m; m.parse(r'BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml'); print('XAML OK')"
```

Expected: テスト全合格、XAMLがXMLとして妥当。

- [ ] **Step 5: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin"
git commit -m "feat: extend the search tab with candidate selection"
```

---

### Task 11: 実機確認項目と手順書

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/TestChecklist_Revit実機確認.md`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt`

**Interfaces:**
- Consumes: Task 1から10のすべて。
- Produces: 会社PCで実施する確認手順。

- [ ] **Step 1: 実機確認項目を追加する**

`TestChecklist_Revit実機確認.md` へ次の行を追加する。各行に日付・PC・Revit build・BUI version・結果・証跡パスの記入欄を設ける。

- 検索タブで絞り込み、候補を選び、選択要素に付与できる
- 同一タイプの件数がRevitの選択件数と一致する
- **Ctrl+Z 1回**で一括付与全体が戻る
- 既にProduct_IDが付いた要素を選ぶと絞り込みが初期化される
- 存在しないProduct_IDが付いた要素では初期化されず、その旨が表示される
- 競合がある場合に確認が2回出る。同意しない場合、競合分が除かれる
- 新規登録して付与できる（**複製CSVで実施すること**）
- 新規登録後にRevit書込を失敗させると、CSVの行が消える
- プロジェクト全体規模で応答が実用的である

- [ ] **Step 2: 問題解決へ症状を追加する**

`Troubleshooting_問題解決.md` へ次を追加する。

- 候補が0件になる … 絞り込みが残っていないか。「絞り込みを解除」を押す
- 絞り込み項目が空 … 設定の `candidateFilterColumns` がCSVの列名と一致しているか。`CheckCsv_CSV診断.ps1` で実際の列名を確認する
- 付与ボタンが押せない … Revitで要素を選択しているか、候補を選んでいるか
- 新規登録が始められない … 製品CSVが読み取り専用か使用中でないか

- [ ] **Step 3: 操作手順を更新する**

`操作手順_会社PC_次にやること.txt` を、候補選択の確認手順へ差し替える。UTF-8 BOM付き・CRLFで保存する。

```bash
python - << 'PYEOF'
p = 'BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt'
raw = open(p, 'rb').read()
if raw.startswith(b'\xef\xbb\xbf'): raw = raw[3:]
t = raw.decode('utf-8').replace('\r\n', '\n').replace('\n', '\r\n')
open(p, 'wb').write(b'\xef\xbb\xbf' + t.encode('utf-8'))
print('BOM+CRLF OK')
PYEOF
```

- [ ] **Step 4: 全テストと走査を実行してコミットする**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
git grep -E "^(<<<<<<< |=======$|>>>>>>> )" -- . && echo "競合マーカーあり" || echo "競合マーカーなし"
git add "BUI_Addin_CSharpアドイン/docs_説明書"
git commit -m "docs: add acceptance items for candidate selection"
```

---

## Implementation Completion Gate

完了と判断する前に次を満たすこと。

1. 全自動テストが合格する。終了コードで判定する。
2. `git status --short` に生成物・会社CSV/RVT・認証情報・ローカル設定・ログ・バックアップ・一時ファイルが無い。
3. 追跡ファイルに競合マーカーと制御文字が無い。
4. `CheckDeploy_配置診断.ps1` の「入っている機能」がすべて `[OK]` である。
5. 会社PCの実機確認で、重大な未解決項目が無い。
6. ビルド成功だけを根拠に完了を報告しない。
