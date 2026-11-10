# Product_IDを使った仕上表出力 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Revitモデルの部屋名と層別Product_IDから、内部仕上表と外部仕上表を同一Excelへ安全に出力するリボン機能を実装する。

**Architecture:** Revit依存の収集処理は要素を不変スナップショットへ変換し、部屋名正規化、層分類、一般名称解決、集約は`Bui.Shared_共有ロジック`の純粋ロジックとして実装する。Excelは同梱雛型をOpen XML SDKでコピー編集し、パネルと同じ`IProductRepository`をセッション共有してCSV／SQL Server切替をそのまま反映する。

**Tech Stack:** C# 12、.NET 8、Revit 2025 API、WPF、MSTest 3.1.1、DocumentFormat.OpenXml 3.5.1

**Spec:** `docs/superpowers/specs/2026-09-24-finish-schedule-export-design.md`

## Global Constraints

- Revitモデルは読取専用とし、出力コマンドでトランザクションを開始しない。
- `st_部屋名（内側）`、`st_部屋名（外側）`、`st_天井・床部屋要素ID`は完全一致名で読む。
- 層別Product_IDは既存の6固定GUIDと順序を変更しない。
- Product_ID空欄は正常な対象外として無視し、警告・赤色表示・備考へ含めない。
- 一般名称は現在選択中のCSVまたはSQL Serverの`general_name`だけから取得し、製品名・建材分類へフォールバックしない。
- 未解決の非空Product_IDがあってもExcelを出力し、影響セルを`#F4CCCC`で塗る。
- `仕上表_雛型.xlsx`をコピー編集し、原本は変更しない。
- Excel COMは使用しない。
- 出力は一時ファイルへ作り、検証成功後だけ最終パスへ移動する。
- F列の巾木とI列の廻縁は空欄のままにする。
- Revitを終了してからDebugビルドする。開いたままだと既存の配置同期ターゲットがDLLロックで失敗する。

## Review Focus

- 正規化キーが同じで原文が異なる部屋名は、部屋集計表の原文1件を表示し、1行へ統合されること（Task 1で固定）。
- 外側部屋名が空の壁では、反対端が`Finish2`でも最大番号側Product_IDを内側部屋の下地として保持すること（Task 2で固定）。
- 解決済みと未解決Product_IDが同じセルに混在しても、解決済み名称を失わず赤色と備考を付けること（Task 4で固定）。
- 101部屋以上でも行書式、罫線、折返しを継承し、元雛型を変更しないこと（Task 5で固定）。
- 出力サービス作成後にCSV／SQL相当のデータソース切替が起きても、キャッシュせず現在のリポジトリ内容を読むこと（Task 3で固定）。

---

## File Structure

### 新規の共有ロジック

- `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleModels_仕上表モデル.cs`: Revit非依存の入力、配置、解決結果、出力行、警告を定義する。
- `.../FinishSchedules_仕上表/RoomNameNormalizer_部屋名正規化.cs`: 表示名を維持しながら照合キーを生成する。
- `.../FinishSchedules_仕上表/FinishLayerClassifier_仕上層分類.cs`: 壁・床・天井・屋根の層を内部／外部の仕上・下地へ分類する。
- `.../FinishSchedules_仕上表/GeneralNameResolver_一般名称解決.cs`: 現在の`IProductRepository`から一般名称を取得する。
- `.../FinishSchedules_仕上表/FinishScheduleAggregator_仕上表集約.cs`: 配置を部屋行と外部タイプ行へ集約する。
- `.../FinishSchedules_仕上表/FinishScheduleWorkbookWriter_仕上表Excel出力.cs`: Open XMLで雛型を編集する。

### 新規のRevit／UIアダプター

- `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/FinishSchedules/RevitFinishSourceCollector_仕上表元データ取得.cs`: 部屋集計表と対象要素をスナップショット化する。
- `.../Revit_連携/FinishSchedules/ExteriorSideResolver_外側判定.cs`: カテゴリ別の外部端を決定する。
- `.../Revit_連携/FinishSchedules/ExportFinishScheduleCommand_仕上表出力.cs`: リボンコマンドを実装する。
- `.../UI_画面/FinishSchedules/FinishScheduleExportDialog_仕上表出力確認.cs`: 事前確認と保存先選択を扱う。
- `.../Application_アプリ起動/FinishScheduleComposition_仕上表構成.cs`: 共有リポジトリ、収集、解決、出力を組み立てる。

### 変更する既存ファイル

- `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/BuiSession_起動時共有.cs`: `IProductRepository`をリボンコマンドへ共有する。
- `.../Application_アプリ起動/BuiApplication_アドイン起動.cs`: セッションへ共有リポジトリを登録する。
- `.../Application_アプリ起動/RibbonService_リボン作成.cs`: 「仕上表出力」ボタンを追加する。
- `.../UI_画面/BuiDockPaneComposition_画面構成.cs`: ViewModelと同じ製品リポジトリを構成結果として返す。
- `.../Revit_連携/BuiPaneProvider_パネル登録.cs`: 構成結果の製品リポジトリを公開する。
- `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`: Open XML依存、雛型、リボン画像を配置する。
- `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj`: Open XML依存とテスト用雛型を追加する。

### 新規テスト

