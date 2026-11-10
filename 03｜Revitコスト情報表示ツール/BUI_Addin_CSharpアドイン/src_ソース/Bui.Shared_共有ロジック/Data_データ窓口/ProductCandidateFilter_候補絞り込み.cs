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
        string ignoreColumn)
    {
        var values = products
            .Where(record => Matches(record, mapping, filterColumns, selections, text, ignoreColumn))
            .Select(record => ProductFieldAccessor.Read(record, mapping, ignoreColumn))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return OrderValues(values);
    }

    /// <summary>
    /// 数値として読める値を先に数値順で並べ、読めない値をその後ろに文字列順で並べます。
    ///
    /// 全体を数値順か文字列順かの二択にすると、実データで破綻します。
    /// 厚さの実データには「5 or 8」という値が1件だけ含まれており、
    /// 「1つでも数値でなければ全体を文字列順」にすると、
    /// 8件中7件が数値なのに 100 → 12.5 → 2 → 2.3 … と並んでしまい、
    /// 目的の厚さを一覧から見つけられなくなります。
    ///
    /// 数値でない値を捨てないこと、順序が実行のたびに変わらないことの両方を守ります。
    /// </summary>
    private static IReadOnlyList<string> OrderValues(IReadOnlyList<string> values)
    {
        var numeric = new List<(string Value, decimal Number)>();
        var text = new List<string>();

        foreach (var value in values)
        {
            if (decimal.TryParse(
                    value,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number))
            {
                numeric.Add((value, number));
            }
            else
            {
                text.Add(value);
            }
        }

        // 同じ数値を表す別々の文字列（12.5 と 12.50）が混ざっても順序が揺れないよう、
        // 数値が同じ場合は文字列で決着させます。揺れると一覧が更新のたびに並び替わります。
        return numeric
            .OrderBy(item => item.Number)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Select(item => item.Value)
            .Concat(text.OrderBy(value => value, StringComparer.Ordinal))
            .ToArray();
    }

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
}
