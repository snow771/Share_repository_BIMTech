# Bui.Curation DB内容更新機能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Bui.Curation`へ「DB内容更新」タブを新設し、GoldStandardの全11表について、SQL Server上の既存行を画面上で修正・削除できるようにする。あわせて接続設定（サーバー名・データベース名・旧新ID CSVのパス）を次回起動時に自動復元し、複数プロジェクトフォルダを親フォルダ指定で一括取込できるようにする。

**Architecture:** 表と表の関連（主キー・外部キー）をBui.Shared側の静的な登録簿として持ち、これを使って「削除前に確認すべき子表の件数」を組み立てる。SQL Serverへの実際の読み書きはBui.Curation側の新しいクラスに閉じ込め（既存の`CurationDbWriter`と同じ方針）、確認ダイアログに出す「変更一覧」の組み立てはBui.Shared側の純粋関数としてテスト可能にする。

**Tech Stack:** C# / .NET 8 / WPF（Bui.Curation）、Microsoft.Data.SqlClient、System.Text.Json、MSTest（Bui.Tests）

## Global Constraints

- 表名・主キー列名・外部キー列名は、すべてTask 1で定義する固定のC#データ（`DbEditorTableRegistry`）から取得する。ユーザー入力やCSVヘッダーに由来する値ではないため、SQL文への埋め込みにおいて別途の許可リスト検証は不要（角括弧によるクォートのみで十分）。これはTask 7（仮登録レビュー機能・`CurationSqlCommandBuilder`）でCSVヘッダー由来の列名に許可リストが必要だったのとは前提が異なる。
- 主キー列は常に読み取り専用として扱う。主キー列の値を変更するUPDATE文は生成しない。
- 新規行の追加（INSERT）はこの計画のスコープ外。
- 元のCSVファイルへの反映はこの計画のスコープ外。SQL Serverのみを対象とする。
- 複数管理者による同時編集の排他制御はこの計画のスコープ外。
- 1回の保存操作（更新・削除の組）は1トランザクションとしてまとめて実行し、一部だけ反映される状態を作らない。
- ローカルの.NET SDKは、リポジトリルートの`.superpowers/dotnet-sdk-8/dotnet.exe`にある（`BUI_Addin_CSharpアドイン`フォルダ内からは`..\.superpowers\dotnet-sdk-8\dotnet.exe`で呼び出す）。`Bui.Curation.csproj`はRevit非依存のため、私物PCでビルド・テスト可能。
- `Bui.Shared_共有ロジック`はプロジェクトファイルを持たない、ソースリンクだけのフォルダ。新規ファイルを追加した場合、Visual Studioでの増分ビルドで反映されないことがある。反映されない場合は`bin`/`obj`を削除してCLIで`dotnet build`し直す。
- このプロジェクトはmainブランチで直接作業する（ワークツリー分離は行わない）。

---

### Task 1: DbEditorTableRegistry（表と関連の定義）

GoldStandardの全11表について、主キー列名と外部キー（列名＋参照先の表）を静的に定義する。design specの「表と関連の定義」表に対応する。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardTables_表定義.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/DbEditorTableRegistry_DB編集用表定義.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/DbEditorTableRegistryTests_DB編集用表定義テスト.cs`

**Interfaces:**
- Consumes: なし（既存の`Bui.Data.GoldStandard.GoldStandardTables.Columns.*`定数のうち、`ProductId`, `ManufacturerId`, `MaterialCategoryId`, `PrimarySourceId`, `StatusId`, `SourceId`, `ReliabilityRankId`, `SpecKey`, `GoldId`は既に存在する。以下は今回`GoldStandardTables_表定義.cs`へ新規追加する）
- Produces:
  - `Bui.Data.GoldStandard.GoldStandardTables.StatusMaster : string`（テーブル名定数。**`GoldStandardTables.All`には追加しない**。追加するとCSVフォルダ走査（`GoldStandardCsvReader.Load`）が現場フォルダにStatus_Master.csvを要求してしまい、既存の仮登録取込フローに無関係な影響が出るため）
  - `Bui.Data.GoldStandard.GoldStandardTables.Columns.VariantId : string = "variant_id"`
  - `Bui.Data.GoldStandard.GoldStandardTables.Columns.DisplayGroupId : string = "display_group_id"`
  - `Bui.Data.GoldStandard.GoldStandardTables.Columns.ParentCategoryId : string = "parent_category_id"`
  - `Bui.Data.GoldStandard.GoldStandardTables.Columns.ImageId : string = "image_id"`
  - `Bui.Curation.ForeignKeyReference(string Column, string TargetTable) : record`
  - `Bui.Curation.TableDefinition(string TableName, string PrimaryKeyColumn, IReadOnlyList<ForeignKeyReference> ForeignKeys) : record`
  - `Bui.Curation.DbEditorTableRegistry.Tables : IReadOnlyList<TableDefinition>`（全11表）
  - `Bui.Curation.DbEditorTableRegistry.Get(string tableName) : TableDefinition`（見つからなければ`InvalidOperationException`）
  - `Bui.Curation.DbEditorTableRegistry.GetChildren(string tableName) : IReadOnlyList<(string ChildTable, string ForeignKeyColumn)>`（他の全表の`ForeignKeys`のうち`TargetTable`が一致するものを集めて返す。`Tables`自体には子表の一覧を持たせず、この関数で毎回導出することで定義の重複・不整合を防ぐ）

- [ ] **Step 1: 既存の`GoldStandardTables_表定義.cs`へ列定数を追加する**

`BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardTables_表定義.cs`の`public const string ProductVariants = "Product_Variants";`の下（`ProductImages`定数の前でも後でも可）に以下を追加する:

```csharp
    public const string StatusMaster = "Status_Master";
```

**注意:** `All`配列（`public static IReadOnlyList<string> All { get; } = new[] { ... }`）には`StatusMaster`を追加しないこと。

`Columns`静的クラス内、`public const string Note = "note";`の下に以下を追加する:

```csharp
        public const string VariantId = "variant_id";
        public const string DisplayGroupId = "display_group_id";
        public const string ParentCategoryId = "parent_category_id";
        public const string ImageId = "image_id";
