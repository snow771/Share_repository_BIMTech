// このファイルの目的:
//   直近のやり取りを、プロンプトへ入れられる長さの文章にします。
//
// なぜ長さで切るのか:
//   件数で切ると、1件が異常に長い会話でプロンプトが溢れます。
//   溢れると、候補の一覧がモデルへ届かないまま回答させることになり、
//   AIは1件も見ていない製品について答え始めます。
//
// 修正時の影響:
//   落とすのは必ず古い方からです。新しい方を落とすと、
//   直前の質問を踏まえた会話が成り立たなくなります。
//   入り切らない1件は切り詰めずに捨てます。途中で切れた文を渡すと、
//   モデルはその続きを推測して答えます。

using System.Text;

namespace Bui.Llm;

/// <summary>会話1件分です。Speakerは画面の表示名（「あなた」「AI」）をそのまま使います。</summary>
public sealed record ChatHistoryTurn(string Speaker, string Text);

/// <summary>直近のやり取りを、上限に収めた1つの文章にします。純粋関数として保ちます。</summary>
public static class ChatHistory
{
    /// <summary>
    /// 履歴に使える長さです。プロンプト全体の上限の1/4までとします。
    /// 全体の上限（<see cref="LlmProviderSupport.MaximumPromptLength"/>）は増やしません。
    /// 2つ目の「全体上限」を作らず、その内訳を切るだけにするためです。
    /// </summary>
    public const int MaximumLength = LlmProviderSupport.MaximumPromptLength / 4;

    private const string Header = "直近のやり取り:\n";

    public static string Build(IReadOnlyList<ChatHistoryTurn>? turns, int budget = MaximumLength)
    {
        if (turns is null || turns.Count == 0) return string.Empty;
        if (budget <= Header.Length + 1) return string.Empty;

        // 新しい方から詰めて、入らなくなった時点で打ち切ります（＝古い方が落ちます）。
        var kept = new List<string>();
        var used = Header.Length + 1;
        for (var index = turns.Count - 1; index >= 0; index--)
        {
            var turn = turns[index];
            var line = turn.Speaker + ": " + turn.Text + "\n";
            if (used + line.Length > budget) break;
            kept.Add(line);
            used += line.Length;
        }

        if (kept.Count == 0) return string.Empty;

        var builder = new StringBuilder(Header);
        for (var index = kept.Count - 1; index >= 0; index--)
        {
            builder.Append(kept[index]);
        }

        builder.Append('\n');
        return builder.ToString();
    }
}
