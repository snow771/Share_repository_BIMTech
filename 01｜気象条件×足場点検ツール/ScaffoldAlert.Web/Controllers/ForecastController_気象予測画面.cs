using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Dashboard;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Forecast;
using ScaffoldAlert.Core.Judgement;
using ScaffoldAlert.Core.Weather;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// 気象予測の画面の窓口。
///
/// 強風や大雨が「起きた後」ではなく「起きる前」に把握し、
/// 対策会議の判断材料にするための画面。
///
/// ★ここに出るのは予測であり、点検義務そのものではない。
///   法令の点検義務は実際に悪天候が起きた後に生じるため、
///   ダッシュボードのアラートとは別の画面に分けている。
///   混ぜると、臨検で「点検していない事象」が並んで見えてしまう。
///
/// この画面はデータベースを読むだけで、書き込まない。
/// </summary>
public class ForecastController : Controller
{
    private readonly ForecastViewRepository _view;
    private readonly SiteDetailRepository _sites;
    private readonly ThresholdRepository _thresholds;
    private readonly AreaGeoJsonCache _areaShapes;
    private readonly IHttpClientFactoryLike _http;
    private readonly ForecastService _fetch;
    private readonly ILogger<ForecastController> _logger;

    public ForecastController(ForecastViewRepository view,
                              SiteDetailRepository sites,
                              ThresholdRepository thresholds,
                              AreaGeoJsonCache areaShapes,
                              IHttpClientFactoryLike http,
                              ForecastService fetch,
                              ILogger<ForecastController> logger)
    {
        _view = view;
        _sites = sites;
        _thresholds = thresholds;
        _areaShapes = areaShapes;
        _http = http;
        _fetch = fetch;
        _logger = logger;
    }