```

- [ ] **Step 2: 失敗するテストを書く**

```csharp
// BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/DbEditorTableRegistryTests_DB編集用表定義テスト.cs
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public sealed class DbEditorTableRegistryTests_DB編集用表定義テスト
{
    [TestMethod]
    public void Tables_全11表が定義されている()
    {
        Assert.AreEqual(11, DbEditorTableRegistry.Tables.Count);
    }

    [TestMethod]
    public void Get_Productsの主キーはproduct_id()
    {
        var definition = DbEditorTableRegistry.Get("Products");
        Assert.AreEqual("product_id", definition.PrimaryKeyColumn);
    }

    [TestMethod]
    public void Get_Productsの外部キーは4件()
    {
        var definition = DbEditorTableRegistry.Get("Products");
        Assert.AreEqual(4, definition.ForeignKeys.Count);
        CollectionAssert.Contains(
            definition.ForeignKeys.Select(fk => fk.TargetTable).ToArray(), "Manufacturers");
        CollectionAssert.Contains(
            definition.ForeignKeys.Select(fk => fk.TargetTable).ToArray(), "Status_Master");
    }

    [TestMethod]
    public void GetChildren_Productsの子表は3表()
    {
        var children = DbEditorTableRegistry.GetChildren("Products");
        Assert.AreEqual(3, children.Count);
        var childTables = children.Select(c => c.ChildTable).ToArray();
        CollectionAssert.Contains(childTables, "Product_Variants");
        CollectionAssert.Contains(childTables, "Product_Specs");
        CollectionAssert.Contains(childTables, "Product_Images");
    }

    [TestMethod]
    public void GetChildren_Status_Masterの子表は6表()
    {
        var children = DbEditorTableRegistry.GetChildren("Status_Master");
        Assert.AreEqual(6, children.Count);
    }

    [TestMethod]
    public void GetChildren_他表から参照されない表は0件()
    {
        Assert.AreEqual(0, DbEditorTableRegistry.GetChildren("Product_Specs").Count);
        Assert.AreEqual(0, DbEditorTableRegistry.GetChildren("Product_Images").Count);
    }

    [TestMethod]
    public void Get_存在しない表名は例外()
    {
        Assert.ThrowsException<InvalidOperationException>(() => DbEditorTableRegistry.Get("NoSuchTable"));
    }
}
```

- [ ] **Step 3: テストを実行して失敗を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~DbEditorTableRegistryTests` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: FAIL（`DbEditorTableRegistry`が存在しない、ビルドエラー）

- [ ] **Step 4: `DbEditorTableRegistry_DB編集用表定義.cs`を実装する**

```csharp
// このファイルの目的:
//   GoldStandardの全11表について、主キー列と外部キー（列名＋参照先の表）を
//   静的に定義します。DB内容更新機能の「削除前に確認すべき子表の件数」や
//   「親表の実在確認」は、すべてこの定義から導出します。
//
// なぜ子表の一覧を別途持たないのか:
//   各表が「自分の外部キー（親への参照）」だけを持てば、「ある表の子表」は
//   全表を走査して逆引きすれば求まります。子表の一覧を別途手で書くと、
//   親側の定義を直したときに子側の書き忘れが起きるため、導出のみにします。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>ある表の、他表への外部キー1本です。</summary>
public sealed record ForeignKeyReference(string Column, string TargetTable);

/// <summary>1つの表の、主キーと外部キー一式です。</summary>
public sealed record TableDefinition(
    string TableName, string PrimaryKeyColumn, IReadOnlyList<ForeignKeyReference> ForeignKeys);

/// <summary>GoldStandard全11表の、主キー・外部キーの定義一覧です。</summary>
public static class DbEditorTableRegistry
{
    public static IReadOnlyList<TableDefinition> Tables { get; } = new[]
    {
        new TableDefinition(GoldStandardTables.Manufacturers, GoldStandardTables.Columns.ManufacturerId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.StatusMaster, GoldStandardTables.Columns.StatusId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.ReliabilityRanks, GoldStandardTables.Columns.ReliabilityRankId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.MaterialCategories, GoldStandardTables.Columns.MaterialCategoryId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ParentCategoryId, GoldStandardTables.MaterialCategories),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.Sources, GoldStandardTables.Columns.SourceId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Manufacturers),
                new ForeignKeyReference(GoldStandardTables.Columns.ReliabilityRankId, GoldStandardTables.ReliabilityRanks),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.Products, GoldStandardTables.Columns.ProductId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Manufacturers),
                new ForeignKeyReference(GoldStandardTables.Columns.MaterialCategoryId, GoldStandardTables.MaterialCategories),
                new ForeignKeyReference(GoldStandardTables.Columns.PrimarySourceId, GoldStandardTables.Sources),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.ProductVariants, GoldStandardTables.Columns.VariantId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ProductId, GoldStandardTables.Products),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.DisplayGroups, GoldStandardTables.Columns.DisplayGroupId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.SpecMaster, GoldStandardTables.Columns.SpecKey,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.DisplayGroupId, GoldStandardTables.DisplayGroups),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.ProductSpecs, GoldStandardTables.Columns.GoldId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ProductId, GoldStandardTables.Products),
                new ForeignKeyReference(GoldStandardTables.Columns.VariantId, GoldStandardTables.ProductVariants),
                new ForeignKeyReference(GoldStandardTables.Columns.SpecKey, GoldStandardTables.SpecMaster),
                new ForeignKeyReference(GoldStandardTables.Columns.SourceId, GoldStandardTables.Sources),
                new ForeignKeyReference(GoldStandardTables.Columns.ReliabilityRankId, GoldStandardTables.ReliabilityRanks),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.ProductImages, GoldStandardTables.Columns.ImageId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ProductId, GoldStandardTables.Products),
                new ForeignKeyReference(GoldStandardTables.Columns.VariantId, GoldStandardTables.ProductVariants),
                new ForeignKeyReference(GoldStandardTables.Columns.SourceId, GoldStandardTables.Sources),
            }),
    };

    public static TableDefinition Get(string tableName)
    {
        var found = Tables.FirstOrDefault(table => string.Equals(table.TableName, tableName, StringComparison.Ordinal));
        if (found is null)
        {
            throw new InvalidOperationException($"未定義の表です: {tableName}");
        }
        return found;
    }

    /// <summary>tableNameの主キーを外部キーとして参照している、他の全表を返します。</summary>
    public static IReadOnlyList<(string ChildTable, string ForeignKeyColumn)> GetChildren(string tableName) =>
        Tables
            .Where(table => !string.Equals(table.TableName, tableName, StringComparison.Ordinal))
            .SelectMany(table => table.ForeignKeys
                .Where(fk => string.Equals(fk.TargetTable, tableName, StringComparison.Ordinal))
                .Select(fk => (table.TableName, fk.Column)))
            .ToArray();
}
```

- [ ] **Step 5: テストを実行して成功を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~DbEditorTableRegistryTests` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（7件すべて）

- [ ] **Step 6: 全体テストを実行し既存テストに影響がないことを確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件、Task開始前の件数＋7件）

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/GoldStandardTables_表定義.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/DbEditorTableRegistry_DB編集用表定義.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/DbEditorTableRegistryTests_DB編集用表定義テスト.cs"
git commit -m "feat: DbEditorTableRegistryを追加し、GoldStandard全11表の主キー・外部キー定義を持たせる"
```

---

### Task 2: TableChangeSetBuilder / DeletionWarningBuilder（変更一覧・削除警告の組立）

