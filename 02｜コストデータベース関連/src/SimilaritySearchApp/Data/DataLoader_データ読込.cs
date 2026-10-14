using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>読込結果。Warning は物価指数補正・建築材料マスタの読込をスキップした場合などの注意。</summary>
public sealed record LoadedData(
    List<ProjectRecord> Projects, List<CostRecord> Assess, List<CostRecord> Revise,
    List<MaterialRecord> Materials, string? Warning);

/// <summary>
/// DB から一式を読み込む（Python の load_all_from_db）。
/// 物件 → 期間判定 → 査定/修正単価 → term 付与 → 物価指数補正 → 建築材料マスタ（標準単価）。
/// </summary>
public sealed class DataLoader
{
    private readonly DbSettings _settings;

    public DataLoader(DbSettings settings) => _settings = settings;

    public async Task<LoadedData> LoadAsync(StructureType structure, CancellationToken ct = default)
    {
        var factory = new SqlConnectionFactory(_settings);
        var timeout = _settings.CommandTimeoutSec;
        var tables = _settings.Tables;
        await using var conn = await factory.OpenAsync(ct);

        var masters = new MasterRepository();
        var costs = new CostRepository();

        var projects = await new ProjectRepository().LoadAsync(conn, tables, structure, timeout, ct);

        // 竣工日 → term
        var terms = await masters.LoadTermsAsync(conn, tables, timeout, ct);
        foreach (var p in projects)
        {
            p.Term = DeflatorService.AssignTerm(p.CompletionDate, terms);
            p.Attributes["term"] = p.Term;
        }

        var assess = await costs.LoadAsync(conn, tables.Assess, CostRepository.AssessPriceColumn, structure, timeout, ct);
        var revise = await costs.LoadAsync(conn, tables.Revise, CostRepository.RevisePriceColumn, structure, timeout, ct);

        // project_id → term を明細へ
        var termByPid = new Dictionary<string, string>();
        foreach (var p in projects) termByPid.TryAdd(p.ProjectId, p.Term);
        foreach (var c in assess.Concat(revise))
            c.Term = termByPid.TryGetValue(c.ProjectId, out var t) ? t : "";

        // 物価指数補正（失敗しても補正なしで続行）
        var warnings = new List<string>();
        try
        {
            var m = await masters.LoadDeflatorMastersAsync(conn, tables, timeout, ct);
            DeflatorService.Apply(assess, m.Categories, m.IndexAllocations, m.Deflators);
            DeflatorService.Apply(revise, m.Categories, m.IndexAllocations, m.Deflators);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            DeflatorService.ApplyNone(assess);
            DeflatorService.ApplyNone(revise);
            warnings.Add("物価指数補正をスキップしました（補正係数=1.0）: " + e.Message);
        }

        // 建築材料マスタ（失敗しても【標準単価】タブ以外は使える）
        var materials = new List<MaterialRecord>();
        try
        {
            materials = await new MaterialRepository().LoadAsync(conn, tables.MaterialMaster, timeout, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            warnings.Add("建築材料マスタを読み込めませんでした（【標準単価】タブは使えません）: " + e.Message);
        }

        return new LoadedData(projects, assess, revise, materials,
            warnings.Count == 0 ? null : string.Join("\n", warnings));
    }
}
