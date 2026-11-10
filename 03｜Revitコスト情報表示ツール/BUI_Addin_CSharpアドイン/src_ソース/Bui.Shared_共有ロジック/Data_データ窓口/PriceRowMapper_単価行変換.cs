// このファイルは単価CSVの1行をPriceRecordへ変換し、列名設定と将来追加列の両方を利用可能にするために存在します。
// 数値・日付の解釈や必須ID・単価の検証を変えると、取込データの品質と後続のコスト計算に影響します。
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Bui.Products;

namespace Bui.Data;

/// <summary>
/// 単価CSVの辞書形式の行を単価情報へ変換します。
/// </summary>
public static class PriceRowMapper
{
    /// <summary>
    /// 指定した列対応を使い、CSV行を単価情報へ変換します。
    /// </summary>
    public static PriceRecord Map(IReadOnlyDictionary<string, string> row, PriceColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);

        // 金額をどの製品へ結び付けるかを保証するため、ID列は欠落・空白とも取込エラーにします。
        var productId = GetRequiredValue(row, mapping.ProductIdColumn, "製品ID");
        var unitPrice = ParseRequiredPrice(row, mapping.UnitPriceColumn);
        var mappedColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            mapping.ProductIdColumn,
            mapping.UnitPriceColumn,
            mapping.CurrencyColumn,
            mapping.ValidFromColumn,
            mapping.RegionColumn,
            mapping.InformationSourceColumn
        };

        // 設定にない列は将来のCSV拡張でも失わないよう、入力辞書とは独立した読取専用辞書へ退避します。
        var additionalAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (column, value) in row)
        {
            if (!mappedColumns.Contains(column))
            {
                additionalAttributes[column] = (value ?? string.Empty).Trim();
            }
        }

        return new PriceRecord(
            productId,
            unitPrice,
            GetOptionalValue(row, mapping.CurrencyColumn),
            ParseOptionalDate(GetOptionalValue(row, mapping.ValidFromColumn), mapping.ValidFromColumn),
            GetOptionalValue(row, mapping.RegionColumn),
            GetOptionalValue(row, mapping.InformationSourceColumn),
            new ReadOnlyDictionary<string, string>(additionalAttributes));
    }

    private static string GetRequiredValue(IReadOnlyDictionary<string, string> row, string column, string fieldName)
    {
        var value = GetOptionalValue(row, column);
        if (value.Length == 0)
        {
            throw new InvalidDataException($"必須列「{column}」に{fieldName}がありません。");
        }

        return value;
    }

    private static decimal ParseRequiredPrice(IReadOnlyDictionary<string, string> row, string column)
    {
        var value = GetRequiredValue(row, column, "単価");
        var styles = NumberStyles.Number;
        if (decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out var parsed)
            || decimal.TryParse(value, styles, CultureInfo.GetCultureInfo("ja-JP"), out parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"必須列「{column}」の単価を数値として読み取れません。");
    }

    private static string GetOptionalValue(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static DateOnly? ParseOptionalDate(string value, string column)
    {
        if (value.Length == 0)
        {
            return null;
        }

        var styles = DateTimeStyles.AllowWhiteSpaces;
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed)
            || DateOnly.TryParse(value, CultureInfo.GetCultureInfo("ja-JP"), styles, out parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"列「{column}」の日付を読み取れません。");
    }
}
