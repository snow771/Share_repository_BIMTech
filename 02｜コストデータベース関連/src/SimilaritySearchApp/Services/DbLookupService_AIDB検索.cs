using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Llm;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>AI が決めた「どのテーブルを・どの言葉で・どの列から探すか」。</summary>
public sealed record LookupPlan(string Schema, string Table, List<string> Keywords, List<string> SearchColumns, List<string> ShowColumns, string Reason);

/// <summary>検索結果（列名・行・該当件数）。</summary>
public sealed record LookupRows(DbTableInfo Table, List<string> Columns, List<List<string?>> Rows, int Total, List<string> Keywords, bool MatchedAll);

/// <summary>
/// AI アシスタントの「データベースを調べて答える」（例:「タイルの標準単価は？」「内装工事の協力業者は？」）。
///
///   1. テーブル一覧（列名つき）を見せて、AI に 調べるテーブル・キーワード・探す列・表示する列 を JSON で選ばせる
///      （テーブル名は選択肢から選ばせ、列名は実在するものだけを使う。SQL は AI に書かせない）
///   2. C# が読み取り専用の SELECT（パラメータ付き・最大 50 行）で検索する
///   3. 検索結果だけを根拠に、AI に答えさせる
/// 変更履歴のテーブルは対象外。
/// </summary>
public static class DbLookupService
{
    public const int MaxRows = 50;
    private const int MaxCatalogColumns = 40;
    private const int MaxShowColumns = 12;
    private const int MaxAnswerRows = 30;
    private const int MaxCellChars = 40;

