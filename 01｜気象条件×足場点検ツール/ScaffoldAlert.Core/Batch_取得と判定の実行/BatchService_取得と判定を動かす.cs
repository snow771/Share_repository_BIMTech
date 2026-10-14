using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Forecast;
using ScaffoldAlert.Core.Judgement;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Batch;

/// <summary>
/// 気象庁からデータを取ってきて保存し、点検義務を判定する係。
///
/// Python版の run_batch.bat と同じ順序で動かす。
///   1. 気象データ (雨・風・雪) の取得と保存
///   2. 地震情報の取得と保存
///   3. 気象予測の取得と保存
///   4. 点検義務の判定とアラートの登録
///
/// 途中で失敗したときは、そこで止める。
/// 取れていないデータで判定まで進めても意味が無く、
/// 古いデータで判定したように見えてしまうため。
/// ただし気象予測は判定に使わないので、失敗しても判定は続ける。
/// </summary>
public class BatchService
{
    private readonly StationRepository _stations;
    private readonly WeatherService _weather;
    private readonly ObservationRepository _observations;
    private readonly EarthquakeFetcher _quakeFetcher;
    private readonly EarthquakeRepository _quakes;
    private readonly IHttpClientFactoryLike _http;
    private readonly ForecastService _forecast;
    private readonly ForecastSaveRepository _forecastSave;
    private readonly JudgeService _judge;
    private readonly AlertRepository _alerts;
    private readonly FetchLogRepository _log;

    public BatchService(StationRepository stations,
                        WeatherService weather,
                        ObservationRepository observations,
                        EarthquakeFetcher quakeFetcher,
                        EarthquakeRepository quakes,
                        IHttpClientFactoryLike http,
                        ForecastService forecast,
                        ForecastSaveRepository forecastSave,
                        JudgeService judge,
                        AlertRepository alerts,
                        FetchLogRepository log)
    {
        _stations = stations;
        _weather = weather;
        _observations = observations;
        _quakeFetcher = quakeFetcher;
        _quakes = quakes;
        _http = http;
        _forecast = forecast;
        _forecastSave = forecastSave;
        _judge = judge;
        _alerts = alerts;
        _log = log;
    }

    /// <summary>
    /// 取得と判定をひととおり動かす。
    /// </summary>
    /// <param name="judgeToo">
    /// true なら判定まで行う。false なら取得と保存だけ。
    /// </param>
    public BatchResult Run(bool judgeToo)
    {
        var result = new BatchResult();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // --- 1. 気象データ ---
        var step = FetchWeather();
        result.Steps.Add(step);

        if (!step.Succeeded)
        {
            // 取れていないまま判定へ進むと、古いデータで
            // 判定したように見えてしまう。ここで止める。
            watch.Stop();
            result.Seconds = watch.Elapsed.TotalSeconds;
            return result;
        }

        // --- 2. 地震 ---
        step = FetchEarthquakes();
        result.Steps.Add(step);

        if (!step.Succeeded)
        {
            watch.Stop();
            result.Seconds = watch.Elapsed.TotalSeconds;
            return result;
        }

        // --- 3. 気象予測 ---
        // これは判定に使わないので、失敗しても先へ進む。
        result.Steps.Add(FetchForecast());

        // --- 4. 判定 ---
        if (judgeToo)
        {
            var judged = Judge();
            result.Steps.Add(judged);
            result.NewAlerts = judged.Count;
        }

        watch.Stop();
        result.Seconds = watch.Elapsed.TotalSeconds;
        return result;
    }

    // ============================================================
    //  1. 気象データ
    // ============================================================
    private BatchStep FetchWeather()
    {
        var step = new BatchStep { Name = "気象データの取得と保存" };

        try
        {
            var targets = _stations.LoadTargetStationIds();

            if (targets.Count == 0)
            {
                step.Succeeded = false;
                step.ErrorMessage = "監視対象の観測所がありません。"
                    + "先に現場を登録してください。";
                return step;
            }

            var weather = _weather.Fetch(targets);

            step.Details.Add($"監視対象の観測所: {targets.Count} 地点");
            step.Details.Add($"降水量が取れた: {weather.Precip.Count} 地点");
            step.Details.Add($"最大風速の値: {weather.WindCount} 地点 "
                             + $"/ 起時: {weather.WindAtCount} 地点");
            step.Details.Add($"最大瞬間風速の値: {weather.GustCount} 地点 "
                             + $"/ 起時: {weather.GustAtCount} 地点");
            step.Details.Add($"24時間降雪量: {weather.Snow24hCount} 地点");

            foreach (var e in weather.Errors)
            {
                step.Details.Add($"[取れませんでした] {e.Key}: {e.Value}");
                _log.Write(e.Key, null, false, null, e.Value);
            }

            // 風速の値が1地点も取れていないのは異常。
            // 列は見つかったが値の表記が変わった場合など。
            // 成功として扱うと、全て空のまま誰も気づけない。
            if (weather.WindCount == 0 && !weather.Errors.ContainsKey("最大風速"))
            {
                step.Details.Add("[注意] 最大風速の値が1地点も取れていません。"
                    + "気象庁CSVの値の表記が変わった可能性があります。");
            }

            var saved = _observations.Save(weather, weather.WindCurrentAt);

            if (saved.ErrorMessage is not null)
            {
                step.Succeeded = false;
                step.ErrorMessage = "保存に失敗しました: " + saved.ErrorMessage;
                _log.Write("SAVE", null, false, 0, saved.ErrorMessage);
                return step;
            }

            step.Details.Add($"保存: {saved.Saved} 件");

            if (saved.SkippedNoTime > 0)
            {
                step.Details.Add("[注意] 観測時刻が決められず保存しなかった"
                                 + $"観測所: {saved.SkippedNoTime} 件");
            }

            step.Succeeded = true;
            step.Summary = $"{saved.Saved} 件を保存しました";
            _log.Write("SAVE", null, true, saved.Saved);
        }
        catch (Exception ex)
        {
            step.Succeeded = false;
            step.ErrorMessage = ex.Message;
            _log.Write("WEATHER", null, false, null, ex.Message);
        }

        return step;
    }

