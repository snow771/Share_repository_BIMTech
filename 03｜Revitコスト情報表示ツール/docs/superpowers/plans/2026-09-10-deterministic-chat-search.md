# AIチャットの検索条件をBUIが組み立てる 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AIチャットの検索条件をBUIが決定的に組み立て、モデルの出来に関係なく「不燃のボードを教えて」のような複数条件の質問が通るようにする。

**Architecture:** 条件の組み立てを3層にする。第1層＝質問文とマスタの値を正規化して直接照合、第2層＝利用者が育てる `表記ゆれ.csv`、第3層＝LLMは言い換えの語を挙げるだけでBUIが採否を決める。上位の層ほど確からしく、下位は上位が埋めていない項目にだけ入れる。あわせてキーワード検索を複数語のANDにする。

**Tech Stack:** .NET 8 / C# 12 / WPF / MSTest

**設計:** `docs/superpowers/specs/2026-09-10-deterministic-chat-search-design.md`

## Global Constraints

- `Bui.Shared_共有ロジック` は Revit に依存しない。`Autodesk.Revit.*` を参照するコードを一切書かない
- `Bui.Addin` は私物PCでコンパイルできない（Revitのアセンブリが無い）。**目視確認どまりであることを報告に明記する。**そこへ渡す値の量・形は `Bui.Shared` 側でテストして固定する
- 全ファイルUTF-8。日本語のファイル名・クラス名・テストメソッド名の既存の付け方に合わせる
- 各ソースファイル冒頭の「このファイルの目的:」「なぜ〜のか:」「修正時の影響:」のコメント様式を保つ
- **辞書をコードへ埋め込まない。**機械的に導ける揺れ（全角半角・カナかな・大小文字・空白）だけを正規化で吸収し、それ以外は `表記ゆれ.csv` に置く
- **第1層の条件を第2層・第3層が上書きしない。**質問文に実際に書かれている値がいちばん確からしい
- **LLMに項目名を決めさせない。**語からマスタの項目と値を突き止めるのはBUI側
- 絞り込みは必ず `ICandidateSource` を通す（既存方針。迂回するとAIだけが知っている検索結果が生まれる）
- 拾えなかった言葉は必ず画面へ出す（既存方針「失敗の理由を必ず画面に出す」）
- CSVは**BOM付きUTF-8**で書き出す（Excel・メモ帳での文字化けを防ぐ）
- テストは**終了コードで判定する**。出力を `tail` や `head` へパイプしない
- テスト実行コマンド（リポジトリルート `C:\Users\anynu\OneDrive\ドキュメント\github\Cost_Maintenance_Display-Project` から）:
  `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
- 開始時点のテスト件数は **630件、終了コード0**

---

## File Structure

| 区分 | パス | 責務 |
| --- | --- | --- |
| 新規 | `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SearchTextNormalizer_検索文字正規化.cs` | 全角半角・カナかな・大小文字・空白の正規化 |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductCandidateFilter_候補絞り込み.cs` | キーワードを複数語のANDにする |
| 新規 | `src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/QuestionTermExtractor_質問語抽出.cs` | 第1層。質問文からマスタの値と語を拾う |
| 新規 | `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SynonymTable_表記ゆれ表.cs` | 第2層の表と照合 |
| 新規 | `src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvSynonymLoader_CSV表記ゆれ読込.cs` | `表記ゆれ.csv` の読込 |
| 新規 | `src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/ChatQueryBuilder_質問から条件.cs` | 3層の合成 |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/CandidateQueryPlan_絞り込み計画.cs` | 拾えなかった言葉を持たせる |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LlmQueryTranslator_質問から条件.cs` | ①を語の列挙へ変える |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LlmChatSession_AI会話.cs` | 3層の配線と、拾えないときの案内 |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/Configuration_設定/BuiSettings_BUI設定.cs` | `SynonymCsvPath` を追加 |
| 変更 | `src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiChatViewModel_AI会話画面.cs` | `IsFromUser` を追加 |
| 変更 | `src_ソース/Bui.Addin/UI_画面/BuiChatWindow_AI会話.xaml` | 吹き出しの左右振り分け |
| 新規 | `config_設定例/表記ゆれ.csv` | 見本。初期値入り |
| 変更 | `docs_説明書/操作手順_会社PC_次にやること.txt` | 段階14 |
| 変更 | `docs_説明書/Troubleshooting_問題解決.md` | 表記ゆれの育て方 |

---

### Task 1: 正規化と複数語のAND検索

「不燃 ボード」で両方に当たる製品だけを返せるようにする。ここが土台で、以降のタスクはこの正規化を使う。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SearchTextNormalizer_検索文字正規化.cs`
- Create: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SearchTextNormalizerTests_検索文字正規化テスト.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/ProductCandidateFilter_候補絞り込み.cs`（`ContainsText`）
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/ProductCandidateFilterTests_候補絞り込みテスト.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `namespace Bui.Data;`
  - `public static class SearchTextNormalizer`
    - `public static string Normalize(string? value);` — 全角英数記号→半角、カタカナ→ひらがな、小文字化、空白（半角・全角）を除去
    - `public static IReadOnlyList<string> SplitTerms(string? text);` — 空白で分割し、各語を正規化。空語は捨てる

- [ ] **Step 1: 正規化の失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/SearchTextNormalizerTests_検索文字正規化テスト.cs` を新規作成する。

```csharp
// このファイルの目的:
//   検索で吸収してよい「機械的な揺れ」の範囲を固定します。
// なぜ必要か:
//   ここを広げると別物が当たり、狭めると「セッコウ」で探せません。
//   意味の揺れ（PB＝せっこうボード）はここでは扱いません。表記ゆれ.csvの仕事です。
using Bui.Data;

namespace Bui.Tests.Data_データ;

[TestClass]
public class SearchTextNormalizerTests_検索文字正規化テスト
{
    [TestMethod]
    public void 全角英数記号を半角にする()
    {
        Assert.AreEqual("pb", SearchTextNormalizer.Normalize("ＰＢ"));
        Assert.AreEqual("12.5", SearchTextNormalizer.Normalize("１２．５"));
    }

    [TestMethod]
    public void カタカナをひらがなにする()
    {
        Assert.AreEqual("せっこう", SearchTextNormalizer.Normalize("セッコウ"));
        // 長音符はそのまま残します。ぼーど と ボード を同じにするために必要です。
        Assert.AreEqual("ぼーど", SearchTextNormalizer.Normalize("ボード"));
    }

    [TestMethod]
    public void 大文字を小文字にする()
    {
        Assert.AreEqual("gb-r", SearchTextNormalizer.Normalize("GB-R"));
    }

    [TestMethod]
    public void 空白を除去する()
    {
        // 全角空白も対象です。「せっこう ボード」と書く人がいます。
        Assert.AreEqual("せっこうぼーど", SearchTextNormalizer.Normalize("せっこう ボード"));
        Assert.AreEqual("せっこうぼーど", SearchTextNormalizer.Normalize("せっこう　ボード"));
    }

    [TestMethod]
    public void 空文字と未指定は空文字にする()
    {
        Assert.AreEqual(string.Empty, SearchTextNormalizer.Normalize(null));
        Assert.AreEqual(string.Empty, SearchTextNormalizer.Normalize("   "));
    }

    [TestMethod]
    public void カタカナに無い文字を壊さない()
    {
        // ヷヸヹヺ にはひらがなが無いため、機械的な引き算をしてはいけません。
        Assert.AreEqual("ヷ", SearchTextNormalizer.Normalize("ヷ"));
        // 漢字はそのまま残ります。
        Assert.AreEqual("不燃", SearchTextNormalizer.Normalize("不燃"));
    }

    [TestMethod]
    public void 語ごとに分けて正規化する()
    {
        CollectionAssert.AreEqual(
            new[] { "不燃", "ぼーど" },
            SearchTextNormalizer.SplitTerms("不燃 ボード").ToArray());
    }

