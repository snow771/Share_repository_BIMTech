namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 気象庁から取ってくるファイルの場所をまとめた表。
///
/// 取得先を1か所にまとめておくと、気象庁の配信先が変わったときに
/// ここだけを直せば済む。
///
/// すべて気象庁が一般公開しているファイルで、契約も費用も要らない。
/// </summary>
public static class JmaUrls
{
    /// <summary>
    /// 地震一覧。最近の地震が新しい順に並んでいる。
    /// 市区町村ごとの震度もこの中に入っている。
    /// </summary>
    public const string QuakeList =
        "https://www.jma.go.jp/bosai/quake/data/list.json";

    /// <summary>
    /// 降水量。1・3・6・12・24・48・72時間と日降水量が1つのファイルに入っている。
    /// 大雨の判定には12時間積算値を使う。
    /// </summary>
    public const string Precipitation =
        "https://www.data.jma.go.jp/stats/data/mdrr/pre_rct/alltable/preall00_rct.csv";

    /// <summary>
    /// 最大風速 (10分間平均の日最大)。強風の判定に使う。
    /// 毎時00分の観測値が50分過ぎに更新される。
    /// </summary>
    public const string Wind =
        "https://www.data.jma.go.jp/stats/data/mdrr/wind_rct/alltable/mxwsp00_rct.csv";

    /// <summary>
    /// 最大瞬間風速。暴風の判定に使う。
    /// </summary>
    public const string Gust =
        "https://www.data.jma.go.jp/stats/data/mdrr/wind_rct/alltable/gust00_rct.csv";

    /// <summary>
    /// アメダスの最新の観測時刻。雪を取るときに使う。
    /// </summary>
    public const string AmedasLatestTime =
        "https://www.jma.go.jp/bosai/amedas/data/latest_time.txt";

    /// <summary>
    /// アメダスの観測値。雪(降雪量・積雪深)はCSVで配信されていないため、
    /// こちらから取る。{stamp} に日時を入れて使う。
    /// </summary>
    public const string AmedasMap =
        "https://www.jma.go.jp/bosai/amedas/data/map/{0}.json";

    /// <summary>
    /// 区域の階層表。市区町村コードから名前を引くのに使う。
    /// </summary>
    public const string Area =
        "https://www.jma.go.jp/bosai/common/const/area.json";
}