保存確認ダイアログに出す「変更内容一覧」と「削除予定行への警告文」を、SQL接続を持たない純粋関数として組み立てる。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/TableChangeSetBuilder_変更差分組立.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/TableChangeSetBuilderTests_変更差分組立テスト.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `Bui.Curation.CellChange(string PrimaryKeyValue, string Column, string OldValue, string NewValue) : record`
  - `Bui.Curation.ChangeSummary(IReadOnlyList<CellChange> Updates, IReadOnlyList<string> DeletedPrimaryKeyValues) : record`
  - `Bui.Curation.TableChangeSetBuilder.Build(IReadOnlyList<IReadOnlyDictionary<string,string>> originalRows, IReadOnlyList<IReadOnlyDictionary<string,string>> currentRows, IReadOnlySet<string> deletedPrimaryKeyValues, string primaryKeyColumn) : ChangeSummary`
  - `Bui.Curation.DeletionWarningBuilder.BuildWarning(string primaryKeyValue, IReadOnlyList<(string ChildTable, int Count)> childCounts) : string?`（0件のみなら`null`）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/TableChangeSetBuilderTests_変更差分組立テスト.cs
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public sealed class TableChangeSetBuilderTests_変更差分組立テスト
{
    private static IReadOnlyDictionary<string, string> Row(string id, string name) =>
        new Dictionary<string, string> { ["product_id"] = id, ["product_name"] = name };

    [TestMethod]
    public void Build_変更したセルが1件検出される()
    {
        var original = new[] { Row("P-1", "旧名") };
        var current = new[] { Row("P-1", "新名") };

        var summary = TableChangeSetBuilder.Build(original, current, new HashSet<string>(), "product_id");

        Assert.AreEqual(1, summary.Updates.Count);
        Assert.AreEqual("P-1", summary.Updates[0].PrimaryKeyValue);
        Assert.AreEqual("product_name", summary.Updates[0].Column);
        Assert.AreEqual("旧名", summary.Updates[0].OldValue);
        Assert.AreEqual("新名", summary.Updates[0].NewValue);
    }

    [TestMethod]
    public void Build_変更が無い行は結果に含まれない()
    {
        var original = new[] { Row("P-1", "同じ名前") };
        var current = new[] { Row("P-1", "同じ名前") };

        var summary = TableChangeSetBuilder.Build(original, current, new HashSet<string>(), "product_id");

        Assert.AreEqual(0, summary.Updates.Count);
    }

    [TestMethod]
    public void Build_削除指定された行はセル変更があってもUpdatesに含めずDeletedへ回す()
    {
        var original = new[] { Row("P-1", "旧名") };
        var current = new[] { Row("P-1", "新名") };
        var deleted = new HashSet<string> { "P-1" };

        var summary = TableChangeSetBuilder.Build(original, current, deleted, "product_id");

        Assert.AreEqual(0, summary.Updates.Count);
        CollectionAssert.Contains(summary.DeletedPrimaryKeyValues.ToArray(), "P-1");
    }

    [TestMethod]
    public void BuildWarning_すべて0件ならnull()
    {
        var warning = DeletionWarningBuilder.BuildWarning("P-1",
            new[] { ("Product_Specs", 0), ("Product_Images", 0) });
        Assert.IsNull(warning);
    }

    [TestMethod]
    public void BuildWarning_1件以上あれば表名と件数を含む文字列()
    {
        var warning = DeletionWarningBuilder.BuildWarning("P-1",
            new[] { ("Product_Specs", 5), ("Product_Images", 0) });

        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "P-1");
        StringAssert.Contains(warning, "Product_Specs");
        StringAssert.Contains(warning, "5件");
        StringAssert.DoesNotMatch(warning, new System.Text.RegularExpressions.Regex("Product_Images"));
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~TableChangeSetBuilderTests` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: FAIL（ビルドエラー、対象クラスが無い）

- [ ] **Step 3: `TableChangeSetBuilder_変更差分組立.cs`を実装する**

```csharp
// このファイルの目的:
//   DB内容更新画面で、Excelライクなグリッド上での編集・削除を、
//   保存前の確認ダイアログに出す「変更一覧」へ組み立てます。
//   SQL接続を一切持たない、純粋な差分抽出ロジックです。

namespace Bui.Curation;

/// <summary>1セルの変更です。</summary>
public sealed record CellChange(string PrimaryKeyValue, string Column, string OldValue, string NewValue);

/// <summary>1回の保存操作でSQL Serverへ反映する変更一式です。</summary>
public sealed record ChangeSummary(IReadOnlyList<CellChange> Updates, IReadOnlyList<string> DeletedPrimaryKeyValues);

/// <summary>元の行一覧と、編集後の行一覧を突き合わせ、変更一覧を組み立てます。</summary>
public static class TableChangeSetBuilder
{
    public static ChangeSummary Build(
        IReadOnlyList<IReadOnlyDictionary<string, string>> originalRows,
        IReadOnlyList<IReadOnlyDictionary<string, string>> currentRows,
        IReadOnlySet<string> deletedPrimaryKeyValues,
        string primaryKeyColumn)
    {
        ArgumentNullException.ThrowIfNull(originalRows);
        ArgumentNullException.ThrowIfNull(currentRows);
        ArgumentNullException.ThrowIfNull(deletedPrimaryKeyValues);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKeyColumn);

        var originalByKey = originalRows.ToDictionary(row => row[primaryKeyColumn], row => row, StringComparer.Ordinal);
        var updates = new List<CellChange>();

        foreach (var currentRow in currentRows)
        {
            var key = currentRow[primaryKeyColumn];
            if (deletedPrimaryKeyValues.Contains(key)) continue;
            if (!originalByKey.TryGetValue(key, out var originalRow)) continue;

            foreach (var pair in currentRow)
            {
                if (pair.Key == primaryKeyColumn) continue;
                var oldValue = originalRow.TryGetValue(pair.Key, out var value) ? value : string.Empty;
                if (!string.Equals(oldValue, pair.Value, StringComparison.Ordinal))
                {
                    updates.Add(new CellChange(key, pair.Key, oldValue, pair.Value));
                }
            }
        }

        return new ChangeSummary(updates, deletedPrimaryKeyValues.ToArray());
    }
}

/// <summary>削除予定の行に対し、子表からの参照件数をもとに警告文を組み立てます。</summary>
public static class DeletionWarningBuilder
{
    /// <summary>全件数が0なら警告は不要としてnullを返します。</summary>
    public static string? BuildWarning(string primaryKeyValue, IReadOnlyList<(string ChildTable, int Count)> childCounts)
    {
        var nonZero = childCounts.Where(entry => entry.Count > 0).ToArray();
        if (nonZero.Length == 0) return null;

        var detail = string.Join("、", nonZero.Select(entry => $"{entry.ChildTable}: {entry.Count}件"));
        return $"「{primaryKeyValue}」は次の表から参照されています。削除すると参照エラーになる可能性があります。{detail}";
    }
}
```

- [ ] **Step 4: テストを実行して成功を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~TableChangeSetBuilderTests` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（5件すべて）

- [ ] **Step 5: 全体テストを実行する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件）