- `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表/RoomNameNormalizerTests_部屋名正規化テスト.cs`
- `.../FinishSchedules_仕上表/FinishLayerClassifierTests_仕上層分類テスト.cs`
- `.../FinishSchedules_仕上表/GeneralNameResolverTests_一般名称解決テスト.cs`
- `.../FinishSchedules_仕上表/FinishScheduleAggregatorTests_仕上表集約テスト.cs`
- `.../FinishSchedules_仕上表/FinishScheduleWorkbookWriterTests_仕上表Excel出力テスト.cs`

---

### Task 1: 仕上表モデルと部屋名正規化

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleModels_仕上表モデル.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/RoomNameNormalizer_部屋名正規化.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表/RoomNameNormalizerTests_部屋名正規化テスト.cs`

**Interfaces:**
- Consumes: なし。
- Produces: `NormalizedRoomName RoomNameNormalizer.Normalize(string value)`、`FinishHostKind`、`FinishColumn`、`FinishLayerInput`、`HostFinishInput`、`ProductPlacement`、`FinishScheduleWarning`。

- [ ] **Step 1: 正規化の失敗テストを書く**

```csharp
using Bui.FinishSchedules;

namespace Bui.Tests.FinishSchedules;

[TestClass]
public class RoomNameNormalizerTests_部屋名正規化テスト
{
    [DataTestMethod]
    [DataRow(" 会議室１ ", "会議室1")]
    [DataRow("会議室　　A", "会議室 A")]
    [DataRow("ROOM-a", "ROOM-A")]
    public void 全角英数字と空白と英字大小文字を照合用に統一する(string source, string expectedKey)
    {
        var result = RoomNameNormalizer.Normalize(source);
        Assert.AreEqual(source.Trim(), result.DisplayName);
        Assert.AreEqual(expectedKey, result.Key);
    }

    [TestMethod]
    public void 部屋番号は削除しない()
    {
        Assert.AreNotEqual(
            RoomNameNormalizer.Normalize("会議室1").Key,
            RoomNameNormalizer.Normalize("会議室2").Key);
    }

    [TestMethod]
    public void 同じ正規化キーの原文は最初の部屋集計表名を代表表示にできる()
    {
        var names = new[] { "会議室１", "会議室1" }
            .Select(RoomNameNormalizer.Normalize)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Single();
        Assert.AreEqual("会議室１", names.First().DisplayName);
    }
}
```

- [ ] **Step 2: テストを実行し、型がないため失敗することを確認する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~RoomNameNormalizerTests"`

Expected: `RoomNameNormalizer`または`NormalizedRoomName`が存在しないコンパイルエラー。

- [ ] **Step 3: 最小モデルと限定的な正規化を実装する**

```csharp
namespace Bui.FinishSchedules;

public sealed record NormalizedRoomName(string DisplayName, string Key);

public enum FinishHostKind { Wall, Floor, Ceiling, Roof }
public enum FinishColumn
{
    FloorFinish, FloorSubstrate, WallFinish, WallSubstrate,
    CeilingFinish, CeilingSubstrate, ExteriorFinish, ExteriorSubstrate
}

public sealed record FinishLayerInput(
    int Order, string Function, int DepthFromInterior, string ProductId);

public sealed record HostFinishInput(
    long ElementId, FinishHostKind Kind, string Category, string FamilyName, string TypeName,
    string InnerRoomName, string OuterRoomName, string HorizontalRoomName,
    IReadOnlyList<FinishLayerInput> Layers, string LayerWarning);

public sealed record ProductPlacement(
    string RoomName, string RoomKey, FinishColumn Column, string ProductId,
    int Depth, long ElementId, string Category, string FamilyName, string TypeName);

public sealed record FinishScheduleWarning(string Code, string Message, long? ElementId = null);
```

`RoomNameNormalizer.Normalize`は、`Trim`、全角スペース変換、`Regex.Replace(value, @"\s+", " ")`、Unicode範囲`FF10-FF19`、`FF21-FF3A`、`FF41-FF5A`のASCII変換、`ToUpperInvariant`だけを行う。互換正規化で他の記号を変換しない。

- [ ] **Step 4: 正規化テストを合格させる**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~RoomNameNormalizerTests"`

Expected: 全テストPASS。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表 BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表
git commit -m "feat: 仕上表の部屋名正規化モデルを追加"
```

---

### Task 2: 壁・床・天井・外部の層分類

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishLayerClassifier_仕上層分類.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleModels_仕上表モデル.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表/FinishLayerClassifierTests_仕上層分類テスト.cs`

**Interfaces:**
- Consumes: `HostFinishInput`、`FinishLayerInput`、`RoomNameNormalizer.Normalize`。
- Produces: `HostClassification FinishLayerClassifier.Classify(HostFinishInput input)`。

- [ ] **Step 1: 内外両面、外側空欄、横要素、外部候補の失敗テストを書く**

