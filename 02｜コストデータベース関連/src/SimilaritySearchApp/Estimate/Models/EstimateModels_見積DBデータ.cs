namespace SimilaritySearchApp.Estimate.Models;

// 見積DB連携で使う型。金額・数量はすべて decimal（double を経由しない）。
// rowversion は画面とのやりとりのため Base64 文字列にする。

// ── 構造化JSON の読込結果（取込元）──

public sealed class ImportMetadata
{
    public string CreatedAt { get; set; } = "";
    public string SourceLayoutJson { get; set; } = "";
    public string CommonRulesFile { get; set; } = "";
    public string SupplierRulesFile { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string EstimateId { get; set; } = "";
}

public sealed class ImportSummary
{
    public int DetailCount { get; set; }
    public int MoneyDetailCount { get; set; }
    public int ReferenceDetailCount { get; set; }
    public int AmountCheckOkCount { get; set; }
    public int DuplicateCandidateCount { get; set; }
    public decimal? HeaderEstimateAmount { get; set; }
    public decimal? DetailAmountTotal { get; set; }
    public decimal? DifferenceToHeaderAmount { get; set; }
    public int AuditRowCount { get; set; }
    public int AuditExcludedCount { get; set; }
}

public sealed class ImportItem
{
    public string RecordType { get; set; } = "";
    public int? PdfPage { get; set; }
    public int? OcrTableIndex { get; set; }
    public int? OcrRowIndex { get; set; }
    public string Trade { get; set; } = "";
    public string Section { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Material { get; set; } = "";
    public string Dimension { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
    public string RemarksOcr { get; set; } = "";
    public decimal? CalculatedAmount { get; set; }
    public decimal? Difference { get; set; }
    public string AmountCheck { get; set; } = "";
    public string DuplicateCandidate { get; set; } = "";
    public string ReviewRequired { get; set; } = "";
    public string ReviewReason { get; set; } = "";
}

public sealed class ImportAuditRow
{
    public int? PdfPage { get; set; }
    public int? OcrTableIndex { get; set; }
    public int? OcrRowIndex { get; set; }
    public string RowStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RowText { get; set; } = "";
}

public sealed class StructuredPayload
{
    public ImportMetadata Metadata { get; set; } = new();
    public ImportSummary Summary { get; set; } = new();
    public List<ImportItem> Items { get; set; } = new();
    public List<ImportAuditRow> AuditRows { get; set; } = new();
}

// ── 取込 ──

/// <summary>取込の指定。構造化Excel・統合OCR JSON は省略時に同じフォルダから探す。</summary>
public sealed class ImportRequest
{
    public string StructuredJsonPath { get; set; } = "";
    public string? StructuredExcelPath { get; set; }
    public string? RawJsonPath { get; set; }
    public string? PdfPath { get; set; }
    public bool IsTestData { get; set; }
}

public sealed record ImportFileView(string Type, string Label, string? Path, long? Size, bool Required);

/// <summary>取込前の確認内容（何を・どれだけ登録するか、既に登録済みか）。</summary>
public sealed record ImportPreview(
    ImportRequest Request, List<ImportFileView> Files, ImportMetadata Metadata, ImportSummary Summary,
    int ItemCount, int AuditCount, int? ExistingHeaderId, List<string> Warnings);

public sealed record ImportResult(int HeaderId, bool Reused, List<string> AddedDocuments, List<string> Warnings);

// ── 一覧・詳細 ──

public sealed class EstimateListQuery
{
    public string? Keyword { get; set; }

    /// <summary>all / unlinked / draft / confirmed</summary>
    public string State { get; set; } = "all";
    public bool IncludeTest { get; set; } = true;
    public int Limit { get; set; } = 300;
}

public sealed record EstimateListItem(
    int HeaderId, DateTime? ImportedAt, string? ImportedBy, string? SupplierNameOcr, string? ProjectNameOcr,
    string? ProjectId, string? ProjectName, int? SupplierId, string? SupplierCode, string? SupplierName,
    int LineCount, decimal? DetailAmountTotal, string State, int? RevisionNo, bool IsTestData, bool HasPdf);

public sealed record HeaderView(
    int HeaderId, int? BatchId, string? ProjectId, string? ProjectName, string? ProjectAddress,
    int? SupplierId, string? SupplierCode, string? SupplierName, string? SupplierAddress,
    string? ProjectNameOcr, string? SupplierNameOcr, string? EstimateIdOcr, DateTime? EstimateDate,
    decimal? HeaderEstimateAmount, decimal? DetailAmountTotal, decimal? DifferenceToHeaderAmount,
    string? ReviewStatus, bool IsTestData, DateTime? ImportedAt, string? ImportedBy,
    string? CommonRulesFile, string? SupplierRulesFile, string RowVersion);

public sealed record LineView(
    int LineId, int Order, string? RecordType, int? PdfPage, int? OcrTableIndex, int? OcrRowIndex,
    string? Trade, string? Section, string? ItemName, string? MaterialSpec, string? DimensionSpec,
    decimal? Quantity, string? Unit, decimal? UnitPrice, decimal? Amount, string? RemarksOcr,
    decimal? CalculatedAmount, decimal? Difference, string? AmountCheck, string? DuplicateCandidate,
    string? ReviewRequired, string? ReviewReason,
    string? HumanReviewStatus, string? HumanReviewNotes, string? ReviewedBy, DateTime? ReviewedAt,
    bool? IsCalculationTarget, string? ExclusionReason,
    decimal? CorrectedQuantity, string? CorrectedUnit, decimal? CorrectedUnitPrice, decimal? CorrectedAmount,
    string? CorrectionReason, string RowVersion);

public sealed record AuditView(int? PdfPage, int? OcrTableIndex, int? OcrRowIndex, string? RowStatus, string? Reason, string? RowText);

public sealed record DocumentView(long DocumentId, string Type, string FileName, long Size, DateTime StoredAt, string StoredBy, string HashHex);

public sealed record AgreementLineView(
    long AgreementLineId, int LineId, int LineOrder, string RecordType, string ItemName, string? MaterialSpec, string? DimensionSpec,
    decimal? Quantity, string? Unit, decimal? OriginalUnitPrice, decimal? OriginalAmount, decimal? AppliedRate,
    decimal? ConvertedUnitPrice, decimal? ConvertedAmount, bool IsCalculationTarget, string? ExclusionReason, string CalculationType)
{
    /// <summary>諸経費込み単価（単価 × 諸経費掛率）。諸経費を配分しないときは null</summary>
    public decimal? OverheadIncludedUnitPrice { get; init; }

    /// <summary>諸経費込み金額（金額 × 諸経費掛率）</summary>
    public decimal? OverheadIncludedAmount { get; init; }
}

public sealed record AgreementView(
    long AgreementId, int RevisionNo, string Status, long? PreviousAgreementId,
    decimal? BaseEstimateAmount, string? BaseAmountSource, string? BaseAmountReason, decimal? AgreedAmount, string? TaxBasis,
    decimal? AppliedRate, DateTime? AgreementDate, long? EvidenceDocumentId, int? EvidencePage,
    decimal? ConvertedDetailTotal, decimal? ResidualAmount, string? ConfirmedBy, DateTime? ConfirmedAt,
    string? Notes, string? ChangeReason, string RowVersion, List<AgreementLineView> Lines)
{
    /// <summary>諸経費の扱い: none / lines（明細の行）/ input（手入力）。列が無い DB では null</summary>
    public string? OverheadSource { get; init; }
    public decimal? OverheadAmount { get; init; }

    /// <summary>諸経費掛率（(内訳合計＋諸経費) ÷ 内訳合計）</summary>
    public decimal? OverheadRate { get; init; }

    /// <summary>諸経費掛率の計算に使った内訳合計</summary>
    public decimal? OverheadItemsTotal { get; init; }

    /// <summary>諸経費として扱った明細の ID</summary>
    public List<int> OverheadLineIds { get; init; } = new();
}

public sealed record EstimateDetail(
    HeaderView Header, List<LineView> Lines, List<AuditView> Audit, List<DocumentView> Documents,
    List<AgreementView> Agreements, bool Locked, string Operator);

// ── 紐づけ候補 ──

public sealed record ProjectCandidate(string ProjectId, string? Name, string? Address, string? Structure, string Match);

public sealed record SupplierCandidate(
    int SupplierId, string? Code, string? Name, string? Kana, string? Address, string? Phone, string? Department, string Match);

// ── 保存の要求 ──

public sealed class LinkRequest
{
    public int HeaderId { get; set; }
    public string? ProjectId { get; set; }
    public int? SupplierId { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class LineReviewRequest
{
    public int LineId { get; set; }
    public string HumanReviewStatus { get; set; } = EstimateDb.ReviewPending;
    public string? HumanReviewNotes { get; set; }
    public bool IsCalculationTarget { get; set; }
    public string? ExclusionReason { get; set; }
    public decimal? CorrectedQuantity { get; set; }
    public string? CorrectedUnit { get; set; }
    public decimal? CorrectedUnitPrice { get; set; }
    public decimal? CorrectedAmount { get; set; }
    public string? CorrectionReason { get; set; }
    public string RowVersion { get; set; } = "";
}

/// <summary>取決めの入力（下書き保存・計算プレビュー・確定で共通）。金額は画面から文字列で受けて decimal にする。</summary>
public sealed class AgreementInput
{
    public int HeaderId { get; set; }
    public string? BaseSource { get; set; }
    public decimal? BaseAmount { get; set; }
    public string? BaseReason { get; set; }
    public string? TaxBasis { get; set; }
    public decimal? AgreedAmount { get; set; }
    public DateTime? AgreementDate { get; set; }
    public long? EvidenceDocumentId { get; set; }
    public int? EvidencePage { get; set; }
    public string? Notes { get; set; }

    /// <summary>取決め総額 0 円を確認した</summary>
    public bool ZeroConfirmed { get; set; }

    /// <summary>残差（取決め総額－換算金額合計）が 0 でないときの理由</summary>
    public string? ResidualReason { get; set; }

    /// <summary>掛率が 1 を超える（増額）ときの理由</summary>
    public string? IncreaseReason { get; set; }

    /// <summary>諸経費の扱い: none（なし）/ lines（明細の行から）/ input（手入力）</summary>
    public string? OverheadSource { get; set; }

    /// <summary>手入力の諸経費（OverheadSource = input のとき）</summary>
    public decimal? OverheadAmount { get; set; }

    /// <summary>諸経費として扱う明細の ID（OverheadSource = lines のとき）</summary>
    public List<int> OverheadLineIds { get; set; } = new();

    /// <summary>内訳合計を画面で変えたときの値（null なら計算対象の明細の合計＝OCR の結果）</summary>
    public decimal? ItemsTotalOverride { get; set; }

    /// <summary>確定要求の識別（通信再送・二重クリックで同じ確定を二重に作らない）</summary>
    public Guid? RequestKey { get; set; }

    /// <summary>画面が読み込んだ時点の見積ヘッダの rowversion（同時編集の検出）</summary>
    public string HeaderRowVersion { get; set; } = "";
}

public sealed record PreviewLine(
    int LineId, int Order, string? RecordType, string? ItemName, string? MaterialSpec, string? DimensionSpec,
    decimal? Quantity, string? Unit, decimal? UnitPrice, decimal? Amount,
    bool IsTarget, string? ExclusionReason, decimal? ConvertedUnitPrice, decimal? ConvertedAmount, bool Corrected)
{
    /// <summary>諸経費として扱う行（換算せず、各明細の単価へ配分する）</summary>
    public bool IsOverhead { get; init; }
    public decimal? OverheadIncludedUnitPrice { get; init; }
    public decimal? OverheadIncludedAmount { get; init; }
}

public sealed record AgreementPreview(
    decimal? Rate, decimal? ConvertedTotal, decimal? Residual, decimal? BaseAmount, decimal? AgreedAmount,
    int TargetCount, int ExcludedCount, List<PreviewLine> Lines, List<string> Blocking, List<string> Warnings)
{
    public bool CanConfirm => Blocking.Count == 0;

    /// <summary>内訳合計（諸経費の行を除いた計算対象行の金額合計）</summary>
    public decimal? ItemsTotal { get; init; }
    public decimal? OverheadAmount { get; init; }

    /// <summary>諸経費掛率（(内訳合計＋諸経費) ÷ 内訳合計）。諸経費なしなら null</summary>
    public decimal? OverheadRate { get; init; }
}

public sealed class DeleteEstimateRequest
{
    public int HeaderId { get; set; }
    public string RowVersion { get; set; } = "";
    public string? Reason { get; set; }

    /// <summary>確定済みの取決めがある見積は、見積ID を入力して確認する</summary>
    public string? ConfirmText { get; set; }
}

public sealed record DeleteEstimateResult(int HeaderId, int Rows, int ChangeBatchId);

public sealed class NewRevisionRequest
{
    public int HeaderId { get; set; }
    public string Reason { get; set; } = "";
}
