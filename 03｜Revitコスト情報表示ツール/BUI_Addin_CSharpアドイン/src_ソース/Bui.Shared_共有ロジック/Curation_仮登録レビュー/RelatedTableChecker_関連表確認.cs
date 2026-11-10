// このファイルの目的:
//   仮登録データに関連する他の表（Product_Specs、Product_Images）の
//   件数と、メーカー名・建材分類がマスタに実在するかを情報として示します。
//
// なぜ登録をブロックしないのか:
//   0件でも正当な製品はあります（画像が無い製品、仕様が少ない製品など）。
//   最終判断は管理者に委ねます。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>関連表の状況です。すべて情報表示用で、登録の可否には使いません。</summary>
public sealed record RelatedTableSummary(int ProductSpecCount, int ProductImageCount, IReadOnlyList<string> Warnings);

/// <summary>関連表の件数とマスタ実在チェックを行います。</summary>
internal static class RelatedTableChecker
{
    public static RelatedTableSummary Check(
        string productId, string manufacturerName, string categoryName, GoldStandardTableSet masterSet)
    {
        ArgumentNullException.ThrowIfNull(masterSet);
        var id = (productId ?? string.Empty).Trim();

        var specCount = masterSet.Rows(GoldStandardTables.ProductSpecs)
            .Count(row => MatchesProductId(row, id));
        var imageCount = masterSet.Rows(GoldStandardTables.ProductImages)
            .Count(row => MatchesProductId(row, id));

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(manufacturerName) && !ExistsInMaster(
            masterSet, GoldStandardTables.Manufacturers, GoldStandardTables.Columns.ManufacturerName, manufacturerName))
        {
            warnings.Add($"メーカー名「{manufacturerName}」はManufacturers.csvに存在しません。");
        }
        if (!string.IsNullOrWhiteSpace(categoryName) && !ExistsInMaster(
            masterSet, GoldStandardTables.MaterialCategories, GoldStandardTables.Columns.CategoryNameJa, categoryName))
        {
            warnings.Add($"建材分類「{categoryName}」はMaterial_Categories.csvに存在しません。");
        }

        return new RelatedTableSummary(specCount, imageCount, warnings);
    }

    private static bool MatchesProductId(IReadOnlyDictionary<string, string> row, string productId) =>
        row.TryGetValue(GoldStandardTables.Columns.ProductId, out var value)
        && string.Equals(value.Trim(), productId, StringComparison.OrdinalIgnoreCase);

    private static bool ExistsInMaster(
        GoldStandardTableSet masterSet, string table, string nameColumn, string name) =>
        masterSet.Rows(table).Any(row =>
            row.TryGetValue(nameColumn, out var value)
            && string.Equals(value.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