```csharp
[TestMethod]
public void 外側部屋がある壁は反対端Finish2を外側仕上にする()
{
    var input = Wall(inner: "101", outer: "102", new[]
    {
        Layer(0, "Finish2", 5, "OUT-FIN"),
        Layer(1, "Structure", 4, "CORE"),
        Layer(5, "Finish1", 0, "IN-FIN")
    });
    var result = FinishLayerClassifier.Classify(input);
    Assert.IsTrue(result.InternalPlacements.Any(x => x.RoomKey == "101" && x.Column == FinishColumn.WallFinish && x.ProductId == "IN-FIN"));
    Assert.IsTrue(result.InternalPlacements.Any(x => x.RoomKey == "102" && x.Column == FinishColumn.WallFinish && x.ProductId == "OUT-FIN"));
}

[TestMethod]
public void 外側部屋が空なら反対端Finish2も内側の下地として残す()
{
    var result = FinishLayerClassifier.Classify(Wall("101", "", new[]
    {
        Layer(0, "Finish2", 5, "OUT-FIN"),
        Layer(5, "Finish1", 0, "IN-FIN")
    }));
    Assert.IsTrue(result.InternalPlacements.Any(x => x.Column == FinishColumn.WallSubstrate && x.ProductId == "OUT-FIN"));
    Assert.IsFalse(result.InternalPlacements.Any(x => x.ProductId == "OUT-FIN" && x.Column == FinishColumn.WallFinish));
}

[TestMethod]
public void 空ProductIdは配置にも警告にも含めない()
{
    var result = FinishLayerClassifier.Classify(Floor("101", new[] { Layer(0, "Finish1", 0, ""), Layer(1, "Structure", 1, "") }));
    Assert.AreEqual(0, result.InternalPlacements.Count);
    Assert.AreEqual(0, result.Warnings.Count);
}
```

同じファイルに、床は`FloorFinish/FloorSubstrate`、天井は`CeilingFinish/CeilingSubstrate`、部屋名がない要素は部屋名・部屋キーが空で`ExteriorFinish/ExteriorSubstrate`の`ExternalPlacements`を返すテスト、7層警告を転送するテストも記述する。

- [ ] **Step 2: 分類テストが未実装で失敗することを確認する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~FinishLayerClassifierTests"`

Expected: `FinishLayerClassifier`と`HostClassification`が存在しないためFAIL。

- [ ] **Step 3: 端部機能を確認する分類器を実装する**

```csharp
public sealed record HostClassification(
    IReadOnlyList<ProductPlacement> InternalPlacements,
    IReadOnlyList<ProductPlacement> ExternalPlacements,
    IReadOnlyList<FinishScheduleWarning> Warnings);

public static class FinishLayerClassifier
{
    public static HostClassification Classify(HostFinishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Kind switch
        {
            FinishHostKind.Wall => ClassifyWall(input),
            FinishHostKind.Floor => ClassifyHorizontal(input, FinishColumn.FloorFinish, FinishColumn.FloorSubstrate),
            FinishHostKind.Ceiling => ClassifyHorizontal(input, FinishColumn.CeilingFinish, FinishColumn.CeilingSubstrate),
            FinishHostKind.Roof => ExternalOnly(input),
            _ => throw new ArgumentOutOfRangeException(nameof(input.Kind))
        };
    }

    private static bool IsFinish(FinishLayerInput layer) =>
        layer.Function is "Finish1" or "Finish2";
}
```

壁は`DepthFromInterior == 0`を内側端、最大深度を反対端として扱う。外側部屋名が非空かつ反対端が仕上機能の場合だけ外側仕上へ昇格する。外側部屋名が空なら反対端を含む残りの非空IDを内側下地へ残す。ただし内外両方の部屋名が空なら、最大深度の仕上機能を`ExteriorFinish`、残りを`ExteriorSubstrate`として外部配置へ変換する。横要素は`HorizontalRoomName`が空ならカテゴリ別外部端を仕上、残りを下地とする外部配置を返し、空でなければ深度0を仕上、残りを下地にする。屋根は常に外部配置を返す。外部配置にも要素ID、カテゴリ、ファミリ、タイプを保持し、Task 3・4へ同じ`ProductPlacement`型で渡す。

- [ ] **Step 4: 分類テストを合格させ、既存層テストも実行する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~FinishLayerClassifierTests|FullyQualifiedName~LayerSlotMapperTests"`

Expected: 全テストPASS。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表 BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表
git commit -m "feat: 仕上表の層分類規則を追加"
```

---

### Task 3: 現在の製品データソースから一般名称を解決

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/GeneralNameResolver_一般名称解決.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleModels_仕上表モデル.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表/GeneralNameResolverTests_一般名称解決テスト.cs`

**Interfaces:**
- Consumes: `IProductRepository.GetByIdAsync`、`ProductPlacement`。
- Produces: `Task<IReadOnlyList<ResolvedProductPlacement>> ResolveAsync(IEnumerable<ProductPlacement>, CancellationToken)`。

- [ ] **Step 1: 解決状態とデータソース切替の失敗テストを書く**

```csharp
[TestMethod]
public async Task 製品あり一般名称なしと製品なしを区別する()
{
    var repository = new FakeProductRepository(new Dictionary<string, ProductRecord>
    {
        ["P-1"] = Product("P-1", new Dictionary<string, string> { ["一般名称"] = "せっこうボード" }),
        ["P-2"] = Product("P-2", new Dictionary<string, string>())
    });
    var result = await new GeneralNameResolver(repository).ResolveAsync(
        new[] { Placement("P-1"), Placement("P-2"), Placement("P-404") }, CancellationToken.None);
    CollectionAssert.AreEqual(
        new[] { ProductResolution.Resolved, ProductResolution.GeneralNameMissing, ProductResolution.ProductMissing },
        result.Select(x => x.Resolution).ToArray());
}

[TestMethod]
public async Task 解決器作成後のDataSourceSwitch切替を次回照会へ反映する()
{
    using var first = new CsvFolderWithProduct("P-1", "旧一般名称");
    using var second = new CsvFolderWithProduct("P-1", "新一般名称");
    var sourceSwitch = DataSourceSwitch.ForCsvFolder(first.Path);
    var resolver = new GeneralNameResolver(new GoldStandardProductRepository(sourceSwitch));
    Assert.AreEqual("旧一般名称", (await resolver.ResolveAsync(new[] { Placement("P-1") }, CancellationToken.None))[0].GeneralName);
    TestSwitch.SwitchTo(sourceSwitch, new CsvTableSource(second.Path));
    Assert.AreEqual("新一般名称", (await resolver.ResolveAsync(new[] { Placement("P-1") }, CancellationToken.None))[0].GeneralName);
}
```

