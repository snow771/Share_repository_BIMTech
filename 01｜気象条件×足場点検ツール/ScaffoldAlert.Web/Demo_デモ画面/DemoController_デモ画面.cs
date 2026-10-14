using System.Text;
using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Dashboard;
using ScaffoldAlert.Core.Forecast;
using ScaffoldAlert.Core.Weather;
using ScaffoldAlert.Web.Controllers;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Demo;

// ============================================================
//  デモ画面の窓口
// ============================================================
//
//  ★このファイルは本番では使わない
//    Demo_デモ画面 フォルダごと消せば、デモ画面はなくなる。
//    本物の画面は何も変わらない。
//    (消したあとは appsettings.json の Demo:Enabled を false にする)
//
//  ★何をしているか
//    本物の画面(.cshtml)と動き(.js)を、そのまま使い回す。
//    渡すデータだけを DemoData の仮データに差し替えている。
//
//      本物 : データベース → 画面
//      デモ : DemoData     → 画面   ← 同じ画面
//
//    こうしておくと、本物の画面を直したときにデモも一緒に直る。
//    デモ用の画面を別に作ると、だんだん本物と違う見た目になっていく。
//
//  ★絶対の決まり
//    デモの窓口は、データベースに一切つながらない。
//    「記録する」を押しても、知らせを出すだけで何も保存しない。
//
//  ★URL の形
//    本物 : /Home/SiteDetail
//    デモ : /Demo/Home/SiteDetail
//    頭に /Demo が付くだけ。画面と JavaScript は
//    window.APP_PREFIX を頭に足すだけで、両方に対応できる。
// ============================================================

/// <summary>
/// デモ画面を止められるようにする印。
///
/// ★なぜ要るか
///   ファイルを消し忘れたまま共有PCへ移しても、
///   appsettings の Demo:Enabled が false なら開けないようにするため。
///   メニューから消えていても、URL を直接打てば開けてしまう。
///   デモの画面と本物の画面を取り違えるのが一番危ない。
/// </summary>
public class デモが有効なときだけAttribute
    : Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute
{
    public override void OnActionExecuting(
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        var 設定 = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>();

        if (!設定.GetValue<bool>("Demo:Enabled"))
        {
            // 「そんな画面はありません」として扱う。
            context.Result = new NotFoundResult();
        }
    }
}

/// <summary>
/// デモのダッシュボード。
/// </summary>
[デモが有効なときだけ]
public class DemoController : Controller
{
    /// <summary>
    /// デモ画面であることを、画面側へ伝える。
    ///
    /// これを入れておくと
    ///   ・上に「デモ画面」の帯が出る
    ///   ・リンクや送り先の頭に /Demo が付く
    /// の2つが働く。
    /// </summary>
    private void デモとして開く()
    {
        ViewData["DemoPrefix"] = "/Demo";
    }

    /// <summary>デモのダッシュボード。本物のダッシュボードの画面を使う。</summary>
    [Route("Demo")]
    public IActionResult Index()
    {
        デモとして開く();

        var model = new DashboardViewModel
        {
            Connected = true,
            Sites = DemoData.SiteMarkers(),
            Alerts = DemoData.Alerts(),
            ActiveSiteCount = DemoData.Sites.Count,

            // 判定基準。本物と同じ数字を出す。
            // デモだけ違う数字が出ていると、
            // 見た人が別の基準だと思い込んでしまう。
            Limits = DemoData.Limits().Values
                .OrderBy(t => DashboardViewModel.LimitOrder(t.EventType))
                .ToList(),
        };

        // まとめて処理したあとの知らせ。本物と同じ流れで出す。
        if (TempData["BulkSuccess"] is string success)
        {
            model.BulkSuccess = success;
        }

        // 地図に渡す形は、本物と同じところで作る。
        model.MapDataJson = HomeController.BuildMapData(model);

        return View("~/Views/Home/Index.cshtml", model);
    }

