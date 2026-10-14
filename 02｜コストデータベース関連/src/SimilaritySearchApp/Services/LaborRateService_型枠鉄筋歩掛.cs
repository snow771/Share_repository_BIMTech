using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>型枠・鉄筋の労務歩掛（物件ごと）。値が出せない側は null。</summary>
/// <param name="FormworkPerM2">型枠工事労務歩掛（円/㎡）</param>
/// <param name="RebarPerTon">鉄筋工事労務歩掛（円/t）</param>
public sealed record LaborRatePoint(string ProjectId, double? FormworkPerM2, double? RebarPerTon);

/// <summary>型枠・鉄筋工事の労務歩掛（Python の calculate_formwork_rebar_stats）。修正単価を使う。</summary>
public static class LaborRateService
{
    /// <summary>型枠工事 労務の工種コード</summary>
    public const string FormworkCode = "02-04-10000";

    /// <summary>鉄筋工事 加工組立の工種コード</summary>
    public const string RebarCode = "02-05-30000";

    public static List<LaborRatePoint> Calculate(
        IReadOnlyList<ProjectRecord> projects, IEnumerable<CostRecord> revise, bool adjusted)
    {
        var byProject = UnitPriceService.GroupByProject(revise);
        var result = new List<LaborRatePoint>();

        foreach (var p in projects)
        {
            if (!byProject.TryGetValue(p.ProjectId.Trim(), out var rows) || rows.Count == 0) continue;

            // 型枠: 工種コード 02-04-10000 の合計 ÷ 型枠（㎡）
            double? formwork = null;
            if (p.Formwork is double fw && fw > 0)
            {
                var hit = rows.Where(r => r.WorkTypeCode.Trim() == FormworkCode).ToList();
                if (hit.Count > 0) formwork = hit.Sum(r => r.GetAmount(adjusted)) / fw;
            }

            // 鉄筋: 工種コード 02-05-30000 の合計 ÷ 「鉄筋（Kg/m2）」列の値
            // ※ Python 版の挙動を踏襲: kg/㎡ × 施工床面積 ÷ 1000 の t 換算はコメントアウトされており、
            //    列の値をそのまま t として割っている。換算を入れる場合は Python 版と合わせて見直すこと。
            double? rebar = null;
            if (p.RebarKgPerM2 is double tons && p.ConstructionFloorArea is double area && area > 0 && tons > 0)
            {
                var hit = rows.Where(r => r.WorkTypeCode.Trim() == RebarCode).ToList();
                if (hit.Count > 0) rebar = hit.Sum(r => r.GetAmount(adjusted)) / tons;
            }

            if (formwork.HasValue || rebar.HasValue)
                result.Add(new LaborRatePoint(p.ProjectId, formwork, rebar));
        }
        return result;
    }
}
