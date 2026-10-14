using Microsoft.AspNetCore.Mvc;
using ScaffoldAlert.Core.Dashboard;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Web.Models;

namespace ScaffoldAlert.Web.Controllers;

/// <summary>
/// アラート対応の画面の窓口。
///
/// ★この画面はデータベースに書き込む。
///   ここまでの画面は読むだけだったが、
///   対応の記録はここから残す。
///
/// できること
///   ① 現場へ指示したことを記録する
///   ② 点検の結果を登録する (アラートは点検完了になる)
///   ③ 足場撤去済みなどで点検不要と判断したことを記録する
/// </summary>
public class AlertController : Controller
{
    private readonly InspectionRepository _repository;
    private readonly ILogger<AlertController> _logger;

    public AlertController(InspectionRepository repository,
                           ILogger<AlertController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>アラート対応の画面を出す。</summary>
    public IActionResult Index(int id)
    {
        var model = new AlertResponseViewModel();

        try
        {
            model.Alert = _repository.LoadAlert(id);

            // 直前の操作の結果を、画面をまたいで持ってくる。
            // 登録したあとに同じ画面を出し直すと、もう一度
            // 送信されてしまうことがあるため、一度別の場所へ
            // 移ってから知らせを出す形にしている。
            if (TempData["Success"] is string success)
            {
                model.SuccessMessage = success;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アラートを読めませんでした AlertId={AlertId}",
                             id);
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }

    /// <summary>① 現場へ指示したことを記録する。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Instruct(int alertId, string who, string? memo)
    {
        if (string.IsNullOrWhiteSpace(who))
        {
            return Back(alertId, "instruct", "指示者名を入力してください。",
                        new() { ["memo"] = memo ?? string.Empty });
        }

        try
        {
            bool saved = _repository.SaveInstruction(
                alertId, who.Trim(), memo);

            if (!saved)
            {
                return Back(alertId, "instruct",
                    "このアラートは既に対応済みでした。"
                    + "別の担当者が先に処理した可能性があります。", new());
            }

            TempData["Success"] = "指示済に更新しました。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "指示を記録できませんでした AlertId={AlertId}",
                             alertId);
            return Back(alertId, "instruct",
                        "記録できませんでした: " + ex.Message, new());
        }

        return RedirectToAction(nameof(Index), new { id = alertId });
    }

    /// <summary>② 点検の結果を登録する。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Inspect(int alertId, DateTime inspectedDate,
                                 string inspectedTime, string name,
                                 string? qualify, string scaffoldType,
                                 string result, string? finding,
                                 string registeredBy)
    {
        // 入力し直しになったときに、書いた内容を残す。
        var typed = new Dictionary<string, string>
        {
            ["name"] = name ?? string.Empty,
            ["qualify"] = qualify ?? string.Empty,
            ["scaffoldType"] = scaffoldType ?? string.Empty,
            ["result"] = result ?? string.Empty,
            ["finding"] = finding ?? string.Empty,
            ["registeredBy"] = registeredBy ?? string.Empty,
            ["inspectedDate"] = inspectedDate.ToString("yyyy-MM-dd"),
            ["inspectedTime"] = inspectedTime ?? string.Empty,
        };

        // 点検実施者の氏名は法令上の必須記録項目。
        // 登録者名も、誰が入れた記録かを残すために必須にしている。
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(registeredBy))
        {
            return Back(alertId, "inspect",
                        "点検実施者氏名と登録者名は必須です。", typed);
        }

        // 選択肢は画面から送られてくるので、決められたものかを確かめる。
        // ここを通さないと、テーブルの決まりに合わない値で
        // 登録に失敗し、原因の分かりにくい不具合になる。
        if (!InspectionChoices.IsKnownResult(result))
        {
            return Back(alertId, "inspect",
                        "点検結果の選び方が正しくありません。", typed);
        }

        if (!InspectionChoices.IsKnownScaffold(scaffoldType))
        {
            return Back(alertId, "inspect",
                        "足場種類の選び方が正しくありません。", typed);
        }