    /// <summary>気象予測の画面。</summary>
    /// <param name="kind">地図に出す種類 (風 / 雨 / 雪)。</param>
    public IActionResult Index(string kind = "風")
    {
        var model = new ForecastBoardViewModel
        {
            Kind = ForecastBoard.KindItem.ContainsKey(kind) ? kind : "風",
        };

        try
        {
            // 判定と同じ基準を使う。片方だけ変えると食い違うため、
            // 数字はデータベース (M_Threshold) の1か所から読む。
            var limits = _thresholds.LoadEnabled();
            model.WindLimit = ValueOf(limits, "WIND", 10m);
            model.RainLimit = ValueOf(limits, "RAIN", 50m);
            model.SnowLimit = ValueOf(limits, "SNOW", 25m);

            var forecasts = _view.LoadForecasts();
            model.Loaded = true;
            model.IsEmpty = forecasts.Count == 0;

            if (model.IsEmpty)
            {
                return View(model);
            }

            model.ReportedAt = forecasts
                .Where(f => f.ReportedAt is not null)
                .Max(f => f.ReportedAt);

            model.Coverage = _view.LoadCoverage();

            model.OverLimit = ForecastBoard.OverLimit(
                forecasts, model.WindLimit, model.RainLimit, model.SnowLimit);

            model.Possibilities = ForecastBoard.Summarize(
                forecasts, DateTime.Now);

            // 風は、気象庁が【高】【中】をほとんど出さない。
            // 地図の色と同じ根拠で、別の一覧にまとめる。
            model.Winds = ForecastBoard.SummarizeWind(forecasts, DateTime.Now);

            model.DuplicateNames = model.Possibilities
                .Select(p => new { p.SiteId, p.SiteName })
                .Distinct()
                .GroupBy(p => p.SiteName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            var levels = ForecastBoard.AreaLevels(
                forecasts, model.Kind, DateTime.Now);

            // 地図に出す現場。区域の警戒度と突き合わせて、
            // 危ない区域にある現場を目立たせる。
            var markers = _view.LoadSiteMarkers();
            model.SiteMarkerCount = markers.Count;

            // 絞り込みの選択肢。地図に出る現場の部署だけを並べる。
            // 現場が1つも無い部署を並べても選べないため。
            model.SiteDepartments = markers
                .Select(m => m.Department)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            model.MapDataJson = BuildMapData(levels, model.Kind, markers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "気象予測の画面を読み込めませんでした");
            model.Loaded = false;
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }

    /// <summary>
    /// 区域の形(GeoJSON)を返す。
    ///
    /// 気象庁から直接読み込ませずにこちらを通すのは、
    /// 一度取ってきたものを覚えておいて使い回すため。
    /// 数MBあるので、画面を開くたびに取りに行くと遅い。
    /// </summary>
    public IActionResult Areas()
    {
        try
        {
            return Content(_areaShapes.Get(_http), "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "区域の形を取得できませんでした");
            return Content("{\"features\":[]}", "application/json");
        }
    }

    /// <summary>
    /// 1つの現場の、時間帯ごとの警報級の可能性を返す。
    ///
    /// 一覧は現場ごとに1行へまとめているため、そこからは
    /// 「いつ高くなって、いつ収まるのか」が読み取れない。
    /// 対策会議では時間の並びが判断材料になる。
    /// </summary>
    /// <param name="kind">
    /// いま見ている種類 (風 / 雨 / 雪)。
    /// その種類のことだけを出す。雨を見ているときに風の話を混ぜると、
    /// 何の判断をしているのか分からなくなる。
    /// </param>
    public IActionResult SiteDetail(int siteId, string kind = "")
    {
        try
        {
            var rows = _view.LoadForSite(siteId);
            if (rows.Count == 0)
            {
                return Json(new { found = false });
            }

            var limits = _thresholds.LoadEnabled();

            // 住所と現場所長は、現場そのものの情報から読む。
            // 予測の表には入っていないため。
            return Json(BuildSiteForecast(rows, _sites.LoadInfo(siteId), kind,
                                          ValueOf(limits, "WIND", 10m),
                                          DateTime.Now));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "現場の予測を読めませんでした SiteId={SiteId}",
                             siteId);
            return Json(new { found = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 取得の確認画面。Python版と数字を突き合わせるためのもの。
    ///
    /// ふだんの運用では使わないので、上のメニューには出していない。
    /// /Forecast/Check で開ける。
    /// </summary>
    public IActionResult Check()
    {
        var model = new ForecastViewModel();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            model.Result = _fetch.Fetch();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "気象予測の取得に失敗しました");
            model.Result.Errors["予期しない不具合"] = ex.Message;
        }

        watch.Stop();
        model.Seconds = watch.Elapsed.TotalSeconds;

        return View(model);
    }

    /// <summary>
    /// 1つの現場の予測を、画面が読める形に組み立てる。
    ///
    /// データベースから読むところと、画面の形にするところを分けてある。
    /// 分けておくと、デモ画面が仮のデータを渡して
    /// 同じ形を作れる。画面(JavaScript)を本物と共通にできる。
    /// </summary>
    /// <param name="rows">その現場に紐付く予測。</param>
    /// <param name="info">現場そのものの情報 (住所・現場所長)。</param>
    /// <param name="kind">いま見ている種類 (風 / 雨 / 雪)。</param>
    /// <param name="windLimit">強風の点検基準 (m/s)。</param>
    /// <param name="now">いまの時刻。過ぎた時間帯を外すのに使う。</param>
    public static object BuildSiteForecast(List<SiteForecast> rows,
                                           SiteInfo? info,
                                           string kind,
                                           decimal windLimit,
                                           DateTime now)
    {
        // 見ている種類だけを出す。指定が無ければ全部。
        var wantedKinds = ForecastBoard.KindItem.ContainsKey(kind)
            ? new[] { kind }
            : ForecastBoard.KindItem.Keys.ToArray();

        var kinds = new List<object>();

        foreach (var one in wantedKinds)
        {
            string item = ForecastBoard.KindItem[one];

            // 同じ時間帯が重なっていたら1行にまとめる。
            var times = rows
                .Where(r => r.ItemType == item
                            && ForecastBoard.StillAhead(r.TargetAt, now))
                .GroupBy(r => r.TargetAt)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    at = g.Key.ToString("MM/dd HH:mm"),
                    level = (g.First().ValueText ?? string.Empty).Trim(),

                    // 「高」「中」が何を意味するかを添える。
                    // 文字だけでは、何をすべきかが伝わらない。
                    note = ForecastBoard.PossibilityNote(
                        (g.First().ValueText ?? string.Empty).Trim()),

                    // いま効いている区切りかどうか。
                    // 6時間ごとの区切りなので、12:00 の区切りは
                    // 18:00 まで効いている。
                    now = ForecastBoard.IsNow(g.Key, now),
                })
                .ToList();

            if (times.Count == 0)
            {
                continue;
            }

            kinds.Add(new
            {
                kind = one,
                times,
            });
        }

        // 風を見ているときは、地図を塗った根拠をそのまま返す。
        //
        // ★これが無いと、地図に色が付いているのにモーダルは
        //   「発表はありません」となり、危ないのかどうかが
        //   分からなくなる。地図は風の強さで塗っているのに、
        //   モーダルは警報級の可能性しか見ていなかったため。
        object? wind = null;

        if (kind == "風")
        {
            // その区域の風の予報を、今日・明日・明後日ぶんとも出す。
            // 1つだけにすると、明日以降に強い風があっても分からない。
            var all = rows
                .Where(r => r.ItemType == ForecastSettings.WindTextItem
                            && !string.IsNullOrEmpty(r.ValueText)
                            // 終わった日は出さない
                            && r.TargetAt.AddHours(24) > now)
                .GroupBy(r => r.TargetAt)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var strength = WindStrength.Of(g.First().ValueText);
                    return new
                    {
                        at = g.Key.ToString("MM/dd HH:mm"),
                        text = g.First().ValueText,
                        name = strength.Name,
                        caution = strength.Caution,
                        color = strength.Color,
                        rank = strength.Rank,
                        note = WindStrength.AgainstLimit(strength, windLimit),
                    };
                })
                .ToList();

            if (all.Count > 0)
            {
                // 地図の色は、この中で最も強いもので決めている。
                var top = all.OrderByDescending(x => x.rank)
                             .ThenBy(x => x.at)
                             .First();

                wind = new
                {
                    days = all,
                    topName = top.name,
                    topCaution = top.caution,
                    topColor = top.color,
                    topRank = top.rank,
                    topAt = top.at,
                    topNote = top.note,
                };
            }
        }

        // 種類ごとに、最も高くなるところを1つずつ出す。
        // 時間ごとの動きは上の kinds に入っているが、
        // まず「いつ、どれくらい危ないか」を一目で見せる。
        var summary = new List<object>();

        foreach (var one in wantedKinds)
        {
            string item = ForecastBoard.KindItem[one];

            var best = rows
                .Where(r => r.ItemType == item
                            && ForecastBoard.RankOf(
                                   (r.ValueText ?? string.Empty).Trim()) > 0)
                .OrderByDescending(r => ForecastBoard.RankOf(
                    (r.ValueText ?? string.Empty).Trim()))
                .ThenBy(r => r.TargetAt)
                .FirstOrDefault();

            if (best is null)
            {
                continue;
            }

            string level = (best.ValueText ?? string.Empty).Trim();

            summary.Add(new
            {
                kind = one,
                level,
                at = best.TargetAt.ToString("MM/dd HH:mm"),
                isNow = ForecastBoard.IsNow(best.TargetAt, now),

                // 「高」「中」が何を意味するかを添える。
                note = ForecastBoard.PossibilityNote(level),
            });
        }

        return new
        {
            found = true,
            now = now.ToString("MM/dd HH:mm"),
            kind,
            wind,
            name = rows[0].SiteName,
            area = rows[0].AreaLabel,
            dept = info?.Department ?? rows[0].Department,
            addr = info?.Address ?? string.Empty,
            manager = string.IsNullOrWhiteSpace(info?.ManagerName)
                ? "未登録" : info!.ManagerName,
            summary,
            kinds,
        };
    }

