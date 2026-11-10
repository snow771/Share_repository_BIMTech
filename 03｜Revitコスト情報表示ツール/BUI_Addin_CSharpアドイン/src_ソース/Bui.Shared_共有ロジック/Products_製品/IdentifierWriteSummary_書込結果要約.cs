// このファイルの目的:
//   ID書込みの結果を、利用者が次に何をすればよいか分かる一文にまとめます。
//
// なぜ必要か:
//   書込み処理は失敗ごとに理由を持っています（パラメータが無い、読取専用、など）。
//   しかし画面には「失敗: 1件」という件数しか出しておらず、
//   利用者から見ると「手順どおり押したのに値が入らない」だけの状態でした。
//   原因が分かっているのに伝えないのは、黙って失敗するのと同じです。
//
// 修正時の影響:
//   理由の文面は書込み処理側が作ります。ここは並べ方と、
//   次の操作へつなぐ案内の付け方だけを決めます。

namespace Bui.Products;

/// <summary>ID書込みの結果を1行の説明にまとめます。</summary>
public static class IdentifierWriteSummary
{
    /// <summary>並べる理由の最大数です。多すぎると読めなくなります。</summary>
    private const int MaximumReportedReasons = 3;

    /// <summary>共有パラメータ未登録を示す理由に含まれる語です。</summary>
    private const string MissingParameterMarker = "パラメータがありません";

    /// <summary>この場合だけは、次にやることがはっきりしているので具体的に案内します。</summary>
    private const string MissingParameterHint =
        "リボンの「ID初期設定」を実行して共有パラメータを登録してから、もう一度お試しください。";

    /// <summary>
    /// 「書込不可」が何件あるかだけでは、利用者は何を直せばよいか分かりません。
    /// プレビューの時点で理由を並べます。0件なら空文字を返し、余計な行を出しません。
    /// </summary>
    public static string DescribeNotWritable(IReadOnlyList<string>? reasons)
    {
        var distinct = (reasons ?? Array.Empty<string>())
            .Select(reason => (reason ?? string.Empty).Trim())
            .Where(reason => reason.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0) return string.Empty;

        var shown = string.Join(" / ", distinct.Take(MaximumReportedReasons));
        var omitted = distinct.Length > MaximumReportedReasons
            ? $" ほか{distinct.Length - MaximumReportedReasons}種類"
            : string.Empty;
        var message = $"{Environment.NewLine}書込不可の理由: {shown}{omitted}";

        return distinct.Any(reason => reason.Contains(MissingParameterMarker, StringComparison.Ordinal))
            ? $"{message}{Environment.NewLine}{MissingParameterHint}"
            : message;
    }

    /// <summary>
    /// 上書き確認が無いために書かなかった件数を伝えます。0件なら空文字を返し、余計な行を出しません。
    /// 「書込不可の理由」とは別に出すのは、パラメータが無い等の失敗ではなく、
    /// 利用者の確認待ちで意図的に見送ったことを区別して伝えるためです。
    /// </summary>
    public static string DescribeConflictSkip(int skippedCount)
    {
        if (skippedCount <= 0) return string.Empty;

        return $"{Environment.NewLine}既存の値と異なるため、{skippedCount}件は書き込みませんでした。"
            + "上書きする場合は、確認のうえもう一度実行してください。";
    }

    public static string Describe(int writtenCount, IReadOnlyList<string>? failureReasons)
    {
        var reasons = (failureReasons ?? Array.Empty<string>())
            .Select(reason => (reason ?? string.Empty).Trim())
            .Where(reason => reason.Length > 0)
            .ToArray();

        if (reasons.Length == 0)
        {
            return $"Product_IDを{writtenCount}件に書き込みました。";
        }

        var distinct = reasons.Distinct(StringComparer.Ordinal).ToArray();
        var shown = string.Join(" / ", distinct.Take(MaximumReportedReasons));
        var omitted = distinct.Length > MaximumReportedReasons
            ? $" ほか{distinct.Length - MaximumReportedReasons}種類"
            : string.Empty;

        var message = $"Product_IDを{writtenCount}件に書き込みました。書き込めなかったもの{reasons.Length}件: {shown}{omitted}";

        return distinct.Any(reason => reason.Contains(MissingParameterMarker, StringComparison.Ordinal))
            ? $"{message} {MissingParameterHint}"
            : message;
    }
}
