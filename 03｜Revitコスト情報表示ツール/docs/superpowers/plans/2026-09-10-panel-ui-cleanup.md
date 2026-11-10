# パネル表示の整理 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 添付PDF（`BUI_Addin_CSharpアドイン/UI修正内容.pdf`）で指摘された4点の表示上の問題を直す。

**Architecture:** 整形の判断（折り返し・末尾0の除去・レイヤー番号の文字列）は `Bui.Shared_共有ロジック`（Revit非依存）の純粋関数として実装し、単体テストで固める。`Bui.Addin` 側のXAMLとリボンには「どれを出すか」だけを残す。私物PCでは `Bui.Addin` をコンパイルできないため、テストできる範囲を最大にする。

**Tech Stack:** .NET 8 / C# 12 / WPF / MSTest / Windows PowerShell 5.1

**設計:** `docs/superpowers/specs/2026-09-10-panel-ui-cleanup-design.md`

## Global Constraints

- `Bui.Shared_共有ロジック` は Revit に依存しない。`Autodesk.Revit.*` を参照するコードを一切書かない
- `Bui.Addin` は私物PCでコンパイルできない（Revitのアセンブリが無い）。**目視確認どまりであることを報告に明記する**
- 全ファイルUTF-8。日本語のファイル名・クラス名・テストメソッド名の既存の付け方に合わせる
- 各ソースファイル冒頭の「このファイルの目的:」「なぜ〜のか:」「修正時の影響:」のコメント様式を保つ
- `.ps1` は**UTF-8 BOM付き**。BOMを外すと Windows PowerShell 5.1 が Shift_JIS として読み、日本語が壊れて構文エラーになる
- 表示幅は全角2桁・半角1桁で数える。`char` 数では数えない
- **丸めは行わない。**落とすのは小数部の末尾の0だけ。`0.225` を `0.23` にしない
- `Order`（層の添字）そのものは変えない。表示だけ1始まりにする
- テストは**終了コードで判定する**。出力を `tail` や `head` へパイプしない
- テスト実行コマンド（リポジトリルート `C:\Users\anynu\OneDrive\ドキュメント\github\Cost_Maintenance_Display-Project` から）:
  `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
- 開始時点のテスト件数は **596件、終了コード0**

---

## File Structure

| 区分 | パス | 責務 |
| --- | --- | --- |
| 新規 | `src_ソース/Bui.Shared_共有ロジック/Common_共通/DisplayWidthWrapper_表示幅折返し.cs` | 表示幅で数えて折り返す純粋関数 |
| 変更 | `src_ソース/Bui.Addin/Revit_連携/SharedParameters/InstallIdentifiersCommand_ID初期設定.cs` | 詳細テキストを折り返してから `ExpandedContent` へ渡す |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/SpecValueFormatter_属性値整形.cs` | 小数部の末尾0を落とす |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiViewContracts_画面連携契約.cs` | `LayerLabel` を2つのレコードへ追加 |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs` | 検索タブの層ViewModelへ `LayerLabel` を追加 |
| 変更 | `src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml` | 行の削除と `LayerLabel` への差し替え |
| 変更 | `src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml` | 同上 |
| 変更 | `src_ソース/Bui.Addin/Application_アプリ起動/RibbonService_リボン作成.cs` | ボタン名・区切り線・アイコン |
| 新規 | `src_ソース/Bui.Addin/Resources_画像と定義/リボン_0*.ico`（3点） | リボンのアイコン |
| 変更 | `src_ソース/Bui.Addin/Bui.Addin.csproj` | アイコンを出力へコピー |
| 変更 | `scripts_開発補助/DeployDev_開発用配置.ps1` | 許可リストへアイコンを追加 |
| 変更 | `scripts_開発補助/CheckDeploy_配置診断.ps1` | アイコンが配置先に届いたかを確認 |
| 新規 | `tests_テスト/Bui.Tests/Common_共通/DisplayWidthWrapperTests_表示幅折返しテスト.cs` | 折り返しのテスト |
| 変更 | `tests_テスト/Bui.Tests/Data_データ/SpecValueFormatterTests_属性値整形テスト.cs` | 末尾0のテスト |
| 変更 | `tests_テスト/Bui.Tests/UI_画面/` の該当テスト | `LayerLabel` のテスト |
| 変更 | `docs_説明書/操作手順_会社PC_次にやること.txt` | 段階13を追加 |
| 変更 | `docs_説明書/Troubleshooting_問題解決.md` | アイコンが出ないときの節 |

---

### Task 1: ID初期設定ダイアログの幅

`TaskDialog` は折りたたまれた詳細も含めていちばん長い行に幅を合わせる。詳細テキストを表示幅で折り返して、ダイアログを本文の幅に収める。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Common_共通/DisplayWidthWrapper_表示幅折返し.cs`
- Create: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Common_共通/DisplayWidthWrapperTests_表示幅折返しテスト.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/SharedParameters/InstallIdentifiersCommand_ID初期設定.cs`（`ShowReport` の `ExpandedContent` を渡している行）

**Interfaces:**
- Consumes: なし
- Produces:
  - `namespace Bui.Common;`
  - `public static class DisplayWidthWrapper`
    - `public const int DefaultColumns = 60;`
    - `public static int MeasureWidth(string? text);` — 全角2・半角1で数えた表示桁
    - `public static string Wrap(string? text, int columns = DefaultColumns);` — 折り返した文字列

- [x] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Common_共通/DisplayWidthWrapperTests_表示幅折返しテスト.cs` を新規作成する。

```csharp
// このファイルの目的:
//   ID初期設定の結果ダイアログが画面いっぱいに広がらないよう、
//   詳細テキストが表示幅で折り返されることを守ります。
// なぜ必要か:
//   RevitのTaskDialogは折りたたまれた詳細も含めて、いちばん長い行に幅を合わせます。
//   幅を指定するAPIが無いため、渡す文字列の側で抑えるしかありません。
//   char数で数えると日本語の行が半分の幅にしかならないため、表示桁で数えます。
using Bui.Common;

namespace Bui.Tests.Common_共通;

[TestClass]
public class DisplayWidthWrapperTests_表示幅折返しテスト
{
    [TestMethod]
    public void 半角は1桁で数える()
    {
        Assert.AreEqual(10, DisplayWidthWrapper.MeasureWidth("Product_ID"));
    }

