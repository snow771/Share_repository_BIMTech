// このファイルの目的:
//   1往復の絞り込み計画（条件・語・会話かどうか）を表します。
//   条件の組み立てそのものはChatQueryBuilderの責務です。
//
// 修正時の影響:
//   DroppedConditions / FellBackToKeywordは、質問文をそのまま検索語にする
//   旧経路（CandidateQueryPlanner、2026-09-10に削除）が使っていた名残です。
//   いまはChatQueryBuilderが常にこの2つを空/falseで埋めるため、書き換えないでください。

using Bui.Data;

namespace Bui.Llm;

/// <summary>
/// 1往復が「製品を探す話」なのか「そうでない話」なのかです。
/// これを持たないと、挨拶にも製品検索が走って必ず0件になります。
/// 読めない値は必ずSearchに倒します（現状の動きに落ちるため、外しても使えなくなりません）。
/// </summary>
public enum ChatIntent
{
    /// <summary>製品を探す。既定。</summary>
    Search,

    /// <summary>挨拶・相談・計算など、候補を引かずに答える。</summary>
    Chat
}

/// <summary>LLMの条件を検証した結果です。画面にはDescribe()を出します。</summary>
/// <param name="DroppedConditions">
///   常に空です。質問文をそのまま検索語にする旧経路（CandidateQueryPlanner）が
///   使っていたフィールドで、その経路は2026-09-10に削除しました
///   （質問文はどの項目の値でもなく、部分一致では0件を保証するだけだったため）。
///   ChatQueryBuilderは常に空のリストで埋めます。
/// </param>
/// <param name="FellBackToKeyword">
///   常にfalseです。理由はDroppedConditionsと同じです
///   （質問文全体をキーワード検索へ落とす旧経路の名残）。
/// </param>
public sealed record CandidateQueryPlan(
    IReadOnlyDictionary<string, string> Selections,
    string Text,
    IReadOnlyList<string> DroppedConditions,
    bool FellBackToKeyword,
    ChatIntent Intent = ChatIntent.Search)
{
    /// <summary>
    /// 表記ゆれ表の行がマスタと合わなかった理由です。画面へ出します。
    /// 黙って効かない行を残すと、利用者は表を直す機会そのものを失います。
    /// 既定は空。呼び出し側にnull判定を書かせません。
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    /// <summary>絞り込みへ渡す問い合わせにします。候補は20件までに揃えます。</summary>
    public CandidateQuery ToQuery(int limit = 20) => new(Selections, Text, limit);

    /// <summary>「何で絞ったか」を画面に出す一文にします。空文字は返しません。</summary>
    public string Describe()
    {
        var parts = new List<string>();
        foreach (var pair in Selections)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }

        if (Text.Length > 0) parts.Add($"語={Text}");
        return parts.Count == 0 ? "絞り込みなし（全件から）" : string.Join(" ／ ", parts);
    }
}
