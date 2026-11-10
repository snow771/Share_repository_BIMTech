// このファイルは単価データの検索・更新に必要な共通契約を定め、CSV実装と利用側を分離するために存在します。
// 製品・地域・適用日の条件やメソッドの形を変えると、見積処理とすべての単価データ実装に影響します。
using Bui.Products;

namespace Bui.Data;

/// <summary>製品、地域、基準日、最大件数による単価検索条件です。</summary>
public sealed record PriceQuery(string? ProductId, string? Region, DateOnly? EffectiveOn, int Limit = 100);

/// <summary>単価情報をデータ保存方式に依存せず利用するための窓口です。</summary>
public interface IPriceRepository
{
    Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery query, CancellationToken cancellationToken);

    Task UpsertAsync(PriceRecord record, CancellationToken cancellationToken);
}