        if (!TimeOnly.TryParse(inspectedTime, out var time))
        {
            return Back(alertId, "inspect",
                        "点検実施時刻の書き方が正しくありません。", typed);
        }

        try
        {
            var alert = _repository.LoadAlert(alertId);
            if (alert is null)
            {
                return Back(alertId, "inspect",
                            "このアラートは見つかりませんでした。", typed);
            }

            var input = new InspectionInput
            {
                AlertId = alertId,
                InspectedAt = inspectedDate.Date.Add(time.ToTimeSpan()),
                InspectorName = name.Trim(),
                InspectorQualify = qualify,
                ScaffoldType = scaffoldType,
                InspectionResult = InspectionChoices.Results[result],
                FindingNote = finding,
                RegisteredBy = registeredBy.Trim(),
            };

            // 点検の理由は「大雨後」のように、何の後の点検かを残す。
            string reason = DisplaySettings.Event(alert.EventType) + "後";

            bool saved = _repository.SaveInspection(input, reason);

            if (!saved)
            {
                return Back(alertId, "inspect",
                    "このアラートは既に対応済みでした。"
                    + "点検記録が二重にならないよう、登録していません。",
                    typed);
            }

            TempData["Success"] = "点検記録を登録しました。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "点検記録を登録できませんでした AlertId={AlertId}", alertId);
            return Back(alertId, "inspect",
                        "登録できませんでした: " + ex.Message, typed);
        }

