// このファイルの目的:
//   仕上表に並ぶ製品配置(ProductPlacement)のProduct_IDを、いま使っている
//   製品データソース(IProductRepository)へ問い合わせ、一般名称（Spec_Master.csvの
//   display_name_ja="一般名称"の行から来る属性）を解決します。
//
// なぜID単位で1回だけ照会するのか:
//   同じProduct_IDが壁・床・天井など複数の配置に何度も登場します。
//   配置の件数だけ照会すると、大きなプロジェクトで同じ製品へ何百回も問い合わせることになります。
//   distinctな非空Product_IDだけを集め、1IDにつき1回だけGetByIdAsyncを呼びます。
//
// Resolved/ProductMissing/GeneralNameMissingの区別:
//   - ProductMissing: リポジトリにその製品IDの行自体が無い（参照切れ）。
//   - GeneralNameMissing: 製品はあるが、一般名称の属性が無い、または空文字。
//   - Resolved: 一般名称の値が取れた。
//   呼び出し側（帳票組立）がこれを区別できないと、「製品が無い」と「名称が未入力」を
//   同じ表示にしてしまい、原因調査ができません。
//
// 修正時の影響:
//   IDの突き合わせは大文字小文字を無視し、前後の空白を除いて行います
//   （GoldStandardProductRepositoryが大文字小文字を無視して更新・削除する既存挙動と揃えます）。

using Bui.Data;

namespace Bui.FinishSchedules;

/// <summary>製品配置のProduct_IDを、いまのデータソースの一般名称へ解決します。</summary>
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
