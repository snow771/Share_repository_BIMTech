using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Batch;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// 取得と判定を画面から動かす窓口。
///
/// ★この画面はデータベースに書き込む。
///   気象庁からデータを取ってきて保存し、点検義務を判定する。
///
/// ふだんは決まった時刻に自動で動かす。
/// この画面は「いま取り直したい」ときと、動きを確かめるときに使う。
/// </summary>
public class BatchController : Controller
{
    private readonly BatchService _batch;
    private readonly ILogger<BatchController> _logger;

    public BatchController(BatchService batch,
                           ILogger<BatchController> logger)
    {
        _batch = batch;
        _logger = logger;
    }

    /// <summary>ボタンだけの画面。</summary>
    public IActionResult Index()
    {
        return View(new BatchResult { Steps = new() });
    }

    /// <summary>取得だけを行う。判定はしない。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Fetch() => RunAndShow(judgeToo: false);

    /// <summary>取得して判定まで行う。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult All() => RunAndShow(judgeToo: true);

    /// <summary>動かして、その結果を画面に出す。</summary>
    private IActionResult RunAndShow(bool judgeToo)
    {
        BatchResult result;

        try
        {
            result = _batch.Run(judgeToo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得と判定に失敗しました");

            result = new BatchResult();
            result.Steps.Add(new BatchStep
            {
                Name = "実行",
                Succeeded = false,
                ErrorMessage = ex.Message,
            });
        }

        return View(nameof(Index), result);
    }
}