テストプロジェクトでは対象ソースが同じテストアセンブリへコンパイルされるため、既存`DataSourceSwitchTests`の`TestSwitch.SwitchTo(DataSourceSwitch, IGoldStandardTableSource)`を再利用し、本番公開APIを増やさない。空Product_IDは呼出側で除外済みだが、解決器でも空文字を受けた場合は結果へ追加しないテストを入れる。

- [ ] **Step 2: 解決器がないため失敗することを確認する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~GeneralNameResolverTests"`

Expected: `GeneralNameResolver`、`ResolvedProductPlacement`、`ProductResolution`がないためFAIL。

- [ ] **Step 3: ID単位で1回だけ照会する解決器を実装する**

```csharp
public enum ProductResolution { Resolved, ProductMissing, GeneralNameMissing }

public sealed record ResolvedProductPlacement(
    ProductPlacement Placement, string GeneralName, ProductResolution Resolution);

public sealed class GeneralNameResolver
{
    private readonly IProductRepository _products;
    public GeneralNameResolver(IProductRepository products) =>
        _products = products ?? throw new ArgumentNullException(nameof(products));

    public async Task<IReadOnlyList<ResolvedProductPlacement>> ResolveAsync(
        IEnumerable<ProductPlacement> placements, CancellationToken cancellationToken)
    {
        var source = placements.Where(x => !string.IsNullOrWhiteSpace(x.ProductId)).ToArray();
        var byId = new Dictionary<string, (string Name, ProductResolution State)>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in source.Select(x => x.ProductId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var product = await _products.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (product is null) byId[id] = (string.Empty, ProductResolution.ProductMissing);
            else if (!product.AdditionalAttributes.TryGetValue("一般名称", out var name) || string.IsNullOrWhiteSpace(name))
                byId[id] = (string.Empty, ProductResolution.GeneralNameMissing);
            else byId[id] = (name.Trim(), ProductResolution.Resolved);
        }
        return source.Select(x => new ResolvedProductPlacement(x, byId[x.ProductId.Trim()].Name, byId[x.ProductId.Trim()].State)).ToArray();
    }
}
```

- [ ] **Step 4: 解決器と既存CSV／SQLテーブルソースのテストを実行する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~GeneralNameResolverTests|FullyQualifiedName~GoldStandardProductRepositoryTests|FullyQualifiedName~DataSourceSwitchTests"`

Expected: 全テストPASS。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表 BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表
git commit -m "feat: 仕上表の一般名称解決を追加"
```

---

### Task 4: 内部部屋行と外部タイプ行の集約

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleAggregator_仕上表集約.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleModels_仕上表モデル.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表/FinishScheduleAggregatorTests_仕上表集約テスト.cs`

**Interfaces:**
- Consumes: 部屋集計表由来の`NormalizedRoomName`一覧、`ResolvedProductPlacement`、分類警告。
- Produces: `FinishScheduleOutput FinishScheduleAggregator.Build(...)`、`InternalFinishRow`、`ExternalFinishRow`、`FinishScheduleCell`。

- [ ] **Step 1: 並び、重複排除、混在未解決、外部集約の失敗テストを書く**

```csharp
[TestMethod]
public void 部屋を正規化キー順にし一般名称を重複排除して改行する()
{
    var output = FinishScheduleAggregator.Build(
        Rooms("洋室２", "洋室1"),
        Resolved("洋室1", FinishColumn.WallFinish, 0, "P-1", "ビニル壁紙"),
        Array.Empty<FinishScheduleWarning>());
    CollectionAssert.AreEqual(new[] { "洋室1", "洋室２" }, output.InternalRows.Select(x => x.RoomName).ToArray());
    Assert.AreEqual("ビニル壁紙", output.InternalRows[0].WallFinish.Text);
}

[TestMethod]
public void 解決済みと未解決が混在しても名称を残し赤色対象と備考を作る()
{
    var output = FinishScheduleAggregator.Build(
        Rooms("101"),
        new[]
        {
            Resolved("101", FinishColumn.WallFinish, 0, "P-1", "ビニル壁紙"),
            Missing("101", FinishColumn.WallFinish, 0, "P-2", ProductResolution.GeneralNameMissing)
        }, Array.Empty<FinishScheduleWarning>());
    var row = output.InternalRows.Single();
    Assert.AreEqual("ビニル壁紙", row.WallFinish.Text);
    Assert.IsTrue(row.WallFinish.HighlightMissing);
    StringAssert.Contains(row.Notes, "一般名称未登録: P-2");
}

[TestMethod]
public void 外部はカテゴリファミリタイプ単位で要素数を数える()
{
    var output = FinishScheduleAggregator.BuildExternal(ExternalResolved(elementId: 1), ExternalResolved(elementId: 2));
    Assert.AreEqual(1, output.Count);
    Assert.AreEqual(2, output[0].ElementCount);
}
```

下地について、最小`Depth`順、その後一般名称順になるテストと、同一タイプ内でProduct_ID署名が違えば2行と警告になるテストも入れる。

