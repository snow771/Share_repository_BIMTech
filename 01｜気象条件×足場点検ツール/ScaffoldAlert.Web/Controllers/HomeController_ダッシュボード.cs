using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Dashboard;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Judgement;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// ダッシュボードの窓口。
///
/// 地図・対応が必要なアラート・現場の一覧を出す。
/// この画面はデータベースを読むだけで、書き込まない。
/// </summary>
public class HomeController : Controller
{
    private readonly DashboardRepository _dashboard;
    private readonly SiteDetailRepository _detail;
    private readonly ThresholdRepository _thresholds;
    private readonly ILogger<HomeController> _logger;

    public HomeController(DashboardRepository dashboard,
                          SiteDetailRepository detail,
                          ThresholdRepository thresholds,
                          ILogger<HomeController> logger)
    {
        _dashboard = dashboard;
        _detail = detail;
        _thresholds = thresholds;
        _logger = logger;
    }

    public IActionResult Index()
    {
        var model = new DashboardViewModel();

        // まとめて処理したあとの知らせを受け取る。
        // 登録した画面から一度こちらへ移ってから出しているので、
        // 画面を更新してももう一度処理されることがない。
        if (TempData["BulkSuccess"] is string success)
        {
            model.BulkSuccess = success;
        }

        if (TempData["BulkError"] is string failure)
        {
            model.BulkError = failure;
        }

        try
        {
            model.Sites = _dashboard.LoadSites();
            model.Alerts = _dashboard.LoadPendingAlerts();
            model.ActiveSiteCount = _dashboard.CountActiveSites();

            // 現場ごとの対応状況を、地図に出す現場へ写す。
            var states = _dashboard.LoadAlertStates();
            foreach (var site in model.Sites)
            {
                site.AlertState =
                    states.TryGetValue(site.SiteId, out var s) ? s : "なし";
            }

            // 判定基準。画面の上に出して、
            // 「なぜこの現場が出ているのか」をその場で分かるようにする。
            model.Limits = _thresholds.LoadEnabled().Values
                .OrderBy(t => DashboardViewModel.LimitOrder(t.EventType))
                .ToList();

            model.MapDataJson = BuildMapData(model);
            model.Connected = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ダッシュボードの読み込みに失敗しました");
            model.Connected = false;
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }

    /// <summary>
    /// 1つの現場の詳細を返す。
    ///
    /// 画面を開き直さずに中身だけを受け取れるよう、JSON で返す。
    /// 553現場すべてを最初に読むのは無駄なので、
    /// 押された現場のぶんだけ、そのときに読みに行く。
    /// </summary>
    public IActionResult SiteDetail(int siteId)
    {
        try
        {
            var info = _detail.LoadInfo(siteId);
            if (info is null)
            {
                return Json(new { found = false });
            }

            var stations = _detail.LoadStations(siteId);
            var weather = _detail.LoadWeather(siteId);
            var quakes = _detail.LoadQuakes(siteId);

            return Json(BuildSiteDetail(info, stations, weather, quakes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "現場の詳細を読めませんでした SiteId={SiteId}",
                             siteId);
            return Json(new { found = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 1つの観測所の、直近24時間の観測値を返す。
    ///
    /// 観測所の行が押されたときだけ読みに行く。
    /// 現場を開くたびに全観測所ぶんを読むと重くなるため。
    /// </summary>
    public IActionResult StationTrend(string stationId)
    {
        try
        {
            return Json(BuildTrend(_detail.LoadTrend(stationId)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "観測所の推移を読めませんでした "
                                 + "StationId={StationId}", stationId);
            return Json(new { rows = Array.Empty<object>(), error = ex.Message });
        }
    }

    /// <summary>
    /// 現場の詳細を、画面が読める形に組み立てる。
    ///
    /// データベースから読むところと、画面の形にするところを分けてある。
    /// 分けておくと、デモ画面が仮のデータを渡して
    /// 同じ形を作れる。画面(JavaScript)を本物と共通にできる。
    /// </summary>
    public static object BuildSiteDetail(SiteInfo info,
                                         List<LinkedStation> stations,
                                         SiteWeather weather,
                                         List<SiteQuake> quakes)
    {
        // 最寄りの観測所。下に出す数値はこの観測所の値ではないため、
        // 画面ではその断りを必ず添える。
        var nearest = stations.FirstOrDefault(s => s.IsNearest);

        return new
        {
            found = true,
            name = info.SiteName,
            dept = info.Department,
            status = SiteStatusLabel(info.SiteStatus),
            addr = info.Address ?? string.Empty,
            manager = string.IsNullOrWhiteSpace(info.ManagerName)
                ? "未登録" : info.ManagerName,
            nearest = nearest is null ? null : new
            {
                name = nearest.StationName,
                distance = nearest.DistanceKm,
            },
            weather = new
            {
                hasAny = weather.HasAny,
                precip12h = weather.Precip12h,
                precip1h = weather.Precip1h,
                wind = weather.MaxWindSpeed,
                gust = weather.MaxGustSpeed,
                observedAt = weather.ObservedAt?.ToString("yyyy-MM-dd HH:mm"),
            },
            quakes = quakes.Select(q => new
            {
                at = q.OccurredAt.ToString("yyyy-MM-dd HH:mm"),
                place = q.Hypocenter ?? string.Empty,
                intensity = q.Intensity ?? string.Empty,
            }),
            stations = stations.Select(s => new
            {
                id = s.StationId,
                name = s.StationName,
                pref = s.PrefName ?? string.Empty,
                distance = s.DistanceKm,
                nearest = s.IsNearest,
            }),
        };
    }

    /// <summary>観測所の推移を、画面が読める形に組み立てる。</summary>
    public static object BuildTrend(List<StationTrendRow> rows)
    {
        // 雪の降らない時期は空欄になるだけなので、
        // 値が1つも無ければ画面から列ごと消す。
        bool hasSnow = rows.Any(r => r.Snow24h is not null);

        return new
        {
            hasSnow,
            rows = rows.Select(r => new
            {
                at = r.ObservedAt.ToString("MM-dd HH:mm"),
                precip12h = r.Precip12h,
                precip1h = r.Precip1h,
                wind = r.MaxWindSpeed,
                gust = r.MaxGustSpeed,
                snow = r.Snow24h,
            }),
        };
    }

    /// <summary>現場の状態を、画面に出す言葉にする。</summary>
    public static string SiteStatusLabel(string status) => status switch
    {
        "ACTIVE" => "稼働中",
        "SUSPENDED" => "休止中",
        "CLOSED" => "竣工",
        _ => status,
    };

    /// <summary>
    /// 地図が必要とするものだけを JSON にまとめる。
    ///
    /// 画面に出さない項目まで送ると、553現場分では無駄が大きい。
    /// 名前を短くしているのは同じ理由。
    /// </summary>
    public static string BuildMapData(DashboardViewModel model)
    {
        var data = new
        {
            tileUrl = DisplaySettings.TileUrl,
            attribution = DisplaySettings.TileAttribution,
            alertColor = DisplaySettings.AlertColor,
            departmentColor = DisplaySettings.DepartmentColor,
            otherColor = DisplaySettings.OtherColor,
            sites = model.Sites.Select(s => new
            {
                id = s.SiteId,
                name = s.SiteName,
                dept = s.Department,
                addr = s.Address ?? string.Empty,
                lat = (double)s.Latitude,
                lon = (double)s.Longitude,
                alert = s.AlertState,
            }),
        };

        // 既定の書き出し方だと日本語が \uXXXX の形になるが、
        // JSON としては正しく、JavaScript 側で元に戻る。
        // HTML に混ぜても壊れない形なので、そのまま使う。
        return JsonSerializer.Serialize(data);
    }
}
