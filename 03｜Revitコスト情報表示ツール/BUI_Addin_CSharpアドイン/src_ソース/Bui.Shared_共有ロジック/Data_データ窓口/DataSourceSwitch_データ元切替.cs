// このファイルの目的:
//   いま使っているデータの出どころを1か所だけで持ちます。
//
// なぜ1か所なのか:
//   出どころの判断が各リポジトリに散ると、「SQLに切り替えたのに
//   ここだけCSVを読んでいた」が必ず起きます。判断はここだけにします。
//
// なぜリポジトリを作り直さないのか:
//   画面が持っているインターフェイスの実体を差し替えると、
//   検索結果・層の表・保留中のプレビューが消えます。
//   実体は据え置き、中身の出どころだけを入れ替えます。
//
// 修正時の影響:
//   SwitchToはUIスレッドから呼ばれます。差し替えは参照1つの入れ替えに留め、
//   ここでI/Oを行わないでください。

using Bui.Data.GoldStandard;

namespace Bui.Data;

/// <summary>いま使っているデータの出どころです。</summary>
public sealed class DataSourceSwitch
{
    private IGoldStandardTableSource _current;

    internal DataSourceSwitch(IGoldStandardTableSource initial) =>
        _current = initial ?? throw new ArgumentNullException(nameof(initial));

    /// <summary>CSVフォルダを出どころにした切替器を作ります。</summary>
    public static DataSourceSwitch ForCsvFolder(string? folderPath) =>
        new(new CsvTableSource(folderPath));

    internal IGoldStandardTableSource Current => Volatile.Read(ref _current);

    /// <summary>出どころを差し替えます。以降の読み込みは新しい方だけを使います。</summary>
    internal void SwitchTo(IGoldStandardTableSource next) =>
        Volatile.Write(ref _current, next ?? throw new ArgumentNullException(nameof(next)));

    /// <summary>画面に出す出どころの説明です。</summary>
    public string Describe() => Current.Describe();

    /// <summary>いまの出どころへ書き込めるかどうかです。</summary>
    public bool SupportsWrite => Current.SupportsWrite;

    /// <summary>いまローカルのCSVフォルダを読んでいるかどうかです。単価CSVの可否に使います。</summary>
    public bool IsCsvFolder => Current.IsCsvFolder;

    /// <summary>画像ファイルの置き場です。</summary>
    public string DataFolderPath => Current.DataFolderPath;
}