    // ============================================================
    //  2. 地震
    // ============================================================
    private BatchStep FetchEarthquakes()
    {
        var step = new BatchStep { Name = "地震情報の取得と保存" };

        try
        {
            var known = _quakes.LoadKnownEventIds();
            var fetched = _quakeFetcher.Fetch(known);

            if (fetched.ErrorMessage is not null)
            {
                step.Succeeded = false;
                step.ErrorMessage = fetched.ErrorMessage;
                _log.Write("QUAKE", JmaUrls.QuakeList, false, null,
                           fetched.ErrorMessage);
                return step;
            }

            // 地域名は震度の記録に名前を残すために使う。
            // 取れなくても保存は続ける (名前が空になるだけ)。
            var areaNames = LoadAreaNames();

            int savedQuakes = 0;
            int savedIntensities = 0;

            foreach (var entry in fetched.Targets)
            {
                int count = _quakes.SaveEarthquake(entry, areaNames);
                if (count >= 0)
                {
                    savedQuakes++;
                    savedIntensities += count;
                }
            }

            step.Details.Add($"気象庁が配信していた件数: "
                             + $"{fetched.DeliveredCount} 件");
            step.Details.Add($"保存した地震: {savedQuakes} 件");
            step.Details.Add($"地域別震度: {savedIntensities} 件");

            step.Succeeded = true;
            step.Summary = $"地震 {savedQuakes} 件を保存しました";
            _log.Write("QUAKE", JmaUrls.QuakeList, true, savedQuakes);
        }
        catch (Exception ex)
        {
            step.Succeeded = false;
            step.ErrorMessage = ex.Message;
            _log.Write("QUAKE", JmaUrls.QuakeList, false, null, ex.Message);
        }

        return step;
    }

    /// <summary>
    /// 市区町村コード → 地域名。
    /// 取れなくても止めない。名前が空になるだけで、判定には影響しない。
    /// </summary>
    private Dictionary<string, string> LoadAreaNames()
    {
        try
        {
            // 震度は7桁のコードで配信される。
            // 名前は気象庁の階層表(class20s)から直接引く。
            return LoadCityNames();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>市区町村コード(7桁) → 名前。</summary>
    private Dictionary<string, string> LoadCityNames()
    {
        var result = new Dictionary<string, string>();

        string json = _http.GetString(JmaUrls.Area);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("class20s", out var cities))
        {
            return result;
        }

        foreach (var city in cities.EnumerateObject())
        {
            if (city.Value.TryGetProperty("name", out var name)
                && name.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                result[city.Name] = name.GetString() ?? string.Empty;
            }
        }

        return result;
    }

    // ============================================================
    //  3. 気象予測
    // ============================================================
    private BatchStep FetchForecast()
    {
        var step = new BatchStep { Name = "気象予測の取得と保存" };

        try
        {
            var fetched = _forecast.Fetch();

            if (fetched.Errors.Count > 0)
            {
                foreach (var e in fetched.Errors)
                {
                    step.Details.Add($"[取れませんでした] {e.Key}: {e.Value}");
                }

                step.Succeeded = false;
                step.ErrorMessage = string.Join(" / ", fetched.Errors.Values);
                step.Summary = "取得できませんでした（判定には影響しません）";
                return step;
            }

            int links = _forecastSave.SaveSiteAreas(fetched.Links);
            int rows = _forecastSave.SaveForecasts(fetched.Rows);
            int winds = _forecastSave.SaveForecasts(fetched.WindTexts);
            int removed = _forecastSave.Cleanup();

            step.Details.Add($"現場と区域の対応: {links} 件");
            step.Details.Add($"予測: {rows} 件");
            step.Details.Add($"風の予報: {winds} 件");

            if (removed > 0)
            {
                step.Details.Add($"過ぎた予測を消しました: {removed} 件");
            }

            if (fetched.UnknownSiteIds.Count > 0)
            {
                step.Details.Add("[注意] 区域を割り出せない現場: "
                                 + $"{fetched.UnknownSiteIds.Count} 件");
            }

            step.Succeeded = true;
            step.Summary = $"予測 {rows + winds} 件を保存しました";
        }
        catch (Exception ex)
        {
            step.Succeeded = false;
            step.ErrorMessage = ex.Message;
            step.Summary = "取得できませんでした（判定には影響しません）";
        }

        return step;
    }

    // ============================================================
    //  4. 判定
    // ============================================================
    private BatchStep Judge()
    {
        var step = new BatchStep { Name = "点検義務の判定" };

        try
        {
            var judged = _judge.Judge();

            foreach (var warning in judged.Warnings)
            {
                step.Details.Add("[注意] " + warning);
            }

            int saved = _alerts.InsertAlerts(judged.Candidates);

            foreach (var c in judged.Candidates.Take(20))
            {
                step.Details.Add($"{c.SiteName} / {c.EventType} / "
                                 + $"{c.OccurredAt:yyyy-MM-dd HH:mm} / "
                                 + c.Detail);
            }

            step.Succeeded = true;
            step.Count = saved;
            step.Summary = saved == 0
                ? "基準に達した現場はありませんでした"
                : $"{saved} 件のアラートを登録しました";
        }
        catch (Exception ex)
        {
            step.Succeeded = false;
            step.ErrorMessage = ex.Message;
        }

        return step;
    }
}
