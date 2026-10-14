using System.Globalization;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>チャット入力の振り分け先。</summary>
public enum ChatIntent
{
    /// <summary>確認観点の提案</summary>
    ReviewPoints,
    /// <summary>選択物件の説明</summary>
    SelectedProject,
    /// <summary>判断サマリー</summary>
    Summary,
    /// <summary>AI で条件設定（LLM 使用）</summary>
    Filter,
}

/// <summary>
/// 判断サマリー・確認観点・物件説明の文章生成（Python の generate_summary_text /
/// generate_review_points_text / generate_selected_project_text）。LLM は使わないルールベース。
/// </summary>
public static class InsightTextService
{
    private static readonly string[] ReviewKeywords = { "確認観点", "注意点", "見るべき", "観点" };
    private static readonly string[] ProjectKeywords = { "選択物件", "この物件", "この案件", "クリックした物件", "個別物件" };
    private static readonly string[] SummaryKeywords = { "判断サマリー", "要約", "まとめ", "判断", "相場感", "単価は" };
    private static readonly string[] ConditionKeywords =
        { "東京都", "千葉", "神奈川", "埼玉", "rc", "s造", "鉄骨", "鉄筋", "内装", "外装", "仮設", "階", "㎡", "免震" };

    /// <summary>チャット入力の振り分け（Python の handle_chat_send と同じ判定）。</summary>
    public static ChatIntent ClassifyChat(string text, bool dataLoaded)
    {
        var t = text ?? "";
        if (ReviewKeywords.Any(t.Contains)) return ChatIntent.ReviewPoints;
        if (ProjectKeywords.Any(t.Contains)) return ChatIntent.SelectedProject;
        if (SummaryKeywords.Any(t.Contains))
        {
            if (!dataLoaded) return ChatIntent.Filter;
            var lower = t.ToLowerInvariant();
            return ConditionKeywords.Any(lower.Contains) ? ChatIntent.Filter : ChatIntent.Summary;
        }
        return ChatIntent.Filter;
    }

    public static string Summary(AnalysisResult r, FilterCriteria c)
    {
        var n = r.ProjectCount;
        var lines = new List<string>
        {
            "条件: " + string.Join(" / ", r.ConditionParts),
            $"根拠: 絞込後の比較母集団は {n} 件です。",
        };

        if (r.Revise.After.N > 0)
        {
            lines.Add($"読み取り: 修正単価の中央値は {Yen(r.Revise.Before.Median)}円/㎡ から {Yen(r.Revise.After.Median)}円/㎡ へ補正後に変化しています。");
            if (r.Revise.After.Q1 is not null && r.Revise.After.Q3 is not null)
                lines.Add($"分布感: 修正単価の中心帯は概ね {Yen(r.Revise.After.Q1)}〜{Yen(r.Revise.After.Q3)}円/㎡ です。");
        }
        else if (r.Assess.After.N > 0)
        {
            lines.Add($"読み取り: 査定単価の中央値は {Yen(r.Assess.Before.Median)}円/㎡ から {Yen(r.Assess.After.Median)}円/㎡ へ補正後に変化しています。");
        }
        else
        {
            lines.Add("読み取り: 単価統計を出せるデータ件数が不足しています。");
        }

        var cautions = new List<string>();
        if (n == 0) cautions.Add("該当案件が0件のため、この条件では判断できません。");
        else if (n <= 5) cautions.Add("件数が少ないため、参考値として扱うのが安全です。");
        else if (n <= 10) cautions.Add("件数はやや少なめのため、近接条件でも確認すると安心です。");

        var coarse = !FilterCriteria.IsSet(c.Trade) || !FilterCriteria.IsSet(c.Work);
        if (coarse) cautions.Add("工種・工事項目の絞込が粗いため、対象範囲が広めです。");
        if (r.ReviseDeflatorMean is double d && Math.Abs(d - 1.0) >= 0.05)
            cautions.Add(string.Create(CultureInfo.InvariantCulture, $"指数補正の影響は比較的大きく、平均deflatorは {d:F3} です。"));
        if (r.TempRatio.After.Median is double ratio)
            lines.Add($"補足: 仮設工事歩掛の中央値は {Pct(ratio)} です。");

        string next;
        if (n == 0) next = "地域・構造・階数・工種のうち、優先度の低い条件から1つずつ緩めて再確認してください。";
        else if (coarse) next = "工種または工事項目をもう一段絞ると、内訳としての単価判断がしやすくなります。";
        else next = "中央値だけでなく、上位25%・下位25%との差も見て採用単価の置き場所を決めるのが安全です。";

        lines.Add("判断上の含意: " + (cautions.Count > 0 ? string.Join(" / ", cautions) : "母集団としては概ね比較に使える条件です。"));
        lines.Add("次の一手: " + next);
        return string.Join("\n", lines);
    }

