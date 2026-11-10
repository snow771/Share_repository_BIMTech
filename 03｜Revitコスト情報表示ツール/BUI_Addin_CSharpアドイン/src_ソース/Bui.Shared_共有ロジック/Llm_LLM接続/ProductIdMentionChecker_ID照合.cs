// このファイルの目的:
//   AIの回答に出てくるProduct_IDを、実際の候補と突き合わせます。
//
// なぜ必要か:
//   LLMは存在しないIDを自信満々に書きます。
//   人がそれを信じて入力すると、モデルには誰も知らないIDが入ります。
//   ①②の作りでは防げないので、最後に文章を見て印を付けます。
//
// 修正時の影響:
//   印は回答の末尾へまとめて足します。文中に差し込むと、
//   モデルが書いた文章を改変することになり、何が原因か分からなくなります。

using System.Text;
using System.Text.RegularExpressions;

namespace Bui.Llm;

/// <summary>回答中のProduct_IDが実在するかを確かめます。</summary>
public static class ProductIdMentionChecker
{
    // 英数字・ハイフン・アンダースコアが3つ以上続き、英字と数字の両方を含むものを候補とみなします。
    // 日本語の文章と混ざっても拾えるよう、単語境界ではなく文字種で区切ります。
    // ここでは「候補」であって「Product_ID」ではありません。ISO-9001やCOVID-19のような
    // 一般的な技術用語もこの形に一致するため、実在するIDの接頭辞（先頭のハイフンまで）と
    // 一致するものだけをID疑いとして扱います。
    private static readonly Regex IdPattern = new(
        @"[A-Za-z0-9_]+(?:-[A-Za-z0-9_]+)+", RegexOptions.Compiled);

    public static string MarkUnknownIds(string answer, IReadOnlyCollection<string> knownIds)
    {
        if (string.IsNullOrWhiteSpace(answer)) return answer ?? string.Empty;
        ArgumentNullException.ThrowIfNull(knownIds);

        var known = new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);
        // 既知IDの「先頭からの最初のハイフンまで」を接頭辞集合とし、
        // 実在するIDと同じ形の語だけを疑います。既知IDが無ければ判断材料が無いため何も疑いません。
        var knownPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in known)
        {
            var hyphenIndex = id.IndexOf('-');
            if (hyphenIndex >= 0)
            {
                knownPrefixes.Add(id.Substring(0, hyphenIndex + 1));
            }
        }

        var unknown = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (knownPrefixes.Count > 0)
        {
            foreach (Match match in IdPattern.Matches(answer))
            {
                var value = match.Value;
                if (!value.Any(char.IsAsciiLetter) || !value.Any(char.IsAsciiDigit)) continue;
                if (known.Contains(value)) continue;

                var hyphenIndex = value.IndexOf('-');
                if (hyphenIndex < 0) continue;
                var prefix = value.Substring(0, hyphenIndex + 1);
                if (!knownPrefixes.Contains(prefix)) continue;

                if (!seen.Add(value)) continue;
                unknown.Add(value);
            }
        }

        if (unknown.Count == 0) return answer;

        var builder = new StringBuilder(answer);
        builder.Append('\n').Append('\n')
            .Append("※ 次のIDは候補データにありません。AIが作った可能性があります: ")
            .Append(string.Join(" / ", unknown));
        return builder.ToString();
    }
}