    /// <summary>デモの現場の詳細。地図の丸を押すと呼ばれる。</summary>
    [Route("Demo/Home/SiteDetail")]
    public IActionResult SiteDetail(int siteId)
    {
        var info = DemoData.Info(siteId);
        if (info is null)
        {
            return Json(new { found = false });
        }

        return Json(HomeController.BuildSiteDetail(
            info,
            DemoData.Stations(siteId),
            DemoData.Weather(siteId),
            DemoData.Quakes(siteId)));
    }

    /// <summary>デモの観測所の推移。観測所の行を押すと呼ばれる。</summary>
    [Route("Demo/Home/StationTrend")]
    public IActionResult StationTrend(string stationId)
    {
        return Json(HomeController.BuildTrend(DemoData.Trend(stationId)));
    }
}

/// <summary>
/// デモの気象予測。
/// </summary>
[デモが有効なときだけ]
public class DemoForecastController : Controller
{
    private readonly AreaGeoJsonCache _areaShapes;
    private readonly IHttpClientFactoryLike _http;

    /// <summary>
    /// 区域の形(地図の塗り)だけは、本物と同じものを使う。
    ///
    /// 形そのものは気象庁が配っている地図の輪郭で、
    /// 現場や観測の中身は入っていない。
    /// デモ用に作り直しても意味がないので、本物を借りる。
    /// </summary>
    public DemoForecastController(AreaGeoJsonCache areaShapes,
                                  IHttpClientFactoryLike http)
    {
        _areaShapes = areaShapes;
        _http = http;
    }

    /// <summary>デモの気象予測。本物の気象予測の画面を使う。</summary>
    [Route("Demo/Forecast")]
    public IActionResult Index(string kind = "風")
    {
        ViewData["DemoPrefix"] = "/Demo";

        var limits = DemoData.Limits();
        var forecasts = DemoData.Forecasts();
        var markers = DemoData.ForecastMarkers();

        var model = new ForecastBoardViewModel
        {
            Loaded = true,
            Kind = ForecastBoard.KindItem.ContainsKey(kind) ? kind : "風",
            WindLimit = ForecastController.ValueOf(limits, "WIND", 10m),
            RainLimit = ForecastController.ValueOf(limits, "RAIN", 50m),
            SnowLimit = ForecastController.ValueOf(limits, "SNOW", 25m),
            IsEmpty = forecasts.Count == 0,
            SiteMarkerCount = markers.Count,
            Coverage = DemoData.Coverage(),
        };

        model.ReportedAt = forecasts
            .Where(f => f.ReportedAt is not null)
            .Max(f => f.ReportedAt);

        model.OverLimit = ForecastBoard.OverLimit(
            forecasts, model.WindLimit, model.RainLimit, model.SnowLimit);

        model.Possibilities = ForecastBoard.Summarize(forecasts, DateTime.Now);
        model.Winds = ForecastBoard.SummarizeWind(forecasts, DateTime.Now);

        model.SiteDepartments = markers
            .Select(m => m.Department)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var levels = ForecastBoard.AreaLevels(
            forecasts, model.Kind, DateTime.Now);

        // 地図に渡す形は、本物と同じところで作る。
        model.MapDataJson = ForecastController.BuildMapData(
            levels, model.Kind, markers);

        return View("~/Views/Forecast/Index.cshtml", model);
    }

    /// <summary>区域の形。本物と同じものを返す。</summary>
    [Route("Demo/Forecast/Areas")]
    public IActionResult Areas()
    {
        try
        {
            return Content(_areaShapes.Get(_http), "application/json");
        }
        catch
        {
            // 区域の形が取れなくても、現場の丸は出したい。
            // インターネットに出られないPCでもデモができるようにする。
            return Content("{\"features\":[]}", "application/json");
        }
    }

    /// <summary>デモの現場の予測。地図の丸を押すと呼ばれる。</summary>
    [Route("Demo/Forecast/SiteDetail")]
    public IActionResult SiteDetail(int siteId, string kind = "")
    {
        var rows = DemoData.ForSite(siteId);
        if (rows.Count == 0)
        {
            return Json(new { found = false });
        }

        return Json(ForecastController.BuildSiteForecast(
            rows,
            DemoData.Info(siteId),
            kind,
            ForecastController.ValueOf(DemoData.Limits(), "WIND", 10m),
            DateTime.Now));
    }
}