- [ ] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/TableChangeSetBuilder_変更差分組立.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/TableChangeSetBuilderTests_変更差分組立テスト.cs"
git commit -m "feat: TableChangeSetBuilder/DeletionWarningBuilderを追加し、保存確認画面向けの変更差分・削除警告を組み立てられるようにする"
```

---

### Task 3: CurationUiSettings / CurationUiSettingsStore（接続設定の自動復元）

サーバー名・データベース名・旧新ID CSVのパスを、次回起動時に自動復元できるようJSONファイルへ保存・読込する。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationUiSettings_画面設定保存.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationUiSettingsStoreTests_画面設定保存テスト.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `Bui.Curation.CurationUiSettings`（`SqlServerName`, `SqlDatabaseName`, `IdAliasCsvPath` の3つの`string`プロパティを持つ可変クラス。既定値はすべて空文字列）
  - `Bui.Curation.CurationUiSettingsStore.Load(string filePath) : CurationUiSettings`（ファイルが無い・壊れている場合は既定値を返す。例外を投げない）
  - `Bui.Curation.CurationUiSettingsStore.Save(string filePath, CurationUiSettings settings) : void`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
// BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationUiSettingsStoreTests_画面設定保存テスト.cs
using Bui.Curation;

namespace Bui.Tests.Curation_仮登録レビュー;

[TestClass]
public sealed class CurationUiSettingsStoreTests_画面設定保存テスト
{
    private string _tempFile = string.Empty;

    [TestInitialize]
    public void Setup() => _tempFile = Path.Combine(Path.GetTempPath(), $"bui_ui_settings_{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [TestMethod]
    public void Load_ファイルが無ければ既定値を返す()
    {
        var settings = CurationUiSettingsStore.Load(_tempFile);
        Assert.AreEqual(string.Empty, settings.SqlServerName);
        Assert.AreEqual(string.Empty, settings.SqlDatabaseName);
        Assert.AreEqual(string.Empty, settings.IdAliasCsvPath);
    }

    [TestMethod]
    public void SaveしてLoadすると値が復元される()
    {
        var settings = new CurationUiSettings
        {
            SqlServerName = "localhost\\sqlexpress",
            SqlDatabaseName = "GoldStandardDB",
            IdAliasCsvPath = @"C:\共有\IdAlias.csv",
        };

        CurationUiSettingsStore.Save(_tempFile, settings);
        var loaded = CurationUiSettingsStore.Load(_tempFile);

        Assert.AreEqual(settings.SqlServerName, loaded.SqlServerName);
        Assert.AreEqual(settings.SqlDatabaseName, loaded.SqlDatabaseName);
        Assert.AreEqual(settings.IdAliasCsvPath, loaded.IdAliasCsvPath);
    }

    [TestMethod]
    public void Load_壊れたJSONなら既定値を返し例外を投げない()
    {
        File.WriteAllText(_tempFile, "{ これは壊れたJSON");
        var settings = CurationUiSettingsStore.Load(_tempFile);
        Assert.AreEqual(string.Empty, settings.SqlServerName);
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~CurationUiSettingsStoreTests` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: FAIL（ビルドエラー）

- [ ] **Step 3: `CurationUiSettings_画面設定保存.cs`を実装する**

```csharp
// このファイルの目的:
//   DB内容更新画面・仮登録レビュー画面で共通の接続設定
//   （サーバー名・データベース名・旧新ID CSVのパス）を、
//   次回起動時に自動復元できるようJSONファイルへ保存・読込します。
//   CurationQueueStore（queue.json）と同じ、単純なSystem.Text.Json方式です。

using System.Text.Json;

namespace Bui.Curation;

/// <summary>画面上部の接続設定です。</summary>
public sealed class CurationUiSettings
{
    public string SqlServerName { get; set; } = string.Empty;
    public string SqlDatabaseName { get; set; } = string.Empty;
    public string IdAliasCsvPath { get; set; } = string.Empty;
}

/// <summary>接続設定(ui_settings.json)の読み書きです。</summary>
public static class CurationUiSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>ファイルが無い・壊れている場合は既定値を返します（例外を投げません）。</summary>
    public static CurationUiSettings Load(string filePath)
    {
        if (!File.Exists(filePath)) return new CurationUiSettings();

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<CurationUiSettings>(json, Options) ?? new CurationUiSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CurationUiSettings();
        }
    }

    public static void Save(string filePath, CurationUiSettings settings)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(filePath, json);
    }
}
```

- [ ] **Step 4: テストを実行して成功を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~CurationUiSettingsStoreTests` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（3件すべて）

- [ ] **Step 5: 全体テストを実行する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件）

- [ ] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/CurationUiSettings_画面設定保存.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/CurationUiSettingsStoreTests_画面設定保存テスト.cs"
git commit -m "feat: CurationUiSettings/CurationUiSettingsStoreを追加し、接続設定を次回起動時に自動復元できるようにする"
```

---

### Task 4: DbEditorSqlGateway（SQL Serverへの汎用読み書き）

任意の表を`SELECT * FROM`で読み込み、変更をUPDATE/DELETEとして1トランザクションで反映する。`Microsoft.Data.SqlClient`を直接使うのはこのファイルだけにする（既存の`CurationDbWriter_DB登録.cs`と同じ方針）。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/DbEditorSqlGateway_DB編集SQL接続.cs`

**Interfaces:**
- Consumes:
  - `Bui.Curation.CellChange`（Task 2）
  - `Bui.Curation.DbEditorTableRegistry` / `TableDefinition` / `ForeignKeyReference`（Task 1）— 表名・主キー列名・外部キー列名は必ずこの登録簿から取得された値を渡すこと（固定データのため、SQL文への埋め込みは角括弧クォートのみで安全）
- Produces:
  - `Bui.Curation.DbEditorTableSnapshot(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string,string>> Rows) : record`
  - `Bui.Curation.DbEditorSqlGateway.LoadTableAsync(string connectionString, string tableName, CancellationToken cancellationToken) : Task<DbEditorTableSnapshot>`
  - `Bui.Curation.DbEditorSqlGateway.CountReferencingRowsAsync(string connectionString, string childTable, string foreignKeyColumn, string keyValue, CancellationToken cancellationToken) : Task<int>`
  - `Bui.Curation.DbEditorSqlGateway.ExistsAsync(string connectionString, string table, string primaryKeyColumn, string keyValue, CancellationToken cancellationToken) : Task<bool>`
  - `Bui.Curation.DbEditorSqlGateway.ApplyChangesAsync(string connectionString, string tableName, string primaryKeyColumn, IReadOnlyList<CellChange> updates, IReadOnlyList<string> deletedPrimaryKeyValues, CancellationToken cancellationToken) : Task<string?>`（成功時null、失敗時は理由）

このファイルはSQL Server接続を要するため単体テストは行わない（目視確認のみ、既存の`CurationDbWriter`と同じ方針）。ビルドが通ることだけをこのタスクで確認する。

- [ ] **Step 1: `DbEditorSqlGateway_DB編集SQL接続.cs`を実装する**

```csharp
// このファイルの目的:
//   DbEditorTableRegistryが定義する任意の表を、SQL Serverから直接
//   読み込み・更新・削除します。Microsoft.Data.SqlClientを直接使うのは
//   このファイルだけにします（CurationDbWriter_DB登録.csと同じ判断）。
//
// なぜ列名・表名の許可リスト検証をしないのか:
//   ここで使う表名・主キー列名・外部キー列名は、すべてDbEditorTableRegistry
//   という固定のC#データから渡されます（CSVヘッダーのような外部入力由来では
//   ありません）。そのため、CurationSqlCommandBuilderのような許可リストは
//   不要で、SQLインジェクション対策としては角括弧によるクォートのみで
//   十分です。呼び出し側は必ずDbEditorTableRegistryから取得した値を渡して
//   ください。
//
// なぜ1件ずつUPDATEするのか:
//   複数セルにまたがる変更も、行（主キー値）単位でまとめて1本のUPDATE文に
//   することで、SQL文の本数を行数に抑えます。削除は1行1本のDELETE文です。
//   全体を1トランザクションにまとめ、一部だけ反映される状態を作りません。

