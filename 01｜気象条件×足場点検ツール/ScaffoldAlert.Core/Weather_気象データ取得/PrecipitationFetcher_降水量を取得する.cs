namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 降水量を取ってくる係。
///
/// 気象庁のCSVには1・3・6・12・24・48・72時間と日降水量が入っている。
/// 大雨の判定に使うのは12時間積算値。
/// </summary>
public class PrecipitationFetcher
{
    private readonly IHttpClientFactoryLike _http;

    public PrecipitationFetcher(IHttpClientFactoryLike http)
    {
        _http = http;
    }

    /// <summary>
    /// 監視対象の観測所の降水量を取ってくる。
    /// </summary>
    public Dictionary<string, PrecipRecord> Fetch(HashSet<string> targetIds)
    {
        var table = CsvTable.Parse(_http.GetBytes(JmaUrls.Precipitation));

        // 列名で探す。列の順番が変わっても動くようにするため。
        string? col1h = table.FindColumn("1時間降水量", "現在値");
        string? col12h = table.FindColumn("12時間降水量", "現在値");
        string? col24h = table.FindColumn("24時間降水量", "現在値");
        string? colQuality = table.FindColumn("12時間降水量", "現在値", "品質");

        // 12時間降水量が見つからないと大雨の判定ができない。
        // 黙って空を返すと、基準に達していても誰も気づけないため、
        // ここで止めて知らせる。
        if (col12h is null)
        {
            throw new InvalidOperationException(
                "12時間降水量の列が見つかりません。気象庁のCSVの形が変わった"
                + "可能性があります。列名: "
                + string.Join(" / ", table.Columns.Take(12)));
        }

        var timeColumns = FindTimeColumns(table);

        var result = new Dictionary<string, PrecipRecord>();

        foreach (var row in table.Rows)
        {
            string id = table.FirstCell(row);
            if (!targetIds.Contains(id))
            {
                continue;
            }

            var observedAt = BuildObservedAt(table, row, timeColumns);
            if (observedAt is null)
            {
                // 観測時刻が決められないものは保存しない。
                // 時刻を推測で作ると、記録が実態と食い違うため。
                continue;
            }

            result[id] = new PrecipRecord
            {
                ObservedAt = observedAt,
                Precip1h = JmaValue.ToDecimal(table.Value(row, col1h)),
                Precip12h = JmaValue.ToDecimal(table.Value(row, col12h)),
                Precip24h = JmaValue.ToDecimal(table.Value(row, col24h)),
                QualityFlag = table.Value(row, colQuality),
            };
        }

        return result;
    }

    /// <summary>
    /// 「現在時刻(年)(月)(日)(時)(分)」の列を探す。
    ///
    /// 列の順番や名称が変わっても動くよう、列名の末尾で探す。
    /// 気象庁は括弧を全角で書くことも半角で書くこともあるため両方見る。
    /// </summary>
    public static Dictionary<string, string> FindTimeColumns(CsvTable table)
    {
        var found = new Dictionary<string, string>();
        string[] keys = { "年", "月", "日", "時", "分" };

        foreach (var name in table.Columns)
        {
            foreach (var key in keys)
            {
                if (found.ContainsKey(key))
                {
                    continue;
                }
                if (name.EndsWith($"({key})") || name.EndsWith($"（{key}）"))
                {
                    found[key] = name;
                }
            }
        }

        return found.Count == 5 ? found : new Dictionary<string, string>();
    }

    /// <summary>年月日時分の列から観測時刻を組み立てる。</summary>
    public static DateTime? BuildObservedAt(
        CsvTable table, string[] row, Dictionary<string, string> timeColumns)
    {
        if (timeColumns.Count != 5)
        {
            return null;
        }

        return JmaValue.BuildDateTime(
            JmaValue.ToInt(table.Value(row, timeColumns["年"])),
            JmaValue.ToInt(table.Value(row, timeColumns["月"])),
            JmaValue.ToInt(table.Value(row, timeColumns["日"])),
            JmaValue.ToInt(table.Value(row, timeColumns["時"])),
            JmaValue.ToInt(table.Value(row, timeColumns["分"])));
    }
}