    [TestMethod]
    public void 全角は2桁で数える()
    {
        Assert.AreEqual(8, DisplayWidthWrapper.MeasureWidth("カテゴリ"));
    }

    [TestMethod]
    public void 混在した行を正しく数える()
    {
        // "ID" が2桁、"の設定" が6桁。
        Assert.AreEqual(8, DisplayWidthWrapper.MeasureWidth("IDの設定"));
    }

    [TestMethod]
    public void 空文字と未指定は0桁()
    {
        Assert.AreEqual(0, DisplayWidthWrapper.MeasureWidth(""));
        Assert.AreEqual(0, DisplayWidthWrapper.MeasureWidth(null));
    }

    [TestMethod]
    public void 上限に収まる行はそのまま返す()
    {
        Assert.AreEqual("・[追加] 壁: 登録しました", DisplayWidthWrapper.Wrap("・[追加] 壁: 登録しました", 60));
    }

    [TestMethod]
    public void どの行も上限を超えない()
    {
        var line = new string('あ', 100);

        var wrapped = DisplayWidthWrapper.Wrap(line, 20);

        foreach (var part in wrapped.Split('\n'))
        {
            Assert.IsTrue(
                DisplayWidthWrapper.MeasureWidth(part) <= 20,
                $"上限20桁を超えた行があります: 『{part}』は{DisplayWidthWrapper.MeasureWidth(part)}桁。");
        }
    }

    [TestMethod]
    public void 折り返した続きの行は字下げする()
    {
        // 字下げが無いと、1件が2行に割れたのか2件並んでいるのか読み手に判別できません。
        var wrapped = DisplayWidthWrapper.Wrap(new string('あ', 30), 20);
        var parts = wrapped.Split('\n');

        Assert.IsTrue(parts.Length >= 2, "折り返されていること。");
        Assert.IsFalse(parts[0].StartsWith(" ", StringComparison.Ordinal), "1行目は字下げしないこと。");
        Assert.IsTrue(parts[1].StartsWith("  ", StringComparison.Ordinal), "続きの行は2桁ぶん字下げすること。");
    }

    [TestMethod]
    public void 元の改行を保つ()
    {
        var wrapped = DisplayWidthWrapper.Wrap("短い行1\n短い行2", 60);

        Assert.AreEqual("短い行1\n短い行2", wrapped);
    }

    [TestMethod]
    public void 全角文字を途中で切らない()
    {
        // 境界に全角がまたがる場合、その文字ごと次の行へ送ります。
        // "あ"3文字＝6桁を5桁で折ると、1行目は"ああ"（4桁）まで。
        var wrapped = DisplayWidthWrapper.Wrap("あああ", 5);
        var parts = wrapped.Split('\n');

        Assert.AreEqual("あああ".Substring(0, 2), parts[0]);
    }

    [TestMethod]
    public void 上限が0以下なら折り返さない()
    {
        // 無限ループを作らないための歯止めです。
        Assert.AreEqual("あああ", DisplayWidthWrapper.Wrap("あああ", 0));
        Assert.AreEqual("あああ", DisplayWidthWrapper.Wrap("あああ", -1));
    }

    [TestMethod]
    public void 空文字はそのまま返す()
    {
        Assert.AreEqual(string.Empty, DisplayWidthWrapper.Wrap("", 60));
        Assert.AreEqual(string.Empty, DisplayWidthWrapper.Wrap(null, 60));
    }

    [TestMethod]
    public void サロゲートペアを2桁で数え途中で切らない()
    {
        // カテゴリ名に現れることはありませんが、char単位で走査すると
        // 折り返し位置がずれ、壊れた文字が画面に出ます。
        const string emoji = "\U0001F600";  // 😀

        Assert.AreEqual(2, DisplayWidthWrapper.MeasureWidth(emoji));

        var wrapped = DisplayWidthWrapper.Wrap(emoji + emoji + emoji, 5);
        foreach (var part in wrapped.Split('\n'))
        {
            Assert.IsFalse(
                char.IsHighSurrogate(part[part.Length - 1]),
                "サロゲートペアを半端に切らないこと。");
        }
    }
}
```

- [x] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~表示幅折返しテスト"`
Expected: コンパイルエラー。`DisplayWidthWrapper` が存在しない。

- [x] **Step 3: 実装する**

`src_ソース/Bui.Shared_共有ロジック/Common_共通/DisplayWidthWrapper_表示幅折返し.cs` を新規作成する。

