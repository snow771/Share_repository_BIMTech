// このファイルの目的:
//   11枚の表を「生の行のまま」返す口を1つに決めます。
//   CSVフォルダから読むか、SQL Serverから読むかは、この口の実装差だけにします。
//
// なぜこの形なのか:
//   結合・整形はGoldStandardFlattener以降が担っています。
//   出どころが違っても同じ形の表を渡せば、その先の経路は1本のままになり、
//   「SQLだと表示が違う」が構造的に起きません。
//
// 修正時の影響:
//   値は必ず文字列で持ってください。CSVの空欄とSQLのNULLを同じ「空文字」に
//   揃えることで、平坦化以降が出どころを気にせず済みます。

namespace Bui.Data.GoldStandard;

/// <summary>11枚の表を読む口です。出どころごとに実装します。</summary>
internal interface IGoldStandardTableSource
{
    /// <summary>表一式を読みます。例外は投げず、問題は Problems に集めます。</summary>
    GoldStandardTableSet Load();

    /// <summary>画面に出す出どころの説明です。接続文字列は含めません。</summary>
    string Describe();

    /// <summary>この出どころへ書き込めるかどうかです。SQLは読み取り専用なのでfalseです。</summary>
    bool SupportsWrite { get; }

    /// <summary>
    /// ローカルのCSVフォルダを読んでいるかどうかです。
    /// 単価CSVを読みに行ってよいかの判定に使います。SupportsWriteとは別に持ちます。
    /// 「書けるか」と「CSVを読んでいるか」は今は一致していますが、意味が違うためです。
    /// </summary>
    bool IsCsvFolder { get; }

    /// <summary>キャッシュの作り直し判定に使う版番号です。変わったら読み直します。</summary>
    long Version { get; }

    /// <summary>
    /// 製品画像の置き場です。CSVでは表の置き場でもあります。
    /// SQL接続中も画像はローカルのファイルとして扱うため、この値が必要です。
    /// </summary>
    string DataFolderPath { get; }
}

/// <summary>データフォルダのCSVから表一式を読みます。</summary>
internal sealed class CsvTableSource : IGoldStandardTableSource
{
    private readonly string _folderPath;

    public CsvTableSource(string? folderPath) => _folderPath = folderPath ?? string.Empty;

    public string DataFolderPath => _folderPath;

    public bool SupportsWrite => true;

    public bool IsCsvFolder => true;

    /// <summary>フォルダ内CSVの最終更新時刻の最大値です。ファイルを直すと変わります。</summary>
    public long Version => GoldStandardCsvReader.FolderTimestampUtc(_folderPath).Ticks;

    public GoldStandardTableSet Load() => GoldStandardCsvReader.Load(_folderPath);

    public string Describe() =>
        _folderPath.Length == 0 ? "CSVフォルダ（未設定）" : "CSVフォルダ: " + _folderPath;
}
