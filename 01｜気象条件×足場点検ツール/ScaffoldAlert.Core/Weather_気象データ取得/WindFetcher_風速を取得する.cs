using System.Text.RegularExpressions;

namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 風速を取ってくる係。
///
/// 「最大風速(10分間平均)」と「最大瞬間風速」は、配信先が違うだけで
/// CSVの形はまったく同じ。そのため、この1つの係で両方を扱う。
///   ・最大風速     … 強風(10m/s)の判定に使う
///   ・最大瞬間風速 … 暴風(30m/s)の判定に使う
///
/// 値は「その日の最大値」で、深夜0時にリセットされる。
/// 実際に何時何分に吹いたか(起時)は別の列に入っているので併せて取る。
/// </summary>
public class WindFetcher
{
    private readonly IHttpClientFactoryLike _http;

    public WindFetcher(IHttpClientFactoryLike http)
    {
        _http = http;
    }

    /// <summary>
    /// 監視対象の観測所の風速を取ってくる。
    /// </summary>
    /// <param name="url">
    /// JmaUrls.Wind (最大風速) か JmaUrls.Gust (最大瞬間風速)。
    /// </param>
    /// <param name="currentAt">
    /// CSVに書かれている「現在時刻」。全行で同じ値なので先頭行から取る。
    /// 降水量が取れなかった観測所を保存するときの観測時刻に使う。
    /// </param>
    public Dictionary<string, WindRecord> Fetch(
        string url, HashSet<string> targetIds, out DateTime? currentAt)
    {
        currentAt = null;

        var table = CsvTable.Parse(_http.GetBytes(url));

        string? valueColumn = FindDailyMaxColumn(table);

        // 値の列が分からないまま空を返すと、風速が取れていないことに
        // 誰も気づけない。実際にこの不具合が起き、強風の点検義務を
        // 長期間検知できていなかった。そのため、ここで止めて知らせる。
        if (valueColumn is null)
        {
            throw new InvalidOperationException(
                "最大値の列が見つかりません。気象庁のCSVの形が変わった"
                + "可能性があります。列名: "
                + string.Join(" / ", table.Columns.Take(12)));
        }

        var (hourColumn, minuteColumn) = FindDailyMaxTimeColumns(table, valueColumn);
        var timeColumns = PrecipitationFetcher.FindTimeColumns(table);

        // 「現在時刻」は全行同じなので先頭行から取る。
        if (timeColumns.Count == 5 && table.Rows.Count > 0)
        {
            currentAt = PrecipitationFetcher.BuildObservedAt(
                table, table.Rows[0], timeColumns);
        }

        var result = new Dictionary<string, WindRecord>();

        foreach (var row in table.Rows)
        {
            string id = table.FirstCell(row);
            if (!targetIds.Contains(id))
            {
                continue;
            }

            DateTime? at = null;

            // 起時は「時」と「分」だけで、日付が入っていない。
            // 日付は現在時刻の列から借りて組み立てる。
            if (hourColumn is not null && minuteColumn is not null
                && timeColumns.Count == 5)
            {
                int? year = JmaValue.ToInt(table.Value(row, timeColumns["年"]));
                int? month = JmaValue.ToInt(table.Value(row, timeColumns["月"]));
                int? day = JmaValue.ToInt(table.Value(row, timeColumns["日"]));
                int? hour = JmaValue.ToInt(table.Value(row, hourColumn));
                int? minute = JmaValue.ToInt(table.Value(row, minuteColumn));

                if (year is not null && month is not null && day is not null
                    && hour is not null && minute is not null)
                {
                    try
                    {
                        // 24時表記が来ても壊れないよう、0時から時間を足して作る。
                        at = new DateTime(year.Value, month.Value, day.Value,
                                          0, minute.Value, 0)
                             .AddHours(hour.Value);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        at = null;
                    }
                }
            }

            result[id] = new WindRecord(
                JmaValue.ToDecimal(table.Value(row, valueColumn)), at);
        }

        return result;
    }

    /// <summary>
    /// 「〇日の最大値(m/s)」の列を探す。
    ///
    /// この列名には日付が入っており、毎日変わる (例: 18日の最大値(m/s))。
    /// 日付を固定で書くと翌日には動かなくなるため、
    /// CSVが示している当日の日から組み立てる。
    /// </summary>
    public static string? FindDailyMaxColumn(CsvTable table)
    {
        string? dayColumn = table.FindColumn("現在時刻", "日");
        if (dayColumn is not null && table.Rows.Count > 0)
        {
            int? day = JmaValue.ToInt(table.Value(table.Rows[0], dayColumn));
            if (day is not null)
            {
                string name = $"{day}日の最大値(m/s)";
                if (table.HasColumn(name))
                {
                    return name;
                }
            }
        }

        // 念のため、形が合う列を探す。
        foreach (var name in table.Columns)
        {
            if (Regex.IsMatch(name, @"^\d+日の最大値\(m/s\)$"))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// 最大値の起時「(時)」「(分)」の列を探す。値の列と同じ日付が付いている。
    ///
    /// 気象庁は括弧を全角で書いているが、半角に変わっただけで
    /// 全地点の起時が空になってしまうため、両方の表記を試す。
    /// 見つかる範囲が広がるだけで、誤った列を拾うことはない。
    /// </summary>
    public static (string? Hour, string? Minute) FindDailyMaxTimeColumns(
        CsvTable table, string valueColumn)
    {
        // 「18日の最大値(m/s)」の「18」の部分を取り出す。
        int cut = valueColumn.IndexOf("日の最大値", StringComparison.Ordinal);
        string head = cut >= 0 ? valueColumn.Substring(0, cut) : valueColumn;

        string? hour = null;
        string? minute = null;

        (string Left, string Right)[] brackets =
        {
            ("（", "）"),   // 全角
            ("(", ")"),     // 半角
        };

        foreach (var (left, right) in brackets)
        {
            string candidateHour = $"{head}日の最大値起時{left}時{right}";
            string candidateMinute = $"{head}日の最大値起時{left}分{right}";

            if (hour is null && table.HasColumn(candidateHour))
            {
                hour = candidateHour;
            }
            if (minute is null && table.HasColumn(candidateMinute))
            {
                minute = candidateMinute;
            }
        }

        return (hour, minute);
    }
}
