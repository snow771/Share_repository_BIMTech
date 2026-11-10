// このファイルの目的:
//   現場のデータフォルダから、仮ID（例: BM_NEW-0001）が付いた
//   Products.csvの行を拾い、関連するProduct_Specsも突き合わせます。
//
// なぜ元のCSVを書き換えないのか:
//   このツールは複数の現場フォルダから読み取るだけの、管理者専用の
//   レビューツールです。元のフォルダを書き換えると、現場のBUIが
//   次に読んだときの内容と食い違う恐れがあります。
//
// 修正時の影響:
//   product_id列での突き合わせは大文字小文字を区別しません
//   （GoldStandardProductRepositoryのIsSameProductIdと同じ規則に揃えます）。
//   読み込みは GoldStandardCsvReader.Load(folderPath) を使います
//   （GoldStandardTableSet自体は読み込み結果を保持するだけで、Loadメソッドは持ちません）。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>1件の仮登録データです。出どころフォルダと、関連するProduct_Specs行を保持します。</summary>
public sealed record ScannedProvisionalRecord(
    string SourceFolder,
    string ProvisionalId,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<IReadOnlyDictionary<string, string>> SpecRows);

/// <summary>Scanの結果です。拾えた行に加え、読み込み時の問題（欠けている表など）も返します。</summary>
public sealed record ProvisionalScanResult(
    IReadOnlyList<ScannedProvisionalRecord> Records,
    IReadOnlyList<string> Problems);

/// <summary>親フォルダ1つと、その直下の1サブフォルダぶんのScan結果です。</summary>
public sealed record ChildFolderScanResult(string SourceFolder, ProvisionalScanResult Result);

/// <summary>データフォルダから仮登録データを拾います。</summary>
public static class ProvisionalRecordScanner
{
    /// <summary>指定フォルダのProducts.csvから、provisionalIdPrefixで始まる行を拾います。</summary>
    public static ProvisionalScanResult Scan(string folderPath, string provisionalIdPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionalIdPrefix);

        var set = GoldStandardCsvReader.Load(folderPath);
        var products = set.Rows(GoldStandardTables.Products);
        var specs = set.Rows(GoldStandardTables.ProductSpecs);

        var results = new List<ScannedProvisionalRecord>();
        foreach (var row in products)
        {
            var productId = row.TryGetValue(GoldStandardTables.Columns.ProductId, out var id) ? id.Trim() : string.Empty;
            if (!productId.StartsWith(provisionalIdPrefix, StringComparison.Ordinal)) continue;

            var matchedSpecs = specs
                .Where(specRow => specRow.TryGetValue(GoldStandardTables.Columns.ProductId, out var specProductId)
                    && string.Equals(specProductId.Trim(), productId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            results.Add(new ScannedProvisionalRecord(folderPath, productId, row, matchedSpecs));
        }

        return new ProvisionalScanResult(results, set.Problems);
    }

    /// <summary>parentFolderPath直下の各サブフォルダへ、それぞれScanを実行します。
    /// サブフォルダが無ければ空の一覧を返します（例外は投げません）。</summary>
    public static IReadOnlyList<ChildFolderScanResult> ScanChildFolders(string parentFolderPath, string provisionalIdPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionalIdPrefix);

        if (!Directory.Exists(parentFolderPath))
        {
            return Array.Empty<ChildFolderScanResult>();
        }

        var results = new List<ChildFolderScanResult>();
        foreach (var childFolder in Directory.GetDirectories(parentFolderPath))
        {
            var result = Scan(childFolder, provisionalIdPrefix);
            results.Add(new ChildFolderScanResult(childFolder, result));
        }

        return results;
    }
}
