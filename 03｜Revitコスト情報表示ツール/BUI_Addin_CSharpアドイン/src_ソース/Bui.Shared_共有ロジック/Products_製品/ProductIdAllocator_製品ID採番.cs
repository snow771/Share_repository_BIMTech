// このファイルは新規登録時のProduct_IDを採番します。
// AssemblyIdAllocatorと同じ考え方ですが、画面から直接呼ぶため
// 例外ではなくOperationResultで失敗を返します。
// 修正時の影響: 接頭辞の比較を緩めると、実在する建材コードを
// 連番の対象に含めてしまい、衝突するIDを発行します。
using System.Globalization;
using Bui.Common;

namespace Bui.Products;

/// <summary>既存IDと衝突しない、連番の新規Product_ID候補を作成します。</summary>
public static class ProductIdAllocator
{
    public static OperationResult<string> Next(IEnumerable<string> existingIds, string prefix, int digits)
    {
        ArgumentNullException.ThrowIfNull(existingIds);

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return OperationResult<string>.Failure(
                "PRODUCT_ID_PREFIX_INVALID", "新規IDの接頭辞が設定されていません。設定を確認してください。", "Prefix is blank.");
        }

        if (digits is < 1 or > 18)
        {
            return OperationResult<string>.Failure(
                "PRODUCT_ID_DIGITS_INVALID", "新規IDの桁数は1から18の範囲で設定してください。", $"digits={digits}");
        }

        long maximum = 0;
        foreach (var existingId in existingIds)
        {
            // 大文字小文字も含めた完全一致にします。緩めると実在コードを巻き込みます。
            if (existingId is null || !existingId.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var numericPart = existingId[prefix.Length..];
            if (numericPart.Length != digits || numericPart.Any(c => c is < '0' or > '9')) continue;

            if (long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                maximum = Math.Max(maximum, value);
            }
        }

        var next = maximum + 1;
        var limit = Pow10(digits) - 1;
        if (next > limit)
        {
            return OperationResult<string>.Failure(
                "PRODUCT_ID_RANGE_EXHAUSTED",
                "新規IDの採番が上限に達しました。設定の桁数を見直してください。",
                $"next={next}; limit={limit}");
        }

        return OperationResult<string>.Success(prefix + next.ToString($"D{digits}", CultureInfo.InvariantCulture));
    }

    private static long Pow10(int digits)
    {
        long value = 1;
        for (var index = 0; index < digits; index++) value = checked(value * 10);
        return value;
    }
}