using Microsoft.Data.SqlClient;

namespace Bui.Curation;

/// <summary>1つの表の、列名一覧と全行です。</summary>
public sealed record DbEditorTableSnapshot(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, string>> Rows);

/// <summary>DbEditorTableRegistryが定義する表へ、SQL Serverごしに読み書きします。</summary>
public static class DbEditorSqlGateway
{
    public static async Task<DbEditorTableSnapshot> LoadTableAsync(
        string connectionString, string tableName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM dbo.[{tableName}]";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<Dictionary<string, string>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                var value = reader[column];
                row[column] = value is DBNull ? string.Empty : Convert.ToString(value) ?? string.Empty;
            }
            rows.Add(row);
        }

        return new DbEditorTableSnapshot(columns, rows);
    }

    /// <summary>childTable.foreignKeyColumn = keyValue の行数です。削除前の関連件数確認に使います。</summary>
    public static async Task<int> CountReferencingRowsAsync(
        string connectionString, string childTable, string foreignKeyColumn, string keyValue,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(childTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(foreignKeyColumn);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM dbo.[{childTable}] WHERE [{foreignKeyColumn}] = @key";
        command.Parameters.AddWithValue("@key", keyValue.Trim());
        return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
    }

    /// <summary>table.primaryKeyColumn = keyValue の行が存在するかです。親IDの実在確認に使います。</summary>
    public static async Task<bool> ExistsAsync(
        string connectionString, string table, string primaryKeyColumn, string keyValue,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKeyColumn);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM dbo.[{table}] WHERE [{primaryKeyColumn}] = @key";
        command.Parameters.AddWithValue("@key", keyValue.Trim());
        var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        return count > 0;
    }

    /// <summary>成功すればnull、失敗すれば理由を返します。1トランザクションで全件反映します。</summary>
    public static async Task<string?> ApplyChangesAsync(
        string connectionString, string tableName, string primaryKeyColumn,
        IReadOnlyList<CellChange> updates, IReadOnlyList<string> deletedPrimaryKeyValues,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKeyColumn);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(deletedPrimaryKeyValues);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var group in updates.GroupBy(change => change.PrimaryKeyValue))
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var setClauses = new List<string>();
                    var index = 0;
                    foreach (var change in group)
                    {
                        var parameterName = $"@p{index}";
                        setClauses.Add($"[{change.Column}] = {parameterName}");
                        command.Parameters.AddWithValue(
                            parameterName,
                            string.IsNullOrWhiteSpace(change.NewValue) ? DBNull.Value : change.NewValue.Trim());
                        index++;
                    }

                    command.CommandText =
                        $"UPDATE dbo.[{tableName}] SET {string.Join(", ", setClauses)} WHERE [{primaryKeyColumn}] = @key";
                    command.Parameters.AddWithValue("@key", group.Key.Trim());
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                foreach (var keyValue in deletedPrimaryKeyValues)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"DELETE FROM dbo.[{tableName}] WHERE [{primaryKeyColumn}] = @key";
                    command.Parameters.AddWithValue("@key", keyValue.Trim());
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

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
            return $"SQL Serverへの反映に失敗しました。{exception.Message}";
        }
    }
}
```

- [ ] **Step 2: `Bui.Curation.csproj`がビルドできることを確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe build src_ソース\Bui.Curation\Bui.Curation.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: ビルド成功、0警告0エラー

- [ ] **Step 3: 全体テストを実行し既存テストに影響がないことを確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件、Task 3終了時点と同じ件数）

- [ ] **Step 4: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/DbEditorSqlGateway_DB編集SQL接続.cs"
git commit -m "feat: DbEditorSqlGatewayを追加し、任意表のSQL Server読み書きを1トランザクションで行えるようにする"
```

---

### Task 5: MainWindow配線（DB内容更新タブ・確認ダイアログ・接続設定の自動復元）

Task 1〜4で作ったロジックを、実際のWPF画面へ配線する。設計スペックの方針どおり、この画面自体は目視確認とし単体テストは追加しない。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/ChangeConfirmationWindow.xaml`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/ChangeConfirmationWindow.xaml.cs`

**Interfaces:**
- Consumes:
  - `Bui.Curation.DbEditorTableRegistry` / `TableDefinition`（Task 1）
  - `Bui.Curation.CellChange` / `ChangeSummary` / `TableChangeSetBuilder` / `DeletionWarningBuilder`（Task 2）
  - `Bui.Curation.CurationUiSettings` / `CurationUiSettingsStore`（Task 3）
  - `Bui.Curation.DbEditorTableSnapshot` / `DbEditorSqlGateway`（Task 4）
  - `Bui.Configuration.SqlConnectionStringFactory.Build(string?, string?) : OperationResult<string>`（既存）
- Produces: なし（最終タスク）

- [ ] **Step 1: `MainWindow.xaml`を、既存内容をTabItemへ移し新タブを追加する形に書き換える**

`Grid.Row="1"`の内容（既存の`<Grid>`要素、仮登録レビュー用の一覧・詳細パネル）を`<TabControl>`の1つ目の`<TabItem Header="仮登録レビュー">`へそのまま移し、2つ目の`<TabItem Header="DB内容更新">`を新設する。上部の接続設定用`StackPanel`（サーバー名・データベース名・旧新ID CSVのパス）は`Grid.Row="0"`のまま変更しない（両タブ共通の設定として維持する）。

`Grid.Row="1"`を、既存の中身を保持したまま次の構造に置き換える:

```xml
        <TabControl Grid.Row="1">
            <TabItem Header="仮登録レビュー">
                <!-- ここに、書き換え前のGrid.Row="1"の中身（QueueGridとStackPanelを含むGrid要素）をそのまま置く -->
            </TabItem>
            <TabItem Header="DB内容更新">
                <Grid Margin="0,8,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8">
                        <TextBlock Text="表" VerticalAlignment="Center" Margin="0,0,4,0" />
                        <ComboBox x:Name="TableSelector" Width="200" SelectionChanged="OnTableSelected" />
                        <Button x:Name="SaveTableButton" Content="保存" Padding="8,4" Margin="16,0,0,0" Click="OnSaveTable" />
                    </StackPanel>

                    <Grid Grid.Row="1">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="3*" />
                            <ColumnDefinition Width="2*" />
                        </Grid.ColumnDefinitions>

                        <DataGrid x:Name="EditorGrid" Grid.Column="0" Margin="0,0,8,0"
                                  AutoGenerateColumns="True" CanUserSortColumns="False"
                                  AutoGeneratingColumn="OnEditorGridAutoGeneratingColumn"
                                  SelectionChanged="OnEditorRowSelected" />

                        <StackPanel Grid.Column="1">
                            <TextBlock Text="関連表（選択中の行）" FontWeight="Bold" />
                            <TextBlock Text="この行が参照している表" Margin="0,8,0,0" />
                            <TextBlock x:Name="ParentTablesText" TextWrapping="Wrap" Margin="0,0,0,8" />
                            <TextBlock Text="この行を参照している表" Margin="0,8,0,0" />
                            <TextBlock x:Name="ChildTablesText" TextWrapping="Wrap" Margin="0,0,0,8" />
                            <Button x:Name="DeleteRowButton" Content="選択行を削除対象にする" Padding="8,4"
                                    HorizontalAlignment="Left" Click="OnMarkRowForDeletion" />
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>
        </TabControl>
```