    /// <summary>基準の数字を取り出す。無ければ既定値を使う。</summary>
    public static decimal ValueOf(Dictionary<string, Threshold> limits,
                                   string eventType, decimal fallback)
        => limits.TryGetValue(eventType, out var t) && t.Value is not null
           ? t.Value.Value : fallback;

    /// <summary>
    /// 地図が必要とするものだけを JSON にまとめる。
    /// </summary>
    public static string BuildMapData(Dictionary<string, AreaLevel> levels,
                                      string kind,
                                      List<ForecastSiteMarker> markers)
    {
        var data = new
        {
            tileUrl = DisplaySettings.TileUrl,
            attribution = DisplaySettings.TileAttribution,
            kind,
            color = ForecastBoard.PossibilityColor,
            noLevelColor = ForecastBoard.NoLevelColor,

            // 現場の丸は部署の色で塗る。ダッシュボードと同じ色にして、
            // 2つの画面で同じ現場が同じ色に見えるようにする。
            departmentColor = DisplaySettings.DepartmentColor,
            otherColor = DisplaySettings.OtherColor,

            // 風の強さの色見本。凡例と地図で同じ色を使う。
            windLevels = WindStrength.Levels.Select(l => new
            {
                name = l.Name,
                rank = l.Rank,
                caution = l.Caution,
                color = l.Color,
            }),
            windNoneColor = WindStrength.None.Color,

            areas = levels.Values.Select(a => new
            {
                code = a.AreaCode,
                name = a.AreaLabel,
                level = a.Level,
                wind = a.Strongest?.Text,
                windAt = a.Strongest?.TargetAt.ToString("MM/dd"),

                // 風の強さ。「見る種類」が風のときは、こちらで塗る。
                // 風の高/中はめったに発表されないため、
                // それだけでは地図がほぼ灰色のままになる。
                windRank = a.Wind.Rank,
                windName = a.Wind.Name,
                windColor = a.Wind.Color,
                windCaution = a.Wind.Caution,
            }),

            sites = markers.Select(m => new
            {
                id = m.SiteId,
                name = m.SiteName,
                dept = m.Department,
                lat = (double)m.Latitude,
                lon = (double)m.Longitude,

                // その現場が属する区域の警戒度。
                // 「高」「中」の区域にある現場を大きく塗る。
                level = levels.TryGetValue(m.AreaCode, out var area)
                    ? area.Level : string.Empty,

                // 風のときは、高/中ではなく風の強さで大きさを決める。
                // 風の高/中はめったに発表されないため。
                windRank = levels.TryGetValue(m.AreaCode, out var wa)
                    ? wa.Wind.Rank : 0,
            }),
        };

        return JsonSerializer.Serialize(data);
    }
}