```csharp
// このファイルの目的:
//   文字列を「表示幅」で折り返します。char数ではなく、画面で占める桁数で数えます。
//
// なぜchar数で数えないのか:
//   「カテゴリ」は4文字ですが画面では8桁を占めます。char数で切ると、
//   日本語だけの行が想定の半分の幅にしかならず、折り返しの意味がなくなります。
//
// 修正時の影響:
//   RevitのTaskDialogは、折りたたまれている詳細も含めていちばん長い行に幅を合わせます。
//   幅を指定するAPIが無いため、ここで抑えるのが唯一の手段です。
//   上限を緩めると、ID初期設定の結果ウィンドウがまた画面いっぱいに広がります。

using System.Globalization;
using System.Text;

namespace Bui.Common;

/// <summary>表示幅で折り返す純粋関数です。</summary>
public static class DisplayWidthWrapper
{
    /// <summary>
    /// 既定の折り返し幅です。ID初期設定ダイアログの本文でいちばん長い行
    /// 「Product_IDとAssembly_IDの初期設定が完了しました。」が49桁のため、
    /// 余白とアイコンの幅を含めてほぼ同じ見た目になります。
    /// </summary>
    public const int DefaultColumns = 60;

    /// <summary>折り返した続きの行の字下げです。1件が2行に割れたことを見て分かるようにします。</summary>
    private const string ContinuationIndent = "  ";

    /// <summary>全角2桁・半角1桁で数えた表示桁を返します。</summary>
    public static int MeasureWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var width = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            width += ElementWidth((string)enumerator.Current);
        }

        return width;
    }

    /// <summary>
    /// 表示幅で折り返します。元の改行は保ち、1行ずつ独立して折り返します。
    /// 日本語には単語の切れ目が無いため、単語単位ではなく文字単位で切ります。
    /// </summary>
    public static string Wrap(string? text, int columns = DefaultColumns)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 続きの行には字下げ（ContinuationIndent.Length）に加えて、
        // 全角文字1つ分（幅2）は入る余地が要ります。
        // それより狭いと、折り返した続きの行が要求した桁数を超えてしまいます
        // （例: columns=3 だと続きの行の上限は1桁になり、全角文字が1つも収まらない）。
        // 文字を分割することはできないため、この場合は折り返さずそのまま返します。
        if (columns < ContinuationIndent.Length + 2) return text;

        var result = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) result.Append('\n');
            AppendWrappedLine(result, lines[index], columns);
        }

        return result.ToString();
    }

    private static void AppendWrappedLine(StringBuilder result, string line, int columns)
    {
        if (line.Length == 0) return;

        var isContinuation = false;
        var used = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(line);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var elementWidth = ElementWidth(element);
            var limit = isContinuation ? columns - ContinuationIndent.Length : columns;

            // 境界をまたぐ文字は、半端に切らずその文字ごと次の行へ送ります。
            if (used > 0 && used + elementWidth > limit)
            {
                result.Append('\n').Append(ContinuationIndent);
                isContinuation = true;
                used = 0;
            }
            else if (used == 0 && isContinuation && result.Length > 0)
            {
                // 続きの行の先頭。字下げは上の分岐で既に書かれています。
            }

            result.Append(element);
            used += elementWidth;
        }
    }

    /// <summary>
    /// 1つのテキスト要素の表示桁です。East Asian Width の Wide / Fullwidth を2桁として扱います。
    /// .NETに幅を返すAPIが無いため、必要な範囲を明示します。
    /// </summary>
    private static int ElementWidth(string element)
    {
        if (element.Length == 0) return 0;

        var codePoint = char.ConvertToUtf32(element, 0);

        // サロゲートペア（絵文字など）は2桁として数えます。
        // 数え漏らすと折り返し位置がずれ、壊れた文字が画面に出ます。
        if (codePoint > 0xFFFF) return 2;

        return codePoint switch
        {
            >= 0x1100 and <= 0x115F => 2,   // ハングル字母
            >= 0x2E80 and <= 0x303E => 2,   // CJK部首補助・かな補助・CJK記号
            >= 0x3041 and <= 0x33FF => 2,   // ひらがな・カタカナ・ハングル・CJK互換
            >= 0x3400 and <= 0x4DBF => 2,   // CJK統合漢字拡張A
            >= 0x4E00 and <= 0x9FFF => 2,   // CJK統合漢字
            >= 0xA000 and <= 0xA4CF => 2,   // イ文字
            >= 0xAC00 and <= 0xD7A3 => 2,   // ハングル音節
            >= 0xF900 and <= 0xFAFF => 2,   // CJK互換漢字
            >= 0xFE30 and <= 0xFE6F => 2,   // CJK互換形・小字形
            >= 0xFF00 and <= 0xFF60 => 2,   // 全角英数・全角記号
            >= 0xFFE0 and <= 0xFFE6 => 2,   // 全角記号
            _ => 1
        };
    }
}
```

- [x] **Step 4: テストが通ることを確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~表示幅折返しテスト"`
Expected: PASS。

- [x] **Step 5: ダイアログへ配線する**

`InstallIdentifiersCommand_ID初期設定.cs` の `ShowReport` の中で、`ExpandedContent` を折り返して渡す。

```csharp
            // TaskDialogは折りたたまれた詳細も含めて、いちばん長い行に幅を合わせます。
            // 幅を指定するAPIが無いため、渡す文字列の側で抑えます。
            // 行を削らず折り返すので、詳細の情報は1文字も失いません。
            ExpandedContent = DisplayWidthWrapper.Wrap(BuildDetailedReport(report)),
```

ファイル先頭の `using` に `using Bui.Common;` を足す（既にあれば足さない）。

- [x] **Step 6: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [x] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Common_共通/DisplayWidthWrapper_表示幅折返し.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Common_共通/DisplayWidthWrapperTests_表示幅折返しテスト.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Revit_連携/SharedParameters/InstallIdentifiersCommand_ID初期設定.cs"
git commit -m "fix: wrap the setup report so the dialog matches its text width"
```

---

### Task 2: 数値の末尾の0を落とす

SQL Serverの列は小数10桁のため、`100.0000000000` と表示される。末尾の0を落とす。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/SpecValueFormatter_属性値整形.cs`（`FormatNumber`）
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SpecValueFormatterTests_属性値整形テスト.cs`

**Interfaces:**
- Consumes: なし
- Produces: `SpecValueFormatter.FormatNumber(string? raw)` の戻り値から小数部の末尾0が消える。シグネチャは変えない

- [x] **Step 1: 失敗するテストを書く**

`SpecValueFormatterTests_属性値整形テスト.cs` のクラス内に追加する。

```csharp
    [TestMethod]
    public void 小数部の末尾の0を落とす()
    {
        // SQL Serverの列は小数10桁のため、100.0000000000 という値が入ってきます。
        Assert.AreEqual("100", SpecValueFormatter.FormatNumber("100.0000000000"));
        Assert.AreEqual("2.5", SpecValueFormatter.FormatNumber("2.5000000000"));
    }

    [TestMethod]
    public void 有効な小数は残す()
    {
        // 0.22を0.2に丸めると別の値になります。落とすのは末尾の0だけです。
        Assert.AreEqual("0.22", SpecValueFormatter.FormatNumber("0.2200000000"));
        Assert.AreEqual("0.22", SpecValueFormatter.FormatNumber("0.22"));
        Assert.AreEqual("0.225", SpecValueFormatter.FormatNumber("0.225"));
    }

    [TestMethod]
    public void 末尾の0を落としても桁区切りは残す()
    {
        Assert.AreEqual("2,800", SpecValueFormatter.FormatNumber("2800.0000000000"));
    }

    [TestMethod]
    public void 数値として読めない値はそのまま返す()
    {
        // 実データのareal_weightには「8.1～11.7」という範囲文字列があります。
        Assert.AreEqual("8.1～11.7", SpecValueFormatter.FormatNumber("8.1～11.7"));
    }

    [TestMethod]
    public void 整数はそのまま返す()
    {
        Assert.AreEqual("12", SpecValueFormatter.FormatNumber("12"));
        Assert.AreEqual("0", SpecValueFormatter.FormatNumber("0"));
    }
```

同じファイルに同名のテストが既にある場合は、そちらを上の内容へ置き換える。

- [x] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~属性値整形テスト"`
Expected: FAIL。`100.0000000000` が `100.0000000000` のまま返る。

- [x] **Step 3: 実装する**