`Window`要素へ`Closing="OnWindowClosing"`属性を追加する。

- [ ] **Step 2: `ChangeConfirmationWindow.xaml`を作成する**

```xml
<Window x:Class="Bui.Curation.ChangeConfirmationWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="変更内容の確認" Height="500" Width="700" WindowStartupLocation="CenterOwner">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <ListBox x:Name="ChangeList" Grid.Row="0" />

        <StackPanel Orientation="Horizontal" Grid.Row="1" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button x:Name="CancelButton" Content="キャンセル" Padding="12,4" Margin="0,0,8,0" Click="OnCancel" />
            <Button x:Name="ConfirmButton" Content="実行" Padding="12,4" Click="OnConfirm" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: `ChangeConfirmationWindow.xaml.cs`を作成する**

```csharp
// このファイルの目的:
//   DB内容更新画面で「保存」を押した際、実際にSQL Serverへ反映する前に
//   変更一覧（旧値→新値、削除予定行とその警告）を表示し、操作者に
//   最終確認させます。ここでの「実行」でDialogResult=trueを返すだけで、
//   実際のSQL実行はMainWindow側（DbEditorSqlGateway呼び出し）が行います。

using System.Windows;

namespace Bui.Curation;

public partial class ChangeConfirmationWindow : Window
{
    public ChangeConfirmationWindow(ChangeSummary summary, IReadOnlyDictionary<string, string?> deletionWarnings)
    {
        InitializeComponent();

        foreach (var update in summary.Updates)
        {
            ChangeList.Items.Add($"[更新] {update.PrimaryKeyValue} / {update.Column}: 「{update.OldValue}」→「{update.NewValue}」");
        }

        foreach (var key in summary.DeletedPrimaryKeyValues)
        {
            var warning = deletionWarnings.GetValueOrDefault(key);
            ChangeList.Items.Add(warning is null
                ? $"[削除] {key}"
                : $"[削除] {key} ⚠ {warning}");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
```

- [ ] **Step 4: `MainWindow.xaml.cs`へDB内容更新タブの配線を追加する**

既存のフィールド宣言のそばに以下を追加する:

```csharp
    private readonly string _uiSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartsCAM", "BUI", "Curation", "ui_settings.json");
    private DbEditorTableSnapshot? _editorOriginalSnapshot;
    private List<Dictionary<string, string>> _editorCurrentRows = new();
    private readonly HashSet<string> _editorDeletedKeys = new(StringComparer.Ordinal);
    private string? _editorSelectedPrimaryKeyValue;
```

コンストラクタで、`_items = CurationQueueStore.Load(_queuePath);`の前後に接続設定の復元処理を追加する:

```csharp
        var uiSettings = CurationUiSettingsStore.Load(_uiSettingsPath);
        SqlServerNameBox.Text = string.IsNullOrEmpty(uiSettings.SqlServerName) ? SqlServerNameBox.Text : uiSettings.SqlServerName;
        SqlDatabaseNameBox.Text = string.IsNullOrEmpty(uiSettings.SqlDatabaseName) ? SqlDatabaseNameBox.Text : uiSettings.SqlDatabaseName;
        IdAliasCsvPathBox.Text = uiSettings.IdAliasCsvPath;

        TableSelector.ItemsSource = DbEditorTableRegistry.Tables.Select(table => table.TableName).ToList();
```

ウィンドウを閉じるときに設定を保存するハンドラを追加する:

```csharp
    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            CurationUiSettingsStore.Save(_uiSettingsPath, new CurationUiSettings
            {
                SqlServerName = SqlServerNameBox.Text,
                SqlDatabaseName = SqlDatabaseNameBox.Text,
                IdAliasCsvPath = IdAliasCsvPathBox.Text,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 設定の保存に失敗しても、ウィンドウを閉じる操作自体は妨げない。
        }
    }
```

表選択時の読込処理を追加する:

```csharp
    private async void OnTableSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        await LoadSelectedTableAsync();
    }

    /// <summary>TableSelectorで選ばれている表を読み込み直し、グリッドへ反映します。
    /// 表を切り替えた時だけでなく、保存が成功した後の再読込にも使います。</summary>
    private async Task LoadSelectedTableAsync()
    {
        if (TableSelector.SelectedItem is not string tableName) return;

        var connectionResult = Bui.Configuration.SqlConnectionStringFactory.Build(
            SqlServerNameBox.Text, SqlDatabaseNameBox.Text);
        if (!connectionResult.IsSuccess)
        {
            ValidationText.Text = connectionResult.Error!.UserMessage;
            return;
        }

        try
        {
            var snapshot = await DbEditorSqlGateway.LoadTableAsync(connectionResult.Value!, tableName, default);
            _editorOriginalSnapshot = snapshot;
            _editorCurrentRows = snapshot.Rows.Select(row => new Dictionary<string, string>(row)).ToList();
            _editorDeletedKeys.Clear();
            _editorSelectedPrimaryKeyValue = null;
            ParentTablesText.Text = string.Empty;
            ChildTablesText.Text = string.Empty;

            EditorGrid.ItemsSource = null;
            EditorGrid.ItemsSource = _editorCurrentRows;
        }
        catch (Exception exception)
        {
            ValidationText.Text = $"表の読込に失敗しました。{exception.Message}";
        }
    }

    private void OnEditorGridAutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
    {
        if (TableSelector.SelectedItem is not string tableName) return;

        var definition = DbEditorTableRegistry.Get(tableName);
        if (string.Equals(e.PropertyName, definition.PrimaryKeyColumn, StringComparison.Ordinal)
            && e.Column is System.Windows.Controls.DataGridTextColumn textColumn)
        {
            textColumn.IsReadOnly = true;
        }
    }
```

行選択時に関連表パネルを更新する処理を追加する:

```csharp
    private async void OnEditorRowSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TableSelector.SelectedItem is not string tableName) return;
        if (EditorGrid.SelectedItem is not Dictionary<string, string> row) return;

        var definition = DbEditorTableRegistry.Get(tableName);
        var keyValue = row.GetValueOrDefault(definition.PrimaryKeyColumn, string.Empty);
        _editorSelectedPrimaryKeyValue = keyValue;
        if (string.IsNullOrWhiteSpace(keyValue)) return;

