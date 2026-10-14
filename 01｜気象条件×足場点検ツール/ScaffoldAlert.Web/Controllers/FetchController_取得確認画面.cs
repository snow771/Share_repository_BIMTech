using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Weather;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// 気象データ取得の確認画面の窓口。
///
/// ★この画面はデータベースに書き込まない。
///   気象庁から取ってきた内容を表示するだけ。
///   Python版と同じ結果になるかを確かめるための画面。
/// </summary>
public class FetchController : Controller
{
    private readonly EarthquakeFetcher _fetcher;
    private readonly WeatherService _weather;
    private readonly EarthquakeRepository _quakes;
    private readonly StationRepository _stations;
    private readonly ILogger<FetchController> _logger;

    public FetchController(EarthquakeFetcher fetcher,
                           WeatherService weather,
                           EarthquakeRepository quakes,
                           StationRepository stations,
                           ILogger<FetchController> logger)
    {
        _fetcher = fetcher;
        _weather = weather;
        _quakes = quakes;
        _stations = stations;
        _logger = logger;
    }

    public IActionResult Index()
    {
        var model = new FetchViewModel();
        var watch = Stopwatch.StartNew();

        try
        {
            // --- 監視対象の観測所 ---
            var targets = _stations.LoadTargetStationIds();
            model.StationNames = _stations.LoadStationNames();

            // --- 降水量・風速・雪 ---
            // 中で取得先ごとに例外を受け止めているので、
            // 1つ失敗しても残りの結果は返ってくる。
            model.Weather = _weather.Fetch(targets);

            // --- 地震 ---
            // 既に取り込んである地震のIDを渡す。
            // Python版と同じ判断で、対象を絞るため。
            var known = _quakes.LoadKnownEventIds();
            model.Quake = _fetcher.Fetch(known);
            model.Stored = _quakes.LoadRecent(5);

            model.Succeeded = string.IsNullOrEmpty(model.Quake.ErrorMessage);
            model.ErrorMessage = model.Quake.ErrorMessage ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "気象データの取得に失敗しました");
            model.Succeeded = false;
            model.ErrorMessage = ex.Message;
        }

        watch.Stop();
        model.Seconds = watch.Elapsed.TotalSeconds;

        return View(model);
    }
}