`FormatNumber` の小数桁数の決め方を差し替える。差し替えるのは次のブロック。

```csharp
        // 元の小数桁数を保ちます。0.22を0.2に丸めると別の値になります。
        var separatorIndex = value.IndexOf('.');
        var decimals = separatorIndex < 0 ? 0 : value.Length - separatorIndex - 1;
        return number.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
```

これを次にする。

```csharp
        // 意味のある小数桁だけを残します。SQL Serverの列は小数10桁のため、
        // 元の桁数をそのまま保つと 100.0000000000 と表示されます。
        // 丸めは行いません。0.22を0.2にすると別の値になります。落とすのは末尾の0だけです。
        var separatorIndex = value.IndexOf('.');
        var decimals = 0;
        if (separatorIndex >= 0)
        {
            var fraction = value.Substring(separatorIndex + 1).TrimEnd('0');
            decimals = fraction.Length;
        }

        return number.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
```

あわせてファイル冒頭のコメントブロックの末尾へ1段落足す。

```csharp
// なぜ末尾の0を落とすのか:
//   SQL Serverの列は小数10桁のため、CSVでは100.0だった値が100.0000000000になります。
//   桁数を固定して丸める案は採りません。0.225を0.23にすると別の値になります。
```

- [x] **Step 4: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。既存のテストも全部通ること。

- [x] **Step 5: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [x] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/GoldStandard/SpecValueFormatter_属性値整形.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SpecValueFormatterTests_属性値整形テスト.cs"
git commit -m "fix: drop the trailing zeros from spec values"
```

---

### Task 3: 「順序 0」を「レイヤー1」にする

概要タブと検索タブの両方で、層の見出しを1始まりの「レイヤーN」にする。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiViewContracts_画面連携契約.cs`（`CompoundLayerViewModel` と `LayerAssignmentRow`）
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiDockPaneViewModel_パネル状態.cs`（`Order` を持つ層のViewModelクラス。`public int Order { get; }` の直後）
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml`（`順序 {0}` の行）
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml`（`Run Text="順序 "` の行）
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/` に新規テストファイル
  `LayerLabelTests_レイヤー表記テスト.cs`

**Interfaces:**
- Consumes: なし
- Produces: 3つの型すべてに `public string LayerLabel => $"レイヤー{Order + 1}";`
  - `Bui.UI.CompoundLayerViewModel.LayerLabel`
  - `Bui.UI.LayerAssignmentRow.LayerLabel`
  - `BuiDockPaneViewModel` 内の層ViewModel（`Order` を持つクラス）の `LayerLabel`

- [x] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/UI_画面/LayerLabelTests_レイヤー表記テスト.cs` を新規作成する。

```csharp
// このファイルの目的:
//   層の見出しが「レイヤー1」から始まることを守ります。
// なぜ必要か:
//   Revitの層の添字は0始まりで、共有パラメータの箱の番号と対応しています。
//   内部の番号まで1始まりにすると書き込み先がずれるため、表示だけを1始まりにします。
//   ここが崩れると、利用者の見ている「レイヤー2」と書き込み先がずれます。
using Bui.Assemblies;
using Bui.UI;

namespace Bui.Tests.UI_画面;

[TestClass]
public class LayerLabelTests_レイヤー表記テスト
{
    [TestMethod]
    public void 概要タブの層は1始まりで表示する()
    {
        var layer = new CompoundLayerViewModel(0, "Finish2", 3m, "ALC-AK-05", "アクリル樹脂系左官仕上_躯体", false);

        Assert.AreEqual("レイヤー1", layer.LayerLabel);
        Assert.AreEqual(0, layer.Order, "内部の添字は0始まりのままであること。");
    }

    [TestMethod]
    public void 概要タブの2層目はレイヤー2になる()
    {
        var layer = new CompoundLayerViewModel(1, "Substrate", 12.5m, "", "せっこうボード", true);

        Assert.AreEqual("レイヤー2", layer.LayerLabel);
    }

    [TestMethod]
    public void 検索タブの層も1始まりで表示する()
    {
        var row = new LayerAssignmentRow(
            0, LayerSlot.Finish, "Product_ID（仕上）", "アクリル樹脂系左官仕上_躯体", 3m, "ALC-AK-05", "ALC-AK-05");

        Assert.AreEqual("レイヤー1", row.LayerLabel);
        Assert.AreEqual(0, row.Order, "内部の添字は0始まりのままであること。");
    }

    [TestMethod]
    public void 検索タブの3層目はレイヤー3になる()
    {
        var row = new LayerAssignmentRow(
            2, LayerSlot.Substrate2, "Product_ID（下地②）", "せっこうボード", 12.5m, "", "");

        Assert.AreEqual("レイヤー3", row.LayerLabel);
    }
}
```

**参考（確認済みの現物）:**

`LayerSlot`（`Assemblies_複合構造/LayerSlot_層スロット.cs`）は
`Finish = 0, Substrate1 = 1, Substrate2 = 2, Substrate3 = 3, Substrate4 = 4, Substrate5 = 5`。

引数の並び（`BuiViewContracts_画面連携契約.cs`）。

```csharp
CompoundLayerViewModel(
    int Order, string Function, decimal ThicknessMillimeters, string ProductId,
    string RevitMaterialName, bool HasMissingProductId,
    long MaterialElementId = -1, bool IsMaterialFallback = false)

LayerAssignmentRow(
    int Order, LayerSlot Slot, string SlotName, string MaterialName,
    decimal ThicknessMillimeters, string CurrentProductId, string SelectedProductId,
    string LayerFunction = "")
```

- [x] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~レイヤー表記テスト"`
Expected: コンパイルエラー。`LayerLabel` が存在しない。

- [x] **Step 3: レコードへ追加する**

`BuiViewContracts_画面連携契約.cs` の `CompoundLayerViewModel` を、本体付きのレコードにして追加する。

```csharp
public sealed record CompoundLayerViewModel(
    int Order,
    string Function,
    decimal ThicknessMillimeters,
    string ProductId,
    string RevitMaterialName,
    bool HasMissingProductId,
    long MaterialElementId = -1,
    bool IsMaterialFallback = false)
{
    /// <summary>
    /// 画面に出す層の見出しです。Revitの添字は0始まりですが、
    /// 「順序 0」は読み手に伝わりません。表示だけ1始まりにします。
    /// Orderそのものは変えません。共有パラメータの箱の番号と対応しており、
    /// 内部まで1始まりにすると書き込み先がずれます。
    /// </summary>
    public string LayerLabel => $"レイヤー{Order + 1}";
}
```