        var connectionResult = Bui.Configuration.SqlConnectionStringFactory.Build(
            SqlServerNameBox.Text, SqlDatabaseNameBox.Text);
        if (!connectionResult.IsSuccess)
        {
            ValidationText.Text = connectionResult.Error!.UserMessage;
            return;
        }

        try
        {
            var parentLines = new List<string>();
            foreach (var foreignKey in definition.ForeignKeys)
            {
                var referencedValue = row.GetValueOrDefault(foreignKey.Column, string.Empty);
                if (string.IsNullOrWhiteSpace(referencedValue)) continue;
                var targetDefinition = DbEditorTableRegistry.Get(foreignKey.TargetTable);
                var exists = await DbEditorSqlGateway.ExistsAsync(
                    connectionResult.Value!, foreignKey.TargetTable, targetDefinition.PrimaryKeyColumn, referencedValue, default);
                parentLines.Add(exists
                    ? $"{foreignKey.TargetTable}: 「{referencedValue}」あり"
                    : $"{foreignKey.TargetTable}: 「{referencedValue}」が見つかりません");
            }
            ParentTablesText.Text = parentLines.Count > 0 ? string.Join(Environment.NewLine, parentLines) : "（参照している表はありません）";

            var childLines = new List<string>();
            foreach (var (childTable, foreignKeyColumn) in DbEditorTableRegistry.GetChildren(tableName))
            {
                var count = await DbEditorSqlGateway.CountReferencingRowsAsync(
                    connectionResult.Value!, childTable, foreignKeyColumn, keyValue, default);
                childLines.Add($"{childTable}: {count}件");
            }
            ChildTablesText.Text = childLines.Count > 0 ? string.Join(Environment.NewLine, childLines) : "（参照されている表はありません）";
        }
        catch (Exception exception)
        {
            ParentTablesText.Text = $"関連表の確認に失敗しました。{exception.Message}";
            ChildTablesText.Text = string.Empty;
        }
    }

    private void OnMarkRowForDeletion(object sender, RoutedEventArgs e)
    {
        if (_editorSelectedPrimaryKeyValue is null) return;
        _editorDeletedKeys.Add(_editorSelectedPrimaryKeyValue);
        ValidationText.Text = $"「{_editorSelectedPrimaryKeyValue}」を削除対象にしました。「保存」を押すと確認画面が出ます。";
    }
```

保存処理（確認ダイアログ表示→実行）を追加する:

```csharp
    private async void OnSaveTable(object sender, RoutedEventArgs e)
    {
        if (TableSelector.SelectedItem is not string tableName || _editorOriginalSnapshot is null) return;

        var definition = DbEditorTableRegistry.Get(tableName);
        var summary = TableChangeSetBuilder.Build(
            _editorOriginalSnapshot.Rows.Select(row => (IReadOnlyDictionary<string, string>)row).ToList(),
            _editorCurrentRows.Select(row => (IReadOnlyDictionary<string, string>)row).ToList(),
            _editorDeletedKeys,
            definition.PrimaryKeyColumn);

        if (summary.Updates.Count == 0 && summary.DeletedPrimaryKeyValues.Count == 0)
        {
            ValidationText.Text = "変更がありません。";
            return;
        }

        var connectionResult = Bui.Configuration.SqlConnectionStringFactory.Build(
            SqlServerNameBox.Text, SqlDatabaseNameBox.Text);
        if (!connectionResult.IsSuccess)
        {
            ValidationText.Text = connectionResult.Error!.UserMessage;
            return;
        }

        var deletionWarnings = new Dictionary<string, string?>();
        foreach (var keyValue in summary.DeletedPrimaryKeyValues)
        {
            var childCounts = new List<(string ChildTable, int Count)>();
            foreach (var (childTable, foreignKeyColumn) in DbEditorTableRegistry.GetChildren(tableName))
            {
                var count = await DbEditorSqlGateway.CountReferencingRowsAsync(
                    connectionResult.Value!, childTable, foreignKeyColumn, keyValue, default);
                childCounts.Add((childTable, count));
            }
            deletionWarnings[keyValue] = DeletionWarningBuilder.BuildWarning(keyValue, childCounts);
        }

        var confirmationWindow = new ChangeConfirmationWindow(summary, deletionWarnings) { Owner = this };
        if (confirmationWindow.ShowDialog() != true) return;

        var applyError = await DbEditorSqlGateway.ApplyChangesAsync(
            connectionResult.Value!, tableName, definition.PrimaryKeyColumn,
            summary.Updates, summary.DeletedPrimaryKeyValues, default);

        if (applyError is not null)
        {
            ValidationText.Text = applyError;
            return;
        }

        ValidationText.Text = "保存しました。";
        await LoadSelectedTableAsync();
    }
```

- [ ] **Step 5: ビルドする**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe build src_ソース\Bui.Curation\Bui.Curation.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: ビルド成功、0警告0エラー

- [ ] **Step 6: 全体テストを実行する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件、Task 4終了時点と同じ件数。UIは目視確認のみのためテスト件数の増加はない）

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/ChangeConfirmationWindow.xaml" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/ChangeConfirmationWindow.xaml.cs"
git commit -m "feat: DB内容更新タブを新設し、表選択・編集・削除・確認画面・SQL反映・接続設定の自動復元を配線する"
```

---

### Task 6: 親フォルダからの一括取込

複数プロジェクトが同時進行している場合に、1フォルダずつではなく「親フォルダを1つ指定→直下の各サブフォルダを自動的に全部スキャン」できるようにする。既存の「フォルダを追加して取込」（1フォルダ単位）は変更せず、並存する形で追加する。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/ProvisionalRecordScanner_仮登録取込.cs`
- Modify: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/ProvisionalRecordScannerTests_仮登録取込テスト.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml.cs`

**Interfaces:**
- Consumes:
  - `Bui.Curation.ProvisionalRecordScanner.Scan(string folderPath, string provisionalIdPrefix) : ProvisionalScanResult`（既存、変更しない）
  - `Bui.Curation.CurationQueueStore.AddIfMissing(List<CurationQueueItem> items, IReadOnlyList<ScannedProvisionalRecord> scanned) : int`（既存）
- Produces:
  - `Bui.Curation.ChildFolderScanResult(string SourceFolder, ProvisionalScanResult Result) : record`
  - `Bui.Curation.ProvisionalRecordScanner.ScanChildFolders(string parentFolderPath, string provisionalIdPrefix) : IReadOnlyList<ChildFolderScanResult>`（`parentFolderPath`の直下にある各サブフォルダへ`Scan`を実行し、フォルダごとの結果をまとめて返す。サブフォルダが無ければ空の一覧を返す）

- [ ] **Step 1: 失敗するテストを書く**

`ProvisionalRecordScannerTests_仮登録取込テスト.cs`の末尾（`ProvisionalRecordScannerTests_仮登録取込テスト`クラスの最後の`}`の直前、`internal sealed class GoldStandardFolderTestFiles`より前）に以下のテストメソッドを追加する。

既存の`GoldStandardFolderTestFiles.Create(...)`は、呼び出しのたびに`Path.GetTempPath()`配下へ自分でランダムなフォルダを作る方式（親フォルダを指定できない）ため、`ScanChildFolders`のテストでは使わず、以下のとおり直接ファイルを書き出す:

