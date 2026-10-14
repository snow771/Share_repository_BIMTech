namespace SimilaritySearchApp.Models;

/// <summary>
/// 建築材料マスタ（MaterialMaster_建築材料マスタ）の1行＝標準単価1件。
/// 物件別の単価ではない。既存の査定・修正単価とは工種コード（Work_Type_Code）で関連付ける。
/// 単価の 0 と null（未設定）は区別して保持する。鉄筋などは差額単価を含むため、一律に絶対単価とは解釈しない。
/// </summary>
public sealed class MaterialRecord
{
    /// <summary>MaterialMaster_ID（自動採番の主キー）</summary>
    public int? Id { get; init; }

    /// <summary>工種コード（Category の工種コードを参照。NULL 許容）</summary>
    public string WorkTypeCode { get; init; } = "";

    /// <summary>建材コード（重複があり得るため一意ではない）</summary>
    public string MaterialCode { get; init; } = "";

    /// <summary>名称</summary>
    public string Name { get; init; } = "";

    /// <summary>規格・寸法</summary>
    public string? Specification { get; init; }

    /// <summary>備考（メモ）</summary>
    public string? Notes { get; init; }

    /// <summary>単位</summary>
    public string? Unit { get; init; }

    /// <summary>標準の査定単価（円/単位）。null は未設定。</summary>
    public double? AssessPrice { get; init; }

    /// <summary>標準の修正単価（円/単位）。null は未設定。</summary>
    public double? RevisePrice { get; init; }

    /// <summary>元シート名（Excel のシート＝工種のまとまり）</summary>
    public string? SourceSheet { get; init; }

    /// <summary>元行番号</summary>
    public int? SourceRow { get; init; }

    /// <summary>確認状態（確認済 / 要確認）</summary>
    public string? ReviewStatus { get; init; }
}