同じファイルの `LayerAssignmentRow` にも同じ本体を足す。

```csharp
    string LayerFunction = "")
{
    /// <summary>
    /// 画面に出す層の見出しです。概要タブと同じ規則で1始まりにします
    /// （CompoundLayerViewModel.LayerLabel と揃えてください。片方だけ直すと表記が食い違います）。
    /// </summary>
    public string LayerLabel => $"レイヤー{Order + 1}";
}
```

- [x] **Step 4: 検索タブのViewModelへ追加する**

`BuiDockPaneViewModel_パネル状態.cs` の `public int Order { get; }` を持つクラスの、その行の直後に足す。

```csharp
        public int Order { get; }

        /// <summary>
        /// 画面に出す層の見出しです。LayerAssignmentRow.LayerLabel と同じ規則で1始まりにします。
        /// </summary>
        public string LayerLabel => $"レイヤー{Order + 1}";
```

- [x] **Step 5: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [x] **Step 6: XAMLを差し替える**

`OverviewView_概要.xaml` の複合レイヤーの見出し行を差し替える。

差し替え前:
```xml
     <TextBlock Text="{Binding Order, StringFormat=順序 {0}}" FontWeight="SemiBold"/>
```

差し替え後:
```xml
     <TextBlock Text="{Binding LayerLabel}" FontWeight="SemiBold"/>
```

`SearchView_検索.xaml` の「層への割り当て」の見出し行を差し替える。

差し替え前:
```xml
        <Run Text="順序 "/><Run Text="{Binding Order, Mode=OneWay}"/>
```

差し替え後:
```xml
        <Run Text="{Binding LayerLabel, Mode=OneWay}"/>
```

- [x] **Step 7: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [x] **Step 8: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/LayerLabelTests_レイヤー表記テスト.cs"
git commit -m "feat: label compound layers from one instead of zero"
```

---

### Task 4: 概要タブと検索タブから不要な行を消す

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml`

**Interfaces:**
- Consumes: なし。ViewModelのプロパティはどれも消さない（他のタブが使っている）
- Produces: なし

**このタスクではC#を1行も変えない。**`SelectedProductId`・`DataConnectionText`・`DataUpdatedText`・
`DataProblemsText`・`StatusMessage` はいずれも他のタブが使っているため、プロパティは残す。

- [x] **Step 1: 概要タブから5行を消す**

`OverviewView_概要.xaml` の「現在の選択」の並びから、次の5行を**削除**する。

```xml
   <TextBlock Text="{Binding SelectedProductId, StringFormat=Product_ID: {0}}" TextWrapping="Wrap"/>
```
```xml
   <TextBlock Text="{Binding DataConnectionText, StringFormat=CSV接続: {0}}" Margin="0,8,0,0" TextWrapping="Wrap"/>
   <TextBlock Text="{Binding DataUpdatedText, StringFormat=CSV更新: {0}}" TextWrapping="Wrap"/>
   <TextBlock Text="{Binding DataProblemsText, StringFormat=CSV詳細: {0}}" TextWrapping="Wrap"/>
   <TextBlock Text="{Binding StatusMessage, StringFormat=状態: {0}}" TextWrapping="Wrap" Margin="0,4,0,10"/>
```

**残すもの:** `カテゴリ` / `要素` / `タイプ` / `同タイプ数` / `Assembly_ID` の各行。

`Assembly_ID` の行の `Margin` を `"0,0,0,10"` にして、下に続く「複合レイヤー」との間隔を保つ。
削除した最後の行が持っていた余白がなくなるためである。

```xml
   <TextBlock Text="{Binding AssemblyId, StringFormat=Assembly_ID: {0}}" TextWrapping="Wrap" Margin="0,0,0,10"/>
```

削除の理由をその場にコメントで残す。

```xml
   <!-- Product_ID・CSV接続・CSV更新・CSV詳細・状態はここには出しません。 -->
   <!-- Product_IDは下の複合レイヤーの表に層ごとに出ています。 -->
   <!-- CSV接続・CSV詳細は【設定】タブ、状態は【検索】タブに同じものが出ています。 -->
   <!-- 概要タブは確認専用で操作をしないため、操作の結果を出す必要がありません。 -->
```

- [x] **Step 2: 検索タブから1行を消す**

`SearchView_検索.xaml` の次の1行を**削除**する。

```xml
   <TextBlock Text="{Binding SelectedProductId, StringFormat=現在のProduct_ID: {0}}" TextWrapping="Wrap" Margin="0,2,0,6"/>
```

すぐ上の「対象:」の `TextBlock` の `Margin` を `"0,8,0,6"` にして、下の「付与範囲」との間隔を保つ。

**残すもの:** 「層への割り当て」の中にある次の行。これから書き込む内容の確認に要る。

```xml
        <Run Text="現在: "/><Run Text="{Binding CurrentProductId, Mode=OneWay}"/>
        <Run Text="  →  "/><Run Text="{Binding SelectedProductId, Mode=OneWay}"/>
```

削除の理由をその場にコメントで残す。

```xml
   <!-- 「現在のProduct_ID」の6層分の一覧はここには出しません。 -->
   <!-- 下の「層への割り当て」に、層ごとの現在値と書き込む値が並んで出ています。 -->
```

- [x] **Step 3: XAMLが壊れていないことを確認する**

Run:
```bash
python -c "
import xml.etree.ElementTree as ET
for f in ['BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/OverviewView_概要.xaml','BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/SearchView_検索.xaml']:
    ET.parse(f); print('OK', f)
"
```
Expected: 両方 `OK`。（XAMLはXMLなので、閉じ忘れや壊れた属性はここで分かる。
このPCでは `Bui.Addin` をコンパイルできないため、これが唯一の機械的な確認手段である。）

- [x] **Step 4: 消したプロパティが他で使われていることを確認する**

Run:
```bash
grep -rn "SelectedProductId\|DataConnectionText\|DataUpdatedText\|DataProblemsText\|StatusMessage" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/"
```
Expected: `SearchView_検索.xaml` と `SettingsView_設定.xaml` に残っていること。
どれか1つでも全タブから消えていたら、それは削りすぎである。

- [x] **Step 5: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。（XAMLだけの変更なので件数は変わらない。）

