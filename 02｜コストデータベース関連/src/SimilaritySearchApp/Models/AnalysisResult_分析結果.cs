using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Models;

/// <summary>補正前と補正後の統計要約の組。</summary>
public sealed record BeforeAfter(RangeSummary Before, RangeSummary After)
{
    public static BeforeAfter Empty { get; } = new(RangeSummary.Empty, RangeSummary.Empty);
}

/// <summary>グラフの1点。X は床面積または物件インデックス。</summary>
public sealed record ChartPoint(string ProjectId, double X, double Y);

/// <summary>グラフ1指標分（箱ひげ図・散布図の両方に使う）。</summary>
/// <param name="Key">assess / revise / temp / formwork / rebar</param>
public sealed record ChartSeries(
    string Key, string Title, string Unit, string XLabel,
    List<ChartPoint> Before, List<ChartPoint> After, BeforeAfter Summary);

/// <summary>物件一覧の1行。</summary>
/// <param name="ReviseYenPerM2">現在条件での修正単価（補正後・表示モード反映済み）。無ければ null。</param>
public sealed record ProjectRow(
    string ProjectId, string? Name, string? Prefecture, string? City, string? Town,
    string? Structure, string? Form, double? FloorsAbove, double? FloorsBelow,
    double? LegalFloorArea, double? ConstructionFloorArea, double? ReviseYenPerM2);

/// <summary>条件1回分の分析結果。画面（統計カード・グラフ・一覧・左パネル）に必要な情報をすべて持つ。</summary>
public sealed class AnalysisResult
{
    /// <summary>絞込後の物件数</summary>
    public int ProjectCount { get; set; }

    /// <summary>工種:… / 工事項目:… / 項目:…</summary>
    public string SelectionLabel { get; set; } = "";

    /// <summary>条件の箇条（チップ表示・サマリー用）</summary>
    public List<string> ConditionParts { get; set; } = new();

    public BeforeAfter Assess { get; set; } = BeforeAfter.Empty;
    public BeforeAfter Revise { get; set; } = BeforeAfter.Empty;
    public BeforeAfter TempUnit { get; set; } = BeforeAfter.Empty;
    public BeforeAfter TempRatio { get; set; } = BeforeAfter.Empty;
    public BeforeAfter Formwork { get; set; } = BeforeAfter.Empty;
    public BeforeAfter Rebar { get; set; } = BeforeAfter.Empty;

    public double? AssessDeflatorMean { get; set; }
    public double? ReviseDeflatorMean { get; set; }

    public int TempN { get; set; }
    public int FormworkN { get; set; }
    public int RebarN { get; set; }

    public List<ChartSeries> Charts { get; set; } = new();
    public List<ProjectRow> Projects { get; set; } = new();
    public Dictionary<string, List<FacetOption>> Facets { get; set; } = new();

    /// <summary>面積スライダーの範囲（全物件の基準面積の最小・最大）</summary>
    public double AreaBoundMin { get; set; }
    public double AreaBoundMax { get; set; }

    public List<string> TradeOptions { get; set; } = new();
    public List<string> WorkOptions { get; set; } = new();
    public List<string> ItemOptions { get; set; } = new();

    /// <summary>実際に適用した条件（面積レンジの補正などを反映済み）</summary>
    public FilterCriteria Criteria { get; set; } = new();

    public string StatusLine { get; set; } = "";
}
