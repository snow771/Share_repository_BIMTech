// このファイルの目的:
//   文字列を「表示幅」で折り返します。char数ではなく、画面で占める桁数で数えます。
//
// なぜchar数で数えないのか:
//   「カテゴリ」は4文字ですが画面では8桁を占めます。char数で切ると、
//   日本語だけの行が想定の半分の幅にしかならず、折り返しの意味がなくなります。
//
// 修正時の影響:
//   RevitのTaskDialogは、折りたたまれている詳細も含めていちばん長い行に幅を合わせます。
//   幅を指定するAPIが無いため、ここで抑えるのが唯一の手段です。
//   上限を緩めると、ID初期設定の結果ウィンドウがまた画面いっぱいに広がります。

using System.Globalization;
using System.Text;

namespace Bui.Common;

/// <summary>表示幅で折り返す純粋関数です。</summary>
public static class DisplayWidthWrapper
{
    /// <summary>
    /// 既定の折り返し幅です。ID初期設定ダイアログの本文でいちばん長い行
    /// 「Product_IDとAssembly_IDの初期設定が完了しました。」が49桁のため、
    /// 余白とアイコンの幅を含めてほぼ同じ見た目になります。
    /// </summary>
    public const int DefaultColumns = 60;

    /// <summary>折り返した続きの行の字下げです。1件が2行に割れたことを見て分かるようにします。</summary>
    private const string ContinuationIndent = "  ";

    /// <summary>全角2桁・半角1桁で数えた表示桁を返します。</summary>
    public static int MeasureWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var width = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            width += ElementWidth((string)enumerator.Current);
        }

        return width;
    }

    /// <summary>
    /// 表示幅で折り返します。元の改行は保ち、1行ずつ独立して折り返します。
    /// 日本語には単語の切れ目が無いため、単語単位ではなく文字単位で切ります。
    /// </summary>
    public static string Wrap(string? text, int columns = DefaultColumns)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 続きの行には字下げ（ContinuationIndent.Length）に加えて、
        // 最も幅の広い1文字（全角文字＝2桁）が入る余地が要ります。
        // それより狭いと、折り返した続きの行が要求した桁数を超えてしまいます
        // （例: columns=3 だと続きの行の上限は1桁になり、全角文字が1つも収まらない）。
        // 文字を分割することはできないため、この場合は折り返さずそのまま返します。
        if (columns < ContinuationIndent.Length + 2) return text;

        var result = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) result.Append('\n');
            AppendWrappedLine(result, lines[index], columns);
        }

        return result.ToString();
    }

    private static void AppendWrappedLine(StringBuilder result, string line, int columns)
    {
        if (line.Length == 0) return;

        var isContinuation = false;
        var used = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(line);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var elementWidth = ElementWidth(element);
            var limit = isContinuation ? columns - ContinuationIndent.Length : columns;

            // 境界をまたぐ文字は、半端に切らずその文字ごと次の行へ送ります。
            if (used > 0 && used + elementWidth > limit)
            {
                result.Append('\n').Append(ContinuationIndent);
                isContinuation = true;
                used = 0;
            }

            result.Append(element);
            used += elementWidth;
        }
    }

    /// <summary>
    /// 1つのテキスト要素の表示桁です。East Asian Width の Wide / Fullwidth を2桁として扱います。
    /// .NETに幅を返すAPIが無いため、必要な範囲を明示します。
    /// </summary>
    private static int ElementWidth(string element)
    {
        if (element.Length == 0) return 0;

        var codePoint = char.ConvertToUtf32(element, 0);

        // サロゲートペア（絵文字など）は2桁として数えます。
        // 数え漏らすと折り返し位置がずれ、壊れた文字が画面に出ます。
        if (codePoint > 0xFFFF) return 2;

        return codePoint switch
        {
            >= 0x1100 and <= 0x115F => 2,   // ハングル字母
            >= 0x2E80 and <= 0x303E => 2,   // CJK部首補助・かな補助・CJK記号
            >= 0x3041 and <= 0x33FF => 2,   // ひらがな・カタカナ・ハングル・CJK互換
            >= 0x3400 and <= 0x4DBF => 2,   // CJK統合漢字拡張A
            >= 0x4E00 and <= 0x9FFF => 2,   // CJK統合漢字
            >= 0xA000 and <= 0xA4CF => 2,   // イ文字
            >= 0xAC00 and <= 0xD7A3 => 2,   // ハングル音節
            >= 0xF900 and <= 0xFAFF => 2,   // CJK互換漢字
            >= 0xFE30 and <= 0xFE6F => 2,   // CJK互換形・小字形
            >= 0xFF00 and <= 0xFF60 => 2,   // 全角英数・全角記号
            >= 0xFFE0 and <= 0xFFE6 => 2,   // 全角記号
            _ => 1
        };
    }
}
