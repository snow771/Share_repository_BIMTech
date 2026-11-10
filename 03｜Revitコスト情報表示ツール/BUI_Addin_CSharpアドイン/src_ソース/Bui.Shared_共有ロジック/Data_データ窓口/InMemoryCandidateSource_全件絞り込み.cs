// このファイルは、製品リポジトリの全件をメモリ上で絞り込む実装です。
// PoCのCSVは数百件規模のため、全件を1回取得して純粋関数へ渡すのが最も単純で速い方法です。
// 修正時の影響: 件数が数十万規模になったら、この実装を
// サーバー側で絞るSQL実装へ差し替えます。画面は変更不要です。
using Bui.Products;

namespace Bui.Data;

/// <summary>全件を取得し、カスケード絞り込みをメモリ上で適用します。</summary>
public sealed class InMemoryCandidateSource : ICandidateSource
{
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

        // 絞り込みと選択肢の算出は全件が揃っていないと成り立たないため、ここで1回だけ全件を取得します。
        // 以前は検索APIの表示上限200件に合わせており、201件目以降の製品が候補にも
        // プルダウンの選択肢にも一切現れませんでした（黙って消えるため気づけません）。
        // 表示件数の打ち切りはProductCandidateFilterがquery.Limitで行い、IsTruncatedで知らせます。
        var all = await _products
            .SearchAsync(new ProductQuery(string.Empty, new Dictionary<string, string>(), ProductQuery.NoLimit), cancellationToken)
            .ConfigureAwait(false);

        return ProductCandidateFilter.Apply(all, _mapping, _filterColumns, query);
    }
}