- [ ] **Step 2: 集約器がないため失敗することを確認する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~FinishScheduleAggregatorTests"`

Expected: `FinishScheduleAggregator`と出力行型がないためFAIL。

- [ ] **Step 3: セル状態を失わない集約器を実装する**

```csharp
public sealed record FinishScheduleCell(string Text, bool HighlightMissing);

public sealed record InternalFinishRow(
    string RoomName,
    FinishScheduleCell FloorFinish, FinishScheduleCell FloorSubstrate,
    FinishScheduleCell WallFinish, FinishScheduleCell WallSubstrate,
    FinishScheduleCell CeilingFinish, FinishScheduleCell CeilingSubstrate,
    string Notes);

public sealed record ExternalFinishRow(
    string Category, string FamilyName, string TypeName,
    FinishScheduleCell Finish, FinishScheduleCell Substrate,
    int ElementCount, string Notes);

public sealed record FinishScheduleOutput(
    IReadOnlyList<InternalFinishRow> InternalRows,
    IReadOnlyList<ExternalFinishRow> ExternalRows,
    IReadOnlyList<FinishScheduleWarning> Warnings);
```

セル文字列は解決済み一般名称だけを`Environment.NewLine`で結ぶ。未解決IDは状態別に備考へ出し、同じIDの備考を重複させない。部屋集計表に存在して配置がない部屋も空行として残し、警告を付ける。

- [ ] **Step 4: 集約テストを合格させる**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~FinishScheduleAggregatorTests"`

Expected: 全テストPASS。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表 BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表
git commit -m "feat: 内部外部の仕上表行を集約"
```

---

### Task 5: Open XMLによる雛型保持Excel出力

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表/FinishScheduleWorkbookWriter_仕上表Excel出力.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表/FinishScheduleWorkbookWriterTests_仕上表Excel出力テスト.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`
- Modify: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj`
- Copy as content: `仕上表_雛型.xlsx`

**Interfaces:**
- Consumes: `FinishScheduleOutput`、雛型パス、最終出力パス。
- Produces: `void FinishScheduleWorkbookWriter.Write(string templatePath, string outputPath, FinishScheduleOutput output)`。

- [ ] **Step 1: Open XML依存とテスト用雛型をプロジェクトへ追加する**

両csprojへ次を追加する。

```xml
<PackageReference Include="DocumentFormat.OpenXml" Version="3.5.1" />
```

Add-in csprojへ追加する。

```xml
<Content Include="..\..\..\仕上表_雛型.xlsx"
         Link="Resources_画像と定義\仕上表_雛型.xlsx"
         CopyToOutputDirectory="PreserveNewest" />
```

Test csprojへ追加する。

```xml
<None Include="..\..\..\仕上表_雛型.xlsx"
      Link="TestData\仕上表_雛型.xlsx"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: セル値、赤色、外部シート、101行目以降、原本不変の失敗テストを書く**

```csharp
[TestMethod]
public void 内部外部シートを書き一般名称未登録セルを薄赤色にする()
{
    var originalHash = SHA256.HashData(File.ReadAllBytes(TemplatePath));
    var outputPath = TempPath("output.xlsx");
    new FinishScheduleWorkbookWriter().Write(TemplatePath, outputPath, OutputWithMissingGeneralName());

    using var document = SpreadsheetDocument.Open(outputPath, false);
    Assert.AreEqual("101", CellText(document, "仕上表", "A3"));
    Assert.AreEqual("ビニル壁紙", CellText(document, "仕上表", "D3"));
    Assert.AreEqual("FFF4CCCC", CellFillRgb(document, "仕上表", "D3"));
    Assert.AreEqual("壁", CellText(document, "外部仕上表", "A2"));
    CollectionAssert.AreEqual(originalHash, SHA256.HashData(File.ReadAllBytes(TemplatePath)));
}

[TestMethod]
public void 百一部屋目も雛型最終行の書式と罫線を継承する()
{
    var outputPath = TempPath("101rooms.xlsx");
    new FinishScheduleWorkbookWriter().Write(TemplatePath, outputPath, OutputWithRooms(101));
    using var document = SpreadsheetDocument.Open(outputPath, false);
    Assert.AreEqual("部屋101", CellText(document, "仕上表", "A103"));
    Assert.AreEqual(CellStyleIndex(document, "仕上表", "A102"), CellStyleIndex(document, "仕上表", "A103"));
}
```

一時ファイル作成失敗時に最終出力が存在しないテスト、セル内改行と`WrapText`のテストも入れる。

- [ ] **Step 3: テストを実行してWriter未実装で失敗することを確認する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~FinishScheduleWorkbookWriterTests"`

Expected: `FinishScheduleWorkbookWriter`がないためFAIL。

- [ ] **Step 4: 雛型コピー、行拡張、外部シート、赤色スタイルを実装する**

```csharp
public sealed class FinishScheduleWorkbookWriter
{
    private const string MissingFillRgb = "FFF4CCCC";

    public void Write(string templatePath, string outputPath, FinishScheduleOutput output)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("出力先フォルダーを取得できません。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp.xlsx");
        try
        {
            File.Copy(templatePath, temporaryPath, true);
            using (var document = SpreadsheetDocument.Open(temporaryPath, true))
            {
                WriteInternalSheet(document, output.InternalRows);
                WriteExternalSheet(document, output.ExternalRows);
            }
            using (SpreadsheetDocument.Open(temporaryPath, false)) { }
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
```