    [TestMethod]
    public void 語が無ければ空の一覧を返す()
    {
        Assert.AreEqual(0, SearchTextNormalizer.SplitTerms("   ").Count);
        Assert.AreEqual(0, SearchTextNormalizer.SplitTerms(null).Count);
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~検索文字正規化テスト"`
Expected: コンパイルエラー。`SearchTextNormalizer` が存在しない。

- [ ] **Step 3: 正規化を実装する**

`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SearchTextNormalizer_検索文字正規化.cs` を新規作成する。

```csharp
// このファイルの目的:
//   検索の照合前に、機械的に導ける表記の揺れだけをそろえます。
//
// なぜ辞書を置かないのか:
//   「PB」と「せっこうボード」は意味の揺れで、文字の変換では導けません。
//   ここへ辞書を持ち込むと、現場の言い方が増えるたびにビルドが必要になります。
//   意味の揺れは利用者が育てる 表記ゆれ.csv が受け持ちます。
//
// 修正時の影響:
//   ここを広げると別物が当たり、狭めると「セッコウ」で探せなくなります。
//   カタカナ→ひらがなの引き算は U+30A1〜U+30F6 に限ってください。
//   ヷヸヹヺ（U+30F7〜U+30FA）に対応するひらがなは無く、未定義の文字ができます。

using System.Text;

namespace Bui.Data;

/// <summary>検索で吸収してよい表記の揺れをそろえます。純粋関数として保ちます。</summary>
public static class SearchTextNormalizer
{
    /// <summary>全角英数記号→半角、カタカナ→ひらがな、小文字化、空白の除去を行います。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            // 半角・全角の空白は落とします。「せっこう ボード」を1語として扱うためです。
            if (char.IsWhiteSpace(character) || character == '\u3000') continue;

            var converted = character;

            // 全角英数記号（！〜～）を半角へ。全角空白はここへ来ません（上で落としています）。
            if (converted >= '\uFF01' && converted <= '\uFF5E')
            {
                converted = (char)(converted - 0xFEE0);
            }
            // カタカナをひらがなへ。ひらがなが存在する範囲だけを対象にします。
            else if (converted >= '\u30A1' && converted <= '\u30F6')
            {
                converted = (char)(converted - 0x60);
            }

            builder.Append(char.ToLowerInvariant(converted));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 空白で区切って語の一覧にします。各語は正規化済みです。
    /// 分割は正規化より先に行います。Normalizeは空白を落とすため、
    /// 順序を逆にすると全体が1語になってしまいます。
    /// </summary>
    public static IReadOnlyList<string> SplitTerms(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(term => term.Length > 0)
            .ToArray();
    }
}
```

- [ ] **Step 4: 正規化のテストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [ ] **Step 5: AND検索の失敗するテストを書く**

`ProductCandidateFilterTests_候補絞り込みテスト.cs` のクラス内に追加する。

そのファイルには既に次のヘルパーがある（25行目付近）。**製品は固定で、テキストだけを渡す形**である。
新しいヘルパーを作らず、これを使うこと。

```csharp
    private static ProductRecord P(string id, string category, string maker, string name) =>
        new(id, name, maker, string.Empty, string.Empty, null,
            new Dictionary<string, string> { ["カテゴリー"] = category });

    private static IReadOnlyList<ProductRecord> Products() => new[]
    {
        P("BM-0001", "内装", "メーカーA", "床タイル"),
        P("BM-0002", "内装", "メーカーB", "床タイル"),
        P("BM-0003", "外装", "メーカーA", "外壁パネル"),
    };

    private static CandidateView Apply(Dictionary<string, string>? selections = null, string text = "", int limit = 200)
```

追加するテスト。

```csharp
    [TestMethod]
    public void Apply_複数語はすべて当たる製品だけ返す()
    {
        // 「内装」はカテゴリー、「メーカーA」はメーカー名。項目をまたいで当たってよい。
        var view = Apply(text: "内装 メーカーA");

        CollectionAssert.AreEqual(
            new[] { "BM-0001" },
            view.Candidates.Select(record => record.ProductId).ToArray(),
            "両方の語に当たる製品だけを返すこと。");
    }

    [TestMethod]
    public void Apply_1語のときの挙動は変わらない()
    {
        var view = Apply(text: "内装");

        Assert.AreEqual(2, view.Candidates.Count);
    }

    [TestMethod]
    public void Apply_語の照合でかなと大小文字の違いを吸収する()
    {
        // 「めーかーa」で「メーカーA」に当たること。
        var view = Apply(text: "めーかーa 内装");

        Assert.AreEqual(2, view.Candidates.Count);
    }

    [TestMethod]
    public void Apply_どの語にも当たらなければ0件()
    {
        // 文章をそのまま渡した場合がこれにあたります。
        var view = Apply(text: "内装のタイルを教えて");

        Assert.AreEqual(0, view.Candidates.Count,
            "分割しても『のたいるをおしえて』は当たらないため0件のままであること。");
    }
```

**4つ目のテストの意味:** ANDにしても、空白の無い日本語の文章は依然として0件になる。
それでよい。文章を条件へ変えるのは Task 2 の抽出器の仕事であり、
この層の責任ではないことを、ここで固定しておく。

- [ ] **Step 6: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~候補絞り込みテスト"`
Expected: FAIL。「不燃 ぼーど」という文字列を含む項目が無いため0件になる。

- [ ] **Step 7: ANDを実装する**

`ProductCandidateFilter_候補絞り込み.cs` の `ContainsText` を差し替える。

差し替え前:
```csharp
    private static bool ContainsText(ProductRecord record, CsvColumnMapping mapping, string text) =>
        ProductFieldAccessor.Fields(record, mapping).Values
            .Any(value => value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase));
```

差し替え後:
```csharp
    /// <summary>
    /// 空白で区切ったすべての語が、いずれかの項目に含まれることを求めます。
    /// 語ごとに当たる項目が違ってよく、項目をまたいで構いません
    /// （建材分類に「ぼーど」、防火性能に「不燃」）。
    ///
    /// 文章をまるごと1語として照合していたのが、AIチャットで
    /// 「不燃のボードを教えて」が必ず0件になっていた原因です（2026-09-10）。
    ///
    /// 対象範囲の定義はProductFieldAccessor.Fieldsに一本化しています。
    /// ここで独自に列挙し直すと、既存の製品検索と挙動が食い違います。
    /// </summary>
    private static bool ContainsText(ProductRecord record, CsvColumnMapping mapping, string text)
    {
        var terms = SearchTextNormalizer.SplitTerms(text);
        if (terms.Count == 0) return true;

        var values = ProductFieldAccessor.Fields(record, mapping).Values
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(SearchTextNormalizer.Normalize)
            .ToArray();

        return terms.All(term => values.Any(value => value.Contains(term, StringComparison.Ordinal)));
    }
```

正規化済みどうしの比較になるため、`StringComparison.Ordinal` を使う。
`OrdinalIgnoreCase` は不要（正規化で小文字化済み）。

- [ ] **Step 8: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。既存のテストも全部通ること。

- [ ] **Step 9: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [ ] **Step 10: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/"
git commit -m "feat: match every search term instead of the whole phrase"
```

---

### Task 2: 第1層 質問文からマスタの値と語を拾う

LLM無しで、質問文と「その時点で選べる値」を突き合わせる。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/QuestionTermExtractor_質問語抽出.cs`
- Create: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/QuestionTermExtractorTests_質問語抽出テスト.cs`

**Interfaces:**
- Consumes: `SearchTextNormalizer.Normalize(string?)`、`Bui.Data.CandidateFilterOptions(string Column, string Selected, IReadOnlyList<string> Values)`
- Produces:
  - `namespace Bui.Llm;`
  - `public sealed record ExtractedConditions(IReadOnlyDictionary<string, string> Selections, IReadOnlyList<string> Terms, string Remainder)`
  - `public static class QuestionTermExtractor`
    - `public const int MaximumQuestionLength = 200;`
    - `public const int MaximumTermLength = 12;`
    - `public static ExtractedConditions Extract(string? question, IReadOnlyList<CandidateFilterOptions> available);`
    - `public static IReadOnlyList<string> ExtractTermsFrom(string? normalizedText, IReadOnlyList<CandidateFilterOptions> available);`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Llm_LLM/QuestionTermExtractorTests_質問語抽出テスト.cs` を新規作成する。

```csharp
// このファイルの目的:
//   LLM無しで、質問文からマスタに実在する条件と語を拾えることを守ります。
// なぜ必要か:
//   条件の組み立てをLLMに依存させたため、モデルが条件を1つも返さず
//   「不燃のボードを教えて」が必ず0件になっていました（2026-09-10）。
//   この抽出器が動く限り、モデルが何も返せなくても検索は動きます。
using Bui.Data;
using Bui.Llm;

namespace Bui.Tests.Llm_LLM;

[TestClass]
public class QuestionTermExtractorTests_質問語抽出テスト
{
    [TestMethod]
    public void 質問文にある値を条件にする()
    {
        var result = QuestionTermExtractor.Extract("不燃のボードを教えて", Available());

        Assert.AreEqual("不燃", result.Selections["防火性能"]);
    }

    [TestMethod]
    public void 値の一部は語として拾う()
    {
        // 「ボード」は建材分類の値そのものではなく「せっこうボード」の一部です。
        var result = QuestionTermExtractor.Extract("不燃のボードを教えて", Available());

        CollectionAssert.Contains(result.Terms.ToArray(), "ぼーど");
    }

    [TestMethod]
    public void 依頼表現は落とす()
    {
        // 「を教えて」はどの値にも現れないため、辞書無しで自然に落ちます。
        var result = QuestionTermExtractor.Extract("不燃のボードを教えて", Available());

        Assert.IsFalse(result.Terms.Any(term => term.Contains("おしえ")), "依頼表現を語にしないこと。");
    }

    [TestMethod]
    public void 同じ項目に複数当たったら長い値を採る()
    {
        // 「せっこうボード」と「ボード」が両方値として存在する場合、
        // 短い方を採ると絞り込みが緩くなります。
        var available = new[]
        {
            new CandidateFilterOptions("建材分類", string.Empty, new[] { "ボード", "せっこうボード" })
        };

        var result = QuestionTermExtractor.Extract("せっこうボードください", available);

        Assert.AreEqual("せっこうボード", result.Selections["建材分類"]);
    }

    [TestMethod]
    public void カタカナと全角の揺れを吸収する()
    {
        var result = QuestionTermExtractor.Extract("フネンのボード", new[]
        {
            new CandidateFilterOptions("防火性能", string.Empty, new[] { "ふねん" })
        });

        Assert.AreEqual("ふねん", result.Selections["防火性能"]);
    }

    [TestMethod]
    public void 条件にした値は語へ二重に入れない()
    {
        // 条件で絞ったうえに同じ語でも絞ると、二重に狭めることになります。
        var result = QuestionTermExtractor.Extract("不燃のボードを教えて", Available());

        Assert.IsFalse(result.Terms.Contains("不燃"), "条件になった値を語にしないこと。");
    }

    [TestMethod]
    public void 何も拾えなければ空で返す()
    {
        var result = QuestionTermExtractor.Extract("こんにちは", Available());

        Assert.AreEqual(0, result.Selections.Count);
        Assert.AreEqual(0, result.Terms.Count);
    }

    [TestMethod]
    public void 空の質問でも落ちない()
    {
        var result = QuestionTermExtractor.Extract(null, Available());

        Assert.AreEqual(0, result.Selections.Count);
        Assert.AreEqual(0, result.Terms.Count);
    }

    [TestMethod]
    public void 極端に長い質問は先頭だけ見る()
    {
        // 語の候補は質問の長さの2乗で増えるため、上限を置きます。
        var question = new string('あ', 5000) + "不燃";

        var result = QuestionTermExtractor.Extract(question, Available());

        Assert.AreEqual(0, result.Selections.Count, "上限を超えた位置の値は見ないこと。");
    }

    [TestMethod]
    public void 語の長さに上限を設ける()
    {
        var available = new[]
        {
            new CandidateFilterOptions("製品名", string.Empty, new[] { new string('ボ', 40) })
        };

        var terms = QuestionTermExtractor.ExtractTermsFrom(new string('ぼ', 30), available);

        Assert.IsTrue(
            terms.All(term => term.Length <= QuestionTermExtractor.MaximumTermLength),
            "語が上限を超えないこと。");
    }

    private static IReadOnlyList<CandidateFilterOptions> Available() => new[]
    {
        new CandidateFilterOptions("建材分類", string.Empty, new[] { "せっこうボード", "ロックウール" }),
        new CandidateFilterOptions("防火性能", string.Empty, new[] { "不燃", "準不燃" }),
        new CandidateFilterOptions("厚さ", string.Empty, new[] { "9.5", "12.5" })
    };
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~質問語抽出テスト"`
Expected: コンパイルエラー。`QuestionTermExtractor` が存在しない。

- [ ] **Step 3: 実装する**

`src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/QuestionTermExtractor_質問語抽出.cs` を新規作成する。

```csharp
// このファイルの目的:
//   質問文から、マスタに実在する条件と検索語を、LLM無しで拾います。
//
// なぜLLM無しで拾うのか:
//   この案件の方針は「検索は決定的に行い、LLMは説明だけ」です。
//   AIチャットだけが条件の組み立てをLLMに依存していて、モデルが条件を
//   1つも返さず「不燃のボードを教えて」が必ず0件になっていました（2026-09-10）。
//   ここが動く限り、モデルが何も返せなくても検索は動きます。
//
// なぜ形態素解析をしないのか:
//   辞書とライセンスを持ち込むことになります。
//   マスタに実在する文字列だけを手がかりにすれば、辞書は要りません。
//   「を教えて」のような依頼表現はマスタに無いため、自然に落ちます。
//
// 修正時の影響:
//   照合対象を絞り込み項目の値より広げないでください。
//   備考欄まで広げると、短い文字列が語として拾われ、AND検索を無意味に狭めます。

using System.Text;
using Bui.Data;

namespace Bui.Llm;

/// <summary>質問文から拾えた条件と語です。Remainderは語を探した後の残りです。</summary>
public sealed record ExtractedConditions(
    IReadOnlyDictionary<string, string> Selections,
    IReadOnlyList<string> Terms,
    string Remainder);

/// <summary>質問文とマスタの値を突き合わせます。純粋関数として保ちます。</summary>
public static class QuestionTermExtractor
{
    /// <summary>語の候補は質問の長さの2乗で増えるため、見る範囲に上限を置きます。</summary>
    public const int MaximumQuestionLength = 200;

    /// <summary>語1つの長さの上限です。暴走防止であり、意味は持ちません。</summary>
    public const int MaximumTermLength = 12;

    /// <summary>語として認める最短の長さです。1文字だと助詞まで拾ってしまいます。</summary>
    private const int MinimumTermLength = 2;

    public static ExtractedConditions Extract(
        string? question, IReadOnlyList<CandidateFilterOptions> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        var normalized = SearchTextNormalizer.Normalize(question);
        if (normalized.Length > MaximumQuestionLength)
        {
            normalized = normalized.Substring(0, MaximumQuestionLength);
        }

        if (normalized.Length == 0)
        {
            return new ExtractedConditions(
                new Dictionary<string, string>(StringComparer.Ordinal),
                Array.Empty<string>(),
                string.Empty);
        }

        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        var remainder = normalized;

        foreach (var option in available)
        {
            // 同じ項目に複数当たったら長い値を採ります。
            // 短い方を採ると絞り込みが緩くなります。
            var matched = option.Values
                .Where(value => value.Length > 0)
                .Where(value => remainder.Contains(SearchTextNormalizer.Normalize(value), StringComparison.Ordinal))
                .OrderByDescending(value => SearchTextNormalizer.Normalize(value).Length)
                .FirstOrDefault();
            if (matched is null) continue;

            selections[option.Column] = matched;

            // 条件にした値は残りから取り除きます。
            // 残さないと、同じ値が語としても拾われ、二重に絞ることになります。
            var normalizedValue = SearchTextNormalizer.Normalize(matched);
            var index = remainder.IndexOf(normalizedValue, StringComparison.Ordinal);
            if (index >= 0)
            {
                remainder = remainder.Remove(index, normalizedValue.Length);
            }
        }

        return new ExtractedConditions(selections, ExtractTermsFrom(remainder, available), remainder);
    }

    /// <summary>
    /// 与えられた文字列から、いずれかの値に含まれる極大な部分文字列を語として取り出します。
    /// 「ぼーど」は「せっこうぼーど」に含まれるため語になります。
    /// どの値にも現れない「をおしえて」は落ちます。
    /// </summary>
    public static IReadOnlyList<string> ExtractTermsFrom(
        string? normalizedText, IReadOnlyList<CandidateFilterOptions> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var text = normalizedText ?? string.Empty;
        if (text.Length < MinimumTermLength) return Array.Empty<string>();

        // 値をすべて連結した1本の文字列に対して部分一致を見ます。
        // 値ごとに走査すると、値の数だけ照合が増えます。
        // 区切り文字を挟み、値をまたいだ偶然の一致を防ぎます。
        var haystack = new StringBuilder();
        foreach (var option in available)
        {
            foreach (var value in option.Values)
            {
                haystack.Append('\u0001').Append(SearchTextNormalizer.Normalize(value));
            }
        }

        haystack.Append('\u0001');
        var values = haystack.ToString();

        var terms = new List<string>();
        for (var start = 0; start < text.Length; start++)
        {
            // 同じ開始位置では、いちばん長く伸ばせたものだけを採ります。
            string? longest = null;
            var maxLength = Math.Min(MaximumTermLength, text.Length - start);
            for (var length = MinimumTermLength; length <= maxLength; length++)
            {
                var candidate = text.Substring(start, length);
                if (!values.Contains(candidate, StringComparison.Ordinal)) break;
                longest = candidate;
            }

            if (longest is not null) terms.Add(longest);
        }

        // 他の語に含まれる語を捨て、極大なものだけ残します。
        // 「ぼーど」と「ーど」の両方を残すと、AND検索が無意味に狭くなります。
        return terms
            .Distinct(StringComparer.Ordinal)
            .Where(term => !terms.Any(other =>
                other.Length > term.Length && other.Contains(term, StringComparison.Ordinal)))
            .ToArray();
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [ ] **Step 5: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [ ] **Step 6: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/QuestionTermExtractor_質問語抽出.cs" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/QuestionTermExtractorTests_質問語抽出テスト.cs"
git commit -m "feat: pull search conditions out of the question without an LLM"
```

---

### Task 3: 第2層 表記ゆれ.csv

「PB」「石膏ボード」のような、文字変換では導けない言い換えを、利用者が育てられるCSVで持つ。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SynonymTable_表記ゆれ表.cs`
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvSynonymLoader_CSV表記ゆれ読込.cs`
- Create: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SynonymTableTests_表記ゆれ表テスト.cs`
- Create: `BUI_Addin_CSharpアドイン/config_設定例/表記ゆれ.csv`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/BuiSettings_BUI設定.cs`（`DataSettings` の末尾）
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Configuration_設定/CsvPathNormalizer_CSVパス整形.cs`

**Interfaces:**
- Consumes: `SearchTextNormalizer.Normalize(string?)`、`CandidateFilterOptions`、既存の `CsvDocumentCodec.Read(string path)`（`CsvFileWriter_CSV安全更新.cs` 内、`internal static`。`Headers` と `Rows` を持つ `CsvDocument` を返す）
- Produces:
  - `namespace Bui.Data;`
  - `public sealed record SynonymEntry(string Spoken, string Column, string Value)`
  - `public sealed record SynonymMatch(IReadOnlyDictionary<string, string> Selections, IReadOnlyList<string> Terms, IReadOnlyList<string> Problems)`
  - `public sealed class SynonymTable`
    - `public SynonymTable(IReadOnlyList<SynonymEntry> entries)`
    - `public static SynonymTable Empty { get; }`
    - `public SynonymMatch Match(string? normalizedText, IReadOnlyList<CandidateFilterOptions> available)`
  - `namespace Bui.Data.Csv;`
  - `public sealed class CsvSynonymLoader`
    - `public IReadOnlyList<string> LastProblems { get; }`
    - `public SynonymTable Load(string? path)`
  - `DataSettings` に `string? SynonymCsvPath = null` を末尾へ追加

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Data_データ/SynonymTableTests_表記ゆれ表テスト.cs` を新規作成する。

```csharp
// このファイルの目的:
//   「PB」のような、文字変換では導けない言い換えが条件になることを守ります。
// なぜ必要か:
//   現場では「PB」「石膏ボード」「プラスターボード」がすべて同じ物を指しますが、
//   マスタの値「せっこうボード」のどこにも含まれません。
//   正規化では絶対に解けないため、利用者が育てる表で持ちます。
using Bui.Data;

namespace Bui.Tests.Data_データ;

[TestClass]
public class SynonymTableTests_表記ゆれ表テスト
{
    [TestMethod]
    public void 言い換えを条件にする()
    {
        var table = new SynonymTable(new[] { new SynonymEntry("PB", "建材分類", "せっこうボード") });

        var match = table.Match(SearchTextNormalizer.Normalize("PBで不燃のものある？"), Available());

        Assert.AreEqual("せっこうボード", match.Selections["建材分類"]);
    }

    [TestMethod]
    public void 全角や大文字の揺れも吸収する()
    {
        var table = new SynonymTable(new[] { new SynonymEntry("PB", "建材分類", "せっこうボード") });

        var match = table.Match(SearchTextNormalizer.Normalize("ｐｂある？"), Available());

        Assert.AreEqual("せっこうボード", match.Selections["建材分類"]);
    }

    [TestMethod]
    public void 項目が空の行は検索語として足す()
    {
        // 項目を特定できない言い換え用です。
        var table = new SynonymTable(new[] { new SynonymEntry("タイガー", string.Empty, "吉野石膏") });

        var match = table.Match(SearchTextNormalizer.Normalize("タイガーのボード"), Available());

        CollectionAssert.Contains(match.Terms.ToArray(), "よしのせっこう");
    }

    [TestMethod]
    public void マスタに無い値の行は落として理由を残す()
    {
        // マスタが変わって値が消えたとき、黙って効かなくなるのを防ぎます。
        var table = new SynonymTable(new[] { new SynonymEntry("PB", "建材分類", "存在しない分類") });

        var match = table.Match(SearchTextNormalizer.Normalize("PBある？"), Available());

        Assert.AreEqual(0, match.Selections.Count);
        Assert.AreEqual(1, match.Problems.Count);
        StringAssert.Contains(match.Problems[0], "存在しない分類");
    }

    [TestMethod]
    public void マスタに無い項目の行は落として理由を残す()
    {
        var table = new SynonymTable(new[] { new SynonymEntry("PB", "存在しない項目", "せっこうボード") });

        var match = table.Match(SearchTextNormalizer.Normalize("PBある？"), Available());

        Assert.AreEqual(0, match.Selections.Count);
        Assert.AreEqual(1, match.Problems.Count);
        StringAssert.Contains(match.Problems[0], "存在しない項目");
    }

    [TestMethod]
    public void 当たらない言い換えは何も返さない()
    {
        var table = new SynonymTable(new[] { new SynonymEntry("PB", "建材分類", "せっこうボード") });

        var match = table.Match(SearchTextNormalizer.Normalize("ロックウール"), Available());

        Assert.AreEqual(0, match.Selections.Count);
        Assert.AreEqual(0, match.Problems.Count, "当たらない行は問題ではないこと。");
    }

    [TestMethod]
    public void 空の表でも落ちない()
    {
        var match = SynonymTable.Empty.Match(SearchTextNormalizer.Normalize("PBある？"), Available());

        Assert.AreEqual(0, match.Selections.Count);
        Assert.AreEqual(0, match.Terms.Count);
    }

    private static IReadOnlyList<CandidateFilterOptions> Available() => new[]
    {
        new CandidateFilterOptions("建材分類", string.Empty, new[] { "せっこうボード", "ロックウール" }),
        new CandidateFilterOptions("メーカー名", string.Empty, new[] { "吉野石膏" }),
        new CandidateFilterOptions("防火性能", string.Empty, new[] { "不燃", "準不燃" })
    };
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~表記ゆれ表テスト"`
Expected: コンパイルエラー。`SynonymTable` が存在しない。

- [ ] **Step 3: 表を実装する**

`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/SynonymTable_表記ゆれ表.cs` を新規作成する。

```csharp
// このファイルの目的:
//   現場の言い方（PB・石膏ボード）を、マスタの値（せっこうボード）へ結び付けます。
//
// なぜコードに書かないのか:
//   現場の言い方は増え続けます。コードへ書くと、増えるたびにビルドと配置が要ります。
//   利用者はその言い方を知っている当人であり、CSVなら本人が足せます。
//
// 修正時の影響:
//   値がマスタに実在するかを必ず確かめてください。確かめないと、
//   マスタが変わって値が消えたときに、黙って効かない行が残ります。
//   当たらなかった行を問題として数えないでください。
//   表には使われない行が普通に入っています。

namespace Bui.Data;

/// <summary>1つの言い換えです。Columnが空なら、値を検索語として足します。</summary>
public sealed record SynonymEntry(string Spoken, string Column, string Value);

/// <summary>表記ゆれ表を引いた結果です。Problemsは画面へ出す理由です。</summary>
public sealed record SynonymMatch(
    IReadOnlyDictionary<string, string> Selections,
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Problems);

/// <summary>言い換えの表です。照合は正規化済みの文字列に対して行います。</summary>
public sealed class SynonymTable
{
    private readonly IReadOnlyList<SynonymEntry> _entries;

    public SynonymTable(IReadOnlyList<SynonymEntry> entries) =>
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    /// <summary>表が無いときに使う空の表です。呼び出し側でnull判定を書かせません。</summary>
    public static SynonymTable Empty { get; } = new(Array.Empty<SynonymEntry>());

    public SynonymMatch Match(string? normalizedText, IReadOnlyList<CandidateFilterOptions> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var text = normalizedText ?? string.Empty;

        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        var terms = new List<string>();
        var problems = new List<string>();

        foreach (var entry in _entries)
        {
            var spoken = SearchTextNormalizer.Normalize(entry.Spoken);
            if (spoken.Length == 0) continue;
            if (!text.Contains(spoken, StringComparison.Ordinal)) continue;

            // 項目が空の行は、値を検索語として足します。項目を特定できない言い換え用です。
            if (entry.Column.Length == 0)
            {
                var term = SearchTextNormalizer.Normalize(entry.Value);
                if (term.Length > 0) terms.Add(term);
                continue;
            }

            var option = available.FirstOrDefault(item =>
                string.Equals(item.Column, entry.Column, StringComparison.Ordinal));
            if (option is null)
            {
                problems.Add($"表記ゆれ「{entry.Spoken}」の項目「{entry.Column}」は絞り込み項目にありません。");
                continue;
            }

            var value = option.Values.FirstOrDefault(item =>
                string.Equals(
                    SearchTextNormalizer.Normalize(item),
                    SearchTextNormalizer.Normalize(entry.Value),
                    StringComparison.Ordinal));
            if (value is null)
            {
                problems.Add($"表記ゆれ「{entry.Spoken}」の値「{entry.Value}」は「{entry.Column}」にありません。");
                continue;
            }

            // 先に書かれた行を優先します。表の上から読む順序が結果を決めます。
            if (!selections.ContainsKey(option.Column)) selections[option.Column] = value;
        }

        return new SynonymMatch(selections, terms, problems);
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [ ] **Step 5: 読込を実装する**

`src_ソース/Bui.Shared_共有ロジック/Data_データ窓口/Csv/CsvSynonymLoader_CSV表記ゆれ読込.cs` を新規作成する。

`CsvIdAliasLoader_CSV旧新ID読込.cs` を先に読み、同じ書き方（`CsvDocumentCodec.Read`、
ヘッダーの検証、`LastStatus` に相当する報告）に合わせること。

```csharp
// このファイルの目的:
//   表記ゆれ.csv を読み、SynonymTable にします。
//
// なぜ読めなくても止めないのか:
//   表記ゆれは検索の精度を上げる補助であり、無くても検索は動きます。
//   ここで例外を投げると、CSVの1行のせいでチャットが使えなくなります。
//   読めなかった理由は画面へ出し、動きは続けます。
//
// 修正時の影響:
//   ヘッダーは 言い方,項目,値 の3列です。変えると既存の表が読めなくなります。

using System.IO;

namespace Bui.Data.Csv;

/// <summary>表記ゆれ.csv を読み込みます。読めなくても空の表を返します。</summary>
public sealed class CsvSynonymLoader
{
    public IReadOnlyList<string> LastProblems { get; private set; } = Array.Empty<string>();

    public SynonymTable Load(string? path)
    {
        LastProblems = Array.Empty<string>();

        // 未設定は異常ではありません。表を使わないだけです。
        if (string.IsNullOrWhiteSpace(path)) return SynonymTable.Empty;
        if (!File.Exists(path)) return SynonymTable.Empty;

        try
        {
            var document = CsvDocumentCodec.Read(path);
            if (document.Headers.Count < 3
                || !string.Equals(document.Headers[0], "言い方", StringComparison.Ordinal)
                || !string.Equals(document.Headers[1], "項目", StringComparison.Ordinal)
                || !string.Equals(document.Headers[2], "値", StringComparison.Ordinal))
            {
                LastProblems = new[] { "表記ゆれCSVの見出しは 言い方,項目,値 にしてください。" };
                return SynonymTable.Empty;
            }

            var entries = new List<SynonymEntry>();
            var problems = new List<string>();
            foreach (var row in document.Rows)
            {
                var spoken = row["言い方"].Trim();
                var column = row["項目"].Trim();
                var value = row["値"].Trim();

                if (spoken.Length == 0 || value.Length == 0)
                {
                    problems.Add($"表記ゆれCSVに、言い方か値が空の行があります（言い方: {spoken}）。");
                    continue;
                }

                entries.Add(new SynonymEntry(spoken, column, value));
            }

            LastProblems = problems;
            return new SynonymTable(entries);
        }
        catch (Exception)
        {
            // 例外メッセージにはパスが混じり得るため、画面へは定型文だけを出します。
            LastProblems = new[] { "表記ゆれCSVを読み取れませんでした。ファイルの形式を確認してください。" };
            return SynonymTable.Empty;
        }
    }
}
```

`CsvDocumentCodec` は `internal static` である。`CsvSynonymLoader` を同じアセンブリに置くので
そのまま呼べる。呼べない場合は `CsvIdAliasLoader` と同じ名前空間・可視性に合わせること。

- [ ] **Step 6: 設定へパスを足す**

`BuiSettings_BUI設定.cs` の `DataSettings` の**末尾**（`SqlDatabaseName` の後ろ）へ足す。
既定値付きにして、既存の設定JSONを壊さない。

```csharp
    string SqlServerName = "",
    string SqlDatabaseName = "",
    // 表記ゆれ.csv のパスです。未設定なら表記ゆれを使いません（検索は動きます）。
    // 構成CSVと同じく、SQL接続中もこのファイルを使います。
    string? SynonymCsvPath = null);
```

`CsvPathNormalizer_CSVパス整形.cs` の、`IdAliasCsvPath = NormalizeOptional(data.IdAliasCsvPath)`
と書かれている行の直後に足す。

```csharp
            IdAliasCsvPath = NormalizeOptional(data.IdAliasCsvPath),
            SynonymCsvPath = NormalizeOptional(data.SynonymCsvPath)
```

**注意:** そのファイルは `with` 式で組み立てている。末尾の要素の後ろにカンマが要るかどうかを
現物を読んで確かめること。

- [ ] **Step 7: 見本CSVを作る**

`config_設定例/表記ゆれ.csv` を **BOM付きUTF-8** で新規作成する。

```csv
言い方,項目,値
PB,建材分類,せっこうボード
石膏ボード,建材分類,せっこうボード
プラスターボード,建材分類,せっこうボード
GB,建材分類,せっこうボード
ジプトーン,建材分類,化粧せっこうボード
ロックウール,建材分類,ロックウール吸音板
グラスウール,建材分類,グラスウール
ケイカル,建材分類,けい酸カルシウム板
ケイカル板,建材分類,けい酸カルシウム板
不燃材,防火性能,不燃
準不燃材,防火性能,準不燃
```

BOMの確認:
```bash
head -c 3 "BUI_Addin_CSharpアドイン/config_設定例/表記ゆれ.csv" | xxd -p
```
Expected: `efbbbf`

**この見本の値は例である。**実際のマスタに無い値の行は、読み込み時に落ちて画面に出る。
利用者が自分のマスタに合わせて直せることが要点であり、
ここで正しい値をすべて言い当てる必要はない。そのことをファイル冒頭のコメント行には書かない
（CSVにコメント行を入れると `CsvDocumentCodec` が見出しとして読む）。

- [ ] **Step 8: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [ ] **Step 9: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Data_データ/SynonymTableTests_表記ゆれ表テスト.cs" "BUI_Addin_CSharpアドイン/config_設定例/表記ゆれ.csv"
git commit -m "feat: let the user teach BUI their wording with a synonym table"
```

---

### Task 4: 3層を合成する

第1層・第2層・第3層の結果を、優先順位どおりに1つの計画へまとめる。

**Files:**
- Create: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/ChatQueryBuilder_質問から条件.cs`
- Create: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/ChatQueryBuilderTests_質問から条件テスト.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/CandidateQueryPlan_絞り込み計画.cs`（`CandidateQueryPlan` に項目を追加）

**Interfaces:**
- Consumes: `QuestionTermExtractor.Extract(question, available)` → `ExtractedConditions(Selections, Terms, Remainder)`、`SynonymTable.Match(normalizedText, available)` → `SynonymMatch(Selections, Terms, Problems)`、`SearchTextNormalizer`、既存の `CandidateQueryPlan(Selections, Text, DroppedConditions, FellBackToKeyword, Intent)`
- Produces:
  - `CandidateQueryPlan` に `IReadOnlyList<string> Problems` を追加（既定は空。第2層の理由を運ぶ）
  - `namespace Bui.Llm;`
  - `public static class ChatQueryBuilder`
    - `public static CandidateQueryPlan Build(string? question, IReadOnlyList<CandidateFilterOptions> available, SynonymTable synonyms, IReadOnlyList<string>? llmTerms, ChatIntent intent = ChatIntent.Search);`

- [ ] **Step 1: 失敗するテストを書く**

`tests_テスト/Bui.Tests/Llm_LLM/ChatQueryBuilderTests_質問から条件テスト.cs` を新規作成する。

```csharp
// このファイルの目的:
//   3層の優先順位を固定します。上位の層ほど確からしいという前提を守ります。
// なぜ必要か:
//   質問文に実際に書かれている値より、LLMが挙げた語を優先すると、
//   モデルを替えるたびに結果が動きます。それを防ぐのがこの順序です。
using Bui.Data;
using Bui.Llm;

namespace Bui.Tests.Llm_LLM;

[TestClass]
public class ChatQueryBuilderTests_質問から条件テスト
{
    [TestMethod]
    public void LLM無しでも質問文から条件を作る()
    {
        var plan = ChatQueryBuilder.Build("不燃のボードを教えて", Available(), SynonymTable.Empty, null);

        Assert.AreEqual("不燃", plan.Selections["防火性能"]);
        StringAssert.Contains(plan.Text, "ぼーど");
        Assert.IsFalse(plan.FellBackToKeyword, "質問文をそのまま検索語にしないこと。");
    }

    [TestMethod]
    public void 表記ゆれが第1層の埋めていない項目を埋める()
    {
        var synonyms = new SynonymTable(new[] { new SynonymEntry("PB", "建材分類", "せっこうボード") });

        var plan = ChatQueryBuilder.Build("PBで不燃のもの", Available(), synonyms, null);

        Assert.AreEqual("せっこうボード", plan.Selections["建材分類"]);
        Assert.AreEqual("不燃", plan.Selections["防火性能"]);
    }

    [TestMethod]
    public void 表記ゆれは第1層の条件を上書きしない()
    {
        // 質問文に「準不燃」と書いてあるのに、表の都合で「不燃」にしてはいけません。
        var synonyms = new SynonymTable(new[] { new SynonymEntry("ボード", "防火性能", "不燃") });

        var plan = ChatQueryBuilder.Build("準不燃のボード", Available(), synonyms, null);

        Assert.AreEqual("準不燃", plan.Selections["防火性能"]);
    }

    [TestMethod]
    public void LLMの語は上位が埋めていない項目にだけ入る()
    {
        var plan = ChatQueryBuilder.Build(
            "準不燃のもの", Available(), SynonymTable.Empty, new[] { "不燃", "せっこうボード" });

        Assert.AreEqual("準不燃", plan.Selections["防火性能"], "質問文の値が勝つこと。");
        Assert.AreEqual("せっこうボード", plan.Selections["建材分類"], "空いている項目は埋めてよいこと。");
    }

    [TestMethod]
    public void マスタに無いLLMの語は黙って捨てる()
    {
        var plan = ChatQueryBuilder.Build(
            "不燃のもの", Available(), SynonymTable.Empty, new[] { "存在しない製品" });

        Assert.AreEqual(1, plan.Selections.Count);
        Assert.IsFalse(plan.Text.Contains("存在しない製品"), "利用者の言葉ではないため捨てること。");
    }

    [TestMethod]
    public void 表記ゆれの理由を計画に残す()
    {
        var synonyms = new SynonymTable(new[] { new SynonymEntry("PB", "建材分類", "存在しない分類") });

        var plan = ChatQueryBuilder.Build("PBある？", Available(), synonyms, null);

        Assert.AreEqual(1, plan.Problems.Count);
        StringAssert.Contains(plan.Problems[0], "存在しない分類");
    }

    [TestMethod]
    public void 何も拾えなければ空の計画を返す()
    {
        var plan = ChatQueryBuilder.Build("いい天気ですね", Available(), SynonymTable.Empty, null);

        Assert.AreEqual(0, plan.Selections.Count);
        Assert.AreEqual(string.Empty, plan.Text, "質問文をそのまま検索語にしないこと。");
    }

    [TestMethod]
    public void 会話なら条件を作らない()
    {
        var plan = ChatQueryBuilder.Build(
            "不燃のボード", Available(), SynonymTable.Empty, null, ChatIntent.Chat);

        Assert.AreEqual(ChatIntent.Chat, plan.Intent);
        Assert.AreEqual(0, plan.Selections.Count);
        Assert.AreEqual(string.Empty, plan.Text);
    }

    private static IReadOnlyList<CandidateFilterOptions> Available() => new[]
    {
        new CandidateFilterOptions("建材分類", string.Empty, new[] { "せっこうボード", "ロックウール" }),
        new CandidateFilterOptions("防火性能", string.Empty, new[] { "不燃", "準不燃" })
    };
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~質問から条件テスト"`
Expected: コンパイルエラー。`ChatQueryBuilder` が存在しない。

- [ ] **Step 3: 計画へ Problems を足す**

`CandidateQueryPlan_絞り込み計画.cs` の `CandidateQueryPlan` に、既定値付きで足す。
既定値付きなので既存の呼び出しは壊れない。

**位置引数には足さない。**レコード本体の `init` プロパティにする。
位置引数へ足すと、既存の `new CandidateQueryPlan(...)` の呼び出しと
分解代入の並びに影響が出る。

`CandidateQueryPlan` の本体（`ToQuery` と `Describe` がある波括弧の中）の先頭へ足す。

```csharp
    /// <summary>
    /// 表記ゆれ表の行がマスタと合わなかった理由です。画面へ出します。
    /// 黙って効かない行を残すと、利用者は表を直す機会そのものを失います。
    /// 既定は空。呼び出し側にnull判定を書かせません。
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
```

設定はオブジェクト初期化子で行う（Task 4 の実装がその形になっている）。
既存の `ToQuery` と `Describe` はそのまま残す。

- [ ] **Step 4: 合成を実装する**

`src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/ChatQueryBuilder_質問から条件.cs` を新規作成する。

```csharp
// このファイルの目的:
//   3層（質問文の直接照合・表記ゆれ表・LLMの語）を1つの絞り込み計画にまとめます。
//
// なぜこの順序なのか:
//   上位の層ほど確からしいためです。質問文に実際に書かれている値がいちばん確かで、
//   利用者が育てた表がその次、LLMの推測が最後です。
//   この順序が「モデルを替えても結果が動かない」ことの根拠です。
//   順序を入れ替えると、LLMの機嫌で結果が変わる状態へ戻ります。
//
// 修正時の影響:
//   質問文をそのまま検索語にしないでください。
//   文章はどの製品名にも含まれず、必ず0件になります（2026-09-10に発生）。

using Bui.Data;

namespace Bui.Llm;

/// <summary>質問文から絞り込み計画を組み立てます。純粋関数として保ちます。</summary>
public static class ChatQueryBuilder
{
    public static CandidateQueryPlan Build(
        string? question,
        IReadOnlyList<CandidateFilterOptions> available,
        SynonymTable synonyms,
        IReadOnlyList<string>? llmTerms,
        ChatIntent intent = ChatIntent.Search)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(synonyms);

        // 会話では検索そのものを行わないため、条件を組み立てません。
        if (intent == ChatIntent.Chat)
        {
            return new CandidateQueryPlan(
                new Dictionary<string, string>(StringComparer.Ordinal),
                string.Empty, Array.Empty<string>(), false, ChatIntent.Chat);
        }

        // 第1層。質問文に実際に書かれている値。
        var extracted = QuestionTermExtractor.Extract(question, available);
        var selections = new Dictionary<string, string>(extracted.Selections, StringComparer.Ordinal);
        var terms = new List<string>(extracted.Terms);

        // 第2層。利用者が育てた表。第1層が埋めていない項目にだけ入れます。
        var synonymMatch = synonyms.Match(SearchTextNormalizer.Normalize(question), available);
        foreach (var pair in synonymMatch.Selections)
        {
            if (!selections.ContainsKey(pair.Key)) selections[pair.Key] = pair.Value;
        }

        terms.AddRange(synonymMatch.Terms);

        // 第3層。LLMが挙げた語。マスタと突き合わせ、実在するものだけ採ります。
        // 項目名はLLMに決めさせません。語からBUIが突き止めます。
        foreach (var raw in llmTerms ?? Array.Empty<string>())
        {
            var normalized = SearchTextNormalizer.Normalize(raw);
            if (normalized.Length == 0) continue;

            var option = available.FirstOrDefault(item =>
                item.Values.Any(value =>
                    string.Equals(SearchTextNormalizer.Normalize(value), normalized, StringComparison.Ordinal)));
            if (option is null) continue;
            if (selections.ContainsKey(option.Column)) continue;

            selections[option.Column] = option.Values.First(value =>
                string.Equals(SearchTextNormalizer.Normalize(value), normalized, StringComparison.Ordinal));
        }

        // 条件として採った値は語から外します。二重に絞らないためです。
        var selectedValues = selections.Values
            .Select(SearchTextNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);
        var text = string.Join(
            ' ',
            terms.Where(term => !selectedValues.Contains(term)).Distinct(StringComparer.Ordinal));

        return new CandidateQueryPlan(selections, text, Array.Empty<string>(), false)
        {
            Problems = synonymMatch.Problems
        };
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [ ] **Step 6: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/ChatQueryBuilderTests_質問から条件テスト.cs"
git commit -m "feat: combine the three layers into one search plan"
```

---

### Task 5: ①を語の列挙へ変え、会話へ配線する

LLMに項目名を当てさせるのをやめる。何も拾えなかったときの案内も入れる。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LlmQueryTranslator_質問から条件.cs`
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/LlmChatSession_AI会話.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/LlmQueryTranslatorTests_質問から条件テスト.cs`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/LlmChatSessionTests_AI会話テスト.cs`

**Interfaces:**
- Consumes: `ChatQueryBuilder.Build(question, available, synonyms, llmTerms, intent)`、`SynonymTable`、既存の `ChatHistoryTurn`、`ChatIntent`
- Produces:
  - `LlmQueryTranslator.TranslateTermsAsync(string question, string selectionContext, CancellationToken cancellationToken, IReadOnlyList<ChatHistoryTurn>? history = null)` → `Task<(ChatIntent Intent, IReadOnlyList<string> Terms)>`
  - `LlmChatSession` のコンストラクタに `SynonymTable? synonyms = null` を末尾へ追加

- [ ] **Step 1: 失敗するテストを書く**

`LlmQueryTranslatorTests_質問から条件テスト.cs` に追加する。既存の `RecordingProvider` を使う。

```csharp
    [TestMethod]
    public async Task 語の一覧を読み取る()
    {
        var sut = new LlmQueryTranslator(
            new RecordingProvider(@"{""intent"":""search"",""terms"":[""せっこうボード"",""不燃""]}"),
            true, "m", TimeSpan.FromSeconds(30));

        var result = await sut.TranslateTermsAsync("不燃のボードは？", "", default);

        Assert.AreEqual(ChatIntent.Search, result.Intent);
        CollectionAssert.AreEqual(new[] { "せっこうボード", "不燃" }, result.Terms.ToArray());
    }

