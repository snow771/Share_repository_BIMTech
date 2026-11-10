// このファイルは壁・床などの複合ホスト全体を不変の値として表すために存在します。
// CSVリポジトリ、構成照合、将来のRevitアダプターがこの契約に依存するため、公開項目を変えるとそれらの連携に影響します。
namespace Bui.Assemblies;

/// <summary>Assembly_IDを持つ複合構造と、順序付きの構成層を保持します。</summary>
public sealed record AssemblyRecord(
    string AssemblyId,
    string HostCategory,
    IReadOnlyList<AssemblyLayerRecord> OrderedLayers,
    DateTimeOffset? SourceUpdatedAt,
    IReadOnlyDictionary<string, string> AdditionalAttributes);