- [x] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/Tabs/"
git commit -m "fix: remove the duplicated status lines from the overview and search tabs"
```

---

### Task 5: リボンの名前・区切り線・アイコン

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/リボン_01_パネル表示.ico`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/リボン_02_パラメータ作成.ico`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/リボン_03_AI.ico`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/RibbonService_リボン作成.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj`
- Modify: `BUI_Addin_CSharpアドイン/scripts_開発補助/DeployDev_開発用配置.ps1`
- Modify: `BUI_Addin_CSharpアドイン/scripts_開発補助/CheckDeploy_配置診断.ps1`

**Interfaces:**
- Consumes: なし
- Produces: なし（リボンの見た目のみ）

**元ファイル:** `BUI_Addin_CSharpアドイン/imgdiet/` に3点ある。
いずれも32×32が1枚だけの `.ico`（Revitの大ボタン用サイズちょうど）。
`imgdiet` は受け取り置き場のまま残し、複製を取り込む。

- [x] **Step 1: アイコンを取り込む**

```bash
cd "BUI_Addin_CSharpアドイン"
cp "imgdiet/01｜パネル表示.ico"  "src_ソース/Bui.Addin/Resources_画像と定義/リボン_01_パネル表示.ico"
cp "imgdiet/02｜初期設定.ico"    "src_ソース/Bui.Addin/Resources_画像と定義/リボン_02_パラメータ作成.ico"
cp "imgdiet/03｜AI確認.ico"      "src_ソース/Bui.Addin/Resources_画像と定義/リボン_03_AI.ico"
```

`｜` は全角縦線（U+FF5C）。シェルによっては元ファイル名の指定に手間取るので、
うまくいかない場合はワイルドカードで指定してよい（例: `imgdiet/01*.ico`）。

コピー後、3ファイルが 4,286 バイトであることを確認する。

```bash
ls -l "src_ソース/Bui.Addin/Resources_画像と定義/"リボン_*.ico
```

- [x] **Step 2: csprojへ出力コピーを足す**

`Bui.Addin.csproj` の `Resources_画像と定義\SharedParameters_共有パラメータ.txt` を扱っている
`<None Update=...>` の並びの直後に足す。

```xml
    <!-- リボンのアイコンです。出力へ運ばないと、リボンにアイコンが出ません。 -->
    <None Update="Resources_画像と定義\リボン_01_パネル表示.ico"
          CopyToOutputDirectory="PreserveNewest" />
    <None Update="Resources_画像と定義\リボン_02_パラメータ作成.ico"
          CopyToOutputDirectory="PreserveNewest" />
    <None Update="Resources_画像と定義\リボン_03_AI.ico"
          CopyToOutputDirectory="PreserveNewest" />
```

- [x] **Step 3: csprojが正しいXMLであることを確認する**

Run:
```bash
python -c "
import xml.etree.ElementTree as ET
ET.parse('BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj'); print('OK')
"
```
Expected: `OK`。

- [x] **Step 4: リボンを書き直す**

`RibbonService_リボン作成.cs` を次の内容にする。

```csharp
// このファイルはRevitリボンにBUIタブ、BUIパネル、利用者向けコマンドボタンを作成します。
// Revit UI APIと各IExternalCommandの完全修飾名に依存し、名前を変えるとリボン表示やコマンド起動に影響します。
//
// なぜアイコンを設定するのか:
//   文字間隔を直接指定するAPIはありません。アイコンが無いとボタンが小文字幅のまま並び、
//   文字が詰まって見えます。LargeImageを設定するとボタンが大ボタン形式になり、間隔が空きます。
//   つまりアイコン設置が、文字間隔の対策そのものです。
//
// 修正時の影響:
//   アイコンが読めなくてもボタンは必ず作ってください。ここで例外を投げると
//   BUIのボタンが1つも出ず、利用者にはアドインが壊れたようにしか見えません。
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Bui.Revit;

namespace Bui.Application;

/// <summary>BUIリボンを一貫した名前で作成します。</summary>
internal static class RibbonService
{
    private const string TabName = "BUI";
    private const string PanelName = "BUI";
    private const string InstallIdentifiersCommandClassName =
        "Bui.Revit.SharedParameters.InstallIdentifiersCommand";

    private const string ResourceFolderName = "Resources_画像と定義";

    public static void Create(UIControlledApplication application)
    {
        application.CreateRibbonTab(TabName);
        var panel = application.CreateRibbonPanel(TabName, PanelName);
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        var showPaneData = new PushButtonData(
            "BuiShowPane",
            "パネル表示",
            assemblyPath,
            typeof(ShowBuiPaneCommand).FullName!);
        var showPaneButton = panel.AddItem(showPaneData) as PushButton
            ?? throw new InvalidOperationException("パネル表示ボタンを作成できませんでした。");
        showPaneButton.ToolTip = "BUIのドッキングパネルを表示します。";
        ApplyIcon(showPaneButton, "リボン_01_パネル表示.ico");

        panel.AddSeparator();

        // リボンから安定した完全修飾名で、明示操作のID初期設定コマンドを呼び出します。
        var installIdentifiersData = new PushButtonData(
            "BuiInstallIdentifiers",
            "パラメータ作成",
            assemblyPath,
            InstallIdentifiersCommandClassName);
        var installIdentifiersButton = panel.AddItem(installIdentifiersData) as PushButton
            ?? throw new InvalidOperationException("パラメータ作成ボタンを作成できませんでした。");
        installIdentifiersButton.ToolTip = "Product_IDとAssembly_IDの共有パラメータを初期設定します。";
        ApplyIcon(installIdentifiersButton, "リボン_02_パラメータ作成.ico");

        panel.AddSeparator();

        // AIチャットは別ウィンドウで開きます。BUIパネルは細長く、会話には狭いためです。
        var askAiData = new PushButtonData(
            "BuiAskAi",
            "AI",
            assemblyPath,
            typeof(AskBuiAiCommand).FullName!);
        var askAiButton = panel.AddItem(askAiData) as PushButton
            ?? throw new InvalidOperationException("AIボタンを作成できませんでした。");
        askAiButton.ToolTip = "自然言語で建材を検索・比較・提案できるチャットウィンドウを開きます。";
        ApplyIcon(askAiButton, "リボン_03_AI.ico");
    }

