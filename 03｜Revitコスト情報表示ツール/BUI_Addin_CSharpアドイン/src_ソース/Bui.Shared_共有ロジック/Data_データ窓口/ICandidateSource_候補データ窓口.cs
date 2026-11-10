// このファイルは画面と絞り込み実装の唯一の境界です。
// 画面はこの契約だけを見るため、CSVからSQL Serverへ実装を差し替えても
// 画面とそのテストは変更が要りません。
// 修正時の影響: 署名を変えると、将来のSQL実装と画面の両方に波及します。
namespace Bui.Data;

/// <summary>現在の絞り込み条件から、各項目の選択肢と該当候補を返します。</summary>
public interface ICandidateSource
{
    Task<CandidateView> QueryAsync(CandidateQuery query, CancellationToken cancellationToken);
}