/// <summary>
/// デモの点検記録簿。
/// </summary>
[デモが有効なときだけ]
public class DemoAuditController : Controller
{
    /// <summary>デモの点検記録簿。本物の点検記録簿の画面を使う。</summary>
    [Route("Demo/Audit")]
    public IActionResult Index(DateTime? from, DateTime? to)
    {
        ViewData["DemoPrefix"] = "/Demo";

        var model = new AuditViewModel
        {
            Loaded = true,

            // 期間は本物と同じ既定（180日前から今日まで）。
            From = from ?? DateTime.Today.AddDays(-180),
            To = to ?? DateTime.Today,
            All = DemoData.AuditRows(),
        };

        // 期間で絞る。仮データでも、期間の絞り込みを試せるようにする。
        model.All = model.All
            .Where(r => r.OccurredAt >= model.From
                        && r.OccurredAt < model.To.AddDays(1))
            .ToList();

        if (TempData["Success"] is string success)
        {
            model.SuccessMessage = success;
        }

        return View("~/Views/Audit/Index.cshtml", model);
    }

    /// <summary>
    /// デモのCSV。
    ///
    /// 中身は仮データだが、書き出し方は本物と同じ。
    /// Excel で開けるか、文字化けしないかを確かめられる。
    /// </summary>
    [Route("Demo/Audit/Csv")]
    public IActionResult Csv(DateTime? from, DateTime? to)
    {
        var start = from ?? DateTime.Today.AddDays(-180);
        var end = to ?? DateTime.Today;

        var rows = DemoData.AuditRows()
            .Where(r => r.OccurredAt >= start && r.OccurredAt < end.AddDays(1))
            .ToList();

        var text = new StringBuilder();
        text.AppendLine(string.Join(",",
            "現場名", "住所", "事象種別", "事象発生日時", "実測値", "実測値補足",
            "根拠条文", "点検実施日時", "点検実施者", "点検者資格",
            "点検結果", "指摘事項", "事象から点検までの日数", "対応状況"));

        foreach (var r in rows)
        {
            text.AppendLine(string.Join(",",
                Cell(r.SiteName),
                Cell(r.Address),
                Cell(DisplaySettings.Event(r.EventType)),
                Cell(r.OccurredAt.ToString("yyyy-MM-dd HH:mm")),
                Cell(r.TriggerValue?.ToString("0.#")),
                Cell(r.TriggerText),
                Cell(r.LegalBasis),
                Cell(r.InspectedAt?.ToString("yyyy-MM-dd HH:mm")),
                Cell(r.InspectorName),
                Cell(r.InspectorQualify),
                Cell(InspectionChoices.ResultLabel(r.InspectionResult)),
                Cell(r.FindingNote),
                Cell(r.DaysToInspection?.ToString()),
                Cell(DisplaySettings.Status(r.AlertStatus))));
        }

        // .NET は既定で Shift_JIS を扱えないため、使えるように登録する。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(932).GetBytes(text.ToString());

        return File(bytes, "text/csv", "点検記録簿_デモ.csv");
    }

    /// <summary>CSVの1つの升目にする。本物と同じ作り。</summary>
    private static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

/// <summary>
/// デモのアラート対応。
///
/// ★ここが一番大事なところ
///   本物のアラート対応は、押すとデータベースに記録が残る。
///   デモでは、同じ画面・同じ入力欄を出しながら、
///   押しても何も保存しない。
///
///   保存しないことを黙っていると「登録できた」と勘違いされるので、
///   画面に必ず「デモなので保存していません」と出す。
/// </summary>
[デモが有効なときだけ]
public class DemoAlertController : Controller
{
    /// <summary>デモのアラート対応。本物のアラート対応の画面を使う。</summary>
    [Route("Demo/Alert")]
    public IActionResult Index(int id)
    {
        ViewData["DemoPrefix"] = "/Demo";

        var model = new AlertResponseViewModel
        {
            Alert = DemoData.Alert(id),
        };

        if (TempData["Success"] is string success)
        {
            model.SuccessMessage = success;
        }

        if (TempData["OpenTab"] is string tab)
        {
            model.OpenTab = tab;
        }

        return View("~/Views/Alert/Index.cshtml", model);
    }