    /// <summary>
    /// ボタンへアイコンを設定します。読めなければ何もしません。
    /// 例外を投げるとリボンが作られず、BUIのボタンが1つも出なくなるためです。
    /// LargeImage（32×32）だけでなくImage（16×16）も設定します。
    /// Imageが無いと、リボンが縮んだ状態でアイコンが消えます。
    /// </summary>
    private static void ApplyIcon(PushButton button, string fileName)
    {
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (assemblyDirectory is null) return;

            var path = Path.Combine(assemblyDirectory, ResourceFolderName, fileName);
            if (!File.Exists(path)) return;

            button.LargeImage = LoadIcon(path, 32);
            button.Image = LoadIcon(path, 16);
        }
        catch (Exception)
        {
            // アイコンが無くてもボタンは使えます。ここで止めません。
        }
    }

    private static BitmapImage LoadIcon(string path, int pixelWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = pixelWidth;
        // ファイルを掴んだままにすると、次のビルドで上書きできなくなります。
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
```

**確認:** `ShowBuiPaneCommand` と `AskBuiAiCommand` の型名、
`InstallIdentifiersCommandClassName` の文字列は元のまま変えないこと。
ここを変えるとボタンを押してもコマンドが起動しない。

- [x] **Step 5: 配置スクリプトの許可リストへ足す**

`DeployDev_開発用配置.ps1` の `Get-BuiDeployAllowlist` の戻り値へ3行足す。

```powershell
function Get-BuiDeployAllowlist {
    # ローカル設定、認証情報、CSV、RVT、ログ、バックアップ、一時ファイルはこの一覧に入れません。
    return @(
        'Bui.dll',
        'Bui.pdb',
        'Bui.deps.json',
        'Config\AddinMetadata_アドイン情報.json',
        'Config\Settings.Default_共通設定.json',
        'Resources_画像と定義\SharedParameters_共有パラメータ.txt',
        # リボンのアイコンです。この一覧に無いと配置先へ届かず、
        # 症状は「アイコンが出ない」だけになり、原因に辿り着けません。
        'Resources_画像と定義\リボン_01_パネル表示.ico',
        'Resources_画像と定義\リボン_02_パラメータ作成.ico',
        'Resources_画像と定義\リボン_03_AI.ico'
    )
}
```

- [x] **Step 6: 配置診断へアイコンの確認を足す**

`CheckDeploy_配置診断.ps1` の「===== 4. SQL Server接続用DLL =====」の節の**直前**に、
新しい節を差し込む。番号は 4 のままにせず、新しい節を 4 にして SQL の節を 5 に、
「判定」はそのままにする。**節番号を重複させないこと。**

差し込む内容。

```powershell
# --- 4. リボンのアイコン ---
Write-Host ''
Write-Host '===== 4. リボンのアイコン =====' -ForegroundColor Cyan

$iconNames = @(
    'リボン_01_パネル表示.ico',
    'リボン_02_パラメータ作成.ico',
    'リボン_03_AI.ico'
)
$missingIcons = @()
foreach ($iconName in $iconNames) {
    $iconPath = Join-Path $addinsRoot (Join-Path 'BUI\Resources_画像と定義' $iconName)
    if (Test-Path -LiteralPath $iconPath) {
        $iconSize = (Get-Item -LiteralPath $iconPath).Length
        Write-Host ("  あり: {0} ({1} バイト)" -f $iconName, $iconSize) -ForegroundColor Green
    }
    else {
        Write-Host ("  なし: {0}" -f $iconName) -ForegroundColor Yellow
        $missingIcons += $iconName
    }
}

if ($missingIcons.Count -gt 0) {
    Write-Host ''
    Write-Host '  [情報] リボンのアイコンが配置されていません。' -ForegroundColor Yellow
    Write-Host '         リボンのボタンはアイコン無しで表示されます（機能は使えます）。'
    Write-Host '         DeployDev_開発用配置.ps1 を実行し直してください。'
}
else {
    Write-Host '  [OK] リボンのアイコンは配置されています。' -ForegroundColor Green
}
```

**確認済み:** そのスクリプトは配置先を `$addinsRoot`（`%APPDATA%\Autodesk\Revit\Addins5`）
で保持しており、BUI本体は `Join-Path $addinsRoot 'BUI\Bui.dll'` の形で参照している。
上のコードはその変数をそのまま使っている（`$addinsRoot` は95行目付近で定義済み）。

- [x] **Step 7: PowerShellスクリプトが構文として正しいことを確認する**

Run:
```powershell
$ErrorActionPreference='Stop'
foreach ($f in @('BUI_Addin_CSharpアドイン\scripts_開発補助\DeployDev_開発用配置.ps1','BUI_Addin_CSharpアドイン\scripts_開発補助\CheckDeploy_配置診断.ps1')) {
  $errors = $null
  [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $f), [ref]$null, [ref]$errors) | Out-Null
  if ($errors) { Write-Host "NG $f"; $errors } else { Write-Host "OK $f" }
}
```
Expected: 両方 `OK`。

- [x] **Step 8: 両スクリプトのBOMを確認する**

Run:
```bash
head -c 3 "BUI_Addin_CSharpアドイン/scripts_開発補助/DeployDev_開発用配置.ps1" | xxd -p
head -c 3 "BUI_Addin_CSharpアドイン/scripts_開発補助/CheckDeploy_配置診断.ps1" | xxd -p
```
Expected: 両方 `efbbbf`。
BOMを外すと Windows PowerShell 5.1 が Shift_JIS として読み、日本語が壊れて構文エラーになる。

- [x] **Step 9: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。（`Bui.Addin` とスクリプトだけの変更なので件数は変わらない。）

- [x] **Step 10: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Resources_画像と定義/" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Application_アプリ起動/RibbonService_リボン作成.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/Bui.Addin.csproj" "BUI_Addin_CSharpアドイン/scripts_開発補助/"
git commit -m "feat: give the ribbon buttons icons, separators and clearer names"
```

---

### Task 6: 会社PC向けの手順と問題解決を書く

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt`（UTF-8 BOM + CRLF）
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`（UTF-8 BOM）

**Interfaces:**
- Consumes: Task 1〜5 の完成した動き
- Produces: なし（文書のみ）

- [x] **Step 1: 既存の書式を読む**

`操作手順_会社PC_次にやること.txt` の段階12（12-1〜12-11）を読み、次を確かめる。