    [TestMethod]
    public async Task 語が読めなくても空で返す()
    {
        // 読めないことは異常ではありません。第1層・第2層で検索は動きます。
        var sut = new LlmQueryTranslator(
            new RecordingProvider("これはJSONではありません"), true, "m", TimeSpan.FromSeconds(30));

        var result = await sut.TranslateTermsAsync("不燃のボードは？", "", default);

        Assert.AreEqual(ChatIntent.Search, result.Intent);
        Assert.AreEqual(0, result.Terms.Count);
    }

    [TestMethod]
    public async Task 会話の判定は残す()
    {
        var sut = new LlmQueryTranslator(
            new RecordingProvider(@"{""intent"":""chat"",""terms"":[]}"),
            true, "m", TimeSpan.FromSeconds(30));

        var result = await sut.TranslateTermsAsync("おはよう", "", default);

        Assert.AreEqual(ChatIntent.Chat, result.Intent);
    }

    [TestMethod]
    public async Task 項目の値一覧をプロンプトへ入れない()
    {
        // 値の一覧は長く、小さいモデルには重すぎます。語を挙げさせるだけにします。
        var provider = new RecordingProvider(@"{""intent"":""search"",""terms"":[]}");
        var sut = new LlmQueryTranslator(provider, true, "m", TimeSpan.FromSeconds(30));

        await sut.TranslateTermsAsync("不燃のボードは？", "", default);

        var prompt = string.Join("\n", provider.LastMessages!.Select(m => m.Content));
        Assert.IsFalse(prompt.Contains("絞り込み項目と、選べる値"), "値の一覧を送らないこと。");
        StringAssert.Contains(prompt, "terms");
    }

