# 複層対応のProduct_IDとAssembly_ID 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 複合レイヤーの層ごとにProduct_IDを付与できるようにし、その並びからAssembly_IDを自動採番する。ID登録は検索タブへ集約し、概要タブは確認専用にする。

**Architecture:** 共有パラメータを `Product_ID（仕上）`＋`Product_ID（下地①〜⑤）` の6つ（Instance）に置き換え、`Assembly_ID`（Type）は既存のまま使う。層とパラメータの対応、Assembly_IDの採番可否は `Bui.Shared` の純粋関数に置き、Revit無しで単体テストできるようにする。Revit側は「層の並び」と「どちら端が室内側か」を渡すだけにする。

**Tech Stack:** .NET 8 / C# 12 / WPF / MSTest。Revit 2025 API。

## Global Constraints

- 設計の根拠は `docs/superpowers/specs/2026-09-07-layered-product-ids-design.md`。判断に迷ったらこれに従う。
- **共有パラメータのGUIDは計画に書かれた値をそのまま使う。絶対に生成し直さない。** 変えると、既にIDを書き込んだモデルとの互換が失われる。
- 判断ロジックは `src_ソース/Bui.Shared_共有ロジック/` に置く。Revit参照もWPF参照も持ち込まない。
- ファイル名は `英語名_日本語説明.cs`。先頭に「このファイルの目的」と「修正時の影響」を書く。
- テストは `tests_テスト/Bui.Tests/<領域>/<Name>Tests_<日本語>.cs`。MSTest、日本語のメソッド名。
- テスト実行（リポジトリのルートから）:
  `./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
- **終了コードで判定する。** 出力を `tail`/`head` に通さない。
- 既存の391件のテストを壊さない。壊れたら原因を直す。テストを消して通さない。
- `Bui.Addin` はこの環境ではコンパイルできない（Revit未インストール）。ビルドは `CS0246`/`CS0103`/`MSB3245` 等のRevit参照エラーだけになる。**それ以外のエラーが出ていないこと**を確認し、Revit依存部分は目視確認である旨を報告に書く。
- 既存の `Product_ID`（`569e8df1-…`）は**読み書きしないが、モデルから削除しない**。利用者の170件のデータを消さない。
- commit前に、変更したファイルに競合マーカー（`<<<<<<<`）と、CR/LF/TAB以外の制御文字が無いことを確認する。

---

## File Structure

**新規（`src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/`）**

| ファイル | 責務 |
| --- | --- |
| `LayerSlot_層スロット.cs` | 6つのスロットの定義と表示名。データだけ |
| `LayerSlotMapper_層スロット割当.cs` | 層の並び＋室内側の向き → スロット割当。純粋関数 |
| `AssemblyIdEligibility_構成ID可否.cs` | Assembly_IDを書いてよいかの判定。純粋関数 |

**新規（`src_ソース/Bui.Addin/`）**

| ファイル | 責務 |
| --- | --- |
| `Revit_連携/ExternalEvents/LayerProductIdWriteHandler_層製品ID書込.cs` | 層ごとのProduct_ID書込みとAssembly_ID書込み |

**変更**

| ファイル | 変更内容 |
| --- | --- |
| `Bui.Addin/Resources_画像と定義/Identifiers_固定GUID.cs` | 6つのGUIDを追加 |
| `Bui.Addin/Resources_画像と定義/SharedParameters_共有パラメータ.txt` | 6行追加 |
| `Bui.Addin/Revit_連携/SharedParameters/SharedParameterService_共有パラメータ処理.cs` | 6つを登録 |
| `Bui.Shared/Assemblies_複合構造/AssemblyFingerprint_構成照合.cs` | 指紋を層IDの並びから作る |
| `Bui.Addin/Revit_連携/Assemblies/CompoundStructureReader_複合構造読取.cs` | 室内側の向きを返す |
| `Bui.Shared/UI_画面/BuiViewContracts_画面連携契約.cs` | 層割当の契約 |
| `Bui.Shared/UI_画面/BuiDockPaneViewModel_パネル状態.cs` | 層割当の状態、付与範囲、概要タブ用メンバーの撤去 |
| `Bui.Addin/Revit_連携/UI/RevitIdentifierWorkflowService_ID付与操作.cs` | 層割当のプレビューと実行 |
| `Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml` | 層の表と付与範囲 |
| `Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml` | 付与機能を撤去、層は表示のみ |
| `Bui.Addin/Revit_連携/Audit/IdentifierAuditService_ID点検.cs` | 6つのパラメータを見る |
| `docs_説明書/操作手順_会社PC_次にやること.txt` | 手順の書き直し |
| `docs_説明書/Troubleshooting_問題解決.md` | 層付与とAssembly_IDの項目 |

---

## Task 1: 共有パラメータを6つに増やす

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/Identifiers_固定GUID.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/SharedParameters_共有パラメータ.txt`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/SharedParameters/SharedParameterService_共有パラメータ処理.cs`

**Interfaces:**
- Consumes: 既存の `ParameterSpec`、`ParameterBindingKind`、`InstallOne`、`CollectProductCategories`、`CollectAssemblyCategories`
- Produces:
  - `Bui.Resources.Identifiers.LayerProductIdParameterGuids` … `static IReadOnlyList<Guid>`（仕上→下地⑤の順、6要素）
  - `Bui.Resources.Identifiers.LayerProductIdParameterNames` … `static IReadOnlyList<string>`（同じ順）

**GUIDは以下をそのまま使う。生成し直さない。**

| 順 | 名前 | GUID |
| --- | --- | --- |
| 0 | `Product_ID（仕上）` | `e10c429c-9ee1-4813-9e59-9178991af83a` |
| 1 | `Product_ID（下地①）` | `3fcefbeb-ddcc-4f74-bd90-d8be05baf555` |
| 2 | `Product_ID（下地②）` | `ba9206e7-d7b1-47aa-894f-1eb71ceb4c53` |
| 3 | `Product_ID（下地③）` | `0cef352d-edbb-441c-9bd8-43ecf239eb4d` |
| 4 | `Product_ID（下地④）` | `5b008bd1-2630-4295-ab09-f7cf4cdbf764` |
| 5 | `Product_ID（下地⑤）` | `79b24248-89df-460d-bbcc-95af64515a63` |

- [ ] **Step 1: GUIDを定義に追加する**

`Identifiers_固定GUID.cs` の `AssemblyIdParameterGuid` の下に追加する:

```csharp
    /// <summary>
    /// 複合レイヤーの層ごとのProduct_IDです。並び順は 仕上 → 下地① → … → 下地⑤ で固定します。
    /// この順序と添字が、層スロットの番号そのものです。並べ替えると既存モデルの値がずれます。
    /// GUIDは初回リリース後は絶対に再生成しません。
    /// </summary>
    public static IReadOnlyList<Guid> LayerProductIdParameterGuids { get; } = new[]
    {
        new Guid("e10c429c-9ee1-4813-9e59-9178991af83a"),
        new Guid("3fcefbeb-ddcc-4f74-bd90-d8be05baf555"),
        new Guid("ba9206e7-d7b1-47aa-894f-1eb71ceb4c53"),
        new Guid("0cef352d-edbb-441c-9bd8-43ecf239eb4d"),
        new Guid("5b008bd1-2630-4295-ab09-f7cf4cdbf764"),
        new Guid("79b24248-89df-460d-bbcc-95af64515a63")
    };

    /// <summary>共有パラメータ定義ファイルに書かれている名前です。GUIDと同じ並びです。</summary>
    public static IReadOnlyList<string> LayerProductIdParameterNames { get; } = new[]
    {
        "Product_ID（仕上）",
        "Product_ID（下地①）",
        "Product_ID（下地②）",
        "Product_ID（下地③）",
        "Product_ID（下地④）",
        "Product_ID（下地⑤）"
    };
```

`ProductIdParameterGuid`（`569e8df1-…`）は**消さない**。コメントを足す:

```csharp
    /// <summary>
    /// 旧Product_ID。2026-09-07以降、BUIは読み書きしません。
    /// 既にモデルへ書き込まれた値を消さないため、定義だけ残します。
    /// </summary>
    public static readonly Guid ProductIdParameterGuid = new("569e8df1-4ca6-448c-8fb9-496cb39b4458");
```

- [ ] **Step 2: 共有パラメータ定義ファイルへ6行足す**

`SharedParameters_共有パラメータ.txt` の末尾へ追加する。既存2行は消さない。
**タブ区切り**であることに注意する（スペースではない）。

```
PARAM	e10c429c-9ee1-4813-9e59-9178991af83a	Product_ID（仕上）	TEXT		1	1	BUI layer product identifier (finish)	1	0
PARAM	3fcefbeb-ddcc-4f74-bd90-d8be05baf555	Product_ID（下地①）	TEXT		1	1	BUI layer product identifier (substrate 1)	1	0
PARAM	ba9206e7-d7b1-47aa-894f-1eb71ceb4c53	Product_ID（下地②）	TEXT		1	1	BUI layer product identifier (substrate 2)	1	0
PARAM	0cef352d-edbb-441c-9bd8-43ecf239eb4d	Product_ID（下地③）	TEXT		1	1	BUI layer product identifier (substrate 3)	1	0
PARAM	5b008bd1-2630-4295-ab09-f7cf4cdbf764	Product_ID（下地④）	TEXT		1	1	BUI layer product identifier (substrate 4)	1	0
PARAM	79b24248-89df-460d-bbcc-95af64515a63	Product_ID（下地⑤）	TEXT		1	1	BUI layer product identifier (substrate 5)	1	0
```

追加後、区切りがタブであることを確認する:

```bash
python -c "
p=r'BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/SharedParameters_共有パラメータ.txt'
for line in open(p,encoding='utf-8-sig').read().splitlines():
    if line.startswith('PARAM'):
        print(len(line.split(chr(9))), line.split(chr(9))[2])
"
```

期待: すべての行で列数が同じ（10）で、3列目に名前が出る。

- [ ] **Step 3: 登録処理へ6つを足す**

`SharedParameterService_共有パラメータ処理.cs`。`AssemblySpec` の下に追加する:

```csharp
    /// <summary>
    /// 層ごとのProduct_IDです。Identifiersの並び順（仕上→下地⑤）をそのまま使います。
    /// 旧Product_IDはここに含めません。BUIは今後書き込まないためです。
    /// ただしモデルからの削除も行いません。既存の値を消さないためです。
    /// </summary>
    private static readonly IReadOnlyList<ParameterSpec> LayerProductSpecs =
        Identifiers.LayerProductIdParameterNames
            .Select((name, index) => new ParameterSpec(
                name,
                Identifiers.LayerProductIdParameterGuids[index],
                ParameterBindingKind.Instance))
            .ToArray();
