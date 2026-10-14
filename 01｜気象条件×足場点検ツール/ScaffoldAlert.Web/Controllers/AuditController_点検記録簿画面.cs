using System.Text;
using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Dashboard;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// 点検記録簿の画面の窓口。
///
/// 労働基準監督署の臨検時に、そのまま提出できる形で出す。
/// この画面はデータベースを読むだけで、書き込まない。
/// </summary>
public class AuditController : Controller
{
    private readonly InspectionRepository _repository;
    private readonly ILogger<AuditController> _logger;

    public AuditController(InspectionRepository repository,
                           ILogger<AuditController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>点検記録簿の画面。</summary>
    /// <param name="from">期間の開始。省略すると180日前。</param>
    /// <param name="to">期間の終了。省略すると今日。</param>
    public IActionResult Index(DateTime? from, DateTime? to)
    {
        var model = new AuditViewModel
        {
            From = from ?? DateTime.Today.AddDays(-180),
            To = to ?? DateTime.Today,
        };

        // 直したあとの知らせを受け取る。
        // 登録した画面から一度こちらへ移ってから出しているので、
        // 画面を更新してももう一度処理されることがない。
        if (TempData["Success"] is string success)
        {
            model.SuccessMessage = success;
        }

        try
        {
            model.All = _repository.LoadAuditTrail(model.From, model.To);
            model.Loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "点検記録簿を読めませんでした");
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }

    /// <summary>
    /// 点検記録簿を CSV で受け取る。
    ///
    /// Excel でそのまま開けるよう Shift_JIS(cp932) で書き出す。
    /// UTF-8 で出すと、Excel が文字化けして開くため。
    /// </summary>
    public IActionResult Csv(DateTime? from, DateTime? to)
    {
        var start = from ?? DateTime.Today.AddDays(-180);
        var end = to ?? DateTime.Today;

        var rows = _repository.LoadAuditTrail(start, end);

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

        string name = $"点検記録簿_{start:yyyyMMdd}-{end:yyyyMMdd}.csv";
        return File(bytes, "text/csv", name);
    }

    /// <summary>
    /// CSVの1つの升目にする。
    ///
    /// 中にカンマ・引用符・改行があると表が崩れるため、
    /// 引用符で囲み、中の引用符は2つ重ねる。
    /// </summary>
    private static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
