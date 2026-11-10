// このファイルはProduct_ID書込要求をRevitへ登録する前に、対象IDが製品リポジトリに存在することを確認します。
// IProductRepositoryとOperationResultに依存し、確認規則を変えると一括付与でモデルへ保存可能なIDの範囲に影響します。
using Bui.Common;
using Bui.Data;

namespace Bui.Products;

/// <summary>製品リポジトリを唯一の正本として、書込前のIDを検証します。</summary>
public static class ProductIdAssignmentValidator
{
    public static async Task<OperationResult<string>> ValidateAsync(
        IProductRepository repository,
        string productId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(productId))
        {
            return OperationResult<string>.Failure(
                "PRODUCT-ID-BLANK",
                "Product_IDを選択してください。",
                "書込対象のProduct_IDが空です。");
        }

        var normalizedId = productId.Trim();
        var product = await repository
            .GetByIdAsync(normalizedId, cancellationToken)
            .ConfigureAwait(false);
        if (product is null)
        {
            return OperationResult<string>.Failure(
                "PRODUCT-ID-NOT-FOUND",
                $"製品データにProduct_ID「{normalizedId}」がありません。データを再読込してください。",
                $"IProductRepository.GetByIdAsync returned null: {normalizedId}");
        }

        return OperationResult<string>.Success(normalizedId);
    }
}
