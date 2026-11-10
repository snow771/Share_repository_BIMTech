// このファイルの目的:
//   質問文から、マスタに実在する条件と検索語を、LLM無しで拾います。
//
// なぜLLM無しで拾うのか:
//   この案件の方針は「検索は決定的に行い、LLMは説明だけ」です。
//   AIチャットだけが条件の組み立てをLLMに依存していて、モデルが条件を
//   1つも返さず「不燃のボードを教えて」が必ず0件になっていました（2026-09-10）。
//   ここが動く限り、モデルが何も返せなくても検索は動きます。
//
// なぜ形態素解析をしないのか:
//   辞書とライセンスを持ち込むことになります。
//   マスタに実在する文字列だけを手がかりにすれば、辞書は要りません。
//   「を教えて」のような依頼表現はマスタに無いため、自然に落ちます。
//
// 修正時の影響:
//   照合対象を絞り込み項目の値より広げないでください。
//   備考欄まで広げると、短い文字列が語として拾われ、AND検索を無意味に狭めます。

using System.Text;
using Bui.Data;

namespace Bui.Llm;

/// <summary>質問文から拾えた条件と語です。</summary>
public sealed record ExtractedConditions(
    IReadOnlyDictionary<string, string> Selections,
    IReadOnlyList<string> Terms);

/// <summary>質問文とマスタの値を突き合わせます。純粋関数として保ちます。</summary>
public static class QuestionTermExtractor
{
    /// <summary>語の候補は質問の長さの2乗で増えるため、見る範囲に上限を置きます。</summary>
    public const int MaximumQuestionLength = 200;

    /// <summary>語1つの長さの上限です。暴走防止であり、意味は持ちません。</summary>
    public const int MaximumTermLength = 12;

    /// <summary>
    /// 語として認める最短の長さです。1文字だと助詞まで拾ってしまいます。
    /// 2026-09-16、実機のSQLデータ（PoC用CSVより値の件数が多い）で、質問文と
    /// 無関係な値の一部がたまたま2文字だけ一致し、語として拾われてAND検索が
    /// 意味なく0件になる不具合が発生しました。値の件数が増えるほど、2文字の
    /// 偶然の一致は起こりやすくなります。3文字にすることで、この種の偶然の
    /// 一致をほぼ排除します（表記ゆれ.csvの「PB」のような正当な短い言い方は、
    /// この層ではなく表記ゆれ表・LLMの語の層で別途一致するため、影響しません）。
    /// </summary>
    private const int MinimumTermLength = 3;

    /// <summary>連結した値の間に挟む区切り文字です。値をまたいだ偶然の一致を防ぎます。</summary>
    private const char ValueSeparator = (char)0x0001;

    /// <summary>
    /// 条件として採った値を、残り文字列から消す代わりに埋める記号です。
    /// 削除すると前後の文字がつながり、質問文に存在しなかった並びが生まれます
    /// （例: 「AB」を削除で「c」「d」が「cd」につながる）。埋めれば前後は離れたままです。
    /// ValueSeparator（U+0001）はhaystackの区切りとして既に使われているため、
    /// これを再利用すると値をまたいだ一致が起きます。ここではU+0002を使います。
    /// </summary>
    private const char BlankSentinel = (char)0x0002;