```

`Install` の中を変更する。変更前:

```csharp
            var productDefinition = FindDefinition(definitionFile, ProductSpec);
            var assemblyDefinition = FindDefinition(definitionFile, AssemblySpec);
            var productTargets = CollectProductCategories(document, categoryResults);
            var assemblyTargets = CollectAssemblyCategories(document, categoryResults);
```

変更後:

```csharp
            var layerDefinitions = LayerProductSpecs
                .Select(spec => (Spec: spec, Definition: FindDefinition(definitionFile, spec)))
                .ToArray();
            var assemblyDefinition = FindDefinition(definitionFile, AssemblySpec);
            var productTargets = CollectProductCategories(document, categoryResults);
            var assemblyTargets = CollectAssemblyCategories(document, categoryResults);
```

変更前:

```csharp
                InstallOne(document, application, ProductSpec, productDefinition, productTargets, categoryResults, conflicts);
                InstallOne(document, application, AssemblySpec, assemblyDefinition, assemblyTargets, categoryResults, conflicts);
```

変更後:

```csharp
                foreach (var (spec, definition) in layerDefinitions)
                {
                    InstallOne(document, application, spec, definition, productTargets, categoryResults, conflicts);
                }

                InstallOne(document, application, AssemblySpec, assemblyDefinition, assemblyTargets, categoryResults, conflicts);
```

`ProductSpec` の定義は**残す**が、使わなくなる。次のコメントを付ける:

```csharp
    /// <summary>
    /// 旧Product_ID。2026-09-07以降は登録も書込みもしません。
    /// 既存モデルに残っている値を消さないため、定義だけ残しています。
    /// </summary>
    private static readonly ParameterSpec ProductSpec = new(
        "Product_ID",
        Identifiers.ProductIdParameterGuid,
        ParameterBindingKind.Instance);
```

未使用の警告が出る場合は、この属性を付けずコメントだけ残し、
`Install` から参照されないことを確認する。ファイルの `using System.Linq;` が
無ければ追加する（`Select`/`ToArray` を使うため）。

- [ ] **Step 4: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件（391件合格）。この変更はBui.Addin側なので、テスト数は変わらない。

- [ ] **Step 5: Bui.Addin のビルドでRevit以外のエラーが無いことを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: 何も出ない。出たら直す。

- [ ] **Step 6: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin"
git commit -m "feat: install six per-layer Product_ID shared parameters"
```

---

## Task 2: 層とパラメータの対応

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/LayerSlot_層スロット.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/LayerSlotMapper_層スロット割当.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造/LayerSlotMapperTests_層スロット割当テスト.cs`

**Interfaces:**
- Consumes: 既存の `Bui.Assemblies.AssemblyLayerRecord(int Order, string Function, decimal ThicknessMillimeters, string ProductId, string RevitMaterialName, long MaterialElementId = -1, bool IsMaterialFallback = false)`
- Produces:
  - `Bui.Assemblies.LayerSlot` … `public enum { Finish = 0, Substrate1 = 1, ... Substrate5 = 5 }`
  - `Bui.Assemblies.LayerSlotNames.Of(LayerSlot)` … `public static string`
  - `Bui.Assemblies.LayerSlotNames.Count` … `public const int` = 6
  - `Bui.Assemblies.InteriorSide` … `public enum { FirstLayer, LastLayer }`
  - `Bui.Assemblies.LayerSlotAssignment` … `public sealed record LayerSlotAssignment(AssemblyLayerRecord Layer, LayerSlot Slot)`
  - `Bui.Assemblies.LayerSlotPlan` … `public sealed record LayerSlotPlan(IReadOnlyList<LayerSlotAssignment> Assigned, IReadOnlyList<AssemblyLayerRecord> Overflow, string Warning)`
  - `Bui.Assemblies.LayerSlotMapper.Map(IReadOnlyList<AssemblyLayerRecord> layers, InteriorSide interiorSide)` … `public static LayerSlotPlan`

**規則:** 室内側の層が `Finish`。そこから外側へ向かって `Substrate1`…`Substrate5`。
7層以上あれば、室内側から6層目までを割り当て、残りは `Overflow` に入れて `Warning` を出す。

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Assemblies_複合構造/LayerSlotMapperTests_層スロット割当テスト.cs`:

```csharp
// このファイルの目的:
//   どの層がどのパラメータに入るかを固定します。
//   ここがずれると、仕上のIDが下地の欄に書き込まれ、モデルの意味が壊れます。
// 修正時の影響:
//   室内側の向きはカテゴリごとに違います。Revit側の判定を変える前に、ここを読んでください。
using Bui.Assemblies;

namespace Bui.Tests.Assemblies;

[TestClass]
public class LayerSlotMapperTests_層スロット割当テスト
{
    /// <summary>順序だけを持つ層を作ります。割当の判定に必要なのは並びだけです。</summary>
    private static AssemblyLayerRecord Layer(int order) =>
        new(order, "構造", 10m, string.Empty, $"材料{order}");

    private static IReadOnlyList<AssemblyLayerRecord> Layers(int count) =>
        Enumerable.Range(0, count).Select(Layer).ToArray();

    [TestMethod]
    public void 室内側が最後の層なら最後が仕上になる()
    {
        // 壁は層順が外部側から始まります。室内側は最後の層です。
        var plan = LayerSlotMapper.Map(Layers(3), InteriorSide.LastLayer);

        CollectionAssert.AreEqual(
            new[] { LayerSlot.Finish, LayerSlot.Substrate1, LayerSlot.Substrate2 },
            plan.Assigned.Select(a => a.Slot).ToArray());
        CollectionAssert.AreEqual(
            new[] { 2, 1, 0 },
            plan.Assigned.Select(a => a.Layer.Order).ToArray());
    }

    [TestMethod]
    public void 室内側が最初の層なら最初が仕上になる()
    {
        // 床は層順が上面から始まります。室内側は最初の層です。
        var plan = LayerSlotMapper.Map(Layers(3), InteriorSide.FirstLayer);

        CollectionAssert.AreEqual(
            new[] { LayerSlot.Finish, LayerSlot.Substrate1, LayerSlot.Substrate2 },
            plan.Assigned.Select(a => a.Slot).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            plan.Assigned.Select(a => a.Layer.Order).ToArray());
    }

    [TestMethod]
    public void 単層なら仕上だけになる()
    {
        var plan = LayerSlotMapper.Map(Layers(1), InteriorSide.LastLayer);

        Assert.AreEqual(1, plan.Assigned.Count);
        Assert.AreEqual(LayerSlot.Finish, plan.Assigned[0].Slot);
        Assert.AreEqual(0, plan.Overflow.Count);
        Assert.AreEqual(string.Empty, plan.Warning);
    }

    [TestMethod]
    public void 六層はちょうど収まり警告を出さない()
    {
        var plan = LayerSlotMapper.Map(Layers(6), InteriorSide.LastLayer);

        Assert.AreEqual(6, plan.Assigned.Count);
        Assert.AreEqual(LayerSlot.Substrate5, plan.Assigned[5].Slot);
        Assert.AreEqual(0, plan.Overflow.Count);
        Assert.AreEqual(string.Empty, plan.Warning);
    }

    [TestMethod]
    public void 七層以上は六層目までを割り当て残りを警告する()
    {
        var plan = LayerSlotMapper.Map(Layers(8), InteriorSide.LastLayer);

        Assert.AreEqual(6, plan.Assigned.Count);
        Assert.AreEqual(2, plan.Overflow.Count);
        StringAssert.Contains(plan.Warning, "2層");
        // 溢れるのは室内側から数えて7番目以降、つまり最も外側の層です。
        CollectionAssert.AreEqual(new[] { 1, 0 }, plan.Overflow.Select(l => l.Order).ToArray());
    }

    [TestMethod]
    public void 層が無ければ何も割り当てない()
    {
        var plan = LayerSlotMapper.Map(Array.Empty<AssemblyLayerRecord>(), InteriorSide.LastLayer);

        Assert.AreEqual(0, plan.Assigned.Count);
        Assert.AreEqual(0, plan.Overflow.Count);
        Assert.AreEqual(string.Empty, plan.Warning);
    }

    [TestMethod]
    public void nullを渡すと例外にする()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => LayerSlotMapper.Map(null!, InteriorSide.LastLayer));
    }

    [TestMethod]
    public void スロットの表示名は共有パラメータの名前と一致する()
    {
        // ここがずれると、画面に出す名前と実際の書き込み先が食い違います。
        Assert.AreEqual("Product_ID（仕上）", LayerSlotNames.Of(LayerSlot.Finish));
        Assert.AreEqual("Product_ID（下地①）", LayerSlotNames.Of(LayerSlot.Substrate1));
        Assert.AreEqual("Product_ID（下地⑤）", LayerSlotNames.Of(LayerSlot.Substrate5));
        Assert.AreEqual(6, LayerSlotNames.Count);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter LayerSlotMapperTests
```

期待: ビルドエラー（`LayerSlot` などが存在しない）。

- [ ] **Step 3: スロットの定義を書く**

`LayerSlot_層スロット.cs`:

```csharp
// このファイルの目的:
//   複合レイヤーの層を入れる「箱」を定義します。
//   箱は6つで、室内側から 仕上 → 下地① → … → 下地⑤ の順です。
//
// なぜ順序が固定なのか:
//   この並びがそのまま共有パラメータの並び（Identifiers.LayerProductIdParameterGuids）です。
//   並べ替えると、既にモデルへ書き込んだ値が別の箱を指すことになります。
//
// 修正時の影響:
//   名前を変える場合は、共有パラメータ定義ファイルの名前も同時に変えてください。
//   食い違うと、画面の表示と実際の書き込み先がずれます。

namespace Bui.Assemblies;

/// <summary>層を入れる箱です。数値はそのまま共有パラメータの添字です。</summary>
public enum LayerSlot
{
    Finish = 0,
    Substrate1 = 1,
    Substrate2 = 2,
    Substrate3 = 3,
    Substrate4 = 4,
    Substrate5 = 5
}

/// <summary>複合構造の層順のうち、どちら端が室内側かを表します。</summary>
public enum InteriorSide
{
    /// <summary>層順の最初が室内側（床など）。</summary>
    FirstLayer,

    /// <summary>層順の最後が室内側（壁・天井・屋根など）。</summary>
    LastLayer
}

/// <summary>スロットの表示名です。共有パラメータの名前と一致させます。</summary>
public static class LayerSlotNames
{
    private static readonly string[] Names =
    {
        "Product_ID（仕上）",
        "Product_ID（下地①）",
        "Product_ID（下地②）",
        "Product_ID（下地③）",
        "Product_ID（下地④）",
        "Product_ID（下地⑤）"
    };

    /// <summary>用意されている箱の数です。</summary>
    public const int Count = 6;

    public static string Of(LayerSlot slot)
    {
        var index = (int)slot;
        if (index is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "未対応の層スロットです。");
        }

        return Names[index];
    }
}
```

