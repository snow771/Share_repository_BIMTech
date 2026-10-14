using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Judgement;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// 判定確認の画面の窓口。
///
/// ★この画面はデータベースに書き込まない。
///   「いま判定したら、どの現場に点検義務が出るか」を見せるだけ。
///   Python版と結果が一致することを確かめるための画面。
/// </summary>
public class JudgementController : Controller
{
    private readonly JudgeService _judge;
    private readonly AlertRepository _alerts;
    private readonly ILogger<JudgementController> _logger;

    public JudgementController(JudgeService judge,
                               AlertRepository alerts,
                               ILogger<JudgementController> logger)
    {
        _judge = judge;
        _alerts = alerts;
        _logger = logger;
    }

    public IActionResult Index()
    {
        var model = new JudgementViewModel();
        var watch = Stopwatch.StartNew();

        try
        {
            model.Thresholds = _judge.LoadThresholds();
            model.Result = _judge.Judge();
            // Python版が最後に表示している一覧と同じもの。
            // 判定の結果と混同しやすいので、画面では分けて出す。
            model.Pending = _alerts.LoadPending();
            model.Connected = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "判定に失敗しました");
            model.Connected = false;
            model.ErrorMessage = ex.Message;
        }

        watch.Stop();
        model.Seconds = watch.Elapsed.TotalSeconds;

        return View(model);
    }
}