    public static ExtractedConditions Extract(
        string? question, IReadOnlyList<CandidateFilterOptions> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        var normalized = SearchTextNormalizer.Normalize(question);
        if (normalized.Length > MaximumQuestionLength)
        {
            normalized = normalized.Substring(0, MaximumQuestionLength);
        }

        if (normalized.Length == 0)
        {
            return new ExtractedConditions(
                new Dictionary<string, string>(StringComparer.Ordinal),
                Array.Empty<string>());
        }

        var selections = new Dictionary<string, string>(StringComparer.Ordinal);

        // 埋める範囲は元の質問文（normalized）に対する一致だけから決めます。
        // 順にremainderを書き換えながら照合すると、後の項目の判定が前の項目の
        // 削除結果に左右され、availableの並び順で選択が変わってしまいます。
        var occupied = new bool[normalized.Length];

        foreach (var option in available)
        {
            // 同じ項目に複数当たったら長い値を採ります。
            // 短い方を採ると絞り込みが緩くなります。
            // 長さは正規化後で判定します。マスタのセルが空白だけだと、生の長さは0より
            // 大きくても正規化後は""になり、""はどんな文字列にもContainsで一致してしまいます
            // （レビュー指摘: 空白だけのマスタ値が誤って条件として選ばれる不具合）。
            var candidates = option.Values
                .Select(value => (Value: value, Normalized: SearchTextNormalizer.Normalize(value)))
                .Where(pair => pair.Normalized.Length > 0)
                .Where(pair => normalized.Contains(pair.Normalized, StringComparison.Ordinal))
                .OrderByDescending(pair => pair.Normalized.Length)
                .ToArray();
            if (candidates.Length == 0) continue;

            var matched = candidates[0];
            selections[option.Column] = matched.Value;

            // 条件にした値の位置を記録します。削除はしません。
            // 削除すると前後の文字がつながり、質問文に無かった並びが語として現れます。
            var index = normalized.IndexOf(matched.Normalized, StringComparison.Ordinal);
            if (index >= 0)
            {
                for (var i = index; i < index + matched.Normalized.Length; i++)
                {
                    occupied[i] = true;
                }
            }
        }

        // 条件になった範囲を記号で埋めて残りを作ります。
        // 埋めた記号はどの値にも現れないため、語抽出では自然に無視されます。
        var remainderBuilder = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++)
        {
            remainderBuilder.Append(occupied[i] ? BlankSentinel : normalized[i]);
        }
        var remainder = remainderBuilder.ToString();

        return new ExtractedConditions(selections, ExtractTermsFrom(remainder, available));
    }

    /// <summary>
    /// 与えられた文字列から、いずれかの値に含まれる極大な部分文字列を語として取り出します。
    /// 「ぼーど」は「せっこうぼーど」に含まれるため語になります。
    /// どの値にも現れない「をおしえて」は落ちます。
    /// </summary>
    public static IReadOnlyList<string> ExtractTermsFrom(
        string? normalizedText, IReadOnlyList<CandidateFilterOptions> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var text = normalizedText ?? string.Empty;
        if (text.Length < MinimumTermLength) return Array.Empty<string>();

        // 値をすべて連結した1本の文字列に対して部分一致を見ます。
        // 値ごとに走査すると、値の数だけ照合が増えます。
        // 区切り文字を挟み、値をまたいだ偶然の一致を防ぎます。
        var haystack = new StringBuilder();
        foreach (var option in available)
        {
            foreach (var value in option.Values)
            {
                haystack.Append(ValueSeparator).Append(SearchTextNormalizer.Normalize(value));
            }
        }

        haystack.Append(ValueSeparator);
        var values = haystack.ToString();

        var terms = new List<string>();
        for (var start = 0; start < text.Length; start++)
        {
            // 同じ開始位置では、いちばん長く伸ばせたものだけを採ります。
            string? longest = null;
            var maxLength = Math.Min(MaximumTermLength, text.Length - start);
            for (var length = MinimumTermLength; length <= maxLength; length++)
            {
                var candidate = text.Substring(start, length);
                if (!values.Contains(candidate, StringComparison.Ordinal)) break;
                longest = candidate;
            }

            if (longest is not null) terms.Add(longest);
        }

        // 他の語に含まれる語を捨て、極大なものだけ残します。
        // 「ぼーど」と「ーど」の両方を残すと、AND検索が無意味に狭くなります。
        return terms
            .Distinct(StringComparer.Ordinal)
            .Where(term => !terms.Any(other =>
                other.Length > term.Length && other.Contains(term, StringComparison.Ordinal)))
            .ToArray();
    }
}
