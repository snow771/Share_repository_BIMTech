// このファイルの目的:
//   仕上表出力の事前確認ダイアログ(TaskDialog)へ渡す本文(MainContent)と
//   警告詳細(ExpandedContent)を、Revit非依存の純粋関数として組み立てます。
//
// なぜ件数の打ち切りだけでは足りないのか:
//   2026-09-10、ID初期設定のTaskDialog.ExpandedContentへカテゴリ874件をそのまま渡した結果、
//   ダイアログが作られるのに描画されず、Revitが操作不能になりました（Escでのみ復帰）。
//   詳細はBui.Products.ParameterBindingReportTextの冒頭コメントを参照してください。
//   その修正は「件数」を打ち切ることで上限を作りましたが、件数だけでは不十分です。
//   1件のメッセージが長く、DisplayWidthWrapperで折り返された結果何行にもなれば、
//   件数を抑えても実際の描画行数は上限を超え得ます。
//   そのためここでは、件数(MaximumWarningItems)と実際の折返し後の行数(MaximumDialogLines)の
//   両方で打ち切ります。テスト(FinishScheduleDialogTextTests)では、件数・長さの両方が
//   大きい状況でも行数が上限を超えないことを機械的に固定しています。
//
// なぜBui.Addin側でこの文字列を直接組み立てないのか:
//   Bui.Addinはこのマシンではビルドできません(RevitAPI.dllが無いため)。
//   行数上限という、過去に実機のRevitを固まらせた安全条件は、
//   ビルド・テストできるBui.Shared側に置かなければ検証しようがありません。
//
// 修正時の影響:
//   MaximumDialogLinesを緩めると、同じ止まり方が仕上表出力の確認ダイアログでも再発します。
//   打ち切ったときは必ず残件数を書いてください。黙って切ると、
//   利用者は画面に出た件数がすべてだと思い込みます。

using System.Globalization;
using System.Text;
using Bui.Common;

namespace Bui.FinishSchedules;

/// <summary>仕上表出力の確認ダイアログ文言を組み立てます。純粋関数として保ちます。</summary>
public static class FinishScheduleDialogText
{
    /// <summary>
    /// ExpandedContentへ渡してよい行数の上限です。
    /// ParameterBindingReportText.MaximumDialogLinesと同じ考え方（実機で874行が
    /// 描画されなかった事故を踏まえ、境目が分からないため桁で余裕を取る）で、
    /// このダイアログ専用に別途固定します。
    /// </summary>
    public const int MaximumDialogLines = 40;

    /// <summary>ExpandedContentへ表示する警告の件数上限です。行数の上限とは独立に、まず件数で絞ります。</summary>
    private const int MaximumWarningItems = 50;

    private const string WarningHeading = "【警告】";

    /// <summary>
    /// MainContentへ渡す本文です。件数だけで、警告の内容そのものは含みません。
    /// 数値6個の固定行数のため、行数の上限を気にする必要はありません。
    /// </summary>
    /// <param name="missingProductIdCount">
    /// 「一般名称未登録」(GeneralNameMissing)と「Product_ID未登録」(ProductMissing)を
    /// 合算した未解決Product_ID数です。設計上この2つは別の状態ですが、この行は合算した
    /// 件数を示すだけなので、ラベルは「未解決Product_ID数」とし片方だけを指す表現にしません
    /// (FinishSchedulePreparedResult.CountDistinctMissingIds参照)。
    /// </param>
    /// <param name="warningCount">
    /// 利用者操作の設計(§3)が求める6項目のうち最後の「その他の警告件数」です。
    /// ExpandedContentを開かない利用者にも、警告が何件あるかを本文だけで伝えます。
    /// </param>
    public static string BuildMainContent(
        int internalRoomCount,
        int externalTypeCount,
        int missingProductIdCount,
        int affectedRoomCount,
        int affectedExternalTypeCount,
        int warningCount)
    {
        var content = new StringBuilder();
        content.Append("内部仕上表: ").Append(internalRoomCount.ToString(CultureInfo.InvariantCulture)).AppendLine(" 部屋");
        content.Append("外部仕上表: ").Append(externalTypeCount.ToString(CultureInfo.InvariantCulture)).AppendLine(" タイプ");
        content.Append("未解決Product_ID数: ").AppendLine(missingProductIdCount.ToString(CultureInfo.InvariantCulture));
        content.Append("影響部屋数: ").AppendLine(affectedRoomCount.ToString(CultureInfo.InvariantCulture));
        content.Append("影響外部タイプ数: ").AppendLine(affectedExternalTypeCount.ToString(CultureInfo.InvariantCulture));
        content.Append("警告件数: ").Append(warningCount.ToString(CultureInfo.InvariantCulture));
        return content.ToString();
    }

    /// <summary>
    /// ExpandedContentへ渡す警告詳細です。件数上限(MaximumWarningItems)と、
    /// 実際の折返し後の行数上限(MaximumDialogLines)の両方で打ち切り、
    /// どちらで打ち切っても残件数を明示します。
    /// </summary>
    public static string BuildExpandedContent(IReadOnlyList<FinishScheduleWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        // 見出し1行と、打ち切ったときの省略通知1行を必ず確保します。
        // 残りだけを警告本文の折返し後行数へ割り当てるため、
        // 「見出し+本文+省略通知」の合計は必ずMaximumDialogLines以下になります。
        const int reservedLines = 2;
        var availableBodyLines = Math.Max(0, MaximumDialogLines - reservedLines);

        var lines = new List<string> { WarningHeading };
        var usedBodyLines = 0;
        var shownCount = 0;
        var consideredCount = Math.Min(warnings.Count, MaximumWarningItems);

        for (var index = 0; index < consideredCount; index++)
        {
            var wrapped = DisplayWidthWrapper.Wrap("・" + warnings[index].Message);
            var wrappedLines = wrapped.Split('\n');

            // ここで1件でも超えるなら、その1件を部分的に混ぜず打ち切ります。
            // 半端に混ぜると「何件目まで出たか」が利用者にも実装にも分からなくなります。
            if (usedBodyLines + wrappedLines.Length > availableBodyLines)
            {
                break;
            }

            lines.AddRange(wrappedLines);
            usedBodyLines += wrappedLines.Length;
            shownCount++;
        }

        var omittedCount = warnings.Count - shownCount;
        if (omittedCount > 0)
        {
            lines.Add($"・ほか {omittedCount.ToString(CultureInfo.InvariantCulture)} 件は省略しました。");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