- 見出しの書き方（`12-1.` のように番号の後にピリオド）
- 角括弧で始まる手順には番号の後に空白を入れない（`12-5.「この条件を…`）
- 平文で始まる手順には空白を入れる（`12-2. BUIパネルが…`）
- 継続行の字下げは**6桁固定**（見出しの桁数によらない）
- 確認は `■ここまでの確認` の見出しの下に3桁字下げで書く

段階12の次の番号（13）から書く。

- [x] **Step 2: BOMと改行を確認する（変更前）**

Run:
```bash
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt" | xxd -p
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md" | xxd -p
```
Expected: 両方 `efbbbf`。

- [x] **Step 3: 段階13を書く**

`操作手順_会社PC_次にやること.txt` の末尾へ、段階12の書式に合わせて足す。
以下は内容であり、字下げは既存の書式に合わせて調整すること。

```
■段階13 パネル表示の修正を確認する

13-1. GitHubから最新を取得し、Visual Studioでビルドします。

13-2. scripts_開発補助\DeployDev_開発用配置.ps1 を実行します。

13-3. scripts_開発補助\CheckDeploy_配置診断.ps1 を実行します。

■ここまでの確認
   「4. リボンのアイコン」の3行すべてが「あり」になること。
   「なし」がある場合は 13-2 をやり直してください。
   アイコンが無くてもボタンは使えますが、見た目が変わりません。

13-4. Revitを起動し、リボンの「BUI」タブを開きます。

■ここまでの確認
   ボタンが「パネル表示」「パラメータ作成」「AI」の3つになっていること。
   それぞれにアイコンが付き、ボタンの間に区切り線が2本入っていること。

13-5.「パラメータ作成」を押します。

■ここまでの確認
   結果ウィンドウの横幅が、書かれている文字の幅に収まっていること。
   画面いっぱいに広がらないこと。
   「詳細の表示(D)」を開いても、横幅が広がらないこと。

13-6. 壁を1つ選び、「パネル表示」を押して【概要】タブを見ます。

■ここまでの確認
   厚さ・幅・長さが「100 mm」のように出ること。
   「100.0000000000 mm」になっていないこと。
   「Product_ID: …」「CSV接続:」「CSV更新:」「CSV詳細:」「状態:」の5行が
   無くなっていること。「Assembly_ID:」は残っていること。
   複合レイヤーの見出しが「レイヤー1」から始まっていること
   （「順序 0」になっていないこと）。

13-7.【検索】タブを開きます。

■ここまでの確認
   「現在のProduct_ID: …」の長い行が無くなっていること。
   「層への割り当て」の見出しが「レイヤー1」から始まっていること。
   その下の「現在: … → …」の行は残っていること。

13-8.【設定】タブを開きます。

■ここまでの確認
   「状態:」とデータ元の情報が今までどおり出ていること。
   概要タブから消した情報は、こちらと【検索】タブに残しています。
```

- [x] **Step 4: Troubleshootingへ節を足す**

`Troubleshooting_問題解決.md` の末尾へ足す。

```markdown
## リボンのボタンにアイコンが出ない

### 何が起きているか

アイコンのファイルが配置先フォルダへ届いていません。
BUIはアイコンを読めなくてもボタンを作るため、**機能は今までどおり使えます。**
見た目だけが変わらない状態です。

### 確認と対処

1. `scripts_開発補助\CheckDeploy_配置診断.ps1` を実行します。
2. 「4. リボンのアイコン」の3行を見ます。「なし」があればそれが原因です。
3. `scripts_開発補助\DeployDev_開発用配置.ps1` を実行し直します。
4. Revitを再起動して、もう一度リボンを見ます。

3行すべてが「あり」なのにアイコンが出ない場合は、Revitのリボンが
古い表示を保持している可能性があります。Revitを完全に終了してから起動し直してください。

## ID初期設定の結果ウィンドウが画面いっぱいに広がる

古いビルドです。RevitのTaskDialogは、**折りたたまれている「詳細の表示(D)」の中身も含めて**
いちばん長い行に幅を合わせます。現在は詳細テキストを折り返して渡すよう直してあります。

最新をビルドし直し、`DeployDev_開発用配置.ps1` を実行してから
Revitを再起動してください。それでも広がる場合は、`CheckDeploy_配置診断.ps1` の
「1. ビルド出力」と「2. 配置先」の日時が一致しているかを確認してください。

## 概要タブから「状態」や「CSV接続」が消えた

意図した変更です。概要タブは確認専用のタブで操作をしないため、操作の結果を出す必要がありません。

同じ情報は次の場所に残しています。

| 見たいもの | どこにあるか |
| --- | --- |
| 状態（操作の結果・失敗の理由） | 【検索】タブ、【設定】タブ |
| CSV接続 / SQL接続 | 【設定】タブ |
| CSV詳細 / SQL詳細 | 【設定】タブ |
| 層ごとのProduct_ID | 【概要】タブの「複合レイヤー」、【検索】タブの「層への割り当て」 |
```

- [x] **Step 5: BOMと改行を確認する（変更後）**

Run:
```bash
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt" | xxd -p
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md" | xxd -p
python -c "
b=open(r'BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt','rb').read()
lf=b.count(b'\n'); crlf=b.count(b'\r\n')
print('LF',lf,'CRLF',crlf,'LFのみの行',lf-crlf)
"
```
Expected: BOMが両方 `efbbbf`、`LFのみの行` が `0`。

- [x] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/docs_説明書/"
git commit -m "docs: add the panel UI walkthrough and troubleshooting"
```

---

## 完了時の確認

- [x] `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"` が終了コード 0
- [x] `git grep -n "<<<<<<<" -- "BUI_Addin_CSharpアドイン"` が何も返さない
- [x] 2つの説明書のBOMが `efbbbf`、`.txt` はCRLFのまま
- [x] 2つの `.ps1` のBOMが `efbbbf` で、構文解析が通る
- [x] 2つのXAMLと `Bui.Addin.csproj` がXMLとして解析できる
- [x] `Resources_画像と定義/` に `.ico` が3つあり、各4,286バイト

## 私物PCでは確認できないもの（会社PCでの確認事項）

- ID初期設定ダイアログの横幅が本文の幅に収まること
- リボンに3つのアイコンと2本の区切り線が出ること
- ボタン名が パネル表示 / パラメータ作成 / AI になっていること
- 概要タブから5行が消え、数値が `100 mm` になっていること
- 概要・検索タブの層が `レイヤー1` から始まること
- アイコンが配置先フォルダへ届いていること
