using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>工種・工事項目・項目のヒント（AI の出力、または辞書からの推定）。</summary>
public sealed record TradeHint(string Trade, string Work, string Item);

/// <summary>実データに存在する 工種項目/工事項目/項目 の組。</summary>
public sealed record TradePath(string Trade, string Work, string Item);

/// <summary>
/// 依頼文から工種を推定する（Python の infer_trade_from_request）と、
/// 実データの階層を逆引きして最も合う組を選ぶ（Python の _app_resolve_trade_path）。
/// </summary>
public static class TradeInferenceService
{
    private const string U = FilterCriteria.Unspecified;

    /// <summary>辞書との一致で工種を推定する。工種語+5、工事項目語+7、項目語+9。</summary>
    public static TradeHint InferFromRequest(string request)
    {
        var nRaw = TextNormalizer.NormKey(request);
        var bestScore = 0;
        var best = new TradeHint(U, U, U);

        foreach (var e in TradeMaster.Entries)
        {
            var score = 0;
            var matchedItem = U;
            foreach (var a in e.TradeAliases)
                if (Hit(a, nRaw)) score += 5;
            foreach (var a in e.WorkAliases)
            {
                if (!Hit(a, nRaw)) continue;
                score += 7;
                if (matchedItem == U) matchedItem = a;
            }
            foreach (var a in e.ItemAliases)
            {
                if (!Hit(a, nRaw)) continue;
                score += 9;
                matchedItem = a;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = new TradeHint(e.Name, e.WorkAliases.Length > 0 ? e.WorkAliases[0] : U, matchedItem);
            }
        }
        return best;
    }

    /// <summary>完全一致なら exact、部分一致（どちらかが含む）なら partial、それ以外 0。</summary>
    public static int ScoreMatch(string term, string value, int exact, int partial)
    {
        var t = TextNormalizer.NormKey(term);
        var v = TextNormalizer.NormKey(value);
        if (t.Length == 0 || v.Length == 0) return 0;
        if (t == v) return exact;
        if (t.Contains(v, StringComparison.Ordinal) || v.Contains(t, StringComparison.Ordinal)) return partial;
        return 0;
    }

    /// <summary>査定・修正の明細から、重複を除いた 工種/工事項目/項目 の組を出現順で返す。</summary>
    public static List<TradePath> BuildHierarchy(IEnumerable<CostRecord> costs)
    {
        var seen = new HashSet<(string, string, string)>();
        var list = new List<TradePath>();
        foreach (var c in costs)
        {
            var t = (c.Trade ?? "").Trim();
            var w = (c.Work ?? "").Trim();
            var i = (c.Item ?? "").Trim();
            if (t.Length == 0 && w.Length == 0 && i.Length == 0) continue;
            if (!seen.Add((t, w, i))) continue;
            list.Add(new TradePath(t, w, i));
        }
        return list;
    }

    /// <summary>
    /// AI のヒントと依頼文から、実データの階層で最もスコアの高い組を選ぶ。
    /// 工種は現在の選択肢（tradeOptions）に寄せる。スコア0以下なら全て未指定。
    /// </summary>
    public static TradePath Resolve(TradeHint aiHints, string request, IReadOnlyList<TradePath> records, IReadOnlyList<string> tradeOptions)
    {
        var none = new TradePath(U, U, U);
        if (records.Count == 0) return none;

        var raw = request ?? "";
        var inferred = InferFromRequest(raw);
        var tradeHint = Fallback(aiHints.Trade, inferred.Trade);
        var workHint = Fallback(aiHints.Work, inferred.Work);
        var itemHint = Fallback(aiHints.Item, inferred.Item);

        var nRaw = TextNormalizer.NormKey(raw);
        var rawHits = TradeMaster.Entries
            .SelectMany(e => e.TradeAliases.Concat(e.WorkAliases).Concat(e.ItemAliases))
            .Where(a => Hit(a, nRaw))
            .ToList();

        var bestScore = -1;
        TradePath? best = null;
        foreach (var rec in records)
        {
            var score = 0;
            score += ScoreMatch(tradeHint, rec.Trade, 90, 50);
            score += ScoreMatch(tradeHint, rec.Work, 50, 25);
            score += ScoreMatch(tradeHint, rec.Item, 35, 18);
            score += ScoreMatch(workHint, rec.Work, 100, 60);
            score += ScoreMatch(workHint, rec.Item, 55, 30);
            score += ScoreMatch(itemHint, rec.Item, 120, 70);
            score += ScoreMatch(itemHint, rec.Work, 45, 24);
            foreach (var alias in rawHits)
            {
                score += ScoreMatch(alias, rec.Trade, 30, 14);
                score += ScoreMatch(alias, rec.Work, 36, 18);
                score += ScoreMatch(alias, rec.Item, 40, 20);
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = rec;
            }
        }

        if (best is null || bestScore <= 0) return none;

        var trade = PickFromOptions(tradeOptions, OrUnspecified(best.Trade));
        return new TradePath(trade, OrUnspecified(best.Work), OrUnspecified(best.Item));
    }

    // ------------------------------------------------------------------

    private static bool Hit(string alias, string normalizedText)
    {
        var a = TextNormalizer.NormKey(alias);
        return a.Length > 0 && normalizedText.Contains(a, StringComparison.Ordinal);
    }

    /// <summary>AI ヒントが空・未指定なら推定値を使う（推定値の未指定はそのまま残す）。</summary>
    private static string Fallback(string? aiValue, string inferred)
    {
        var v = TextNormalizer.Coalesce(aiValue, "");
        return v.Length == 0 || v == U ? TextNormalizer.Coalesce(inferred, "") : v;
    }

    private static string OrUnspecified(string s) => string.IsNullOrEmpty(s) ? U : s;

    private static string PickFromOptions(IReadOnlyList<string> options, string value)
    {
        if (options.Contains(value)) return value;
        var nValue = TextNormalizer.NormKey(value);
        foreach (var opt in options)
        {
            if (opt == U) continue;
            var nOpt = TextNormalizer.NormKey(opt);
            if (nOpt.Length == 0) continue;
            if (nOpt == nValue) return opt;
        }
        foreach (var opt in options)
        {
            if (opt == U) continue;
            var nOpt = TextNormalizer.NormKey(opt);
            if (nOpt.Length == 0) continue;
            if (nValue.Contains(nOpt, StringComparison.Ordinal) || nOpt.Contains(nValue, StringComparison.Ordinal)) return opt;
        }
        return U;
    }
}
