// このファイルの目的:
//   CSVの1行が読めなくても、残りの行を使えるようにします。
//   読み飛ばした行数と最初の理由を、利用者向けの文言としてまとめます。
//
// なぜ必要か:
//   以前は1行でも不正だとCSV全体を読み込み拒否していました。
//   しかし実際の単価情報CSVでは、建材コードへの紐付けが未完了の行が
//   4042行中3124行あります。これは壊れたデータではなく作業途中の正常な状態です。
//   全件拒否だと、紐付け済みの918行まで使えなくなり、機能そのものが成立しません。
//
// 安全性:
//   読み飛ばすのは「メモリ上の変換」だけです。CSVへの書き戻しは
//   解析済みの生の行から行うため、読み飛ばした行がファイルから消えることはありません。
//
// 修正時の影響:
//   見出し（列名）の不足は、ここではなく呼び出し側で全体エラーとして扱います。
//   列名が違えば全行が無意味になるため、その区別は維持してください。

namespace Bui.Data.Csv;

/// <summary>不正な行を読み飛ばしつつ、その事実を利用者へ伝えるための共通処理です。</summary>
public static class CsvRowSkipReport
{
    /// <summary>利用者向けメッセージに並べる理由の最大数です。多すぎると読めなくなります。</summary>
    private const int MaximumReportedReasons = 3;

    /// <summary>
    /// 各行を変換し、失敗した行だけを飛ばします。
    /// <paramref name="problems"/> には、読み飛ばしがあった場合だけ説明が入ります。
    /// </summary>
    public static IReadOnlyList<T> MapRows<T>(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        Func<IReadOnlyDictionary<string, string>, T> map,
        out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);

        var mapped = new List<T>(rows.Count);
        var reasons = new List<string>();
        var skipped = 0;

        for (var index = 0; index < rows.Count; index++)
        {
            try
            {
                mapped.Add(map(rows[index]));
            }
            catch (InvalidDataException exception)
            {
                skipped++;
                // 見出しが1行目なので、データの1件目はファイル上の2行目です。
                if (reasons.Count < MaximumReportedReasons)
                {
                    reasons.Add($"{index + 2}行目: {exception.Message}");
                }
            }
        }

        problems = skipped == 0
            ? Array.Empty<string>()
            : new[] { $"{skipped}行を読み飛ばしました（{rows.Count}行中）。{string.Join(" / ", reasons)}" };

        return mapped;
    }
}
