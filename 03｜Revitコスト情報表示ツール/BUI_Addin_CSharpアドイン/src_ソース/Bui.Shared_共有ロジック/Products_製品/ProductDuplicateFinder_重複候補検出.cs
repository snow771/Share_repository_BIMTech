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