    /// <summary>デモの「記録を直す」画面。点検記録簿から開く。</summary>
    [Route("Demo/Alert/Edit")]
    public IActionResult Edit(int id)
    {
        ViewData["DemoPrefix"] = "/Demo";

        return View("~/Views/Alert/Edit.cshtml", new AlertEditViewModel
        {
            Alert = DemoData.EditAlert(id),
            Inspection = DemoData.EditInspection(id),
        });
    }

    /// <summary>直した内容（デモなので保存しない）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Demo/Alert/Update")]
    public IActionResult Update(int alertId, string block)
    {
        string 何を = block == "inspection" ? "点検記録" : "指示の内容";

        TempData["Success"] = $"【デモ】{何を}を直す操作です。"
            + " 実際の運用ではここで記録が直りますが、"
            + "デモ画面なので何も保存していません。";

        return Redirect("/Demo/Audit");
    }

    /// <summary>① 指示の記録（デモなので保存しない）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Demo/Alert/Instruct")]
    public IActionResult Instruct(int alertId, string who, string? memo)
        => 保存したことにする(alertId, "instruct",
                              $"「{空なら未入力(who)}」からの指示として記録する"
                              + "操作です。");

    /// <summary>② 点検結果の登録（デモなので保存しない）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Demo/Alert/Inspect")]
    public IActionResult Inspect(int alertId, string name)
        => 保存したことにする(alertId, "inspect",
                              $"点検者「{空なら未入力(name)}」の点検結果を"
                              + "登録する操作です。");

    /// <summary>③ 点検不要の記録（デモなので保存しない）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Demo/Alert/NotRequired")]
    public IActionResult NotRequired(int alertId, string who, string why)
        => 保存したことにする(alertId, "skip",
                              $"「{空なら未入力(who)}」の判断で対象外に"
                              + "する操作です。");

    /// <summary>まとめて指示（デモなので保存しない）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Demo/Alert/BulkInstruct")]
    public IActionResult BulkInstruct(int[] alertIds, string who, string? memo)
        => まとめて保存したことにする(alertIds, "指示済にする");

    /// <summary>まとめて対象外（デモなので保存しない）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Demo/Alert/BulkNotRequired")]
    public IActionResult BulkNotRequired(int[] alertIds, string who, string why)
        => まとめて保存したことにする(alertIds, "対象外にする");

    /// <summary>
    /// 「保存したことにして」画面へ戻る。
    ///
    /// 実際には何も書き込まない。
    /// 本物と同じく、一度別の場所へ移ってから知らせを出す形にしている。
    /// 画面を更新したときに、もう一度送信されないようにするため。
    /// </summary>
    private IActionResult 保存したことにする(int alertId, string tab,
                                             string 説明)
    {
        TempData["Success"] = "【デモ】" + 説明
            + " 実際の運用ではここで記録が残りますが、"
            + "デモ画面なので何も保存していません。";
        TempData["OpenTab"] = tab;

        return Redirect($"/Demo/Alert?id={alertId}");
    }

    /// <summary>まとめて処理したことにして、デモのダッシュボードへ戻る。</summary>
    private IActionResult まとめて保存したことにする(int[] alertIds,
                                                     string 何を)
    {
        int 件数 = alertIds?.Length ?? 0;

        TempData["BulkSuccess"] = $"【デモ】{件数}件を{何を}操作です。"
            + " 実際の運用ではここで記録が残りますが、"
            + "デモ画面なので何も保存していません。";

        return Redirect("/Demo");
    }

    /// <summary>空欄のときに、画面へ出す言葉。</summary>
    private static string 空なら未入力(string? value)
        => string.IsNullOrWhiteSpace(value) ? "未入力" : value;
}
