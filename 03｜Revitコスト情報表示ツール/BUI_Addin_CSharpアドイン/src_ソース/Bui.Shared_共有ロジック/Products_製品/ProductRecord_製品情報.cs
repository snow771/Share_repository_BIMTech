// このファイルは製品の取込結果を不変の値として表し、CSV取込・検索・画面表示で同じ製品情報を共有するために存在します。
// 項目の名前や型を変えると、CSV変換処理と製品情報を利用するすべての後続機能の互換性に影響します。
namespace Bui.Products;

/// <summary>
/// 製品IDを中心に、CSVから得た製品情報と将来列を保持します。
/// </summary>
public sealed record ProductRecord(
    string ProductId,
    string ProductName,
    string Manufacturer,
    string ModelNumber,
    string Unit,
    DateTimeOffset? SourceUpdatedAt,
    IReadOnlyDictionary<string, string> AdditionalAttributes);
