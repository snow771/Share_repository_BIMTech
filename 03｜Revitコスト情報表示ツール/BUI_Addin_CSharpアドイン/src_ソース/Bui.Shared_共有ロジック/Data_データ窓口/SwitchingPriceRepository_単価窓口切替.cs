// このファイルの目的:
//   SQL接続中に単価CSVを読みに行かないようにします。
//   また、単価CSVのパスを、実体を作り直さずに切り替えられるようにします。
//
// なぜ必要か:
//   単価情報はまだSQL側にありません（GoldStandardDBに単価テーブルがない）。
//   何もしないと、SQLへ切り替えた後も選択のたびにPrices.csvを開きに行き、
//   「SQLに切り替えたのにCSVを触っている」状態になります。
//
//   2026-09-15までは、設定タブで単価CSVのパスを変えても、Revitを
//   再起動するまで反映されませんでした（BuiDockPaneComposition.Create
//   で起動時に1回だけ実体を作っていたため）。SwitchCsvPathで
//   「切り替えて読み直す」ボタンから即時反映できるようにします。
//
// 修正時の影響:
//   読めないことを黙って隠さず、状態表示に理由を出してください。
//   単価が空になった理由が分からないと、原因の切り分けができません。
//   SwitchCsvPathは列マッピング（mapping）を切り替え時にも使い回します。
//   マッピングは設定タブでは編集できない値のため、切り替え時に
//   変える理由がありません。

using System.IO;
using Bui.Products;

namespace Bui.Data;

/// <summary>データ元がCSVのときだけ単価CSVを読みます。パスは実体を作り直さずに切り替えられます。</summary>
public sealed class SwitchingPriceRepository : IPriceRepository, ICsvPathSwitchable
{
    private IPriceRepository _csvPrices;
    private readonly DataSourceSwitch _source;
    private readonly PriceColumnMapping _mapping;

    public SwitchingPriceRepository(IPriceRepository csvPrices, DataSourceSwitch source, PriceColumnMapping mapping)
    {
        _csvPrices = csvPrices ?? throw new ArgumentNullException(nameof(csvPrices));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
    }

    /// <summary>読み込み先を新しいCSVパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    public void SwitchCsvPath(string csvPath) =>
        Volatile.Write(ref _csvPrices, new Csv.CsvPriceRepository(csvPath, _mapping));

    public Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? Volatile.Read(ref _csvPrices).GetStatusAsync(cancellationToken)
            : Task.FromResult(new DataSourceStatus(
                false, 0, null,
                new[] { "SQL接続中は単価を表示しません。単価情報はまだSQL側にありません。設定タブでデータ元を「CSVフォルダ」に切り替えると表示されます。" }));

    public Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery query, CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? Volatile.Read(ref _csvPrices).SearchAsync(query, cancellationToken)
            : Task.FromResult<IReadOnlyList<PriceRecord>>(Array.Empty<PriceRecord>());

    public Task UpsertAsync(PriceRecord record, CancellationToken cancellationToken) =>
        _source.IsCsvFolder
            ? Volatile.Read(ref _csvPrices).UpsertAsync(record, cancellationToken)
            : throw new InvalidDataException(
                "SQL接続中は単価を更新できません。設定タブでデータ元を「CSVフォルダ」に切り替えてください。");
}