行3からA～Jを更新する。F・Iは空欄にする。101部屋以上では行102を複製し、行番号とセル参照を更新する。外部シートは新規WorksheetPartとして作り、A～Gのヘッダーとデータを追加する。赤色用CellFormatは既存罫線、配置、フォント、数値形式を複製し、Fillだけ`FFF4CCCC`へ差し替えて再利用する。

- [ ] **Step 5: Writerテストと全共有テストを合格させる**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`

Expected: 全テストPASS。

- [ ] **Step 6: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表 BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表
git commit -m "feat: Excel雛型へ仕上表を出力"
```

---

### Task 6: Revit部屋集計表と要素スナップショットの収集

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/FinishSchedules/RevitFinishSourceCollector_仕上表元データ取得.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/FinishSchedules/ExteriorSideResolver_外側判定.cs`

**Interfaces:**
- Consumes: `Autodesk.Revit.DB.Document`、`Identifiers.LayerProductIdParameterGuids`、`CompoundStructureReader`と`LayerSlotMapper`の既存規則。
- Produces: `FinishSourceSnapshot RevitFinishSourceCollector.Collect(Document document)`。`FinishSourceSnapshot`は部屋一覧、`HostFinishInput`一覧、警告を持つ。

- [ ] **Step 1: Revit非依存部分で必要な収集結果契約を追加しテストする**

`FinishScheduleModels_仕上表モデル.cs`へ追加する。

```csharp
public sealed record FinishSourceSnapshot(
    IReadOnlyList<NormalizedRoomName> Rooms,
    IReadOnlyList<HostFinishInput> Hosts,
    IReadOnlyList<FinishScheduleWarning> Warnings);
```

Task 1のテストへ、同じ正規化キーの部屋集計表名が1件へまとまるテストを追加して合格させる。

- [ ] **Step 2: 部屋集計表と要素の収集器を実装する**

```csharp
public sealed class RevitFinishSourceCollector
{
    private const string RoomScheduleName = "部屋集計表";
    private const string RoomNameHeading = "名前";
    private const string InnerRoomParameter = "st_部屋名（内側）";
    private const string OuterRoomParameter = "st_部屋名（外側）";
    private const string HorizontalRoomParameter = "st_天井・床部屋要素ID";

