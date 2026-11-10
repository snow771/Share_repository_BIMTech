// このファイルは新規登録の一連の流れをまとめます。
// 採番 → CSVへ追記 → Revitへ付与、の途中で失敗したときに
// CSVだけへ幽霊レコードが残らないよう、追加した行を取り消します。
// 修正時の影響: 取り消しを外すと、実体の無いProduct_IDがCSVへ蓄積します。
using System.IO;
using Bui.Common;
using Bui.Configuration;
using Bui.Data;

namespace Bui.Products;

/// <summary>新規製品の採番・追記・付与を、失敗時の取り消し込みで実行します。</summary>
public sealed class ProductRegistrationCoordinator
{
    private readonly IProductRepository _products;

    public ProductRegistrationCoordinator(IProductRepository products) =>
        _products = products ?? throw new ArgumentNullException(nameof(products));

    /// <param name="applyAsync">採番したIDをRevitへ付与する処理。失敗したら追記を取り消します。</param>
    /// <param name="confirmedProductId">
    /// 「内容を確認」で利用者へ提示した採番予定ID。渡された場合、ここで採番し直した結果と
    /// 一致するかを検証します。一致しなければ登録を中止します（CSVには何も書きません）。
    /// 提示したIDと実際に書くIDが違うと、利用者は自分が見ていない番号の製品を作ったことになり、
    /// 手元のメモや図面注記と実データが食い違います。
    /// </param>
    public async Task<OperationResult<string>> RegisterAsync(
        NewProductDraft draft,
        DataSettings settings,
        Func<string, CancellationToken, Task<OperationResult<string>>> applyAsync,
        CancellationToken cancellationToken,
        string? confirmedProductId = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applyAsync);

        var missing = draft.IsMissingRequired(settings.NewProductRequiredColumns);
        if (missing.Count > 0)
        {
            return OperationResult<string>.Failure(
                "NEW_PRODUCT_REQUIRED_MISSING",
                $"必須項目（{string.Join("、", missing)}）を入力してください。",
                $"missing={string.Join(",", missing)}");
        }

        // 全件で採番します。上限200件で採番すると、201件目以降にある最大のBM_NEW-番号を
        // 見落として同じIDを再発行し、UpsertAsyncがそのProduct_IDの既存行（顧客の実データ）を
        // 上書きします。さらに付与に失敗した場合、取り消しがその行を削除して失われます。
        var existing = await _products
            .SearchAsync(new ProductQuery(string.Empty, new Dictionary<string, string>(), ProductQuery.NoLimit), cancellationToken)
            .ConfigureAwait(false);

        var allocated = ProductIdAllocator.Next(
            existing.Select(record => record.ProductId), settings.NewProductIdPrefix, settings.NewProductIdDigits);
        if (!allocated.IsSuccess) return allocated;

        var newId = allocated.Value!;
        // 確認時に見せたIDと今の採番結果が違う＝確認後に誰かが製品を追加しています。
        // 黙って別のIDで登録せず、いったん止めて再確認を求めます。
        var confirmed = (confirmedProductId ?? string.Empty).Trim();
        if (confirmed.Length > 0 && !string.Equals(confirmed, newId, StringComparison.Ordinal))
        {
            return OperationResult<string>.Failure(
                "NEW_PRODUCT_ID_CHANGED",
                $"確認時の採番予定ID「{confirmed}」は使えなくなりました（現在は「{newId}」）。もう一度「内容を確認」を押してください。",
                $"confirmed={confirmed}; allocated={newId}");
        }
        // ここでの例外はマスタに無い値や書き込み不可などの「利用者が直せる理由」で、
        // 書き込みが行われる前にリポジトリが投げます（GoldStandardProductRepository.UpsertAsync参照）。
        // 何もCSVへ書かれていないので取り消しは不要ですが、握りつぶすと画面に何も出ず
        // 「押しても反応しない」ように見えます。メッセージは既に利用者向けなので、そのまま伝えます。
        try
        {
            await _products.UpsertAsync(Build(newId, draft, settings), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return OperationResult<string>.Failure(
                "NEW_PRODUCT_WRITE_REJECTED",
                exception.Message,
                exception.GetType().FullName ?? "Unknown");
        }

        var applied = await applyAsync(newId, cancellationToken).ConfigureAwait(false);
        if (applied.IsSuccess) return applied;

        // ここから先は取り消しです。取り消し自体の失敗も利用者へ必ず伝えます。
        try
        {
            await _products.RemoveAsync(newId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return OperationResult<string>.Failure(
                "NEW_PRODUCT_ROLLBACK_FAILED",
                $"付与に失敗し、追加した「{newId}」の取り消しもできませんでした。製品CSVを確認してください。",
                exception.GetType().FullName ?? "Unknown");
        }

        return OperationResult<string>.Failure(
            applied.Error!.Code,
            $"{applied.Error.UserMessage}追加した製品は取り消しました。",
            applied.Error.TechnicalDetail);
    }

    /// <summary>入力値を製品レコードへ移します。対応付け済みの列は専用項目へ、それ以外は追加属性へ入れます。</summary>
    private static ProductRecord Build(string productId, NewProductDraft draft, DataSettings settings)
    {
        var mapping = settings.ProductColumns;
        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in settings.NewProductColumns)
        {
            if (IsMapped(column, mapping)) continue;
            var value = draft.Read(column);
            if (value.Length > 0) extras[column] = value;
        }

        return new ProductRecord(
            productId,
            draft.Read(mapping.ProductNameColumn),
            draft.Read(mapping.ManufacturerColumn),
            draft.Read(mapping.ModelNumberColumn),
            draft.Read(mapping.UnitColumn),
            null,
            extras);
    }

    private static bool IsMapped(string column, CsvColumnMapping mapping) =>
        string.Equals(column, mapping.ProductIdColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.ProductNameColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.ManufacturerColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.ModelNumberColumn, StringComparison.Ordinal)
        || string.Equals(column, mapping.UnitColumn, StringComparison.Ordinal);
}