        return RedirectToAction(nameof(Index), new { id = alertId });
    }

    /// <summary>③ 点検が不要と判断したことを記録する。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult NotRequired(int alertId, string who, string why)
    {
        var typed = new Dictionary<string, string>
        {
            ["skipWho"] = who ?? string.Empty,
            ["skipWhy"] = why ?? string.Empty,
        };

        // 臨検で理由を問われることがあるため、理由は必ず残す。
        if (string.IsNullOrWhiteSpace(who) || string.IsNullOrWhiteSpace(why))
        {
            return Back(alertId, "skip", "判断者名と理由は必須です。", typed);
        }

        try
        {
            bool saved = _repository.SaveNotRequired(
                alertId, who.Trim(), why.Trim());

            if (!saved)
            {
                return Back(alertId, "skip",
                    "このアラートは既に対応済みでした。", typed);
            }

            TempData["Success"] = "対象外にしました。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対象外にできませんでした AlertId={AlertId}",
                             alertId);
            return Back(alertId, "skip",
                        "記録できませんでした: " + ex.Message, typed);
        }

        return RedirectToAction(nameof(Index), new { id = alertId });
    }

    // ============================================================
    //  記録を直す
    // ============================================================
    //
    //  ★直せるのは「指示済」と「対象外」だけ
    //    点検完了になったものは直せない。
    //    臨検に出す記録を後から書き換えられると、
    //    その記録自体が信用されなくなる。
    //
    //  ★入れ直しではなく「直し」
    //    指示した日時はそのまま残る。
    //    名前の書き間違いや、理由の書き足しのためのもの。
    // ============================================================

    /// <summary>記録を直す画面を出す。点検記録簿から開く。</summary>
    public IActionResult Edit(int id)
    {
        return View(Build(id, string.Empty));
    }

    /// <summary>
    /// 直した内容を記録する。
    ///
    /// 指示の欄と点検の欄は、同じ画面で別々に送る。
    /// どちらから送られてきたかは block で見分ける。
    /// 1つの送信で両方を書き換えると、
    /// 片方だけ失敗したときにどこまで直ったのか分からなくなる。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Update(int alertId, string block,
                                string? who, string? note,
                                DateTime? inspectedDate, string? inspectedTime,
                                string? name, string? qualify,
                                string? scaffoldType, string? result,
                                string? finding, string? registeredBy)
    {
        try
        {
            // いまの状態は、送られてきたものではなくデータベースから読む。
            // 画面から送らせると、送られてきたものを信じることになる。
            var alert = _repository.LoadAlert(alertId);

            if (alert is null)
            {
                return BackToEdit(alertId, "この記録は見つかりませんでした。");
            }

            if (block == "instruction")
            {
                return UpdateInstructionBlock(alertId, alert, who, note);
            }

            if (block == "inspection")
            {
                return UpdateInspectionBlock(alertId, inspectedDate,
                    inspectedTime, name, qualify, scaffoldType, result,
                    finding, registeredBy);
            }

            return BackToEdit(alertId, "直す先が分かりませんでした。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "記録を直せませんでした AlertId={AlertId}",
                             alertId);
            return BackToEdit(alertId, ex.Message);
        }
    }

    /// <summary>指示（または対象外の理由）の欄を直す。</summary>
    private IActionResult UpdateInstructionBlock(int alertId, AlertDetail alert,
                                                 string? who, string? note)
    {
        if (string.IsNullOrWhiteSpace(who))
        {
            return BackToEdit(alertId, "名前を入力してください。");
        }

        bool ok;

        if (alert.AlertStatus == "NOT_REQUIRED")
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return BackToEdit(alertId, "対象外の理由は必須です。");
            }

            ok = _repository.UpdateNotRequired(alertId, who.Trim(),
                                               note.Trim());
        }
        else if (alert.AlertStatus == "INSTRUCTED"
                 || alert.AlertStatus == "COMPLETED")
        {
            ok = _repository.UpdateInstruction(alertId, who.Trim(), note);
        }
        else
        {
            return BackToEdit(alertId,
                "まだ指示していないため、指示の内容は直せません。");
        }

        if (!ok)
        {
            return BackToEdit(alertId,
                "直せませんでした。ほかの人が先に対応した可能性があります。");
        }

        TempData["Success"] = "指示の内容を直しました。";
        return Redirect("/Audit");
    }

    /// <summary>点検記録の欄を直す。</summary>
    private IActionResult UpdateInspectionBlock(
        int alertId, DateTime? inspectedDate, string? inspectedTime,
        string? name, string? qualify, string? scaffoldType,
        string? result, string? finding, string? registeredBy)
    {
        var existing = _repository.LoadInspection(alertId);

        if (existing is null)
        {
            return BackToEdit(alertId,
                "この記録にはまだ点検記録がありません。"
                + "点検の登録は、アラート対応の画面から行ってください。");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return BackToEdit(alertId, "点検実施者氏名は必須です。");
        }

        if (string.IsNullOrWhiteSpace(registeredBy))
        {
            return BackToEdit(alertId, "登録者名は必須です。");
        }

        if (inspectedDate is null)
        {
            return BackToEdit(alertId, "点検実施日を入力してください。");
        }

        // 日付と時刻を1つの日時にする。
        // 時刻が空のときは、その日の 0:00 として扱う。
        var when = inspectedDate.Value.Date;

        if (!string.IsNullOrWhiteSpace(inspectedTime)
            && TimeSpan.TryParse(inspectedTime, out var time))
        {
            when = when.Add(time);
        }

        // 事象より前の点検は、記録として成り立たない。
        var alert = _repository.LoadAlert(alertId);
        if (alert is not null && when < alert.OccurredAt)
        {
            return BackToEdit(alertId,
                "点検実施日時が、事象の発生より前になっています。");
        }

        if (when > DateTime.Now.AddMinutes(5))
        {
            return BackToEdit(alertId, "点検実施日時が未来になっています。");
        }

        // 選択肢は決められたものだけを受け取る。
        // 画面を通さずに送られてきても、決まりの外の値が入らないようにする。
        if (!InspectionChoices.IsKnownResult(result))
        {
            return BackToEdit(alertId, "点検結果を選んでください。");
        }

        if (!string.IsNullOrWhiteSpace(scaffoldType)
            && !InspectionChoices.IsKnownScaffold(scaffoldType))
        {
            return BackToEdit(alertId, "足場種類を選んでください。");
        }

        bool ok = _repository.UpdateInspection(new InspectionInput
        {
            AlertId = alertId,
            InspectedAt = when,
            InspectorName = name,
            InspectorQualify = qualify,
            ScaffoldType = scaffoldType,
            InspectionResult = InspectionChoices.Results[result],
            FindingNote = finding,
            RegisteredBy = registeredBy,
        });

        if (!ok)
        {
            return BackToEdit(alertId, "直せませんでした。");
        }

        TempData["Success"] = "点検記録を直しました。";
        return Redirect("/Audit");
    }

    /// <summary>直せなかったときに、理由を出して同じ画面へ戻る。</summary>
    private IActionResult BackToEdit(int alertId, string reason)
    {
        return View(nameof(Edit), Build(alertId, reason));
    }

    /// <summary>直す画面に渡すものを集める。</summary>
    private AlertEditViewModel Build(int alertId, string error)
    {
        var model = new AlertEditViewModel { ErrorMessage = error };

        try
        {
            model.Alert = _repository.LoadAlert(alertId);
            model.Inspection = _repository.LoadInspection(alertId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "記録を読めませんでした AlertId={AlertId}",
                             alertId);

            if (string.IsNullOrEmpty(model.ErrorMessage))
            {
                model.ErrorMessage = ex.Message;
            }
        }

        return model;
    }

    /// <summary>
    /// 選んだアラートを、まとめて「指示済」にする。
    ///
    /// 同じ担当者が複数の現場を持っていることがあり、
    /// 1件ずつ開いて入れ直すのは手間が大きい。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BulkInstruct(int[] alertIds, string who, string? memo)
    {
        return Bulk(alertIds, who, () =>
            _repository.SaveInstructionMany(alertIds, who, memo),
            "指示済にしました");
    }

    /// <summary>
    /// 選んだアラートを、まとめて「対象外」にする。
    ///
    /// ★理由は選んだ全件に同じものが記録される。
    ///   臨検で理由を問われることがあるため、
    ///   まとめてよい理由かを確かめてから使うこと。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BulkNotRequired(int[] alertIds, string who, string why)
    {
        if (string.IsNullOrWhiteSpace(why))
        {
            TempData["BulkError"] = "対象外とする理由は必須です。";
            return Redirect("/");
        }

        return Bulk(alertIds, who, () =>
            _repository.SaveNotRequiredMany(alertIds, who, why),
            "対象外にしました");
    }

    /// <summary>
    /// まとめて処理するときの、共通の流れ。
    ///
    /// 終わったらダッシュボードへ戻す。
    /// 同じ画面に留まると、うまくいったのか分からず
    /// もう一度押してしまう。
    /// </summary>
    private IActionResult Bulk(int[] alertIds, string who,
                               Func<int> action, string doneWord)
    {
        if (alertIds is null || alertIds.Length == 0)
        {
            TempData["BulkError"] = "現場が選ばれていません。";
            return Redirect("/");
        }

        if (string.IsNullOrWhiteSpace(who))
        {
            TempData["BulkError"] = "名前は必須です。";
            return Redirect("/");
        }

        try
        {
            int changed = action();

            if (changed == alertIds.Length)
            {
                TempData["BulkSuccess"] = $"{changed} 件を{doneWord}。";
            }
            else
            {
                // 画面を開いたまま別の担当者が先に処理していると、
                // 選んだ数と変わった数が食い違う。黙って通すと
                // 全部できたつもりになるため、そのまま知らせる。
                TempData["BulkSuccess"] =
                    $"{changed} 件を{doneWord}。"
                    + $"（選んだ {alertIds.Length} 件のうち "
                    + $"{alertIds.Length - changed} 件は、"
                    + "既に別の担当者が対応済みでした）";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "まとめて処理できませんでした");
            TempData["BulkError"] = "処理できませんでした: " + ex.Message;
        }

        return Redirect("/");
    }

    /// <summary>
    /// 入力し直しのために、同じ画面へ戻す。
    ///
    /// 書いた内容と、どのタブを開いていたかを持ち帰らせる。
    /// 全部消えてしまうと、入れ直しの手間が大きいため。
    /// </summary>
    private IActionResult Back(int alertId, string tab, string message,
                               Dictionary<string, string> typed)
    {
        var model = new AlertResponseViewModel
        {
            Alert = _repository.LoadAlert(alertId),
            ErrorMessage = message,
            OpenTab = tab,
            Input = typed,
        };

        return View(nameof(Index), model);
    }
}
