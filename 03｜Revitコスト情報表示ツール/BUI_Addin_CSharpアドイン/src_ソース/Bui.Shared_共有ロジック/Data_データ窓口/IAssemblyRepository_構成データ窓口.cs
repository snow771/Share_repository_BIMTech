// このファイルは複合構造データの検索・登録・補償削除を保存方式から分離するために存在します。
// AssemblyRecord に依存し、メソッド契約を変えるとRevit処理や将来のSQL実装を含むすべての利用側に影響します。
using Bui.Assemblies;

namespace Bui.Data;

/// <summary>構成フィンガープリントを使って複合構造を保存・検索するための窓口です。</summary>
public interface IAssemblyRepository
{
    Task<AssemblyRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken cancellationToken);

    Task AddAsync(AssemblyRecord record, string fingerprint, CancellationToken cancellationToken);

    Task RemoveAsync(string assemblyId, CancellationToken cancellationToken);
}
