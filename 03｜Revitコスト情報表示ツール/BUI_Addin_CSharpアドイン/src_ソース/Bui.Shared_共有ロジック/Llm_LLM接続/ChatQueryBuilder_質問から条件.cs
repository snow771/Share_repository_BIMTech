// このファイルの目的:
//   3層（質問文の直接照合・表記ゆれ表・LLMの語）を1つの絞り込み計画にまとめます。
//
// なぜこの順序なのか:
//   上位の層ほど確からしいためです。質問文に実際に書かれている値がいちばん確かで、
//   利用者が育てた表がその次、LLMの推測が最後です。
//   この順序が「モデルを替えても結果が動かない」ことの根拠です。
//   順序を入れ替えると、LLMの機嫌で結果が変わる状態へ戻ります。
//
// 修正時の影響:
//   質問文をそのまま検索語にしないでください。
//   文章はどの製品名にも含まれず、必ず0件になります（2026-09-10に発生）。

using Bui.Data;

namespace Bui.Llm;

/// <summary>質問文から絞り込み計画を組み立てます。純粋関数として保ちます。</summary>
public static class ChatQueryBuilder
{
    public static CandidateQueryPlan Build(
        string? question,
        IReadOnlyList<CandidateFilterOptions> available,
        SynonymTable synonyms,
        IReadOnlyList<string>? llmTerms,
        ChatIntent intent = ChatIntent.Search)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(synonyms);

        // 会話では検索そのものを行わないため、条件を組み立てません。
        if (intent == ChatIntent.Chat)
        {
            return new CandidateQueryPlan(
                new Dictionary<string, string>(StringComparer.Ordinal),
                string.Empty, Array.Empty<string>(), false, ChatIntent.Chat);
        }

        // 第1層。質問文に実際に書かれている値。
        var extracted = QuestionTermExtractor.Extract(question, available);
        var selections = new Dictionary<string, string>(extracted.Selections, StringComparer.Ordinal);
        var terms = new List<string>(extracted.Terms);

        // 第2層。利用者が育てた表。第1層が埋めていない項目にだけ入れます。
        var synonymMatch = synonyms.Match(SearchTextNormalizer.Normalize(question), available);
        foreach (var pair in synonymMatch.Selections)
        {
            if (!selections.ContainsKey(pair.Key)) selections[pair.Key] = pair.Value;
        }

        terms.AddRange(synonymMatch.Terms);

        // 第3層。LLMが挙げた語。マスタと突き合わせ、実在するものだけ採ります。
        // 項目名はLLMに決めさせません。語からBUIが突き止めます。
        foreach (var raw in llmTerms ?? Array.Empty<string>())
        {
            var normalized = SearchTextNormalizer.Normalize(raw);
            if (normalized.Length == 0) continue;

            var option = available.FirstOrDefault(item =>
                item.Values.Any(value =>
                    string.Equals(SearchTextNormalizer.Normalize(value), normalized, StringComparison.Ordinal)));
            if (option is null) continue;
            if (selections.ContainsKey(option.Column)) continue;

            selections[option.Column] = option.Values.First(value =>
                string.Equals(SearchTextNormalizer.Normalize(value), normalized, StringComparison.Ordinal));
        }

        // 条件として採った値は語から外します。二重に絞らないためです。
        var selectedValues = selections.Values
            .Select(SearchTextNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);
        var text = string.Join(
            ' ',
            terms.Where(term => !selectedValues.Contains(term)).Distinct(StringComparer.Ordinal));

        return new CandidateQueryPlan(selections, text, Array.Empty<string>(), false)
        {
            Problems = synonymMatch.Problems
        };
    }
}