```csharp
    [TestMethod]
    public void ScanChildFolders_直下の各サブフォルダをそれぞれスキャンする()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"bui_parent_{Guid.NewGuid():N}");
        var childA = Path.Combine(parent, "現場A");
        var childB = Path.Combine(parent, "現場B");
        Directory.CreateDirectory(childA);
        Directory.CreateDirectory(childB);
        var encoding = new System.Text.UTF8Encoding(false, true);
        try
        {
            File.WriteAllText(Path.Combine(childA, "Products.csv"),
                "product_id,product_name,manufacturer_id,material_category_id\r\n"
                    + "BM_NEW-0001,仮製品A,,\r\n", encoding);
            File.WriteAllText(Path.Combine(childA, "Product_Specs.csv"),
                "product_id,spec_key,value_type,value_number,value_string\r\n", encoding);

            File.WriteAllText(Path.Combine(childB, "Products.csv"),
                "product_id,product_name,manufacturer_id,material_category_id\r\n"
                    + "BM_NEW-0002,仮製品B,,\r\n", encoding);
            File.WriteAllText(Path.Combine(childB, "Product_Specs.csv"),
                "product_id,spec_key,value_type,value_number,value_string\r\n", encoding);

            var results = ProvisionalRecordScanner.ScanChildFolders(parent, "BM_NEW-");

            Assert.AreEqual(2, results.Count);
            var resultA = results.Single(r => Path.GetFileName(r.SourceFolder) == "現場A");
            Assert.AreEqual(1, resultA.Result.Records.Count);
            Assert.AreEqual("BM_NEW-0001", resultA.Result.Records[0].ProvisionalId);

            var resultB = results.Single(r => Path.GetFileName(r.SourceFolder) == "現場B");
            Assert.AreEqual(1, resultB.Result.Records.Count);
            Assert.AreEqual("BM_NEW-0002", resultB.Result.Records[0].ProvisionalId);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public void ScanChildFolders_Products_csvが無いサブフォルダはProblemsに反映される()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"bui_parent_{Guid.NewGuid():N}");
        var emptyChild = Path.Combine(parent, "空フォルダ");
        Directory.CreateDirectory(emptyChild);
        try
        {
            var results = ProvisionalRecordScanner.ScanChildFolders(parent, "BM_NEW-");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(0, results[0].Result.Records.Count);
            Assert.IsTrue(results[0].Result.Problems.Count > 0);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public void ScanChildFolders_サブフォルダが無ければ空の一覧を返す()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"bui_parent_{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            var results = ProvisionalRecordScanner.ScanChildFolders(parent, "BM_NEW-");
            Assert.AreEqual(0, results.Count);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~ScanChildFolders` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: FAIL（`ScanChildFolders`が存在しない、ビルドエラー）

- [ ] **Step 3: `ProvisionalRecordScanner_仮登録取込.cs`へ`ScanChildFolders`を追加する**

既存の`ScanResult`にあたる`ProvisionalScanResult`を返す既存の`Scan`メソッドのすぐ下に、以下を追加する:

```csharp
/// <summary>親フォルダ1つと、その直下の1サブフォルダぶんのScan結果です。</summary>
public sealed record ChildFolderScanResult(string SourceFolder, ProvisionalScanResult Result);
```

（この`record`は`ScannedProvisionalRecord`と同じファイル内、`ProvisionalRecordScanner`クラスの外側に置く）

`ProvisionalRecordScanner`クラス内、`Scan`メソッドの下に以下を追加する:

```csharp
    /// <summary>parentFolderPath直下の各サブフォルダへ、それぞれScanを実行します。
    /// サブフォルダが無ければ空の一覧を返します（例外は投げません）。</summary>
    public static IReadOnlyList<ChildFolderScanResult> ScanChildFolders(string parentFolderPath, string provisionalIdPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionalIdPrefix);

        if (!Directory.Exists(parentFolderPath))
        {
            return Array.Empty<ChildFolderScanResult>();
        }

        var results = new List<ChildFolderScanResult>();
        foreach (var childFolder in Directory.GetDirectories(parentFolderPath))
        {
            var result = Scan(childFolder, provisionalIdPrefix);
            results.Add(new ChildFolderScanResult(childFolder, result));
        }

        return results;
    }
```

- [ ] **Step 4: テストを実行して成功を確認する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj --filter FullyQualifiedName~ScanChildFolders` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（3件すべて）

- [ ] **Step 5: 全体テストを実行する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件）

- [ ] **Step 6: `MainWindow.xaml`へ一括取込ボタンを追加する**

`MainWindow.xaml`の上部`StackPanel`（`AddFolderButton`のすぐ右）に以下を追加する:

```xml
            <Button x:Name="AddParentFolderButton" Content="親フォルダから一括取込" Padding="8,4" Margin="8,0,0,0" Click="OnAddParentFolder" />
```

- [ ] **Step 7: `MainWindow.xaml.cs`へ一括取込ハンドラを追加する**

既存の`OnAddFolder`メソッドの下に以下を追加する:

```csharp
    private void OnAddParentFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "複数プロジェクトが入っている親フォルダを選択してください" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var childResults = ProvisionalRecordScanner.ScanChildFolders(dialog.FolderName, "BM_NEW-");
            var totalAdded = 0;
            var problemLines = new List<string>();

            foreach (var childResult in childResults)
            {
                var added = CurationQueueStore.AddIfMissing(_items, childResult.Result.Records);
                totalAdded += added;
                if (childResult.Result.Problems.Count > 0)
                {
                    problemLines.Add($"{Path.GetFileName(childResult.SourceFolder)}: {string.Join(" / ", childResult.Result.Problems)}");
                }
            }

            CurationQueueStore.Save(_queuePath, _items);
            RefreshGrid();

            var message = $"{childResults.Count} 個のフォルダを確認し、{totalAdded} 件を新しく取り込みました。";
            if (problemLines.Count > 0)
            {
                message += Environment.NewLine + $"（{problemLines.Count}件の問題がありました: {string.Join(" / ", problemLines)}）";
            }
            MessageBox.Show(message, "一括取込完了");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"取込に失敗しました。{exception.Message}", "エラー");
        }
    }
```

- [ ] **Step 8: ビルドする**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe build src_ソース\Bui.Curation\Bui.Curation.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: ビルド成功、0警告0エラー

- [ ] **Step 9: 全体テストを実行する**

Run: `..\.superpowers\dotnet-sdk-8\dotnet.exe test tests_テスト\Bui.Tests\Bui.Tests.csproj` (from `BUI_Addin_CSharpアドイン`ディレクトリ)
Expected: PASS（全件）

- [ ] **Step 10: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Curation_仮登録レビュー/ProvisionalRecordScanner_仮登録取込.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Curation_仮登録レビュー/ProvisionalRecordScannerTests_仮登録取込テスト.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Curation/MainWindow.xaml.cs"
git commit -m "feat: ProvisionalRecordScanner.ScanChildFoldersを追加し、親フォルダ指定で複数プロジェクトを一括取込できるようにする"
```