    /// <summary>検索の対象にしないテーブル（変更履歴）</summary>
    public static bool IsExcluded(DbTableInfo t, TableNames names) =>
        string.Equals(t.Name, names.ChangeBatch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(t.Name, names.ChangeRow, StringComparison.OrdinalIgnoreCase);

    public static string Key(DbTableInfo t) => $"{t.Schema}.{t.Name}";

    /// <summary>AI に見せるテーブル一覧（1 行 1 テーブル: 名前・日本語名・行数・列名）。</summary>
    public static string BuildCatalog(IEnumerable<DbTableInfo> tables, IEnumerable<ColumnRef> columns)
    {
        var byTable = columns.GroupBy(c => (c.Schema, c.Table)).ToDictionary(g => g.Key, g => g.Select(c => c.Column).ToList());
        var sb = new StringBuilder();
        foreach (var t in tables)
        {
            var cols = byTable.TryGetValue((t.Schema, t.Name), out var list) ? list : new List<string>();
            var shown = cols.Take(MaxCatalogColumns).ToList();
            var more = cols.Count > shown.Count ? $" …ほか{cols.Count - shown.Count}列" : "";
            var role = t.AppRole is null ? "" : $"・アプリでの役割: {t.AppRole}";
            sb.AppendLine($"- {Key(t)}（{t.Label}・{t.RowCount:N0}行{role}）列: {string.Join(", ", shown)}{more}");
        }
        return sb.ToString().TrimEnd();
    }

    public static JsonObject BuildPlanSchema(IEnumerable<string> tableKeys)
    {
        static JsonObject StrArray() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["table"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(tableKeys.Select(k => (JsonNode)k).ToArray()) },
                ["keywords"] = StrArray(),
                ["search_columns"] = StrArray(),
                ["show_columns"] = StrArray(),
                ["reason"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("table", "keywords", "search_columns", "show_columns", "reason"),
            ["additionalProperties"] = false,
        };
    }

    public static List<ChatMessage> BuildPlanPrompt(string question, IReadOnlyList<ChatMessage> history, string catalog)
    {
        var system = $"""
あなたはデータベース検索の計画係です。ユーザーの質問に答えるために、下のテーブル一覧から調べるテーブルを1つ選び、検索の方法を JSON で返してください。
- table: 調べるテーブル（一覧の「スキーマ.テーブル名」から選ぶ）。標準単価・材料の単価は建築材料マスタ系、物件の情報は物件情報系など、日本語名と列名から判断する
- keywords: 行を絞り込む言葉（例: タイル、内装）。質問の中の具体的な名詞だけを、短く1〜3個。「標準単価」「協力業者」のようにテーブルや列そのものを表す言葉は入れない。全件を見たいときは空
- search_columns: keywords を探す列（名称・品名・工種・会社名など文字の列）。分からなければ空（文字の列すべてを探す）
- show_columns: 答えに必要な列（名称・規格・単位・単価・会社名など、最大12列）。分からなければ空
- reason: 選んだ理由（短く）
列名は一覧に書かれているものだけを使ってください。

# テーブル一覧
{catalog}
""";
        var messages = new List<ChatMessage> { new("system", system) };
        messages.AddRange(history.TakeLast(4));
        messages.Add(new ChatMessage("user", question));
        return messages;
    }

    /// <summary>AI の計画を、実在するテーブル・列だけに絞って読み取る。</summary>
    public static LookupPlan ParsePlan(JsonObject ai, IReadOnlyList<DbTableInfo> tables)
    {
        var key = Str(ai["table"]);
        var table = tables.FirstOrDefault(t => Key(t) == key)
                    ?? tables.FirstOrDefault(t => t.Name == key || Key(t).EndsWith("." + key, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"AI が選んだテーブル「{key}」が見つかりません。");
        return new LookupPlan(table.Schema, table.Name,
            Strs(ai["keywords"]).Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().Take(3).ToList(),
            Strs(ai["search_columns"]), Strs(ai["show_columns"]), Str(ai["reason"]));
    }

    /// <summary>キーワードの表記ゆれ（全角カナ ⇔ 半角カナ）。DB に「ﾀｲﾙ」のような半角カナで入っていても見つかるようにする。</summary>
    public static List<string> KeywordVariants(string keyword)
    {
        var full = keyword.Normalize(NormalizationForm.FormKC);   // 半角カナ → 全角カナ、全角英数 → 半角
        var variants = new List<string> { keyword, full, ToHalfKana(full) };
        return variants.Where(v => v.Length > 0).Distinct().ToList();
    }

    private static readonly Lazy<Dictionary<string, string>> HalfKana = new(() =>
    {
        // 半角カナ（U+FF66〜U+FF9D）と濁点・半濁点付きを NFKC で全角にした対応表を逆にする
        var map = new Dictionary<string, string>();
        for (var c = 'ｦ'; c <= 'ﾝ'; c++)
        {
            var h = c.ToString();
            map.TryAdd(h.Normalize(NormalizationForm.FormKC), h);
            foreach (var mark in new[] { "ﾞ", "ﾟ" })
            {
                var composed = (h + mark).Normalize(NormalizationForm.FormKC);
                if (composed.Length == 1) map.TryAdd(composed, h + mark);
            }
        }
        map.TryAdd("ー", "ｰ");
        map.TryAdd("・", "･");
        return map;
    });

    public static string ToHalfKana(string s)
    {
        var sb = new StringBuilder(s.Length * 2);
        foreach (var ch in s) sb.Append(HalfKana.Value.TryGetValue(ch.ToString(), out var h) ? h : ch.ToString());
        return sb.ToString();
    }

    /// <summary>答えを作る AI への指示（検索結果の表を TSV で渡す）。</summary>
    public static List<ChatMessage> BuildAnswerPrompt(string question, LookupRows r)
    {
        const string system = """
あなたは建設会社の積算担当を手伝う AIアシスタントです。データベースを検索した結果（下の表）だけを根拠に、ユーザーの質問に日本語で簡潔に答えてください。
- 表に無いことは推測しない。数値は表の値のまま（単価なら「円」など分かる単位を添える）
- 該当が多いときは代表的なものを数件挙げ、全件は「DB編集で開く」から確認できると伝える
- 該当が0件のときは見つからなかったことを伝え、別の言い方（半角カナ・略称・上位の工種名など）を提案する
- 箇条書きを使ってよい。前置きは短く
""";
        var match = r.Keywords.Count == 0 ? "（キーワードなし・先頭から）"
            : r.MatchedAll ? $"「{string.Join("」と「", r.Keywords)}」をすべて含む行"
            : $"「{string.Join("」または「", r.Keywords)}」のどれかを含む行（すべてを含む行は無し）";
        var user = $"""
質問: {question}

検索したテーブル: {r.Table.Label}（{Key(r.Table)}）
検索条件: {match}
該当件数: {r.Total} 件（下の表は先頭 {Math.Min(r.Rows.Count, MaxAnswerRows)} 件）

{Tsv(r)}
""";
        return new List<ChatMessage> { new("system", system), new("user", user) };
    }

    /// <summary>表をタブ区切りにする（長いセルは切る）。</summary>
    public static string Tsv(LookupRows r)
    {
        if (r.Rows.Count == 0) return "（該当する行はありません）";
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", r.Columns));
        foreach (var row in r.Rows.Take(MaxAnswerRows))
        {
            sb.AppendLine(string.Join("\t", row.Select(v =>
            {
                var t = (v ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace("\r", "");
                return t.Length > MaxCellChars ? t[..MaxCellChars] + "…" : t;
            })));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>表示する列を決める（AI の指定 → 無ければ先頭の列。最大 12 列、バイナリ列は除く）。</summary>
    public static List<DbColumnInfo> ShowColumns(List<DbColumnInfo> all, IEnumerable<string> requested)
    {
        var usable = all.Where(c => !DbValueConverter.IsBinary(c.TypeName)).ToList();
        var picked = requested.Select(n => usable.FirstOrDefault(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
            .OfType<DbColumnInfo>().Distinct().ToList();
        if (picked.Count == 0) picked = usable;
        return picked.Take(MaxShowColumns).ToList();
    }

    /// <summary>キーワードを探す列（AI の指定のうち文字の列 → 無ければ文字の列すべて）。</summary>
    public static List<DbColumnInfo> SearchColumns(List<DbColumnInfo> all, IEnumerable<string> requested)
    {
        var text = all.Where(c => IsText(c.TypeName)).ToList();
        var picked = requested.Select(n => text.FirstOrDefault(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
            .OfType<DbColumnInfo>().Distinct().ToList();
        return picked.Count > 0 ? picked : text;
    }

    public static bool IsText(string typeName) =>
        typeName.ToLowerInvariant() is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";

    /// <summary>チャットに添える出典（テーブル名・件数）。</summary>
    public static string SourceNote(LookupRows r) =>
        $"📋 参照: {r.Table.Label}（{Key(r.Table)}）" +
        (r.Keywords.Count == 0 ? "" : $"・検索語「{string.Join("」「", r.Keywords)}」") +
        $"・該当 {r.Total.ToString("N0", CultureInfo.InvariantCulture)} 件";

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    private static List<string> Strs(JsonNode? n) =>
        (n as JsonArray ?? new JsonArray()).Select(Str).Where(s => s.Length > 0).ToList();
}