    public FinishSourceSnapshot Collect(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rooms = ReadScheduleRoomNames(document, RoomScheduleName, RoomNameHeading);
        var hosts = CollectHosts(document);
        return new FinishSourceSnapshot(rooms, hosts, hosts.SelectMany(x =>
            string.IsNullOrWhiteSpace(x.LayerWarning)
                ? Array.Empty<FinishScheduleWarning>()
                : new[] { new FinishScheduleWarning("LAYER_OVERFLOW", x.LayerWarning, x.ElementId) }).ToArray());
    }
}
```

`ViewSchedule.GetTableData()`から本文行と「名前」列を読み、空表示行を除外する。壁・床・天井・屋根は`FilteredElementCollector(document).WhereElementIsNotElementType()`で収集する。各要素からカテゴリ、ファミリ、タイプ、部屋名、複合構造を読み、`LayerSlotMapper.Map`の結果をレイヤー順へ戻して固定GUIDのProduct_IDと結ぶ。

- [ ] **Step 3: 外部面判定をカテゴリ別の小さなクラスへ分離する**

```csharp
internal static class ExteriorSideResolver
{
    public static InteriorSide InteriorFor(FinishHostKind kind) => kind switch
    {
        FinishHostKind.Floor => InteriorSide.FirstLayer,
        FinishHostKind.Wall or FinishHostKind.Ceiling or FinishHostKind.Roof => InteriorSide.LastLayer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

壁と屋根は複合構造の外部側端、床・天井の部屋名空欄要素はカテゴリ規則で反対端を外部候補としてスナップショット化する。端部を決められない場合は`EXTERIOR_SIDE_AMBIGUOUS`警告を返し、推測した配置を作らない。

- [ ] **Step 4: Add-inをビルドしてRevit API呼出しのコンパイルを確認する**

Run: `dotnet build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" -c Debug`

Expected: Build succeeded。Revitが開いていて配置同期で失敗した場合はRevitを終了して同じコマンドを再実行する。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/FinishSchedules BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/FinishSchedules_仕上表 BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/FinishSchedules_仕上表
git commit -m "feat: Revitから仕上表元データを収集"
```

---

### Task 7: 出力調整、事前確認、保存先選択

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/FinishScheduleComposition_仕上表構成.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/FinishSchedules/FinishScheduleExportDialog_仕上表出力確認.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/BuiSession_起動時共有.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/BuiPaneProvider_パネル登録.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/BuiApplication_アドイン起動.cs`

**Interfaces:**
- Consumes: 同じ`IProductRepository`、`FinishSourceSnapshot`、`FinishScheduleWorkbookWriter`。
- Produces: `FinishScheduleComposition.Create(IProductRepository products)`、`BuiSession.ProductRepository`、確認結果と`.xlsx`保存先。

- [ ] **Step 1: パネル構成がViewModelと製品リポジトリを返すよう変更する**

```csharp
internal sealed record BuiDockPaneCompositionResult(
    BuiDockPaneViewModel ViewModel,
    IProductRepository ProductRepository);
```

既存の`BuiDockPaneComposition.Create`の戻り値をこの型へ変更する。現在生成している`viewModel`と、その作成に渡している同一の`products`を`new BuiDockPaneCompositionResult(viewModel, products)`で返し、呼出側は`.ViewModel`をパネルへ、`.ProductRepository`をセッションへ渡す。

`BuiPaneProvider`は`ViewModel`と`ProductRepository`を読み取り専用プロパティで公開する。`BuiApplication`は次のセッションAPIを使う。

```csharp
public static BuiDockPaneViewModel? PaneViewModel { get; private set; }
public static IProductRepository? ProductRepository { get; private set; }

public static void Set(BuiDockPaneViewModel viewModel, IProductRepository productRepository)
{
    PaneViewModel = viewModel;
    ProductRepository = productRepository;
}

public static void Clear()
{
    PaneViewModel = null;
    ProductRepository = null;
}
```

- [ ] **Step 2: 出力調整サービスを実装する**

```csharp
internal sealed record FinishSchedulePreparedResult(
    FinishScheduleOutput Output,
    int InternalRoomCount,
    int ExternalTypeCount,
    int MissingProductIdCount,
    int AffectedRoomCount,
    int AffectedExternalTypeCount)
{
    public static FinishSchedulePreparedResult From(FinishScheduleOutput output) =>
        new(
            output,
            output.InternalRows.Count,
            output.ExternalRows.Count,
            CountDistinctMissingIds(output),
            output.InternalRows.Count(x => HasMissingCell(x)),
            output.ExternalRows.Count(x => x.Finish.HighlightMissing || x.Substrate.HighlightMissing));
}

internal sealed class FinishScheduleExportService
{
    private readonly RevitFinishSourceCollector _collector;
    private readonly GeneralNameResolver _resolver;
    private readonly FinishScheduleWorkbookWriter _writer;

    public async Task<FinishSchedulePreparedResult> PrepareAsync(Document document, CancellationToken token)
    {
        var snapshot = _collector.Collect(document);
        var classifications = snapshot.Hosts.Select(FinishLayerClassifier.Classify).ToArray();
        var placements = classifications
            .SelectMany(x => x.InternalPlacements.Concat(x.ExternalPlacements))
            .ToArray();
        var resolved = await _resolver.ResolveAsync(placements, token);
        var output = FinishScheduleAggregator.Build(snapshot.Rooms, resolved,
            snapshot.Warnings.Concat(classifications.SelectMany(x => x.Warnings)).ToArray());
        return FinishSchedulePreparedResult.From(output);
    }
}
```

`CountDistinctMissingIds`と`HasMissingCell`は`FinishScheduleOutput`のセル状態・備考から集計するprivate helperとして同じファイルに実装する。`PrepareAsync`はExcelを書かず件数と警告を返す。

同じファイルに`FinishScheduleCommandResult(bool Cancelled, string? OutputPath, string ErrorMessage)`と`FinishScheduleExportController`を定義する。`FinishScheduleComposition.Create(IProductRepository products)`はcollector、resolver、aggregator、writer、確認画面を組み立てたcontrollerを返す。controllerの同期メソッド`FinishScheduleCommandResult Run(Document document, IntPtr ownerHandle)`は、`PrepareAsync(...).GetAwaiter().GetResult()`、確認、保存先選択、`Write`の順で実行し、確認または保存ダイアログのキャンセル時は`Cancelled=true`を返す。技術エラーは`ErrorMessage`へ利用者向け要約だけを入れ、Excelを作らない。

- [ ] **Step 3: TaskDialog確認とSaveFileDialogを実装する**

確認画面は内部部屋数、外部タイプ数、一般名称未登録ID数、影響部屋数、影響外部タイプ数を本文へ表示する。警告詳細は`ExpandedContent`へ最大50件表示し、`TaskDialogCommonButtons.Ok | Cancel`で続行を確認する。

```csharp
var saveDialog = new Microsoft.Win32.SaveFileDialog
{
    Title = "仕上表の保存先",
    Filter = "Excelブック (*.xlsx)|*.xlsx",
    DefaultExt = ".xlsx",
    AddExtension = true,
    OverwritePrompt = true,
    FileName = $"{SafeFileName(document.Title)}_仕上表_{DateTime.Now:yyyyMMdd}.xlsx"
};
```

Revitの`MainWindowHandle`を親に設定し、背面へ隠れないようにする。キャンセル時はファイルを作らず`Result.Cancelled`へつなぐ。

- [ ] **Step 4: Add-inビルドと共有テストを実行する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`

Expected: 全テストPASS。

Run: `dotnet build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" -c Debug`

Expected: Build succeeded。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動 BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/FinishSchedules BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/BuiPaneProvider_パネル登録.cs
git commit -m "feat: 仕上表出力サービスをセッションへ接続"
```

---

### Task 8: リボンコマンドと配置物を接続

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/FinishSchedules/ExportFinishScheduleCommand_仕上表出力.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/リボン_04_仕上表出力.png`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/RibbonService_リボン作成.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`

**Interfaces:**
- Consumes: `BuiSession.ProductRepository`、`FinishScheduleComposition.Create`、アクティブ`Document`。
- Produces: Revitリボンの「仕上表出力」ボタンと読取専用`IExternalCommand`。

- [ ] **Step 1: リボンコマンドを実装する**

```csharp
[Transaction(TransactionMode.Manual)]
public sealed class ExportFinishScheduleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show("BUI 仕上表出力", "Revitプロジェクトを開いてください。");
            return Result.Cancelled;
        }

