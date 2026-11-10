// このファイルの目的:
//   ID初期設定の結果を、ダイアログ用の短いテキストとファイル用の全一覧に分けて組み立てます。
//
// なぜ分けるのか:
//   2026-09-10、カテゴリ874件をそのままTaskDialogのExpandedContentへ渡したところ、
//   ダイアログが作られるのに描画されず、Revitが操作不能になりました（Escでのみ復帰）。
//   Windowsのタスクダイアログは中身の量で大きさが決まり、上限を超えると描画されません。
//   幅を指定するAPIも、中身をスクロールさせる手段もありません。
//   折り返しで幅を詰める案は既に外しています。幅は縮みましたが行数が倍になり、
//   かえって描画されない側へ振り切れました。中身の量そのものを減らすしかありません。
//
//   874行はそもそも画面で読めるものではありませんでした。
//   人が判断する必要がある「競合」だけをダイアログに残し、
//   全一覧はファイルへ書き出して保存先を示します。情報は1件も失いません。
//
// 修正時の影響:
//   MaximumDialogLines を緩めると、同じ止まり方が再発します。
//   ダイアログへカテゴリ一覧を戻さないでください。カテゴリ数は環境によって増えます。
//   打ち切ったときは必ず残件数を書いてください。黙って切ると、
//   利用者は画面に出た件数がすべてだと思い込みます。

using System.Globalization;
using System.Text;

namespace Bui.Products;

/// <summary>初期設定の結果テキストを組み立てます。純粋関数として保ちます。</summary>
public static class ParameterBindingReportText
{
    /// <summary>
    /// ダイアログの「詳細の表示」へ渡してよい行数の上限です。
    /// 実機で1,808行が描画されず、876行は描画されていました。
    /// 境目は分からないため、桁で余裕を取ります。
    /// </summary>
    public const int MaximumDialogLines = 40;

    /// <summary>見出しや案内で使う行数を除いた、競合に使える行数です。</summary>
    private const int MaximumConflictLines = 20;

    /// <summary>ダイアログの「詳細の表示」に出すテキストです。競合と保存先だけを書きます。</summary>
    public static string BuildDialogDetail(ParameterBindingReport report, string? savedFilePath)
    {
        ArgumentNullException.ThrowIfNull(report);

        var detail = new StringBuilder();

        if (report.Conflicts.Count > 0)
        {
            detail.AppendLine("【競合・要確認】");
            var shown = Math.Min(report.Conflicts.Count, MaximumConflictLines);
            for (var index = 0; index < shown; index++)
            {
                detail.Append("・").AppendLine(report.Conflicts[index]);
            }

            var omitted = report.Conflicts.Count - shown;
            if (omitted > 0)
            {
                detail.Append("・ほか ").Append(omitted.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("件は省略しました。全件は下記のファイルにあります。");
            }

            detail.AppendLine();
        }

        detail.AppendLine("【カテゴリごとの結果】");
        detail.Append("カテゴリ ").Append(report.Categories.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine("件の内訳は、この画面では読み切れないためファイルに書き出しています。");

        if (string.IsNullOrWhiteSpace(savedFilePath))
        {
            detail.AppendLine("ただし、そのファイルを保存できませんでした。");
            detail.AppendLine("内訳が必要な場合は、この画面をBUIの管理者へお伝えください。");
        }
        else
        {
            detail.AppendLine(savedFilePath);
        }

        return detail.ToString();
    }

    /// <summary>ファイルへ書き出す全一覧です。ここには打ち切りを入れません。</summary>
    public static string BuildFileReport(ParameterBindingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var details = new StringBuilder();
        if (report.Conflicts.Count > 0)
        {
            details.AppendLine("【競合・要確認】");
            foreach (var conflict in report.Conflicts)
            {
                details.Append("・").AppendLine(conflict);
            }

            details.AppendLine();
        }

        details.AppendLine("【カテゴリごとの結果】");
        foreach (var result in report.Categories)
        {
            details
                .Append("・[").Append(result.Status).Append("] ")
                .Append(result.CategoryName).Append(": ")
                .AppendLine(result.Message);
        }

        return details.ToString();
    }

    /// <summary>
    /// 保存するファイル名です。実行のたびに上書きすると、前回の結果と見比べられません。
    /// </summary>
    public static string BuildFileName(DateTime timestamp) =>
        "初期設定結果_" + timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt";
}
