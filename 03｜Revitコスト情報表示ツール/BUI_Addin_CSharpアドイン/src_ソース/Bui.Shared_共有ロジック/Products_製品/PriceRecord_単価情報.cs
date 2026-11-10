// このファイルは製品単価の取込結果を不変の値として表し、CSV取込とコスト計算で同じ金額情報を共有するために存在します。
// 単価、通貨、適用日の型や意味を変えると、金額計算と地域別・期間別の単価選択に影響します。
namespace Bui.Products;

/// <summary>
/// 製品IDに対応する単価と、その適用条件・将来列を保持します。
/// </summary>
public sealed record PriceRecord(
    string ProductId,
    decimal UnitPrice,
    string Currency,
    DateOnly? ValidFrom,
    string Region,
    string InformationSource,
    IReadOnlyDictionary<string, string> AdditionalAttributes);
