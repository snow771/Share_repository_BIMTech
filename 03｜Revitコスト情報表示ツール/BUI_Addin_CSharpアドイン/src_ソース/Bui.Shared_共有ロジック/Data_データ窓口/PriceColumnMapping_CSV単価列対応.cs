// このファイルは単価CSVの列名を設定可能にし、取込元ごとの差異を単価変換ロジックから分離するために存在します。
// 既定列名や対応項目を変えると、既存単価CSVの読込み結果と追加属性へ残る列の範囲に影響します。
namespace Bui.Data;

/// <summary>
/// 単価CSVで使用する各項目の列名を表します。
/// </summary>
public sealed record PriceColumnMapping(
    string ProductIdColumn,
    string UnitPriceColumn,
    string CurrencyColumn,
    string ValidFromColumn,
    string RegionColumn,
    string InformationSourceColumn)
{
    /// <summary>
    /// POCで利用する単価CSVの標準的な列対応です。
    /// </summary>
    public static PriceColumnMapping PocDefault { get; } =
        new("db_key", "単価", "通貨", "適用開始日", "地域", "情報源");
}