- [ ] **Step 4: 割当を書く**

`LayerSlotMapper_層スロット割当.cs`:

```csharp
// このファイルの目的:
//   複合構造の層の並びを、6つの箱へ割り当てます。
//
// 規則:
//   室内側の層が「仕上」。そこから外側へ向かって 下地①→⑤。
//   7層以上ある場合は、室内側から6層目までを割り当て、残りは割り当てません。
//   割り当てなかった層は捨てずにOverflowへ入れ、画面で知らせます。
//
// なぜRevitに聞かないのか:
//   Revitの層順は、壁なら外部側から、床なら上面から始まります。
//   どちら端が室内側かはカテゴリごとに違い、その判定はRevit側の仕事です。
//   ここは「どちら端か」だけを受け取り、割当の規則だけを持ちます。
//   こうすることでRevit無しで検証できます。
//
// 修正時の影響:
//   割当がずれると、仕上のIDが下地の欄に書き込まれます。
//   モデルを見ても間違いに気づけないため、必ずテストで固定してください。

namespace Bui.Assemblies;

/// <summary>1つの層と、その層が入る箱の組です。</summary>
public sealed record LayerSlotAssignment(AssemblyLayerRecord Layer, LayerSlot Slot);

/// <summary>割当の結果です。溢れた層と、その説明を含みます。</summary>
public sealed record LayerSlotPlan(
    IReadOnlyList<LayerSlotAssignment> Assigned,
    IReadOnlyList<AssemblyLayerRecord> Overflow,
    string Warning);

/// <summary>層の並びを箱へ割り当てる純粋関数です。</summary>
public static class LayerSlotMapper
{
    public static LayerSlotPlan Map(IReadOnlyList<AssemblyLayerRecord> layers, InteriorSide interiorSide)
    {
        ArgumentNullException.ThrowIfNull(layers);

        // 室内側から外側への並びに揃えます。以降はこの順で数えるだけです。
        var fromInterior = interiorSide == InteriorSide.LastLayer
            ? layers.Reverse().ToArray()
            : layers.ToArray();

        var assigned = fromInterior
            .Take(LayerSlotNames.Count)
            .Select((layer, index) => new LayerSlotAssignment(layer, (LayerSlot)index))
            .ToArray();

        var overflow = fromInterior.Skip(LayerSlotNames.Count).ToArray();
        var warning = overflow.Length == 0
            ? string.Empty
            : $"層が{fromInterior.Length}あります。室内側から{LayerSlotNames.Count}層目までに付与し、"
                + $"外側の{overflow.Length}層には付与しません。";

        return new LayerSlotPlan(assigned, overflow, warning);
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter LayerSlotMapperTests
```

期待: 8件すべてPASS。

- [ ] **Step 6: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造"
git commit -m "feat: map compound layers onto the six Product_ID slots"
```

---

## Task 3: Assembly_IDの採番可否と指紋

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/AssemblyIdEligibility_構成ID可否.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造/AssemblyFingerprint_構成照合.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造/AssemblyIdEligibilityTests_構成ID可否テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造/AssemblyFingerprintTests_構成照合テスト.cs`（既存に追記）

**Interfaces:**
- Consumes: Task 2の `LayerSlot` / `LayerSlotAssignment`、既存の `AssemblyRecord`、`OperationResult<T>`
- Produces:
  - `Bui.Assemblies.AssemblyIdEligibility.Evaluate(IReadOnlyList<IReadOnlyList<string>> layerIdsPerElement)` … `public static AssemblyIdDecision`
  - `Bui.Assemblies.AssemblyIdDecision` … `public sealed record AssemblyIdDecision(bool CanAssign, string Reason)`

**規則（設計 §4 / §4.1）:**
- 層のIDに1つでも空欄があれば採番しない
- 同じタイプの要素すべてでIDの並びが一致していなければ採番しない
- どちらの場合も層IDの書き込みは止めない。Assembly_IDだけ見送る

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Assemblies_複合構造/AssemblyIdEligibilityTests_構成ID可否テスト.cs`:

```csharp
// このファイルの目的:
//   Assembly_IDを書いてよい場合だけ書くことを守ります。
//
// なぜ必要か:
//   層のIDは要素ごと、Assembly_IDはタイプごとに付きます。
//   同じタイプの要素で層IDが揃っていないのにタイプへ番号を振ると、
//   その番号はどの要素の実態とも一致しません。
// 修正時の影響:
//   緩めると、モデルに「構成は同じはずなのに中身が違う」状態が残ります。
using Bui.Assemblies;

namespace Bui.Tests.Assemblies;

[TestClass]
public class AssemblyIdEligibilityTests_構成ID可否テスト
{
    [TestMethod]
    public void 全要素でIDが揃っていれば採番できる()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[]
        {
            new[] { "BD-1", "BD-2" },
            new[] { "BD-1", "BD-2" }
        });

        Assert.IsTrue(decision.CanAssign);
        Assert.AreEqual(string.Empty, decision.Reason);
    }

    [TestMethod]
    public void 要素が1つでも揃っているとみなす()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[] { new[] { "BD-1" } });

        Assert.IsTrue(decision.CanAssign);
    }

    [TestMethod]
    public void 空欄があれば採番しない()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[]
        {
            new[] { "BD-1", "" }
        });

        Assert.IsFalse(decision.CanAssign);
        StringAssert.Contains(decision.Reason, "空欄");
    }

    [TestMethod]
    public void 空白だけの値も空欄として扱う()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[]
        {
            new[] { "BD-1", "   " }
        });

        Assert.IsFalse(decision.CanAssign);
    }

    [TestMethod]
    public void 同一タイプ内でIDが揃っていなければ採番しない()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[]
        {
            new[] { "BD-1", "BD-2" },
            new[] { "BD-1", "BD-9" }
        });

        Assert.IsFalse(decision.CanAssign);
        StringAssert.Contains(decision.Reason, "揃っていない");
    }

    [TestMethod]
    public void 層の数が違う場合も揃っていないとみなす()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[]
        {
            new[] { "BD-1", "BD-2" },
            new[] { "BD-1" }
        });

        Assert.IsFalse(decision.CanAssign);
        StringAssert.Contains(decision.Reason, "揃っていない");
    }

    [TestMethod]
    public void 要素が無ければ採番しない()
    {
        var decision = AssemblyIdEligibility.Evaluate(Array.Empty<IReadOnlyList<string>>());

        Assert.IsFalse(decision.CanAssign);
        Assert.IsTrue(decision.Reason.Length > 0);
    }

    [TestMethod]
    public void 層が無ければ採番しない()
    {
        var decision = AssemblyIdEligibility.Evaluate(new[] { Array.Empty<string>() });

        Assert.IsFalse(decision.CanAssign);
    }
}
```

`AssemblyFingerprintTests_構成照合テスト.cs` のクラス内へ追記する:

```csharp
    [TestMethod]
    public void 層のProduct_IDの並びが同じなら同じ指紋になる()
    {
        // 厚さや材料名が違っても、入っている製品の並びが同じなら同じ構成として扱います。
        var a = new AssemblyRecord("ID-A", "壁", new[]
        {
            new AssemblyLayerRecord(0, "構造", 100m, "BD-1", "材料X"),
            new AssemblyLayerRecord(1, "仕上", 12.5m, "BD-2", "材料Y")
        });
        var b = new AssemblyRecord("ID-B", "壁", new[]
        {
            new AssemblyLayerRecord(0, "下地", 120m, "BD-1", "材料Z"),
            new AssemblyLayerRecord(1, "仕上", 9.5m, "BD-2", "材料W")
        });

        Assert.AreEqual(
            AssemblyFingerprint.Create(a).Value,
            AssemblyFingerprint.Create(b).Value);
    }

    [TestMethod]
    public void 層のProduct_IDの並び順が違えば別の指紋になる()
    {
        var a = new AssemblyRecord("ID-A", "壁", new[]
        {
            new AssemblyLayerRecord(0, "構造", 100m, "BD-1", "材料X"),
            new AssemblyLayerRecord(1, "仕上", 12.5m, "BD-2", "材料Y")
        });
        var b = new AssemblyRecord("ID-B", "壁", new[]
        {
            new AssemblyLayerRecord(0, "構造", 100m, "BD-2", "材料X"),
            new AssemblyLayerRecord(1, "仕上", 12.5m, "BD-1", "材料Y")
        });

        Assert.AreNotEqual(
            AssemblyFingerprint.Create(a).Value,
            AssemblyFingerprint.Create(b).Value);
    }
```

**既存テストの扱い:** `AssemblyFingerprintTests_構成照合テスト.cs` に、厚さや機能の
違いで指紋が変わることを確認しているテストがあれば、それは仕様変更により
成立しなくなる。そのテストは**削除せず**、期待値を新しい規則へ書き換え、
なぜ変えたかをコメントに残す。判断に迷ったら実装を止めて報告する。

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter Assembly
```

期待: ビルドエラー（`AssemblyIdEligibility` が無い）と、指紋テストの失敗。

- [ ] **Step 3: 採番可否を書く**

`AssemblyIdEligibility_構成ID可否.cs`:

```csharp
// このファイルの目的:
//   Assembly_IDを書いてよいかを判定します。
//
// なぜ判定が要るのか:
//   層のProduct_IDは要素ごと、Assembly_IDはタイプごとに付きます。
//   同じタイプの要素AとBがあり、Aだけに層IDを付けた状態でタイプへ番号を振ると、
//   その番号はBの実態と一致しません。番号が嘘になります。
//   また、層のIDが一部だけ埋まっている状態で番号を振ると、
//   残りを埋めた時点で別の番号になり、同じ構成に2つの番号が付きます。
//
// 修正時の影響:
//   採番できない場合でも、層IDの書き込みは止めません。
//   Assembly_IDだけを見送り、理由を画面へ返します。

namespace Bui.Assemblies;

/// <summary>Assembly_IDを付けてよいかと、付けない場合の理由です。</summary>
public sealed record AssemblyIdDecision(bool CanAssign, string Reason);

/// <summary>採番可否の判定です。純粋関数として保ちます。</summary>
public static class AssemblyIdEligibility
{
    /// <param name="layerIdsPerElement">
    /// 同じタイプの要素ごとの、層のProduct_IDの並び（室内側から外側の順）。
    /// </param>
    public static AssemblyIdDecision Evaluate(IReadOnlyList<IReadOnlyList<string>> layerIdsPerElement)
    {
        ArgumentNullException.ThrowIfNull(layerIdsPerElement);

        if (layerIdsPerElement.Count == 0)
        {
            return new AssemblyIdDecision(false, "対象の要素がないため、Assembly_IDは付けませんでした。");
        }

        var normalized = layerIdsPerElement
            .Select(ids => (ids ?? Array.Empty<string>())
                .Select(id => (id ?? string.Empty).Trim())
                .ToArray())
            .ToArray();

        if (normalized.Any(ids => ids.Length == 0))
        {
            return new AssemblyIdDecision(false, "層がないため、Assembly_IDは付けませんでした。");
        }

        if (normalized.Any(ids => ids.Any(id => id.Length == 0)))
        {
            return new AssemblyIdDecision(
                false,
                "層のProduct_IDに空欄があるため、Assembly_IDは付けませんでした。すべての層を埋めると採番されます。");
        }

        var first = normalized[0];
        var allSame = normalized.All(ids =>
            ids.Length == first.Length
            && ids.Zip(first, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(same => same));

        return allSame
            ? new AssemblyIdDecision(true, string.Empty)
            : new AssemblyIdDecision(
                false,
                "同一タイプ内でProduct_IDが揃っていないため、Assembly_IDは付けませんでした。"
                    + "付与範囲を「同一タイプすべて」にして付け直すと採番されます。");
    }
}
```

