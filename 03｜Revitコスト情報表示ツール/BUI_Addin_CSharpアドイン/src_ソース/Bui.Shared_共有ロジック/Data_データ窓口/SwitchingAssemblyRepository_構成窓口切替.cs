// このファイルの目的:
//   構成CSV（Assemblies.csv）の読み込み先パスを、実体を作り直さずに
//   切り替えられるようにします。SwitchingPriceRepositoryの構成CSV版です。
//
// なぜ必要か:
//   2026-09-15までは、設定タブで構成CSVのパスを変えても、Revitを
//   再起動するまで反映されませんでした（BuiDockPaneComposition.Create
//   で起動時に1回だけCsvAssemblyRepositoryを作っていたため）。
//   SwitchCsvPathで「切り替えて読み直す」ボタンから即時反映できるように
//   します。
//
//   単価と違い、構成CSVはCSV/SQLの出どころ切替（DataSourceSwitch）の
//   対象ではありません。Assembly_IDの採番はSQL接続中でも常に構成CSVへ
//   書き込むためです。そのためDataSourceSwitchのような「出どころの種類」
//   ではなく、「パスだけを持つ薄い差し替え窓口」として作ります。
//
// 修正時の影響:
//   SwitchCsvPathはUIスレッドから呼ばれる想定です。差し替えは参照1つの
//   入れ替えに留め、ここでI/Oを行わないでください（DataSourceSwitchと
//   同じ制約）。

using Bui.Assemblies;
using Bui.Data.Csv;

namespace Bui.Data;

/// <summary>構成CSVの読み込み先を、実体を作り直さずに切り替えられる窓口です。</summary>
public sealed class SwitchingAssemblyRepository : IAssemblyRepository, ICsvPathSwitchable
{
    private IAssemblyRepository _current;

    public SwitchingAssemblyRepository(IAssemblyRepository initial) =>
        _current = initial ?? throw new ArgumentNullException(nameof(initial));

    /// <summary>読み込み先を新しいCSVパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    public void SwitchCsvPath(string csvPath) =>
        Volatile.Write(ref _current, new CsvAssemblyRepository(csvPath));

    public Task<AssemblyRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).FindByFingerprintAsync(fingerprint, cancellationToken);

    public Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).GetAllIdsAsync(cancellationToken);

    public Task AddAsync(AssemblyRecord record, string fingerprint, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).AddAsync(record, fingerprint, cancellationToken);

    public Task RemoveAsync(string assemblyId, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).RemoveAsync(assemblyId, cancellationToken);
}
