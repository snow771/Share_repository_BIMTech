namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 降水量・風速・雪をまとめて取ってくる係。
///
/// ★この段階ではデータベースに書き込まない。
///   取ってきた内容を返すだけにして、Python版と突き合わせる。
///
/// 取得先ごとに try で囲んでいるのは、1つが失敗しても
/// 残りを取りに行くため。3本は別々のHTTPなので、
/// 片方だけ失敗するのは普通に起こる。
/// ここでまとめて止めてしまうと、降水量は取れているのに
/// 風速の失敗に巻き込まれて大雨の判定材料まで失われる。
/// </summary>
public class WeatherService
{
    private readonly PrecipitationFetcher _precip;
    private readonly WindFetcher _wind;
    private readonly SnowFetcher _snow;

    public WeatherService(PrecipitationFetcher precip, WindFetcher wind,
                          SnowFetcher snow)
    {
        _precip = precip;
        _wind = wind;
        _snow = snow;
    }

    /// <summary>監視対象の観測所の気象データを取ってくる。</summary>
    public WeatherFetchResult Fetch(HashSet<string> targetIds)
    {
        var result = new WeatherFetchResult
        {
            TargetStationCount = targetIds.Count,
        };

        // --- 降水量 ---
        try
        {
            result.Precip = _precip.Fetch(targetIds);
        }
        catch (Exception ex)
        {
            result.Errors["降水量"] = ex.Message;
        }

        // --- 最大風速 (強風の判定に使う) ---
        try
        {
            result.Wind = _wind.Fetch(JmaUrls.Wind, targetIds, out var at);
            result.WindCurrentAt = at;
        }
        catch (Exception ex)
        {
            result.Errors["最大風速"] = ex.Message;
        }

        // --- 最大瞬間風速 (暴風の判定に使う) ---
        try
        {
            result.Gust = _wind.Fetch(JmaUrls.Gust, targetIds, out var at);
            // 最大風速の側が取れなかったときの控えとして使う。
            result.WindCurrentAt ??= at;
        }
        catch (Exception ex)
        {
            result.Errors["最大瞬間風速"] = ex.Message;
        }

        // --- 雪 (大雪の判定に使う) ---
        try
        {
            result.Snow = _snow.Fetch(targetIds, out var at);
            result.SnowObservedAt = at;
        }
        catch (Exception ex)
        {
            result.Errors["雪"] = ex.Message;
        }

        return result;
    }
}