- [ ] **Step 4: 指紋を層IDの並びから作るよう差し替える**

`AssemblyFingerprint_構成照合.cs` の `Create` の中身を変更する。
`NormalizedLayer` に渡していた `Function` と厚さを外し、順序とProduct_IDだけにする。

変更前:

```csharp
            // 表示名は利用者向けの補助情報で、構成そのものの一致条件には含めません。
            normalizedLayers.Add(new NormalizedLayer(
                layer.Order,
                Normalize(layer.Function),
                decimal.Round(layer.ThicknessMillimeters, 3, MidpointRounding.AwayFromZero)
                    .ToString("0.000", CultureInfo.InvariantCulture),
                Normalize(layer.ProductId)));
```

変更後:

```csharp
            // 構成の同一性は「どの製品がどの順で入っているか」で決めます。
            // 厚さや層の機能は含めません。同じ製品の並びなら同じ構成として扱う、
            // という取り決め（設計 §4）に合わせるためです。
            // 位置は層の並び順そのもの（室内側からの通し番号）で表します。
            normalizedLayers.Add(new NormalizedLayer(
                normalizedLayers.Count,
                Normalize(layer.ProductId)));
```

`NormalizedLayer` の定義を次へ変更する（同ファイル内）:

```csharp
    private sealed record NormalizedLayer(int Position, string ProductId);
```

`using System.Globalization;` が他で使われていなければ削除する。
`AssemblyRecord.OrderedLayers` の並び順が意味を持つため、
呼び出し側は**室内側から外側の順**で渡すこと。その旨をメソッドのコメントへ足す:

```csharp
    /// <summary>
    /// 構成を正規化してSHA-256の16進フィンガープリントを返します。
    /// OrderedLayersは「室内側から外側」の順で渡してください。順序が指紋に含まれます。
    /// </summary>
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter Assembly
```

期待: すべてPASS。既存の指紋テストで期待値の書き換えが必要なものは書き換える。

- [ ] **Step 6: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Assemblies_複合構造" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Assemblies_複合構造"
git commit -m "feat: derive the Assembly_ID from the layer product sequence"
```

---

## Task 4: 室内側がどちら端かをRevitから返す

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Assemblies/CompoundStructureReader_複合構造読取.cs`

**Interfaces:**
- Consumes: Task 2の `Bui.Assemblies.InteriorSide`
- Produces: `CompoundStructureReadResult` に `InteriorSide InteriorSide` を追加（末尾の位置引数）

**規則（設計 §3）:**

| ホスト | 室内側 |
| --- | --- |
| 壁（`Wall`） | `LastLayer` |
| 床（`Floor`） | `FirstLayer` |
| 天井（`Ceiling`） | `LastLayer` |
| 屋根（`RoofBase`） | `LastLayer` |

**この対応はこの環境では検証できない。** 誤っていても気づけないため、
Task 7以降で「どの層がどのパラメータへ入るか」を必ず画面に出す。

- [ ] **Step 1: 戻り値へ向きを足す**

`CompoundStructureReadResult` を変更する:

```csharp
/// <summary>選択ホストとタイプの識別子、構成順序の層、室内側がどちら端かを保持します。</summary>
public sealed record CompoundStructureReadResult(
    long HostElementId,
    long HostTypeElementId,
    string HostCategoryIdentity,
    string HostCategoryDisplayName,
    bool IsVerticallyCompound,
    IReadOnlyList<CompoundLayerSnapshot> Layers,
    /// <summary>
    /// Layersの並びのうち、どちら端が室内側かです。
    /// 「仕上」をどの層に割り当てるかがこれで決まります（設計 §3）。
    /// </summary>
    InteriorSide InteriorSide);
```

- [ ] **Step 2: カテゴリから向きを決める**

同ファイルへ次を追加する:

```csharp
    /// <summary>
    /// Revitの複合構造の層順は、壁なら外部側から、床なら上面から始まります。
    /// どちら端が室内側かはホストの種類で決まります。
    ///
    /// この対応はRevitが無い環境では検証できません。誤っていると、
    /// 仕上のIDが最も外側の層へ書き込まれ、モデルを見ても気づけません。
    /// そのため画面では必ず「どの層がどのパラメータへ入るか」を先に見せます。
    /// </summary>
    private static InteriorSide ResolveInteriorSide(Element host) => host switch
    {
        Floor => InteriorSide.FirstLayer,
        _ => InteriorSide.LastLayer
    };
```

`Read` が `CompoundStructureReadResult` を作っている箇所へ
`ResolveInteriorSide(selectedElement)` を渡す。既存の引数の順序は変えない。

- [ ] **Step 3: Bui.Addin のビルドでRevit以外のエラーが無いことを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: 何も出ない。

- [ ] **Step 4: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 5: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Assemblies/CompoundStructureReader_複合構造読取.cs"
git commit -m "feat: report which end of the compound structure faces the room"
```

---

## Task 5: 層ごとのProduct_ID書込み

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/LayerProductIdWriteHandler_層製品ID書込.cs`

**Interfaces:**
- Consumes: Task 1の `Identifiers.LayerProductIdParameterGuids`、Task 2の `LayerSlot`、既存の `RevitActionDispatcher`、`IRevitActionRequest`、`RevitDocumentMatch`、`OperationResult<T>`、`IdentifierWriteSummary`
- Produces:
  - `Bui.Revit.Products.LayerAssignmentRequest` … `public sealed record LayerAssignmentRequest(LayerSlot Slot, string ProductId)`
  - `Bui.Revit.Products.LayerProductIdPreview` … `public sealed record LayerProductIdPreview(Document SourceDocument, string SourceDocumentTitle, IReadOnlyList<long> TargetElementIds, IReadOnlyList<LayerAssignmentRequest> Assignments, string LayerWarning)`
  - `Bui.Revit.Products.LayerProductIdWriteReport` … `public sealed record LayerProductIdWriteReport(int WrittenElementCount, IReadOnlyList<string> FailureReasons)`
  - `LayerProductIdWriteHandler.EnqueueWriteAsync(RevitActionDispatcher dispatcher, LayerProductIdPreview preview, Action<OperationResult<LayerProductIdWriteReport>> completed, CancellationToken cancellationToken)` … `public static Task<OperationResult<bool>>`

**この実装の要点は既存の `ProductIdWriteHandler_製品ID書込.cs` と同じ規律に従うこと。**
その3点を必ず守る。

1. 文書の一致判定は `RevitDocumentMatch.IsSame` を使う。`ReferenceEquals` を使わない。
2. 失敗処理は `FailureSeverity.None` を**続行**として扱う。エラーだけロールバックする。
   Noneをロールバック扱いにすると、書き込みに成功しても全部取り消される。
   実際にその事故を起こしている。
3. `TransactionGroup` と単一 `Transaction` で囲み、Ctrl+Z 1回でまとめて戻せるようにする。

- [ ] **Step 1: ハンドラを書く**

`LayerProductIdWriteHandler_層製品ID書込.cs`:

