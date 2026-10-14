namespace SimilaritySearchApp.Ocr.Models;

// 見積AI-OCR で使うデータの形。
// 構造化結果（EstimateItem / AuditRow / StructuredSummary）は Python 版 v6 の JSON と同じ項目
// （JSON 保存時は snake_case: estimate_id, record_type ... に変換する）。

/// <summary>構造化明細の1行（金額明細 または 参考明細）。</summary>
public sealed class EstimateItem
{
    public string EstimateId { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string SourceFile { get; set; } = "";

    /// <summary>金額明細 / 参考明細</summary>
    public string RecordType { get; set; } = "";
    public int? PdfPage { get; set; }
    public int OcrTableIndex { get; set; }
    public int OcrRowIndex { get; set; }
    public string Trade { get; set; } = "";
    public string Section { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Material { get; set; } = "";
    public string Dimension { get; set; } = "";
    public double? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public double? UnitPrice { get; set; }
    public double? Amount { get; set; }
    public string RemarksOcr { get; set; } = "";

    /// <summary>数量×単価（四捨五入。金額明細のみ）</summary>
    public long? CalculatedAmount { get; set; }

    /// <summary>金額 − 計算金額</summary>
    public long? Difference { get; set; }

    /// <summary>OK / 要確認 / 対象外</summary>
    public string AmountCheck { get; set; } = "";

    /// <summary>同一明細候補 / 近似重複候補 / 空</summary>
    public string DuplicateCandidate { get; set; } = "";

    /// <summary>要確認 / 空</summary>
    public string ReviewRequired { get; set; } = "";
    public string ReviewReason { get; set; } = "";
}

/// <summary>OCR行監査の1行（明細表の各行を 採用／見出し／除外／空行 に判定した結果）。</summary>
public sealed class AuditRow
{
    public int? PdfPage { get; set; }
    public int OcrTableIndex { get; set; }
    public int OcrRowIndex { get; set; }

    /// <summary>採用 / 見出し / 除外 / 空行</summary>
    public string RowStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RowText { get; set; } = "";
}

public sealed class StructuredMetadata
{
    public string CreatedAt { get; set; } = "";
    public string SourceLayoutJson { get; set; } = "";
    public string CommonRulesFile { get; set; } = "";
    public string SupplierRulesFile { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string EstimateId { get; set; } = "";
}

public sealed class StructuredSummary
{
    public int DetailCount { get; set; }
    public int MoneyDetailCount { get; set; }
    public int ReferenceDetailCount { get; set; }
    public int AmountCheckOkCount { get; set; }
    public int DuplicateCandidateCount { get; set; }
    public double? HeaderEstimateAmount { get; set; }
    public double DetailAmountTotal { get; set; }
    public long? DifferenceToHeaderAmount { get; set; }
    public int AuditRowCount { get; set; }
    public int AuditExcludedCount { get; set; }

    /// <summary>要確認の明細数（画面表示用。Python 版には無い項目）</summary>
    public int ReviewRequiredCount { get; set; }
}

/// <summary>構造化の結果（構造化 JSON の中身そのもの）。</summary>
public sealed class StructuredResult
{
    public StructuredMetadata Metadata { get; set; } = new();
    public StructuredSummary Summary { get; set; } = new();
    public List<EstimateItem> Items { get; set; } = new();
    public List<AuditRow> AuditRows { get; set; } = new();
}

// ── 画面とのやりとり ──

/// <summary>選んだ PDF（ページ数は読めなければ null）。</summary>
public sealed record PdfFileInfo(string Path, string Name, int? Pages, string? Error);

/// <summary>1つの PDF の処理結果（画面の一覧の1行）。</summary>
public sealed class OcrJobResult
{
    public int Index { get; set; }
    public string PdfPath { get; set; } = "";
    public string PdfName { get; set; } = "";

    /// <summary>ok / warn / error / canceled</summary>
    public string Status { get; set; } = "ok";
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();

    public string? JobFolder { get; set; }
    public int? PdfPages { get; set; }
    public int? OcrPages { get; set; }

    /// <summary>この PDF で Azure に送って解析されたページ数（利用枚数に数えた分）</summary>
    public int PagesCounted { get; set; }

    public string? RawJsonPath { get; set; }
    public string? CheckExcelPath { get; set; }
    public string? StructuredJsonPath { get; set; }
    public string? StructuredExcelPath { get; set; }

    /// <summary>構造化した場合の結果（画面のプレビュー用。C# 側に保持し、一覧には要約だけ送る）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public StructuredResult? Structured { get; set; }

    public StructuredMetadata? Metadata => Structured?.Metadata;
    public StructuredSummary? Summary => Structured?.Summary;
}

/// <summary>利用枚数の状況（ヘッダーのメーター）。</summary>
public sealed record OcrUsageView(
    string Period,          // day / month
    string PeriodLabel,     // 本日 / 今月
    int Used,               // 期間内の処理ページ数
    int Files,              // 期間内の処理 PDF 数
    int Limit,
    int WarnAt,
    int DangerAt,
    string Level,           // safe / caution / danger / over
    int Remaining,
    int SessionPages,       // このアプリを起動してからのページ数
    int SessionFiles,
    List<OcrUsageDay> Recent);

public sealed record OcrUsageDay(string Date, int Pages, int Files);