    public static string ReviewPoints(AnalysisResult r, FilterCriteria c)
    {
        var n = r.ProjectCount;
        var points = new List<string>();
        if (n <= 5) points.Add("1. 母集団件数が少ないため、近接条件でも再確認する。");
        else if (n <= 10) points.Add("1. 母集団件数はやや少なめなので、参考値として幅を持って見る。");
        else points.Add("1. 母集団件数は一定数あるため、中央値と四分位の両方で見る。");

        points.Add(!FilterCriteria.IsSet(c.Trade) || !FilterCriteria.IsSet(c.Work)
            ? "2. 工種・工事項目が未指定または粗いため、対象範囲が広すぎないか確認する。"
            : "2. 工種・工事項目・項目の指定が意図どおりかを確認する。");

        points.Add(r.ReviseDeflatorMean is double d
            ? string.Create(CultureInfo.InvariantCulture, $"3. 指数補正の平均deflatorは {d:F3} なので、補正影響の大きさを確認する。")
            : "3. 指数補正の影響が見えていないため、補正前後の差を確認する。");

        points.Add("4. 中央値だけでなく、上位25%・下位25%との差を見て安全側か攻め側かを決める。");
        return string.Join("\n", points);
    }

    public static string SelectedProject(ProjectRecord? p, AnalysisResult r)
    {
        if (p is null) return "先に散布図の点か物件一覧の行をクリックして、説明したい物件を選択してください。";

        var lines = new List<string> { $"対象物件ID: {p.ProjectId}" };
        if (!string.IsNullOrWhiteSpace(p.Name)) lines.Add($"物件名: {p.Name}");

        var attrs = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Prefecture)) attrs.Add($"都道府県:{p.Prefecture}");
        if (!string.IsNullOrWhiteSpace(p.Structure)) attrs.Add($"構造:{p.Structure}");
        if (!string.IsNullOrWhiteSpace(p.Form)) attrs.Add($"形態:{p.Form}");
        if (p.FloorsAbove is double fl) attrs.Add($"地上:{ProjectFilterService.FormatFloor(fl)}階");
        if (p.ConstructionFloorArea is double area) attrs.Add($"施工床面積:{area:N0}㎡");
        if (attrs.Count > 0) lines.Add("属性: " + string.Join(" / ", attrs));

        if (r.Revise.After.Median is double med)
        {
            lines.Add($"比較の基準: 現在条件の修正単価中央値は {Yen(med)}円/㎡ です。");
            var row = r.Projects.FirstOrDefault(x => x.ProjectId == p.ProjectId);
            if (row?.ReviseYenPerM2 is double own && med != 0)
                lines.Add($"この物件: {Yen(own)}円/㎡（中央値比 {(own / med - 1) * 100:+0.0;-0.0;0.0}%）");
        }
        lines.Add("見方: この物件単体の良し悪しではなく、現在の母集団の中で極端に外れていないかを確認してください。");
        lines.Add("追加確認: 工種の絞込、面積帯、地域条件を少し緩めた場合に位置づけが変わるかを見ると判断しやすいです。");
        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------

    public static string Yen(double? v) =>
        v is double d && double.IsFinite(d) ? Math.Round(d, MidpointRounding.ToEven).ToString("N0", CultureInfo.InvariantCulture) : "---";

    public static string Pct(double? v) =>
        v is double d && double.IsFinite(d) ? d.ToString("F2", CultureInfo.InvariantCulture) + "%" : "---";
}
