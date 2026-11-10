// このファイルは製品CSVの列名を設定可能にし、取込元ごとの差異を製品変換ロジックから分離するために存在します。
// 既定列名や対応項目を変えると、既存CSVの読込み結果と追加属性へ残る列の範囲に影響します。
namespace Bui.Data;

/// <summary>
/// 製品CSVで使用する各項目の列名を表します。
/// </summary>
public sealed record CsvColumnMapping(
    string ProductIdColumn,
    string ProductNameColumn,
    string ManufacturerColumn,
    string ModelNumberColumn,
    string UnitColumn,
    string UpdatedAtColumn)
{
    /// <summary>
    /// POCで利用する製品CSVの標準的な列対応です。
    /// </summary>
    public static CsvColumnMapping PocDefault { get; } =
        new("db_key", "製品名", "メーカー名", "型番", "単位", "更新日");

    /// <summary>
    /// GoldStandardスキーマで使う項目名です。
    /// ここに並ぶのはCSVの見出しではなく、画面の絞り込み・検索で使う「項目名」です。
    /// GoldStandardFlattenerがこの名前で値を詰め、設定のcandidateFilterColumnsが
    /// 同じ名前を指します。片方だけ変えると絞り込みが空になります。
    /// 型番・単位・更新日はProduct_Variantsが0件のため、現時点では常に空です。
    /// </summary>
    public static CsvColumnMapping GoldStandardDefault { get; } =
        new("product_id", "製品名", "メーカー名", "型番", "単位", "更新日");
}
