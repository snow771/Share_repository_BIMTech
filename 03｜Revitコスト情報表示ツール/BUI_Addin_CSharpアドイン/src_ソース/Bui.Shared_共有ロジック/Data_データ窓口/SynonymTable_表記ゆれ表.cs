// このファイルの目的:
//   現場の言い方（PB・石膏ボード）を、マスタの値（せっこうボード）へ結び付けます。
//
// なぜコードに書かないのか:
//   現場の言い方は増え続けます。コードへ書くと、増えるたびにビルドと配置が要ります。
//   利用者はその言い方を知っている当人であり、CSVなら本人が足せます。
//
// 修正時の影響:
//   値がマスタに実在するかを必ず確かめてください。確かめないと、
//   マスタが変わって値が消えたときに、黙って効かない行が残ります。
//   当たらなかった行を問題として数えないでください。
//   表には使われない行が普通に入っています。

namespace Bui.Data;

/// <summary>1つの言い換えです。Columnが空なら、値を検索語として足します。</summary>
public sealed record SynonymEntry(string Spoken, string Column, string Value);

/// <summary>
/// 表記ゆれ.csvを1回読んだ結果です。TableとProblemsを必ずセットで運びます。
/// チャットは表記ゆれ.csvを1往復ごとに読み直します（CsvSynonymLoaderをターンごとに
/// 新しく作って読み捨てる形）。そのため、読んだ側（Loader.LastProblems）を呼び出し元が
/// 直接見る術がありません。Tableだけを返すと、起動時以外のCSVの見出し誤り・行の不備が
/// 画面へ二度と出せなくなります（レビュー指摘）。
/// </summary>
public sealed record SynonymLoadResult(SynonymTable Table, IReadOnlyList<string> Problems)
{
    /// <summary>表を使わない・読めないときの既定値です。</summary>
    public static SynonymLoadResult Empty { get; } = new(SynonymTable.Empty, Array.Empty<string>());
}

/// <summary>表記ゆれ表を引いた結果です。Problemsは画面へ出す理由です。</summary>
public sealed record SynonymMatch(
    IReadOnlyDictionary<string, string> Selections,
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Problems);

/// <summary>言い換えの表です。照合は正規化済みの文字列に対して行います。</summary>
public sealed class SynonymTable
{
    private readonly IReadOnlyList<SynonymEntry> _entries;

    public SynonymTable(IReadOnlyList<SynonymEntry> entries) =>
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    /// <summary>表が無いときに使う空の表です。呼び出し側でnull判定を書かせません。</summary>
    public static SynonymTable Empty { get; } = new(Array.Empty<SynonymEntry>());

    public SynonymMatch Match(string? normalizedText, IReadOnlyList<CandidateFilterOptions> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var text = normalizedText ?? string.Empty;

        var terms = new List<string>();
        var problems = new List<string>();
        // 列ごとに、いちばん長く一致した言い方の行だけを残します。表の並び順（先勝ち）に
        // 頼ると、短い言い方（「不燃材」）が長い言い方（「準不燃材」）に含まれる場合に、
        // 単に先に書かれているという理由だけで短い方が勝ってしまいます（レビュー指摘）。
        // 第1層（QuestionTermExtractor）も同じ「長い方を採る」規則のため、ここでも揃えます。
        var bestByColumn = new Dictionary<string, (int SpokenLength, string Value)>(StringComparer.Ordinal);

        foreach (var entry in _entries)
        {
            var spoken = SearchTextNormalizer.Normalize(entry.Spoken);
            if (spoken.Length == 0) continue;
            if (!text.Contains(spoken, StringComparison.Ordinal)) continue;

            var normalizedValue = SearchTextNormalizer.Normalize(entry.Value);

            // 項目が空の行は、値を検索語として足します。項目を特定できない言い換え用です。
            // ここも値がマスタに実在するかを確かめます。確かめないと、値の打ち間違いが
            // 実在しないAND条件として静かに0件を保証するだけになり、Problemsにも出ません
            // （レビュー指摘）。
            if (entry.Column.Length == 0)
            {
                var exists = available.Any(item => item.Values.Any(value =>
                    string.Equals(SearchTextNormalizer.Normalize(value), normalizedValue, StringComparison.Ordinal)));
                if (!exists)
                {
                    problems.Add($"表記ゆれ「{entry.Spoken}」の値「{entry.Value}」はどの絞り込み項目の値にもありません。");
                    continue;
                }

                if (normalizedValue.Length > 0) terms.Add(normalizedValue);
                continue;
            }

            var option = available.FirstOrDefault(item =>
                string.Equals(item.Column, entry.Column, StringComparison.Ordinal));
            if (option is null)
            {
                problems.Add($"表記ゆれ「{entry.Spoken}」の項目「{entry.Column}」は絞り込み項目にありません。");
                continue;
            }

            var value = option.Values.FirstOrDefault(item =>
                string.Equals(SearchTextNormalizer.Normalize(item), normalizedValue, StringComparison.Ordinal));
            if (value is null)
            {
                problems.Add($"表記ゆれ「{entry.Spoken}」の値「{entry.Value}」は「{entry.Column}」にありません。");
                continue;
            }

            if (!bestByColumn.TryGetValue(option.Column, out var current) || spoken.Length > current.SpokenLength)
            {
                bestByColumn[option.Column] = (spoken.Length, value);
            }
        }

        var selections = bestByColumn.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
        return new SynonymMatch(selections, terms, problems);
    }
}
