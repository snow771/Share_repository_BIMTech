using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Bridge;

/// <summary>アプリの実行時状態（読込済みデータ・現在の条件・選択物件）。</summary>
public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();
    public StructureType StructureType { get; set; } = StructureType.RC;

    public List<ProjectRecord> Projects { get; private set; } = new();
    public List<CostRecord> Assess { get; private set; } = new();
    public List<CostRecord> Revise { get; private set; } = new();

    /// <summary>建築材料マスタ（標準単価）。読込に失敗した場合は空。</summary>
    public List<MaterialRecord> Materials { get; private set; } = new();

    /// <summary>査定＋修正（工種の選択肢・階層の元データ）</summary>
    public List<CostRecord> AllCosts { get; private set; } = new();

    public List<TradePath> Hierarchy { get; private set; } = new();

    public FilterCriteria Criteria { get; set; } = new();

    /// <summary>AI アシスタントとの直近の会話（自然な会話の流れを保つため）。</summary>
    public List<Llm.ChatMessage> ChatHistory { get; } = new();
    public string? SelectedProjectId { get; set; }
    public AnalysisResult? LastResult { get; set; }

    public bool IsLoaded { get; private set; }

    public void SetData(StructureType structure, List<ProjectRecord> projects, List<CostRecord> assess, List<CostRecord> revise,
        List<MaterialRecord> materials)
    {
        Materials = materials;
        StructureType = structure;
        Projects = projects;
        Assess = assess;
        Revise = revise;
        AllCosts = assess.Concat(revise).ToList();
        Hierarchy = TradeInferenceService.BuildHierarchy(AllCosts);
        Criteria = new FilterCriteria();
        SelectedProjectId = null;
        LastResult = null;
        IsLoaded = true;
    }

    public ProjectRecord? FindProject(string? id) =>
        id is null ? null : Projects.FirstOrDefault(p => p.ProjectId == id);
}
