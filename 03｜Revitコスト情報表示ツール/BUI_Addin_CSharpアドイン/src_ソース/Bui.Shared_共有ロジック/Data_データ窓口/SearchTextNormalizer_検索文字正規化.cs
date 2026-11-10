// このファイルの目的:
//   検索の照合前に、機械的に導ける表記の揺れだけをそろえます。
//
// なぜ辞書を置かないのか:
//   「PB」と「せっこうボード」は意味の揺れで、文字の変換では導けません。
//   ここへ辞書を持ち込むと、現場の言い方が増えるたびにビルドが必要になります。
//   意味の揺れは利用者が育てる 表記ゆれ.csv が受け持ちます。
//
// 修正時の影響:
//   ここを広げると別物が当たり、狭めると「セッコウ」で探せなくなります。
//   カタカナ→ひらがなの引き算は U+30A1〜U+30F6 に限ってください。
//   ヷヸヹヺ（U+30F7〜U+30FA）に対応するひらがなは無く、未定義の文字ができます。

using System.Text;

namespace Bui.Data;

/// <summary>検索で吸収してよい表記の揺れをそろえます。純粋関数として保ちます。</summary>
public static class SearchTextNormalizer
{
    /// <summary>全角英数記号→半角、カタカナ→ひらがな、小文字化、空白の除去を行います。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            // 半角・全角の空白は落とします。「せっこう ボード」を1語として扱うためです。
            if (char.IsWhiteSpace(character) || character == '　') continue;

            var converted = character;

            // 全角英数記号（！〜～）を半角へ。全角空白はここへ来ません（上で落としています）。
            if (converted >= '！' && converted <= '～')
            {
                converted = (char)(converted - 0xFEE0);
            }
            // カタカナをひらがなへ。ひらがなが存在する範囲だけを対象にします。
            else if (converted >= 'ァ' && converted <= 'ヶ')
            {
                converted = (char)(converted - 0x60);
            }

            builder.Append(char.ToLowerInvariant(converted));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 空白で区切って語の一覧にします。各語は正規化済みです。
    /// 分割は正規化より先に行います。Normalizeは空白を落とすため、
    /// 順序を逆にすると全体が1語になってしまいます。
    /// </summary>
    public static IReadOnlyList<string> SplitTerms(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(term => term.Length > 0)
            .ToArray();
    }
}