    [TestMethod]
    public async Task LLMが無効なら空で返す()
    {
        var sut = new LlmQueryTranslator(null, false, "", TimeSpan.FromSeconds(30));

        var result = await sut.TranslateTermsAsync("不燃のボードは？", "", default);

        Assert.AreEqual(ChatIntent.Search, result.Intent);
        Assert.AreEqual(0, result.Terms.Count);
    }
```

`LlmChatSessionTests_AI会話テスト.cs` に追加する。

```csharp
    [TestMethod]
    public async Task LLMが語を返せなくても質問文から検索する()
    {
        // これが今回の修正の要点です。モデルが何も返さなくても検索が動くこと。
        var source = new RecordingCandidateSource(Product("BD-1"));
        var sut = Create(source, translatorJson: "壊れた応答");

        var turn = await sut.AskAsync("不燃のボードを教えて", "", default);

        Assert.IsFalse(turn.Plan.FellBackToKeyword, "質問文をそのまま検索語にしないこと。");
        Assert.AreEqual("せっこうボード", source.LastQuery!.Selections["建材分類"]);
    }

    [TestMethod]
    public async Task 何も拾えなければ案内を出す()
    {
        var sut = Create(new RecordingCandidateSource(), translatorJson: "壊れた応答");

        var turn = await sut.AskAsync("いい天気ですね", "", default);

        StringAssert.Contains(turn.Answer, "絞り込みに使える言葉が見つかりません");
        StringAssert.Contains(turn.Answer, "建材分類", "実データの項目名を案内に含めること。");
    }
```

**注意:** `RecordingCandidateSource` が返す `CandidateFilterOptions` の列名・値は、
そのファイルの現物に合わせること（既存は 建材分類=せっこうボード、厚さ=12.5）。
上のテストはその値を前提にしている。合わなければテスト側を現物に合わせる。

- [ ] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~質問から条件テスト|FullyQualifiedName~AI会話テスト"`
Expected: コンパイルエラー。`TranslateTermsAsync` が存在しない。

- [ ] **Step 3: ①を語の列挙へ変える**

`LlmQueryTranslator_質問から条件.cs` の `SystemInstruction` を差し替える。

```csharp
    private const string SystemInstruction =
        "あなたは、利用者の発言に出てくる建材の言い方を、短い単語として抜き出す補助者です。"
        + "回答はJSONだけを出力してください。形式は "
        + "{\"intent\":\"search\",\"terms\":[\"単語\",\"単語\"]} です。"
        + "製品を探す・比べる・選ぶ話なら intent は \"search\" にし、"
        + "建材の分類・メーカー・製品名・性能・寸法にあたる言葉を terms に入れてください。"
        + "略語や別の言い方は、正式な言い方に直して入れてください（例: PB → せっこうボード）。"
        + "挨拶・雑談・お礼・進め方の相談など、製品を探す話でなければ "
        + "intent を \"chat\" にし、terms を空にしてください。"
        + "助詞や『教えて』のような言い回しは入れないでください。"
        + "説明文やコードブロックの記号は書かないでください。";
```

`TranslateTermsAsync` を足す。既存の `TranslateAsync` は**残す**
（他の呼び出し元があるため。無ければTask 6で消す）。

```csharp
    /// <summary>
    /// 質問に出てくる建材の言い方を、短い語として挙げさせます。
    /// 項目名は決めさせません。語からマスタの項目と値を突き止めるのはBUI側です。
    /// 読めなくても空で返します。第1層・第2層で検索は動くため、異常ではありません。
    /// </summary>
    public async Task<(ChatIntent Intent, IReadOnlyList<string> Terms)> TranslateTermsAsync(
        string question,
        string selectionContext,
        CancellationToken cancellationToken,
        IReadOnlyList<ChatHistoryTurn>? history = null)
    {
        var text = (question ?? string.Empty).Trim();
        if (!_isEnabled || _provider is null)
        {
            return (ChatIntent.Search, Array.Empty<string>());
        }

        var prompt = ChatHistory.Build(history) + "質問: " + text;
        if (!string.IsNullOrWhiteSpace(selectionContext))
        {
            prompt = ChatHistory.Build(history) + selectionContext + "\n質問: " + text;
        }

        if (prompt.Length > LlmProviderSupport.MaximumPromptLength)
        {
            prompt = prompt.Substring(0, LlmProviderSupport.MaximumPromptLength);
        }

        var request = new LlmChatRequest(
            _model,
            new[]
            {
                new LlmMessage("system", SystemInstruction),
                new LlmMessage("user", prompt)
            },
            _timeout);

        try
        {
            var result = await _provider.ChatAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess && result.Error!.Code == LlmProviderSupport.CancelledErrorCode)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value?.Content))
            {
                return (ChatIntent.Search, Array.Empty<string>());
            }

            return ParseTerms(result.Value.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 接続先やパスが混じり得るため、例外の中身は捨てます。
            return (ChatIntent.Search, Array.Empty<string>());
        }
    }

    /// <summary>応答から intent と terms を取り出します。読めなければ search と空を返します。</summary>
    private static (ChatIntent Intent, IReadOnlyList<string> Terms) ParseTerms(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return (ChatIntent.Search, Array.Empty<string>());

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;

            var intent = root.TryGetProperty("intent", out var intentElement)
                && intentElement.ValueKind == JsonValueKind.String
                && string.Equals(intentElement.GetString(), "chat", StringComparison.OrdinalIgnoreCase)
                ? ChatIntent.Chat
                : ChatIntent.Search;

            var terms = new List<string>();
            if (root.TryGetProperty("terms", out var termsElement)
                && termsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in termsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var term = (item.GetString() ?? string.Empty).Trim();
                    if (term.Length > 0) terms.Add(term);
                }
            }

            return (intent, terms);
        }
        catch (JsonException)
        {
            return (ChatIntent.Search, Array.Empty<string>());
        }
    }
```

- [ ] **Step 4: 会話へ配線する**

`LlmChatSession_AI会話.cs` のコンストラクタへ `SynonymTable? synonyms = null` を末尾に足し、
フィールドへ `synonyms ?? SynonymTable.Empty` を保持する。

`AskAsync` の中の、`_translator.TranslateAsync(...)` を呼んでいる箇所を差し替える。

差し替え前:
```csharp
        var plan = await _translator
            .TranslateAsync(question, selectionContext, available.Filters, cancellationToken, history)
            .ConfigureAwait(false);
```

差し替え後:
```csharp
        // LLMは語を挙げるだけです。条件の組み立てはBUIが行います。
        // モデルが何も返せなくても、質問文と表記ゆれ表から条件が作れます。
        var llm = await _translator
            .TranslateTermsAsync(question, selectionContext, cancellationToken, history)
            .ConfigureAwait(false);
        var plan = ChatQueryBuilder.Build(
            question, available.Filters, _synonyms, llm.Terms, llm.Intent);
```

0件のときの案内へ、拾えなかった場合の分岐を足す。
`view.Candidates.Count == 0` の分岐の中、データ元の状態を見た後ろに置く。

```csharp
            // 条件も語も無いなら、探しようがありません。
            // 「条件を1つ減らしてください」は実行できない案内になります。
            if (plan.Selections.Count == 0 && plan.Text.Length == 0)
            {
                return new ChatTurnResult(
                    "質問から絞り込みに使える言葉が見つかりませんでした。\n"
                    + "例えば次のように聞いてください。\n"
                    + BuildExample(available.Filters)
                    + "\n略語や別の言い方は、表記ゆれ.csv に追加すると使えるようになります。",
                    plan, 0, 0, notice);
            }
```

`BuildExample` を同じクラスへ足す。

```csharp
    /// <summary>
    /// 実データから案内の例を作ります。固定の例文を書くと、
    /// データが変わったときに存在しない値を案内してしまいます。
    /// </summary>
    private static string BuildExample(IReadOnlyList<CandidateFilterOptions> filters)
    {
        var parts = filters
            .Where(filter => filter.Values.Count > 0)
            .Take(3)
            .Select(filter => $"{filter.Column}: {filter.Values[0]}")
            .ToArray();
        return parts.Length == 0 ? "　（選べる項目がありません）" : "　" + string.Join(" / ", parts);
    }
```

`plan.Problems` を知らせへ混ぜる。`BuildNotice` の `parts` を組み立てている箇所へ足す。

```csharp
        // 表記ゆれ表の行がマスタと合わないことは、利用者が直せる問題です。
        // 黙って効かない行を残すと、直す機会そのものが無くなります。
        if (plan.Problems.Count > 0)
        {
            parts.Add(string.Join(" / ", plan.Problems));
        }
```

- [ ] **Step 5: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [ ] **Step 6: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。既存テストのうち、①がJSONで `selections` を返す前提のものは
**新しい経路に合わせて書き換える**（消さない）。書き換えた理由を報告に書くこと。

- [ ] **Step 7: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/Llm_LLM接続/" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Llm_LLM/"
git commit -m "feat: ask the model for words and let BUI build the conditions"
```

---

### Task 6: 会話の左右振り分け

質問を右詰め・薄いブルー、回答を左詰めにする。

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiChatViewModel_AI会話画面.cs`（`ChatMessageViewModel`）
- Modify: `BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiChatWindow_AI会話.xaml`
- Test: `BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/BuiChatViewModelTests_AI会話画面テスト.cs`

**Interfaces:**
- Consumes: 既存の `ChatMessageViewModel(string Speaker, string Text, string FilterSummary, bool CanSendToSearchTab, CandidateQueryPlan? Plan)`
- Produces: `ChatMessageViewModel.IsFromUser` — 話し手が利用者なら true

- [ ] **Step 1: 失敗するテストを書く**

`BuiChatViewModelTests_AI会話画面テスト.cs` に追加する。

```csharp
    [TestMethod]
    public async Task 質問と回答を見分けられる()
    {
        // 左右の振り分けをXAMLの文字列比較で書くと、こちらで検証できません。
        // Bui.Addinはこのマシンでコンパイルできないためです。
        var sut = Create();
        sut.Input = "12.5mmは？";

        await sut.SendAsync();

        var question = sut.Messages.First();
        var answer = sut.Messages.Last();
        Assert.IsTrue(question.IsFromUser, "利用者の発言であること。");
        Assert.IsFalse(answer.IsFromUser, "AIの発言であること。");
    }

    [TestMethod]
    public async Task 失敗した回答も利用者の発言にはしない()
    {
        var sut = Create(candidates: new ThrowingCandidateSource());
        sut.Input = "質問";

        try { await sut.SendAsync(); } catch { }

        Assert.IsFalse(sut.Messages.Last().IsFromUser);
    }
```

**注意:** `ThrowingCandidateSource` はそのファイルに既にある。無ければその行を落とし、
1つ目のテストだけにする。

- [ ] **Step 2: 失敗を確認する**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj" --filter "FullyQualifiedName~AI会話画面テスト"`
Expected: コンパイルエラー。`IsFromUser` が存在しない。

- [ ] **Step 3: 判定を足す**

`BuiChatViewModel_AI会話画面.cs` の `ChatMessageViewModel` を本体付きにする。

```csharp
/// <summary>会話1件分の表示です。</summary>
public sealed record ChatMessageViewModel(
    string Speaker, string Text, string FilterSummary, bool CanSendToSearchTab, CandidateQueryPlan? Plan)
{
    /// <summary>話し手の表示名です。この1か所だけで決めます。</summary>
    public const string UserSpeaker = "あなた";

    /// <summary>
    /// 利用者の発言かどうかです。画面の左右と背景を切り替えるために使います。
    /// XAMLで話し手の文字列を比較する書き方にしないでください。
    /// Bui.Addinはこちらでコンパイルできず、間違いに気づけません。
    /// </summary>
    public bool IsFromUser => string.Equals(Speaker, UserSpeaker, StringComparison.Ordinal);
}
```

同じファイルで `"あなた"` と直接書いている箇所（`Messages.Add(new ChatMessageViewModel("あなた", ...)`）を
`ChatMessageViewModel.UserSpeaker` に置き換える。文字列を2か所に置くと、
片方だけ直したときに振り分けが黙って壊れる。

- [ ] **Step 4: テストが通ることを確認する**

Run: 上と同じコマンド
Expected: PASS。

- [ ] **Step 5: XAMLを差し替える**

`BuiChatWindow_AI会話.xaml` の `ItemsControl.ItemTemplate` の中の `StackPanel` を、
枠付きに差し替える。

差し替え前:
```xml
      <StackPanel Margin="0,0,0,10">
       <TextBlock Text="{Binding Speaker}" FontWeight="SemiBold"/>
```

差し替え後:
```xml
      <!-- 質問は右、回答は左に寄せます。左右で話し手が分かるとチャットとして読めます。 -->
      <!-- 振り分けはIsFromUser（真偽値）で行います。話し手の文字列をここで比較すると、
           Bui.Addinはこちらでコンパイルできないため、間違いに気づけません。 -->
      <Border Margin="0,0,0,10" CornerRadius="6" Padding="8"
              MaxWidth="460" HorizontalAlignment="Left" Background="Transparent">
       <Border.Style>
        <Style TargetType="Border">
         <Style.Triggers>
          <DataTrigger Binding="{Binding IsFromUser}" Value="True">
           <Setter Property="HorizontalAlignment" Value="Right"/>
           <Setter Property="Background" Value="#FFE3F2FD"/>
          </DataTrigger>
         </Style.Triggers>
        </Style>
       </Border.Style>
       <StackPanel>
       <TextBlock Text="{Binding Speaker}" FontWeight="SemiBold"/>
```

そして同じテンプレートの末尾、`</StackPanel>` の後ろへ `</Border>` を足す。

差し替え前:
```xml
      </StackPanel>
     </DataTemplate>
```

差し替え後:
```xml
       </StackPanel>
      </Border>
     </DataTemplate>
```

**`HorizontalAlignment` と `Background` を `Border` の属性と `Style` の両方に書いている。**
属性側が既定（左・背景なし）、`Style` の `DataTrigger` が利用者の発言のときだけ上書きする。
`Style` に `Setter` だけを置くと、既定値が無いときに前の項目の見た目を引きずる。

`MaxWidth="460"` はウィンドウ幅640に対して約7割である。全幅だと左右に寄せた意味がなくなる。

- [ ] **Step 6: XAMLがXMLとして解析できることを確認する**

Run:
```bash
python -c "
import xml.etree.ElementTree as ET
ET.parse('BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiChatWindow_AI会話.xaml'); print('OK')
"
```
Expected: `OK`。
このマシンでは `Bui.Addin` をコンパイルできないため、これが唯一の機械的な確認手段である。

- [ ] **Step 7: 全体テストを流す**

Run: `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"`
Expected: 終了コード 0。

- [ ] **Step 8: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/src_ソース/Bui.Shared_共有ロジック/UI_画面/BuiChatViewModel_AI会話画面.cs" "BUI_Addin_CSharpアドイン/src_ソース/Bui.Addin/UI_画面/BuiChatWindow_AI会話.xaml" "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/UI_画面/BuiChatViewModelTests_AI会話画面テスト.cs"
git commit -m "feat: put questions on the right and answers on the left"
```

---

### Task 7: 会社PC向けの手順と問題解決

**Files:**
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt`（UTF-8 BOM + CRLF）
- Modify: `BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md`（UTF-8 BOM）

**Interfaces:**
- Consumes: Task 1〜6 の完成した動き
- Produces: なし（文書のみ）

- [ ] **Step 1: BOMと書式を確認する**

```bash
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt" | xxd -p
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md" | xxd -p
```
Expected: 両方 `efbbbf`。

段階13（末尾）を読み、書式を確かめる。見出しは80個の `=` の行、
次に半角スペース1つで始まる ` 段階14 : タイトル`、また80個の `=`。
節の中は `14-1.` 形式、角括弧・鍵括弧で始まる手順は番号の後に空白なし、
平文で始まる手順は空白あり、継続行は6桁字下げ、確認は `■ここまでの確認` の下に3桁字下げ。

- [ ] **Step 2: 段階14を書く**

`操作手順_会社PC_次にやること.txt` の末尾へ足す。

```
================================================================================
 段階14 : AIチャットで複数条件の検索ができるか確かめる
================================================================================

14-1. GitHubから最新を取得し、Visual Studioでビルドします。

14-2. scripts_開発補助\DeployDev_開発用配置.ps1 を実行します。

14-3. config_設定例\表記ゆれ.csv を、製品CSVと同じフォルダへコピーします。
      【設定】タブの「データフォルダ」に指定しているフォルダです。

■ここまでの確認
   コピー先に 表記ゆれ.csv があること。

14-4. Revitを起動し、リボンの「AI」を押します。

14-5.「不燃のボードを教えて」と入力して［送信］を押します。

■ここまでの確認
   候補が出ること。0件にならないこと。
   緑色の行に「絞り込み: 防火性能=不燃 …」のように、拾った条件が出ていること。

      これが今回の修正の要点です。LLMが何も返せなくても、
      BUIが質問文から条件を拾うため検索が動きます。

14-6.「PBで不燃のものある？」と入力して［送信］を押します。

■ここまでの確認
   「絞り込み: 建材分類=せっこうボード ／ 防火性能=不燃」のように出ること。
   表記ゆれ.csv の PB が効いています。

      値が出ない場合は、表記ゆれ.csv の「値」列が、実際のマスタの値と
      一致していない可能性があります。状態欄に理由が出ます。

14-7.【設定】タブでモデルを別のものに切り替え、Revitを再起動して
      14-5をもう一度行います。

■ここまでの確認
   モデルを替えても同じ条件が拾えること。
   結果がモデルに左右されないことの確認です。

14-8. 会話欄の見た目を確認します。

■ここまでの確認
   自分の質問が右に寄り、背景が薄いブルーになっていること。
   AIの回答が左に寄っていること。
```

- [ ] **Step 3: Troubleshootingへ節を足す**

`Troubleshooting_問題解決.md` の末尾へ足す。

```markdown
## AIチャットで「絞り込みに使える言葉が見つかりませんでした」と出る

質問文の中に、マスタに実在する言葉が1つも見つからなかったという意味です。

### 対処

1. 画面に出ている例（「建材分類: … / 防火性能: …」）の言い方で聞き直します。
2. それでも通らない言い方は、`表記ゆれ.csv` に追加します。

`表記ゆれ.csv` は製品CSVと同じフォルダに置きます。3列です。

```csv
言い方,項目,値
PB,建材分類,せっこうボード
```

| 列 | 意味 |
| --- | --- |
| 言い方 | 現場で使う言葉。質問文にこれが含まれていれば効きます |
| 項目 | 絞り込み項目の名前。空にすると検索語として足します |
| 値 | マスタに実在する値。実在しない値を書くと、その行は無視されます |

**追加にRevitの再起動もビルドも要りません。**次の質問から効きます。

## 表記ゆれ.csvの行が効かない

状態欄に理由が出ています。

| 出る文言 | 意味 |
| --- | --- |
| 項目「…」は絞り込み項目にありません | 「項目」列の名前が【検索】タブの項目名と違います |
| 値「…」は「…」にありません | 「値」列がマスタに実在しません。【検索】タブの選択肢と見比べてください |
| 見出しは 言い方,項目,値 にしてください | 1行目の見出しが違います |
| 表記ゆれCSVを読み取れませんでした | ファイルが壊れているか、開いたままです。Excelを閉じてください |

**マスタの値は【検索】タブの各項目の選択肢と同じ文字列です。**
そこからコピーして貼り付けるのが確実です。

## AIチャットの結果がモデルによって変わる

**条件の組み立てはBUIが行うため、モデルを替えても拾う条件は変わりません。**
変わるのは回答の文章の質だけです。

もしモデルを替えて拾える条件が変わるようなら、それは不具合です。
そのときの質問文と、緑色の「絞り込み:」の行をBUIの管理者へお伝えください。
```

- [ ] **Step 4: BOMと改行を確認する**

```bash
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt" | xxd -p
head -c 3 "BUI_Addin_CSharpアドイン/docs_説明書/Troubleshooting_問題解決.md" | xxd -p
python -c "
b=open(r'BUI_Addin_CSharpアドイン/docs_説明書/操作手順_会社PC_次にやること.txt','rb').read()
print('LFonly', b.count(b'\n')-b.count(b'\r\n'))"
```
Expected: BOMが両方 `efbbbf`、`LFonly` が `0`。

- [ ] **Step 5: コミット**

```bash
git add "BUI_Addin_CSharpアドイン/docs_説明書/"
git commit -m "docs: add the chat search walkthrough and the synonym table guide"
```

---

## 完了時の確認

- [ ] `.superpowers/dotnet-sdk-8/dotnet test "BUI_Addin_CSharpアドイン/tests_テスト/Bui.Tests/Bui.Tests.csproj"` が終了コード 0
- [ ] `git grep -n "<<<<<<<" -- "BUI_Addin_CSharpアドイン"` が何も返さない
- [ ] 2つの説明書のBOMが `efbbbf`、`.txt` はCRLFのまま
- [ ] `config_設定例/表記ゆれ.csv` のBOMが `efbbbf`
- [ ] `BuiChatWindow_AI会話.xaml` がXMLとして解析できる
- [ ] `Bui.Shared_共有ロジック` に `Autodesk.Revit` の参照が無い

## 私物PCでは確認できないもの（会社PCでの確認事項）

- 利用者のモデルが `terms` を返すこと（返さなくても第1層・第2層で動く）
- 「不燃のボードを教えて」で候補が出ること
- 「PBで不燃のものある？」で表記ゆれが効くこと
- モデルを替えても拾う条件が変わらないこと
- 吹き出しが左右に分かれ、質問の背景が薄いブルーになること