```csharp
// このファイルの目的:
//   複合レイヤーの層ごとのProduct_IDを、選んだ要素へまとめて書き込みます。
//   Ctrl+Z 1回で全部戻せるよう、TransactionGroupと単一Transactionで囲みます。
//
// 既存のProductIdWriteHandlerと同じ規律を守ること:
//   1. 文書の一致はRevitDocumentMatchで判定する。ReferenceEqualsは使わない。
//      同じ文書でも別のラッパーが返り、書き込めなくなる。
//   2. FailureSeverity.Noneは「問題なし」。続行する。
//      これをロールバック扱いにすると、成功した書込みまで全部取り消される。
//   3. 失敗理由は必ず呼び出し側へ返す。件数だけ返すと原因が追えない。
//
// 修正時の影響:
//   上の3つはいずれも過去に事故を起こした箇所です。外さないでください。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Assemblies;
using Bui.Common;
using Bui.Resources;
using Bui.Revit.ExternalEvents;
using Bui.Revit.UI;

namespace Bui.Revit.Products;

/// <summary>1つの層スロットへ書き込むProduct_IDです。</summary>
public sealed record LayerAssignmentRequest(LayerSlot Slot, string ProductId);

/// <summary>書込み前に画面へ見せる内容です。</summary>
public sealed record LayerProductIdPreview(
    Document SourceDocument,
    string SourceDocumentTitle,
    IReadOnlyList<long> TargetElementIds,
    IReadOnlyList<LayerAssignmentRequest> Assignments,
    string LayerWarning);

/// <summary>書込みの結果です。</summary>
public sealed record LayerProductIdWriteReport(int WrittenElementCount, IReadOnlyList<string> FailureReasons);

/// <summary>層ごとのProduct_IDを1つのTransactionで書き込みます。</summary>
public sealed class LayerProductIdWriteHandler : IRevitActionRequest
{
    private readonly LayerProductIdPreview _preview;
    private readonly Action<OperationResult<LayerProductIdWriteReport>> _completed;

    private LayerProductIdWriteHandler(
        LayerProductIdPreview preview,
        Action<OperationResult<LayerProductIdWriteReport>> completed)
    {
        _preview = preview;
        _completed = completed;
    }

    public string Name => "層Product_ID書込み";

    public static Task<OperationResult<bool>> EnqueueWriteAsync(
        RevitActionDispatcher dispatcher,
        LayerProductIdPreview preview,
        Action<OperationResult<LayerProductIdWriteReport>> completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(completed);
        return dispatcher.EnqueueAsync(new LayerProductIdWriteHandler(preview, completed), cancellationToken);
    }

    public void Execute(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        try
        {
            var document = application.ActiveUIDocument?.Document;
            if (!RevitDocumentMatch.IsSame(document, _preview.SourceDocument))
            {
                _completed(OperationResult<LayerProductIdWriteReport>.Failure(
                    "REVIT-DOCUMENT-CHANGED",
                    "プレビュー後にRevitドキュメントが切り替わったため、書き込みませんでした。"
                        + RevitDocumentMatch.DescribeMismatch(document, _preview.SourceDocument),
                    Name));
                return;
            }

            WriteBatch(document!);
        }
        catch (Exception exception)
        {
            _completed(OperationResult<LayerProductIdWriteReport>.Failure(
                "LAYER-PRODUCT-ID-WRITE-FAILED",
                "層Product_IDの書込みを開始できませんでした。",
                exception.ToString()));
        }
    }

    private void WriteBatch(Document document)
    {
        var failures = new List<string>();
        var written = 0;

        using var group = new TransactionGroup(document, "BUI 層Product_ID付与");
        if (group.Start() != TransactionStatus.Started)
        {
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, new[] { "TransactionGroupを開始できませんでした。" })));
            return;
        }

        using var transaction = new Transaction(document, "層Product_IDを書込み");
        if (transaction.Start() != TransactionStatus.Started)
        {
            group.RollBack();
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, new[] { "Transactionを開始できませんでした。" })));
            return;
        }

        var preprocessor = new LayerFailuresPreprocessor();
        var options = transaction.GetFailureHandlingOptions();
        options.SetForcedModalHandling(true).SetClearAfterRollback(true).SetFailuresPreprocessor(preprocessor);
        transaction.SetFailureHandlingOptions(options);

        foreach (var elementId in _preview.TargetElementIds.Distinct())
        {
            if (TryWriteOne(document, elementId, failures)) written++;
        }

        if (transaction.Commit() != TransactionStatus.Committed)
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            failures.Add("Transactionを確定できず、全変更を取り消しました。" + preprocessor.DescribeErrors());
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, failures)));
            return;
        }

        // 子Transaction終了後のAssimilateで、Undo一覧では一つのBUI操作になります。
        if (group.Assimilate() != TransactionStatus.Committed)
        {
            failures.Add("TransactionGroupを確定できませんでした。");
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, failures)));
            return;
        }

        _completed(OperationResult<LayerProductIdWriteReport>.Success(
            new LayerProductIdWriteReport(written, failures)));
    }

    private bool TryWriteOne(Document document, long elementId, List<string> failures)
    {
        var element = document.GetElement(new ElementId(elementId));
        if (element is null)
        {
            failures.Add("要素が見つかりません。");
            return false;
        }

        var wroteAny = false;
        foreach (var assignment in _preview.Assignments)
        {
            var guid = Identifiers.LayerProductIdParameterGuids[(int)assignment.Slot];
            var parameter = element.get_Parameter(guid);
            if (parameter is null)
            {
                failures.Add(
                    $"「{LayerSlotNames.Of(assignment.Slot)}」パラメータがありません"
                        + "（このカテゴリに共有パラメータが割り当てられていません）。");
                continue;
            }

            if (parameter.IsReadOnly)
            {
                failures.Add($"「{LayerSlotNames.Of(assignment.Slot)}」が読み取り専用です。");
                continue;
            }

            if (!parameter.Set(assignment.ProductId))
            {
                failures.Add($"Revitが「{LayerSlotNames.Of(assignment.Slot)}」の設定を受け付けませんでした。");
                continue;
            }

            wroteAny = true;
        }

        return wroteAny;
    }

    /// <summary>
    /// FailureSeverity.Noneは「失敗が無い」状態です。続行しなければなりません。
    /// これをロールバック扱いにすると、成功した書込みまで全部取り消されます。
    /// 過去に実際その事故を起こしました。
    /// </summary>
    private sealed class LayerFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly SortedSet<string> _errors = new(StringComparer.Ordinal);

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            ArgumentNullException.ThrowIfNull(failuresAccessor);
            var severity = failuresAccessor.GetSeverity();
            if (severity == FailureSeverity.None) return FailureProcessingResult.Continue;
            if (severity == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteAllWarnings();
                return FailureProcessingResult.Continue;
            }

            foreach (var message in failuresAccessor.GetFailureMessages())
            {
                var description = message.GetDescriptionText();
                if (!string.IsNullOrWhiteSpace(description)) _errors.Add(description.Trim());
            }

            return failuresAccessor.IsTransactionBeingCommitted()
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }

        public string DescribeErrors() =>
            _errors.Count == 0 ? string.Empty : $" Revitからのエラー: {string.Join(" / ", _errors.Take(3))}";
    }
}
```

`RevitActionDispatcher` の予約メソッド名が `EnqueueAsync` でない場合は、
`ProductIdWriteHandler` が使っている実際のメソッド名に合わせる。

- [ ] **Step 2: Bui.Addin のビルドでRevit以外のエラーが無いことを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: 何も出ない。

- [ ] **Step 3: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 4: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/LayerProductIdWriteHandler_層製品ID書込.cs"
git commit -m "feat: write per-layer Product_IDs in one undoable transaction"
```

---

## Task 6: Assembly_IDの書込みを層IDから行う

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/LayerProductIdWriteHandler_層製品ID書込.cs`

**Interfaces:**
- Consumes: Task 3の `AssemblyIdEligibility.Evaluate`、`AssemblyIdDecision`、Task 1の `Identifiers.AssemblyIdParameterGuid`
- Produces: `LayerProductIdPreview` に `string AssemblyId` と `string AssemblyIdSkipReason` を追加（末尾の位置引数2つ）、`LayerProductIdWriteReport` に `bool AssemblyIdWritten` を追加（末尾）

**規則（設計 §4.1）:** Assembly_IDは**タイプ**へ書く。書いてよいかはTask 3の判定に従う。
書けない場合でも層IDの書込みは止めない。

- [ ] **Step 1: プレビューと報告へ項目を足す**

```csharp
public sealed record LayerProductIdPreview(
    Document SourceDocument,
    string SourceDocumentTitle,
    IReadOnlyList<long> TargetElementIds,
    IReadOnlyList<LayerAssignmentRequest> Assignments,
    string LayerWarning,
    /// <summary>採番済みのAssembly_ID。付けない場合は空文字。</summary>
    string AssemblyId,
    /// <summary>Assembly_IDを付けない理由。付ける場合は空文字。</summary>
    string AssemblyIdSkipReason);

public sealed record LayerProductIdWriteReport(
    int WrittenElementCount,
    IReadOnlyList<string> FailureReasons,
    bool AssemblyIdWritten);
```

`WriteBatch` の中で `new LayerProductIdWriteReport(...)` を作っている箇所すべてに
第3引数を渡す。失敗経路では `false`。

- [ ] **Step 2: タイプへAssembly_IDを書く**

`WriteBatch` の `foreach` の直後、`transaction.Commit()` の**前**へ追加する:

```csharp
        // Assembly_IDはタイプに付きます（設計 §4.1）。
        // 層IDは要素ごとなので、同じタイプの要素で揃っていないと番号が嘘になります。
        // 揃っているかの判定は画面側で済ませ、ここでは書くかどうかだけを見ます。
        var assemblyIdWritten = false;
        if (_preview.AssemblyId.Length > 0)
        {
            assemblyIdWritten = TryWriteAssemblyId(document, failures);
        }
        else if (_preview.AssemblyIdSkipReason.Length > 0)
        {
            failures.Add(_preview.AssemblyIdSkipReason);
        }
```

同クラスへ次を追加する:

```csharp
    /// <summary>対象要素のタイプへAssembly_IDを書きます。タイプは要素間で共有されます。</summary>
    private bool TryWriteAssemblyId(Document document, List<string> failures)
    {
        var firstElementId = _preview.TargetElementIds.FirstOrDefault();
        if (firstElementId == 0)
        {
            failures.Add("Assembly_IDを書く対象がありません。");
            return false;
        }

        var element = document.GetElement(new ElementId(firstElementId));
        var typeId = element?.GetTypeId() ?? ElementId.InvalidElementId;
        if (typeId == ElementId.InvalidElementId || document.GetElement(typeId) is not Element type)
        {
            failures.Add("Assembly_IDを書くタイプを取得できませんでした。");
            return false;
        }

        var parameter = type.get_Parameter(Identifiers.AssemblyIdParameterGuid);
        if (parameter is null)
        {
            failures.Add("Assembly_IDパラメータがありません（タイプに共有パラメータが割り当てられていません）。");
            return false;
        }

        if (parameter.IsReadOnly)
        {
            failures.Add("Assembly_IDパラメータが読み取り専用です。");
            return false;
        }

        if (!parameter.Set(_preview.AssemblyId))
        {
            failures.Add("RevitがAssembly_IDの設定を受け付けませんでした。");
            return false;
        }

        return true;
    }
```

成功経路の報告を `new LayerProductIdWriteReport(written, failures, assemblyIdWritten)` にする。

- [ ] **Step 3: Bui.Addin のビルドでRevit以外のエラーが無いことを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: 何も出ない。

- [ ] **Step 4: 全テストを実行する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: 失敗0件。

- [ ] **Step 5: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/ExternalEvents/LayerProductIdWriteHandler_層製品ID書込.cs"
git commit -m "feat: write the Assembly_ID onto the type in the same transaction"
```

---

## Task 7: 画面と書込みをつなぐ契約

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiViewContracts_画面連携契約.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/UI/RevitIdentifierWorkflowService_ID付与操作.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/LayerAssignmentContractTests_層割当契約テスト.cs`

**Interfaces:**
- Consumes: Task 2の `LayerSlot` / `LayerSlotNames`、Task 5・6の `LayerProductIdWriteHandler`
- Produces（すべて `Bui.UI` 名前空間、`public`）:
  - `LayerAssignmentRow` … `public sealed record LayerAssignmentRow(int Order, LayerSlot Slot, string SlotName, string MaterialName, decimal ThicknessMillimeters, string CurrentProductId, string SelectedProductId)`
  - `LayerAssignmentView` … `public sealed record LayerAssignmentView(IReadOnlyList<LayerAssignmentRow> Rows, string LayerWarning)`
  - `ProductAssignmentTarget` … `public enum { SameTypeAll, SelectionOnly }`
  - `ILayerAssignmentService` … 次の2メソッド
    - `Task<OperationResult<LayerAssignmentView>> LoadLayersAsync(SelectionSnapshot selection, CancellationToken cancellationToken)`
    - `Task<OperationResult<IdentifierWorkflowPreview>> PreviewLayerAssignmentAsync(SelectionSnapshot selection, IReadOnlyList<LayerAssignmentRow> rows, ProductAssignmentTarget target, CancellationToken cancellationToken)`

