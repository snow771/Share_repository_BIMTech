// このファイルの目的:
//   「おはよう」のような定型的な挨拶・お礼・雑談だけの発言を、LLMの判定に頼らず
//   決定的に見分けます。
//
// なぜ必要か:
//   intentの判定は①（LlmQueryTranslator）の出力に完全に従う設計です。
//   小さいローカルモデルは挨拶に対しても intent:"search" を返すことがあり、
//   その場合「おはよう」が製品検索として扱われ、必ず0件で「見つかりませんでした」
//   になります（実機で確認済みの不具合）。
//   検索は決定的に、LLMは説明だけという方針（bui-deterministic-not-llm）に合わせ、
//   このありふれた失敗パターンだけは①より先に、質問文そのものから決定的に判定します。
//
// なぜ完全一致だけなのか:
//   「おはよう、せっこうボードある？」のような、挨拶に建材の話が続く発言まで
//   chatへ倒すと、検索が必要な発言が会話に飲み込まれます。短い定型句そのものだけの
//   ときに限ることで、誤判定の範囲を最小限に留めます。

using Bui.Data;

namespace Bui.Llm;

/// <summary>短い挨拶・お礼・雑談の定型句だけで構成された質問かどうかを判定します。</summary>
public static class GreetingDetector
{
    private static readonly string[] Phrases =
    [
        "おはよう", "おはようございます",
        "こんにちは", "こんにちわ", "こんばんは",
        "ありがとう", "ありがとうございます", "ありがとうございました",
        "どうも", "お疲れ様", "お疲れさまです", "おつかれさま",
        "よろしく", "よろしくお願いします", "はじめまして",
        "さようなら", "またね", "バイバイ",
        "hello", "hi", "thanks", "thank you"
    ];

    /// <summary>質問全体が、上の定型句のいずれかと（末尾の記号を除いて）一致するときにtrueを返します。</summary>
    public static bool IsGreetingOrSmallTalk(string? question)
    {
        var normalized = TrimTrailingPunctuation(SearchTextNormalizer.Normalize(question));
        if (normalized.Length == 0) return false;

        foreach (var phrase in Phrases)
        {
            if (string.Equals(normalized, SearchTextNormalizer.Normalize(phrase), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string TrimTrailingPunctuation(string value) =>
        value.TrimEnd('!', '！', '?', '？', '.', '。', ',', '、', '~', '〜', '♪', '♡');
}
