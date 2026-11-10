// このファイルは製品データの検索・取得・更新に必要な共通契約を定め、CSV実装と利用側を分離するために存在します。
// 問合せ条件やメソッドの形を変えると、画面・サービス層とすべての製品データ実装に影響します。
using Bui.Products;

namespace Bui.Data;

/// <summary>製品の全文検索、項目絞込み、最大件数を表します。</summary>
public sealed record ProductQuery(string Text, IReadOnlyDictionary<string, string> Filters, int Limit = 50)
{
    /// <summary>
    /// 画面表示用の上限（既定200件）を外し、該当する全行を受け取るための指定です。
    /// 採番と絞り込み選択肢の算出は「全件が揃っていること」が正しさの前提です。
    /// 一部だけを見て採番すると、201件目以降にある最大のBM_NEW-番号を見落とし、
    /// 既に使われているIDを再発行してUpsertが顧客の実データ行を上書きします。
    /// 画面の一覧表示には使わないでください（数万行を一度に描画してしまいます）。
    /// </summary>
    public const int NoLimit = int.MaxValue;
}

/// <summary>製品情報をデータ保存方式に依存せず利用するための窓口です。</summary>
public interface IProductRepository
{
    Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<ProductRecord?> GetByIdAsync(string productId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery query, CancellationToken cancellationToken);

    Task UpsertAsync(ProductRecord record, CancellationToken cancellationToken);

    /// <summary>指定したProduct_IDの行を削除します。新規登録の取り消しに使います。</summary>
    Task RemoveAsync(string productId, CancellationToken cancellationToken);
}