        var products = BuiSession.ProductRepository;
        if (products is null)
        {
            message = "BUIの製品データが準備されていません。";
            return Result.Failed;
        }

        var commandResult = FinishScheduleComposition.Create(products)
            .Run(document, commandData.Application.MainWindowHandle);
        if (commandResult.Cancelled) return Result.Cancelled;
        if (!string.IsNullOrWhiteSpace(commandResult.ErrorMessage))
        {
            message = commandResult.ErrorMessage;
            return Result.Failed;
        }
        return Result.Succeeded;
    }
}
```

属性は既存コマンドと同じ`TransactionMode.Manual`を使うが、トランザクションは開始しない。全体を`BuiRuntimeSafety.Run`で囲み、生の例外や社内パスをTaskDialogへ出さない。成功時は出力先をTaskDialogで通知する。

- [ ] **Step 2: リボン画像を作る**

`imagegen`で「透明背景、32×32、BUIの青を基調にした白い表計算グリッドと下向き出力矢印、文字なし、細線でも16×16で判別可能」というプロンプトを使い、`リボン_04_仕上表出力.png`として保存する。32×32と16×16の両方で視認性を確認する。

- [ ] **Step 3: リボンボタンと配置項目を追加する**

```csharp
var exportData = new PushButtonData(
    "BuiExportFinishSchedule",
    "仕上表出力",
    assemblyPath,
    typeof(ExportFinishScheduleCommand).FullName!);
var exportButton = panel.AddItem(exportData) as PushButton
    ?? throw new InvalidOperationException("仕上表出力ボタンを作成できませんでした。");
exportButton.ToolTip = "部屋とProduct_IDから内部・外部仕上表をExcelへ出力します。";
ApplyIcon(exportButton, "リボン_04_仕上表出力.png");
```

csprojへ画像の`CopyToOutputDirectory="PreserveNewest"`を追加し、雛型とOpen XML依存DLLも出力・開発配置先へコピーされることを確認する。

- [ ] **Step 4: ソリューション全体をビルドする**

Run: `dotnet build "BUI_Addin_CSharpアドイン/Bui.sln" -c Debug`

Expected: Build succeeded。`bin/Debug/net8.0-windows/Resources_画像と定義/仕上表_雛型.xlsx`と`リボン_04_仕上表出力.png`が存在し、`DocumentFormat.OpenXml.dll`と依存DLLが出力直下に存在する。

- [ ] **Step 5: コミットする**

```bash
git add BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/FinishSchedules BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/RibbonService_リボン作成.cs BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/リボン_04_仕上表出力.png BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj
git commit -m "feat: リボンへ仕上表出力を追加"
```

---

### Task 9: 回帰確認とRevit実機受入れ

**Files:**
- Modify only if a defect is found: files owned by Tasks 1–8
- Verify: `docs/superpowers/specs/2026-09-24-finish-schedule-export-design.md`

**Interfaces:**
- Consumes: 完成したリボンコマンド一式。
- Produces: 自動テスト、ビルド、Excel検証、Revit実機チェックの証跡。

- [ ] **Step 1: 全自動テストを実行する**

Run: `dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" -c Debug`

Expected: 全テストPASS。失敗時は該当タスクの最小修正を行い、失敗テストを残したまま修正しない。

- [ ] **Step 2: ソリューションをクリーンビルドする**

Run: `dotnet clean "BUI_Addin_CSharpアドイン/Bui.sln" -c Debug`

Run: `dotnet build "BUI_Addin_CSharpアドイン/Bui.sln" -c Debug`

Expected: 警告を確認した上でBuild succeeded。既存のSQL Client実体置換と4依存DLL強制コピーが引き続き成功する。

- [ ] **Step 3: 自動生成Excelを構造検証する**

テスト出力をOpen XMLで再読込し、次を機械確認する。

```text
仕上表!A3:J<最終行> が存在する
外部仕上表!A1:G<最終行> が存在する
一般名称未登録セルのfill RGBがFFF4CCCC
空Product_IDだけのセルは元書式のまま
101部屋目の行スタイルが雛型最終行と一致
原本のSHA-256が処理前後で一致
```

- [ ] **Step 4: Revit 2025で実機受入れを行う**

次のモデルケースを1つずつ実行し、確認結果を作業ログへ記録する。

```text
1. 壁を反転した内壁
2. 両面仕上が異なり、外側仕上が下地③にある壁
3. 両面仕上が異なり、外側仕上が下地⑤にある壁
4. 外側部屋名が空で最大番号が下地として残る壁
5. st_天井・床部屋要素IDを持つ床と天井
6. 内外部屋名が空の外壁と屋根
7. 一般名称未登録Product_ID
8. 空気層としてProduct_IDが空のレイヤー
9. CSV使用中の出力
10. SQL Serverへ切替後の出力
```

各ケースで、プレビュー件数、出力先、内部行、外部行、赤色、備考、Excelの印刷表示を確認する。

- [ ] **Step 5: 最終差分と作業ツリーを確認する**

Run: `git diff --check`

Run: `git status --short`

Expected: 意図したファイルだけが変更され、未追跡の一時Excelやビルド補助ファイルがない。

- [ ] **Step 6: 受入れ修正がある場合だけコミットする**

修正が発生した場合は、`git status --short`に表示されたうち、その不具合修正に直接関係するファイルだけを明示的なパス指定で`git add`し、`git commit -m "fix: 仕上表出力の実機検証問題を修正"`を実行する。修正がなければ空コミットは作らない。
