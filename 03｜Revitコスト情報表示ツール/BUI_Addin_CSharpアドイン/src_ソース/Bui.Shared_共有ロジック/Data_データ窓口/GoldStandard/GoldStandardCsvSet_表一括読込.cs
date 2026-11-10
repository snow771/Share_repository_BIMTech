// このファイルの目的:
//   11枚の表の中身を、生の行のまま保持します。
//   結合や変換はここで行いません（GoldStandardFlattenerの仕事です）。
//   読む処理はGoldStandardCsvReaderに分けています。出どころがCSVだけではなくなったためです。
//
// なぜ「欠けても止めない」のか:
//   Product_VariantsとProduct_Imagesは現時点で0件であり、
//   ファイルごと存在しないことが普通に起こります。
//   1枚欠けただけでBUIパネルが開かなくなるのは、実運用で許容できません。
//
// 修正時の影響:
//   Productsだけは必須です。ここが読めないと候補が一切作れないため、
//   HasProductsをfalseにして利用側へ伝えます。

using System.IO;
using Bui.Data.Csv;

namespace Bui.Data.GoldStandard;

/// <summary>1つの表の内容です。書き戻しのためBOM有無も保持します。</summary>
internal sealed record GoldStandardTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<Dictionary<string, string>> Rows,
    bool HasUtf8Bom);

/// <summary>11枚の表一式です。出どころは問いません。</summary>
internal sealed class GoldStandardTableSet
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> NoRows =
        Array.Empty<IReadOnlyDictionary<string, string>>();

    private readonly IReadOnlyDictionary<string, GoldStandardTable> _tables;

    public GoldStandardTableSet(IReadOnlyDictionary<string, GoldStandardTable> tables, IReadOnlyList<string> problems)
    {
        _tables = tables;
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>Productsが読めたかどうかです。falseなら候補は0件になります。</summary>
    public bool HasProducts => _tables.ContainsKey(GoldStandardTables.Products);

    public GoldStandardTable? Table(string table) =>
        _tables.TryGetValue(table, out var value) ? value : null;

    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string table) =>
        _tables.TryGetValue(table, out var value) ? value.Rows : NoRows;
}

/// <summary>データフォルダのCSVを表一式として読みます。</summary>
internal static class GoldStandardCsvReader
{
    /// <summary>
    /// フォルダ内CSVの最終更新時刻の最大値です。キャッシュの有効判定に使います。
    /// フォルダ自体の更新時刻では、ファイル内容だけが変わった場合に気づけません。
    /// </summary>
    public static DateTime FolderTimestampUtc(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return DateTime.MinValue;

        var newest = DateTime.MinValue;
        foreach (var table in GoldStandardTables.All)
        {
            var path = Path.Combine(folderPath, table + ".csv");
            if (!File.Exists(path)) continue;
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime > newest) newest = writeTime;
        }

        return newest;
    }

    /// <summary>フォルダを読みます。例外は投げず、問題は Problems に集めます。</summary>
    public static GoldStandardTableSet Load(string? folderPath)
    {
        var tables = new Dictionary<string, GoldStandardTable>(StringComparer.Ordinal);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            // 例外本文はパスを含むため画面に出せません。原因が分かる案内だけを返します。
            problems.Add("データフォルダが見つかりません。設定タブのパスを確認してください。");
            return new GoldStandardTableSet(tables, problems);
        }

        foreach (var table in GoldStandardTables.All)
        {
            var path = Path.Combine(folderPath, table + ".csv");
            if (!File.Exists(path))
            {
                problems.Add($"{table}.csv がありません。この表は空として扱います。");
                continue;
            }

            try
            {
                var document = CsvDocumentCodec.Read(path);
                tables[table] = new GoldStandardTable(document.Headers, document.Rows, document.HasUtf8Bom);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{table}.csv を読み取れません。{exception.Message}");
            }
        }

        if (!tables.ContainsKey(GoldStandardTables.Products))
        {
            problems.Insert(0, $"{GoldStandardTables.Products}.csv を読み取れないため、製品候補を表示できません。");
        }

        return new GoldStandardTableSet(tables, problems);
    }
}
