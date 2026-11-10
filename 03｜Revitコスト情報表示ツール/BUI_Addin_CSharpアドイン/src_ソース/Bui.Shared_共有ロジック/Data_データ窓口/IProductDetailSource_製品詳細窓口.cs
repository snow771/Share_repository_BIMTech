// このファイルは画面と製品詳細実装の唯一の境界です。
// 概要タブはこの契約だけを見るため、CSVからSQL Serverへ実装を差し替えても
// 画面とそのテストは変更が要りません。ICandidateSourceと同じ役割です。
// 修正時の影響: 署名を変えると、将来のSQL実装と概要タブの両方に波及します。
using Bui.Products;

namespace Bui.Data;

/// <summary>Product_IDから、概要タブの製品カードに出す内容を取得します。</summary>
public interface IProductDetailSource
{
    /// <summary>見つからない場合はnullを返します。例外は投げません。</summary>
    Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken);
}