**なぜ契約を分けるか:** 画面は `ILayerAssignmentService` だけを見る。
Revitの型が画面側へ漏れない。テストでは偽実装に差し替えられる。

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/UI_画面/LayerAssignmentContractTests_層割当契約テスト.cs`:

```csharp
// このファイルの目的:
//   層割当の表が、画面へ渡す形として成立していることを守ります。
//   特に「どの層がどのパラメータへ入るか」が行に入っていることを固定します。
// 修正時の影響:
//   SlotNameを落とすと、利用者は書き込み先を確認できないまま実行することになります。
//   仕上のIDが下地へ入っても気づけません。
using Bui.Assemblies;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class LayerAssignmentContractTests_層割当契約テスト
{
    [TestMethod]
    public void 行はどのパラメータへ入るかを持つ()
    {
        var row = new LayerAssignmentRow(
            Order: 5, Slot: LayerSlot.Finish, SlotName: LayerSlotNames.Of(LayerSlot.Finish),
            MaterialName: "せっこうボード", ThicknessMillimeters: 12.5m,
            CurrentProductId: string.Empty, SelectedProductId: "BD-YOSHINO-01");

        Assert.AreEqual("Product_ID（仕上）", row.SlotName);
        Assert.AreEqual("BD-YOSHINO-01", row.SelectedProductId);
    }

    [TestMethod]
    public void 表は溢れた層の警告を持てる()
    {
        var view = new LayerAssignmentView(Array.Empty<LayerAssignmentRow>(), "層が8あります。");

        StringAssert.Contains(view.LayerWarning, "8");
    }

    [TestMethod]
    public void 付与範囲は2種類だけ()
    {
        var values = Enum.GetValues<ProductAssignmentTarget>();

        CollectionAssert.AreEquivalent(
            new[] { ProductAssignmentTarget.SameTypeAll, ProductAssignmentTarget.SelectionOnly },
            values);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter LayerAssignmentContractTests
```

期待: ビルドエラー（型が存在しない）。

- [ ] **Step 3: 契約を書く**

`BuiViewContracts_画面連携契約.cs` の末尾へ追加する:

```csharp
/// <summary>層とパラメータの対応を1行で表します。画面はこの形だけを見ます。</summary>
public sealed record LayerAssignmentRow(
    int Order,
    Bui.Assemblies.LayerSlot Slot,
    /// <summary>書き込み先のパラメータ名。利用者が実行前に確認するための情報です。</summary>
    string SlotName,
    string MaterialName,
    decimal ThicknessMillimeters,
    string CurrentProductId,
    string SelectedProductId);

/// <summary>層割当の表と、割り当てられなかった層の説明です。</summary>
public sealed record LayerAssignmentView(IReadOnlyList<LayerAssignmentRow> Rows, string LayerWarning);

/// <summary>Product_IDを付ける範囲です。</summary>
public enum ProductAssignmentTarget
{
    /// <summary>プロジェクト全体にある、選択要素と同じタイプの要素すべて。</summary>
    SameTypeAll,

    /// <summary>Revitで選択中の要素だけ。</summary>
    SelectionOnly
}

/// <summary>
/// 層ごとのProduct_ID付与の窓口です。画面はこの契約だけを見るため、
/// Revitの型が画面側へ漏れません。テストでは偽実装に差し替えられます。
/// </summary>
public interface ILayerAssignmentService
{
    /// <summary>選択要素の複合レイヤーを読み、どの層がどのパラメータへ入るかを返します。</summary>
    Task<OperationResult<LayerAssignmentView>> LoadLayersAsync(
        SelectionSnapshot selection, CancellationToken cancellationToken);

    /// <summary>書き込む内容を確認するためのプレビューを作ります。書き込みは行いません。</summary>
    Task<OperationResult<IdentifierWorkflowPreview>> PreviewLayerAssignmentAsync(
        SelectionSnapshot selection,
        IReadOnlyList<LayerAssignmentRow> rows,
        ProductAssignmentTarget target,
        CancellationToken cancellationToken);
}
```

必要な `using Bui.Common;`（`OperationResult`）が無ければ追加する。

- [ ] **Step 4: Revit側の実装をつなぐ**

`RevitIdentifierWorkflowService_ID付与操作.cs` に `ILayerAssignmentService` を実装させる。
`LoadLayersAsync` は次の順で行う。

1. `CompoundStructureReader.Read` で層と `InteriorSide` を得る
2. `LayerSlotMapper.Map(layers, interiorSide)` で割当を作る
3. 各層の現在の `Product_ID（…）` の値を読み、`LayerAssignmentRow` を作る
4. `plan.Warning` を `LayerAssignmentView.LayerWarning` に載せる

複合構造を持たない要素（`Read` が `ASSEMBLY_HOST_NOT_SUPPORTED` を返す）の場合は、
**1行だけ**（`LayerSlot.Finish`、材料名は要素のタイプ名、厚さ0）の表を返す。
設計 §8「複合構造を持たない要素 … 仕上の1つだけを扱う」に合わせる。

`PreviewLayerAssignmentAsync` は次の順で行う。

1. `target` に応じて対象要素を集める
   （`SameTypeAll` → `ProductIdTargetScope.FullProject` ＋ 同タイプ絞込み、
   `SelectionOnly` → `ProductIdTargetScope.Selection`）
2. 対象要素それぞれの層IDの並びを作り、`AssemblyIdEligibility.Evaluate` に渡す
3. 採番してよければ既存の `AssemblyIdAllocator` でIDを作る。だめなら空文字と理由
4. `LayerProductIdPreview` を作り、`IdentifierWorkflowPreview` の `Summary` に
   次を入れる

```
対象: {件数}件（{同一タイプすべて|選択部材のみ}）
Product_ID（仕上）    : BD-YOSHINO-01
Product_ID（下地①）  : （未選択）
Assembly_ID          : BM_ASM-0007
```

`LayerWarning` と `AssemblyIdSkipReason` があれば、その下へ続けて出す。

**Summaryは利用者が実行前に見る唯一の情報である。**
どの層がどのパラメータへ入るかを必ず含めること。

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter LayerAssignmentContractTests
```

期待: 3件PASS。

- [ ] **Step 6: Bui.Addin のビルドと全テスト**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

期待: ビルドはRevit参照エラーのみ。テストは失敗0件。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト"
git commit -m "feat: contract for per-layer assignment and its Revit implementation"
```

---

## Task 8: 画面状態に層割当と付与範囲を持たせる

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiDockPaneComposition_画面構成.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/LayerAssignmentViewModelTests_層割当画面テスト.cs`

**Interfaces:**
- Consumes: Task 7の `ILayerAssignmentService`、`LayerAssignmentRow`、`LayerAssignmentView`、`ProductAssignmentTarget`
- Produces:
  - コンストラクタ末尾に `ILayerAssignmentService? layerAssignment = null` を追加
  - `public ObservableCollection<LayerAssignmentRowViewModel> LayerRows { get; }`
  - `public LayerAssignmentRowViewModel? SelectedLayerRow { get; set; }`
  - `public string LayerWarning { get; private set; }`
  - `public ProductAssignmentTarget AssignmentTarget { get; set; }`（既定 `SameTypeAll`）
  - `public AsyncCommand ApplySelectedCandidateToLayerCommand { get; }`
  - `public AsyncCommand PreviewLayerAssignmentCommand { get; }`
  - `LayerAssignmentRowViewModel` … `Order` / `SlotName` / `MaterialName` / `ThicknessMillimeters` / `CurrentProductId` / `SelectedProductId`（`SelectedProductId` だけ変更通知が要る）

**要点:**
- 選択が変わったら `LoadLayersAsync` を呼び、`LayerRows` を作り直す
- 候補一覧で製品を選び、層の行を選んで `ApplySelectedCandidateToLayerCommand` を押すと、
  その行の `SelectedProductId` に候補のProduct_IDが入る
- `_layerAssignment` が null でも例外にしない。パネルが開かなくなる事故を繰り返さない

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/UI_画面/LayerAssignmentViewModelTests_層割当画面テスト.cs`:

```csharp
// このファイルの目的:
//   層割当の画面状態を固定します。
//   特に「候補を選んで層へ入れる」操作と、窓口が未設定でも落ちないことを守ります。
// 修正時の影響:
//   窓口がnullで例外を投げると、BUIパネルが開かなくなります。過去に2回起きています。
using Bui.Assemblies;
using Bui.Common;
using Bui.Products;
using Bui.UI;

namespace Bui.Tests.UI;

[TestClass]
public class LayerAssignmentViewModelTests_層割当画面テスト
{
    private static LayerAssignmentView TwoLayers() => new(
        new[]
        {
            new LayerAssignmentRow(5, LayerSlot.Finish, LayerSlotNames.Of(LayerSlot.Finish),
                "せっこうボード", 12.5m, string.Empty, string.Empty),
            new LayerAssignmentRow(0, LayerSlot.Substrate1, LayerSlotNames.Of(LayerSlot.Substrate1),
                "ALCパネル", 100m, "ALC-AK-01", string.Empty)
        },
        string.Empty);

    private sealed class FakeLayerService(LayerAssignmentView view) : ILayerAssignmentService
    {
        public Task<OperationResult<LayerAssignmentView>> LoadLayersAsync(
            SelectionSnapshot selection, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<LayerAssignmentView>.Success(view));

        public Task<OperationResult<IdentifierWorkflowPreview>> PreviewLayerAssignmentAsync(
            SelectionSnapshot selection, IReadOnlyList<LayerAssignmentRow> rows,
            ProductAssignmentTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<IdentifierWorkflowPreview>.Success(
                new("token", IdentifierWorkflowKind.InstanceProductId, "確認", $"{rows.Count}層 / {target}", false)));
    }

    [TestMethod]
    public async Task 選択すると層の表ができる()
    {
        var sut = PaneTestDoubles.CreateViewModel(layerAssignment: new FakeLayerService(TwoLayers()));

        await sut.RefreshSelectionAsync(new SelectionSnapshot(true, new long[] { 10 }, false, false));

        Assert.AreEqual(2, sut.LayerRows.Count);
        Assert.AreEqual("Product_ID（仕上）", sut.LayerRows[0].SlotName);
        Assert.AreEqual("ALC-AK-01", sut.LayerRows[1].CurrentProductId);
    }

    [TestMethod]
    public async Task 候補を選んで層へ入れられる()
    {
        var sut = PaneTestDoubles.CreateViewModel(layerAssignment: new FakeLayerService(TwoLayers()));
        await sut.RefreshSelectionAsync(new SelectionSnapshot(true, new long[] { 10 }, false, false));
        sut.SelectedCandidate = PaneTestDoubles.Product("BD-YOSHINO-01");
        sut.SelectedLayerRow = sut.LayerRows[0];

        await sut.ApplySelectedCandidateToLayerAsync();

        Assert.AreEqual("BD-YOSHINO-01", sut.LayerRows[0].SelectedProductId);
        // 他の行は巻き添えにしません。
        Assert.AreEqual(string.Empty, sut.LayerRows[1].SelectedProductId);
    }

    [TestMethod]
    public async Task 層を選ばずに入れようとすると案内を出す()
    {
        var sut = PaneTestDoubles.CreateViewModel(layerAssignment: new FakeLayerService(TwoLayers()));
        await sut.RefreshSelectionAsync(new SelectionSnapshot(true, new long[] { 10 }, false, false));
        sut.SelectedCandidate = PaneTestDoubles.Product("BD-YOSHINO-01");

        await sut.ApplySelectedCandidateToLayerAsync();

        StringAssert.Contains(sut.StatusMessage, "層");
    }

    [TestMethod]
    public async Task 付与範囲の既定は同一タイプすべて()
    {
        var sut = PaneTestDoubles.CreateViewModel(layerAssignment: new FakeLayerService(TwoLayers()));

        Assert.AreEqual(ProductAssignmentTarget.SameTypeAll, sut.AssignmentTarget);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task 窓口が未設定でもパネルは動く()
    {
        var sut = PaneTestDoubles.CreateViewModel();

        await sut.RefreshSelectionAsync(new SelectionSnapshot(true, new long[] { 10 }, false, false));

        Assert.AreEqual(0, sut.LayerRows.Count);
        Assert.AreEqual(1, sut.Selection.SelectedCount);
    }
}
```

`PaneTestDoubles.CreateViewModel` に `ILayerAssignmentService? layerAssignment = null` の
引数を足す（`tests_テスト/Bui.Tests/UI_画面/PaneTestDoubles_画面テスト用ダミー.cs`）。

- [ ] **Step 2: テストが失敗することを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter LayerAssignmentViewModelTests
```

期待: ビルドエラー。

- [ ] **Step 3: 画面状態を足す**

`BuiDockPaneViewModel_パネル状態.cs` へ追加する。コンストラクタ引数は**末尾**に足し、
既存の呼び出し側を壊さない。

```csharp
    /// <summary>層の1行です。選んだIDだけが画面から変わります。</summary>
    public sealed class LayerAssignmentRowViewModel : ObservableObject
    {
        private string _selectedProductId = string.Empty;

        public LayerAssignmentRowViewModel(LayerAssignmentRow row)
        {
            Order = row.Order;
            SlotName = row.SlotName;
            MaterialName = row.MaterialName;
            ThicknessMillimeters = row.ThicknessMillimeters;
            CurrentProductId = row.CurrentProductId;
            Slot = row.Slot;
            _selectedProductId = row.SelectedProductId;
        }

        public int Order { get; }
        public Bui.Assemblies.LayerSlot Slot { get; }
        public string SlotName { get; }
        public string MaterialName { get; }
        public decimal ThicknessMillimeters { get; }
        public string CurrentProductId { get; }

        public string SelectedProductId
        {
            get => _selectedProductId;
            set => SetProperty(ref _selectedProductId, value);
        }
    }
```

```csharp
    public ObservableCollection<LayerAssignmentRowViewModel> LayerRows { get; } = new();

    private LayerAssignmentRowViewModel? _selectedLayerRow;
    public LayerAssignmentRowViewModel? SelectedLayerRow
    {
        get => _selectedLayerRow;
        set => SetProperty(ref _selectedLayerRow, value);
    }

    private string _layerWarning = string.Empty;
    /// <summary>割り当てられなかった層の説明です。無ければ空文字。</summary>
    public string LayerWarning { get => _layerWarning; private set => SetProperty(ref _layerWarning, value); }

    private ProductAssignmentTarget _assignmentTarget = ProductAssignmentTarget.SameTypeAll;
    /// <summary>
    /// 付与範囲です。既定は同一タイプすべて。設計上そちらが本来の使い方で、
    /// 1件だけ付けたいときに切り替えます。
    /// </summary>
    public ProductAssignmentTarget AssignmentTarget
    {
        get => _assignmentTarget;
        set => SetProperty(ref _assignmentTarget, value);
    }
```

```csharp
    /// <summary>
    /// 候補一覧で選んだ製品を、選んでいる層の行へ入れます。
    /// IDを手で打たせないための操作です。打ち間違いはそのままモデルへ書き込まれます。
    /// </summary>
    public async Task ApplySelectedCandidateToLayerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedCandidate is null)
        {
            await SetStatusAsync("先に候補一覧から製品を選んでください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedLayerRow is null)
        {
            await SetStatusAsync("入れる層を一覧から選んでください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var productId = SelectedCandidate.ProductId;
        var slotName = SelectedLayerRow.SlotName;
        await _ui.InvokeAsync(() =>
        {
            SelectedLayerRow.SelectedProductId = productId;
            StatusMessage = $"{slotName} に {productId} を入れました。";
        }, cancellationToken).ConfigureAwait(false);
    }
```

`RefreshSelectionAsync` の中、選択情報を反映した後に層の読み込みを足す。
`_layerAssignment` が null なら `LayerRows` を空にするだけで、例外にしない。

- [ ] **Step 4: 組み立てへつなぐ**

`BuiDockPaneComposition_画面構成.cs` で、`RevitIdentifierWorkflowService` の
インスタンスを `layerAssignment:` にも渡す（同じオブジェクトが両方の契約を実装する）。

- [ ] **Step 5: テストが通ることを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter LayerAssignmentViewModelTests
```

期待: 5件PASS。

- [ ] **Step 6: 全テストとビルド**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: テスト失敗0件、Revit以外のビルドエラー無し。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト"
git commit -m "feat: hold the per-layer assignment table and the target scope in the pane"
```

---

## Task 9: 検索タブに層の表と付与範囲を出す

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml`

**Interfaces:**
- Consumes: Task 8の `LayerRows` / `SelectedLayerRow` / `LayerWarning` / `AssignmentTarget` / `ApplySelectedCandidateToLayerCommand` / `PreviewLayerAssignmentCommand`
- Produces: なし

**この環境ではXAMLをコンパイルできない。**次を目視で確認する。

1. XMLとして整形式（`python -c "import xml.etree.ElementTree as ET; ET.parse(path)"`）
2. すべての `{Binding ...}` の名前が実在するメンバーであること（ソースを見て確かめる）
3. `<` `>` がバインド式や属性値の中に無いこと

- [ ] **Step 1: 付与範囲の選択を足す**

`<TextBlock Text="{Binding SelectedProductId, StringFormat=現在のProduct_ID: {0}}" .../>` の直後へ:

```xml
   <!-- 付与範囲。既定は同一タイプすべて。1件だけ付けたいときに切り替えます。 -->
   <TextBlock Text="付与範囲" Margin="0,6,0,2"/>
   <ComboBox SelectedValue="{Binding AssignmentTarget}" SelectedValuePath="Tag" Margin="0,0,0,6">
    <ComboBoxItem Content="同一タイプすべて" Tag="{x:Static ui:ProductAssignmentTarget.SameTypeAll}"/>
    <ComboBoxItem Content="選択部材のみ" Tag="{x:Static ui:ProductAssignmentTarget.SelectionOnly}"/>
   </ComboBox>
```

`UserControl` のタグへ `xmlns:ui="clr-namespace:Bui.UI"` が無ければ追加する
（`OverviewView_概要.xaml` に同じ宣言がある）。

- [ ] **Step 2: 層の表を足す**

「選択中の候補」の `Border` の直後、手順1のボタンの**前**へ:

```xml
   <!-- 層の割り当て：どの層がどのパラメータへ入るかを、書き込む前に必ず見せます。 -->
   <!-- 室内側がどちら端かはRevitのカテゴリで決まり、こちらでは検証できません。 -->
   <!-- 対応が意図と違えば、利用者がこの表で気づけるようにしています。 -->
   <TextBlock Text="層への割り当て" FontWeight="SemiBold" Margin="0,10,0,2"/>
   <TextBlock Text="{Binding LayerWarning}" TextWrapping="Wrap" Foreground="#FFB00020" Margin="0,0,0,4">
    <TextBlock.Style>
     <Style TargetType="TextBlock">
      <Setter Property="Visibility" Value="Visible"/>
      <Style.Triggers>
       <DataTrigger Binding="{Binding LayerWarning}" Value="">
        <Setter Property="Visibility" Value="Collapsed"/>
       </DataTrigger>
      </Style.Triggers>
     </Style>
    </TextBlock.Style>
   </TextBlock>
   <ListBox ItemsSource="{Binding LayerRows}" SelectedItem="{Binding SelectedLayerRow, Mode=TwoWay}"
            MaxHeight="180" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
    <ListBox.ItemTemplate>
     <DataTemplate>
      <StackPanel Margin="2,4">
       <TextBlock FontWeight="SemiBold" TextWrapping="Wrap">
        <Run Text="{Binding SlotName, Mode=OneWay}"/>
       </TextBlock>
       <TextBlock TextWrapping="Wrap" Foreground="#FF616161">
        <Run Text="順序 "/><Run Text="{Binding Order, Mode=OneWay}"/>
        <Run Text="  "/><Run Text="{Binding MaterialName, Mode=OneWay}"/>
        <Run Text="  "/><Run Text="{Binding ThicknessMillimeters, Mode=OneWay}"/><Run Text=" mm"/>
       </TextBlock>
       <TextBlock TextWrapping="Wrap">
        <Run Text="現在: "/><Run Text="{Binding CurrentProductId, Mode=OneWay}"/>
        <Run Text="  →  "/><Run Text="{Binding SelectedProductId, Mode=OneWay}"/>
       </TextBlock>
      </StackPanel>
     </DataTemplate>
    </ListBox.ItemTemplate>
   </ListBox>
   <Button Content="選んだ候補をこの層に入れる" Command="{Binding ApplySelectedCandidateToLayerCommand}"
           Margin="0,4,0,0"
           ToolTip="候補一覧で製品を選び、上の表で層を選んでから押します。IDを手で打つ必要はありません。"/>
```

- [ ] **Step 3: 手順1のボタンを層割当のプレビューへ差し替える**

変更前:

```xml
   <Button Content="手順1: 付与する内容を確認する" Command="{Binding AssignSelectedCandidateCommand}" Margin="0,6,0,0"
           ToolTip="ここではまだRevitに書き込みません。下の内訳を見てから手順2を押します。"/>
```

変更後:

```xml
   <Button Content="手順1: 付与する内容を確認する" Command="{Binding PreviewLayerAssignmentCommand}" Margin="0,6,0,0"
           ToolTip="ここではまだRevitに書き込みません。下の内訳を見てから手順2を押します。"/>
```

対象範囲を説明していた `※` の文言を次へ差し替える:

```xml
   <TextBlock TextWrapping="Wrap" Foreground="#FF616161" Margin="0,4,0,10"
              Text="※ 付与範囲で対象を選べます。件数と、どの層がどのパラメータへ入るかは手順1の内訳で確認できます。"/>
```

- [ ] **Step 4: XAMLが整形式であることを確認する**

```bash
python -c "import xml.etree.ElementTree as ET; ET.parse(r'BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml'); print('OK')"
```

期待: `OK`。

- [ ] **Step 5: バインド名がすべて実在することを確認する**

`BuiDockPaneViewModel_パネル状態.cs` と `LayerAssignmentRowViewModel` を見て、
追加したバインド（`AssignmentTarget` / `LayerWarning` / `LayerRows` / `SelectedLayerRow` /
`ApplySelectedCandidateToLayerCommand` / `PreviewLayerAssignmentCommand` /
`SlotName` / `Order` / `MaterialName` / `ThicknessMillimeters` / `CurrentProductId` /
`SelectedProductId`）が実在することを1つずつ確かめ、報告に列挙する。
**WPFのバインド失敗は無言で空欄になる。** 誤りは実行時まで表に出ない。

- [ ] **Step 6: 全テストとビルド**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: テスト失敗0件、Revit以外のビルドエラー無し。

- [ ] **Step 7: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml"
git commit -m "feat: show the layer-to-parameter table and the target scope in the search tab"
```

---

## Task 10: 概要タブを確認専用にする

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`

**Interfaces:**
- Consumes: なし
- Produces: なし（撤去のみ）

**撤去する順序が大事。** Task 9で検索タブに層付与ができてから外す。
先に外すと、層へIDを付ける手段が一時的に無くなる。

- [ ] **Step 1: 概要タブから付与のUIを外す**

`OverviewView_概要.xaml` から次をすべて削除する。

- 「ID付与」の見出し
- 「付与するProduct_ID」のラベル、候補プルダウン、「候補を読み込む」ボタン、直接入力の`TextBox`
- 「対象範囲」の`ComboBox`と「選択要素と同じタイプだけに限定する」の`CheckBox`とその注記
- 「Product_IDをプレビュー」「選択層の材料をプレビュー」「Assembly_IDをプレビュー」の3ボタン
- 「プレビュー内容」の見出しと`Border`
- 「プレビュー内容をRevitへ書き込む」「プレビューをリセット」のボタン

**残すもの**（表示だけ）:

- 製品カード一式
- 「現在の選択」以下の表示
- 「複合レイヤー」の一覧。ただし選択できる必要はないので `ListBox` のままでよい
- CSV接続・状態の表示

複合レイヤーの説明文を次へ差し替える:

```xml
   <TextBlock Text="この要素の複合レイヤーです。IDの付与は「検索」タブで行います。" TextWrapping="Wrap" Margin="0,2,0,4"/>
```

- [ ] **Step 2: 使われなくなった画面状態を整理する**

ViewModelから次を削除する。**他から参照されていないことを確認してから消す。**

- `RequestedProductId`
- `ProductAssignmentScope`（enumではなくプロパティ）
- `SameTypeOnly`
- `PreviewProductIdCommand` / `PreviewMaterialProductIdCommand` / `PreviewAssemblyIdCommand`
- `ResetIdentifierPreviewCommand` と `ResetIdentifierPreviewAsync`
- `AssignSelectedCandidateCommand` と `AssignSelectedCandidateAsync`

`SelectedCandidate` のsetterにある「`RequestedProductId` へ入れる」処理も削除する。
候補は層の行へ入れる操作（Task 8）に置き換わったため。

**削除でテストが落ちる場合、そのテストは撤去された機能を見ている。**
テストごと削除してよいが、**削除したテスト名を報告に列挙すること。**
判断に迷うものがあれば実装を止めて報告する。

`IdentifierPreviewText` / `PendingIdentifierPreview` / `HasPendingIdentifierPreview` /
`ApplyIdentifierPreviewCommand` は**検索タブの手順2で使い続けるので残す。**

材料へのProduct_ID付与（`MaterialProductIdService_材料製品ID処理.cs`、
`PreviewMaterialAsync`、`ApplyMaterialAsync`）は、層への付与に置き換わるため
**使われなくなる**（設計 §7）。ただしこのタスクでは**削除しない**。
Task 11まで終えて会社PCで層付与が動くことを確認してから消す方が安全である。
`SelectedLayer` を参照している箇所が他に無いかだけ確認し、
未使用になったことを報告に書く。

- [ ] **Step 3: XAMLが整形式であることを確認する**

```bash
python -c "import xml.etree.ElementTree as ET; ET.parse(r'BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml'); print('OK')"
```

期待: `OK`。

- [ ] **Step 4: 参照が残っていないことを確認する**

```bash
grep -rn "RequestedProductId\|SameTypeOnly\|PreviewProductIdCommand\|PreviewMaterialProductIdCommand\|PreviewAssemblyIdCommand\|ResetIdentifierPreviewCommand\|AssignSelectedCandidateCommand" --include=*.cs --include=*.xaml "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト" | grep -v "/obj/"
```

期待: 何も出ない。

- [ ] **Step 5: 全テストとビルド**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: テスト失敗0件、Revit以外のビルドエラー無し。

- [ ] **Step 6: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/tests_テスト"
git commit -m "refactor: make the overview tab read-only and move assignment to search"
```

---

## Task 11: 点検タブと手順書を新しい形に合わせる

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/Audit/IdentifierAuditService_ID点検.cs`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt`
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`

**Interfaces:**
- Consumes: Task 1の `Identifiers.LayerProductIdParameterGuids` / `LayerProductIdParameterNames`
- Produces: なし

- [ ] **Step 1: 点検が6つのパラメータを見るようにする**

`IdentifierAuditService_ID点検.cs` で `Identifiers.ProductIdParameterGuid` を
読んでいる箇所を、6つのパラメータを順に見る形へ変える。

報告の粒度は、**要素ごとに「どのスロットが空か」**とする。例:

```
Product_ID未設定: 壁 [12345] の Product_ID（下地①）
```

すべての層が空の要素は1件にまとめ、次のように出す。

```
Product_ID未設定: 壁 [12345]（すべての層）
```

旧 `Product_ID`（`569e8df1-…`）は**点検しない。** BUIが書かなくなったため、
未設定であることは異常ではない。

- [ ] **Step 2: 手順書を書き直す**

`操作手順_会社PC_次にやること.txt`。**UTF-8 BOM付き・CRLF**を保つ。
段階の骨格は変えず、次の点を反映する。

- 段階3の後に「**ID初期設定を実行する**」段階を足す。6つのパラメータが
  作られること、旧 `Product_ID` は残るが使わないことを書く
- 段階8を層付与の手順へ書き直す
  1. 要素を選ぶ
  2. 付与範囲を選ぶ（既定は同一タイプすべて）
  3. 絞り込んで候補を選ぶ
  4. 層の表で行を選び「選んだ候補をこの層に入れる」
  5. 4を層の数だけ繰り返す
  6. 手順1で内訳を確認 → 手順2で書き込む
- 確認ポイントに次を必ず入れる
  - **層の表に出る「どのパラメータへ入るか」が意図どおりか**
    （仕上が室内側の層になっているか。ここは私物PCで検証できていない）
  - プロパティの「識別情報」に6つのパラメータが出ること
  - **タイププロパティ**に Assembly_ID が出ること
  - Ctrl+Z 1回で層のIDがまとめて戻ること
- 概要タブは確認専用になったことを書く

- [ ] **Step 3: Troubleshootingへ項目を足す**

次の見出しを追加する。

- 「層のIDが意図と違うパラメータに入る」…
  室内側の判定がカテゴリごとに違うこと、手順1の表で確認してから実行すること、
  違っていた場合は表の内容を添えて連絡してほしいこと
- 「Assembly_IDが付かない」…
  空欄の層がある場合と、同一タイプ内でIDが揃っていない場合の2つ。
  それぞれ画面に出る文言と対処
- 「旧Product_IDに入っている値はどうなるのか」…
  BUIは読み書きしないこと、モデルからは消していないこと、
  付け直しが必要なこと、不要ならRevit側で手動削除できること

既存の「同じタイプの要素すべてに同じIDを付けたい」の節を、
新しい付与範囲の選択に合わせて書き直す。概要タブでの手順は削除する。

- [ ] **Step 4: 文書の形式を確認する**

```bash
python -c "
for p in [r'BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt',
          r'BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md']:
    b=open(p,'rb').read(); s=b.decode('utf-8-sig')
    print(p, 'BOM' if b[:3]==b'\xef\xbb\xbf' else 'no-BOM',
          'CRLF-only' if s.count(chr(13)+chr(10))==s.count(chr(10)) else 'mixed',
          'ctrl:', sorted({hex(ord(c)) for c in s if ord(c)<32 and c not in chr(13)+chr(10)+chr(9)}))
"
```

期待: `.txt` は `BOM` かつ `CRLF-only`、制御文字は空。

- [ ] **Step 5: 全テストとビルド**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

期待: テスト失敗0件、Revit以外のビルドエラー無し。

- [ ] **Step 6: commit**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース" "BUI_Addin_CSharpアドイン/docs_説明書"
git commit -m "docs: rewrite the walkthrough for per-layer assignment"
```

---

## 最終確認（全タスク完了後）

- [ ] **全テストを実行し、終了コードで判定する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"
```

- [ ] **Revit参照以外のビルドエラーが無いことを確認する**

```bash
./.superpowers/dotnet-sdk-8/dotnet.exe build "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" 2>&1 | grep -E "error CS" | grep -v "CS0246\|CS0103\|CS0234\|CS1061\|CS0518\|CS0012\|CS7069"
```

- [ ] **GUIDが計画どおりであることを確認する**

```bash
grep -c "e10c429c-9ee1-4813-9e59-9178991af83a\|3fcefbeb-ddcc-4f74-bd90-d8be05baf555\|ba9206e7-d7b1-47aa-894f-1eb71ceb4c53\|0cef352d-edbb-441c-9bd8-43ecf239eb4d\|5b008bd1-2630-4295-ab09-f7cf4cdbf764\|79b24248-89df-460d-bbcc-95af64515a63" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/Identifiers_固定GUID.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/SharedParameters_共有パラメータ.txt"
```

期待: どちらのファイルも 6。

- [ ] **競合マーカーと制御文字が無いことを確認する**

```bash
git grep -n -E "^(<<<<<<<|>>>>>>>)" -- .
python -c "
import subprocess
files=subprocess.check_output(['git','ls-files','-z']).decode('utf-8').split(chr(0))
bad=0
for f in files:
    if not f or f.lower().endswith(('.xlsx','.png','.jpg','.dll','.sqlite','.vsidx','.wsuo','.suo','.ico')): continue
    try: s=open(f,'rb').read().decode('utf-8-sig')
    except Exception: continue
    h=sorted({hex(ord(c)) for c in s if ord(c)<32 and c not in chr(13)+chr(10)+chr(9)})
    if h: print(f,h); bad+=1
print('control-char files:',bad)
"
```

期待: どちらも何も出ない（`control-char files: 0`）。

- [ ] **手順書を利用者へ渡す**

`docs_説明書/操作手順_会社PC_次にやること.txt` を SendUserFile で渡す。
**層とパラメータの対応（仕上がどの層か）はこちらで検証できていない**ことを明示し、
手順1の表で必ず確認してもらう。
