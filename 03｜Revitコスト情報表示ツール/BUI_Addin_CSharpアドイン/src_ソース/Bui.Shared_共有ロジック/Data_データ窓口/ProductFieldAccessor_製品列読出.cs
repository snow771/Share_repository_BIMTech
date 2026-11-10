// このファイルは、列名だけを頼りに製品レコードから値を取り出すために存在します。
// ProductRecordは対応付け済みの項目と追加属性に分かれているため、
// 絞り込み側が両者の違いを意識しなくて済むよう、ここで吸収します。
// 修正時の影響: 対応付け項目を増やした場合、ここにも追加しないと
// その列だけ絞り込みで空扱いになります。
using Bui.Products;

namespace Bui.Data;

/// <summary>製品レコードから、列名を指定して値を取り出します。</summary>
public static class ProductFieldAccessor
{
    /// <summary>見つからない列は空文字を返します。設定に無い列でも機能を止めないためです。</summary>
    public static string Read(ProductRecord record, CsvColumnMapping mapping, string column)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(mapping);
        if (string.IsNullOrWhiteSpace(column)) return string.Empty;

        var value = ReadMapped(record, mapping, column)
            ?? (record.AdditionalAttributes.TryGetValue(column, out var extra) ? extra : null);

        return value?.Trim() ?? string.Empty;
    }

    private static string? ReadMapped(ProductRecord record, CsvColumnMapping mapping, string column)
    {
        if (Same(column, mapping.ProductIdColumn)) return record.ProductId;
        if (Same(column, mapping.ProductNameColumn)) return record.ProductName;
        if (Same(column, mapping.ManufacturerColumn)) return record.Manufacturer;
        if (Same(column, mapping.ModelNumberColumn)) return record.ModelNumber;
        if (Same(column, mapping.UnitColumn)) return record.Unit;
        return null;
    }

    /// <summary>
    /// 対応付け済みの項目と追加属性を、列名をキーにした1つの表へまとめます。
    /// 文字列検索の対象範囲はこの表が決めます。製品データのリポジトリもこれを使います。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Fields(ProductRecord record, CsvColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(mapping);

        return new Dictionary<string, string>(record.AdditionalAttributes, StringComparer.Ordinal)
        {
            [mapping.ProductIdColumn] = record.ProductId,
            [mapping.ProductNameColumn] = record.ProductName,
            [mapping.ManufacturerColumn] = record.Manufacturer,
            [mapping.ModelNumberColumn] = record.ModelNumber,
            [mapping.UnitColumn] = record.Unit,
            [mapping.UpdatedAtColumn] =
                record.SourceUpdatedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
