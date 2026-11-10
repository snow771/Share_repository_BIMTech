// このファイルは、画面と絞り込みの間でやり取りする形を定めます。
// 画面はこの3つの型だけを見て表示を組み立てるため、
// 絞り込みの実装をCSVからSQLへ差し替えても画面は変わりません。
// 修正時の影響: ここを変えると画面とSQL実装の両方に波及します。
using Bui.Products;

namespace Bui.Data;

/// <summary>選択中の絞り込み条件。値が空文字なら未選択（すべて）を意味します。</summary>
public sealed record CandidateQuery(
    IReadOnlyDictionary<string, string> Selections,
    string Text,
    int Limit = 200);

/// <summary>1つの絞り込み項目と、その時点で選べる値。表示用の「(すべて)」は画面側が付けます。</summary>
public sealed record CandidateFilterOptions(
    string Column,
    string Selected,
    IReadOnlyList<string> Values);

/// <summary>絞り込み結果。TotalCountは打ち切り前の総数です。</summary>
public sealed record CandidateView(
    IReadOnlyList<CandidateFilterOptions> Filters,
    IReadOnlyList<ProductRecord> Candidates,
    int TotalCount,
    bool IsTruncated);
