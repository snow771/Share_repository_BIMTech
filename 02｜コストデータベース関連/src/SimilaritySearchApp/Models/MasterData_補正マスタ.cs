namespace SimilaritySearchApp.Models;

/// <summary>Category_カテゴリマスタ: 工種コード → カテゴリーコード</summary>
public sealed record CategoryRow(string WorkTypeCode, int? CategoryCode);

/// <summary>Index_Allocation_指数割当マスタ: カテゴリーコード → 指数割当</summary>
public sealed record IndexAllocationRow(int? CategoryCode, string IndexAllocation);

/// <summary>Deflator_補正係数: (term, 指数割当) → deflator</summary>
public sealed record DeflatorRow(string Term, string IndexAllocation, double? Deflator);

/// <summary>Term_期間マスタ: term と期間（両端を含む）</summary>
public sealed record TermRow(string Term, DateTime? Start, DateTime? End);
