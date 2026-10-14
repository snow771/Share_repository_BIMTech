namespace SimilaritySearchApp.Models;

/// <summary>画面の絞込条件一式。文字列条件は未指定のとき <see cref="Unspecified"/>。</summary>
public sealed class FilterCriteria
{
    public const string Unspecified = "(未指定)";

    public static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != Unspecified;

    // ── 地域・建物諸元 ──
    public string Prefecture { get; set; } = Unspecified;
    public string City { get; set; } = Unspecified;
    public string Town { get; set; } = Unspecified;
    public string Structure { get; set; } = Unspecified;
    public string Form { get; set; } = Unspecified;
    public string FloorsAbove { get; set; } = Unspecified;
    public string FloorsBelow { get; set; } = Unspecified;

    // ── 算出モード ──
    public BasisMode BasisMode { get; set; } = BasisMode.LegalFloorArea;
    public CostMode CostMode { get; set; } = CostMode.NetConstruction;

    // ── 面積レンジ（null は制限なし） ──
    public double? AreaMin { get; set; }
    public double? AreaMax { get; set; }

    // ── 工種 ──
    public string Trade { get; set; } = Unspecified;
    public string Work { get; set; } = Unspecified;
    public string Item { get; set; } = Unspecified;

    public FilterCriteria Clone() => (FilterCriteria)MemberwiseClone();
}
