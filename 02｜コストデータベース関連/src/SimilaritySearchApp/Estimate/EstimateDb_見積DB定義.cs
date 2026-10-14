using SimilaritySearchApp.Data;

namespace SimilaritySearchApp.Estimate;

/// <summary>
/// 見積DB連携で使う表名・状態値・操作者。
/// 表は docs/2026-10-13_OCR見積DB連携 の構造SQLで作成・拡張済みのもの（アプリからは DDL を実行しない）。
/// </summary>
public static class EstimateDb
{
    public const string Schema = "dbo";

    public const string ImportBatch = "OcrImportBatch_OCR取込";
    public const string Header = "OcrEstimateHeader_OCR見積ヘッダ";
    public const string Line = "OcrEstimateLine_OCR見積明細";
    public const string Audit = "OcrAuditRow_OCR行監査";
    public const string Document = "DocumentFile_資料ファイル";
    public const string Agreement = "Agreement_取決め情報";
    public const string AgreementLine = "AgreementLine_取決め明細";
    public const string Supplier = "SupplierMaster_協力業者マスタ";

    /// <summary>[dbo].[表名]</summary>
    public static string T(string table) => $"{TableEditRepository.Q(Schema)}.{TableEditRepository.Q(table)}";

    /// <summary>このアプリが読み書きする表（起動時の構造確認に使う）</summary>
    public static readonly string[] RequiredTables = { ImportBatch, Header, Line, Audit, Document, Agreement, AgreementLine, Supplier };

    // ── 資料の種類（DocumentType）──
    public const string DocOriginalPdf = "OriginalPdf";
    public const string DocStructuredExcel = "StructuredExcel";
    public const string DocStructuredJson = "StructuredJson";
    public const string DocRawJson = "RawJson";

    // ── 取決めの状態（Status）──
    public const string Draft = "Draft";
    public const string Confirmed = "Confirmed";
    public const string Superseded = "Superseded";

    // ── 人の確認状態（HumanReviewStatus）──
    public const string ReviewPending = "未確認";
    public const string ReviewDone = "確認済";
    public const string ReviewExcluded = "計算対象外";
    public static readonly string[] ReviewStatuses = { ReviewPending, ReviewDone, ReviewExcluded };

    // ── 基準総額の根拠（BaseAmountSource）──
    public const string BaseFromHeader = "表紙金額";
    public const string BaseFromDetail = "明細合計";
    public const string BaseFromInput = "確認入力";

    public const string TaxExcluded = "TaxExcluded";
    public const string TaxIncluded = "TaxIncluded";

    /// <summary>計算規則の版（AgreementLine の計算方法が変わったら上げる）</summary>
    public const string CalculationRuleVersion = "TotalRate-v1";
    public const string CalcTotalRate = "TotalRateAllocation";
    public const string CalcExcluded = "Excluded";

    /// <summary>1ファイルの上限（DB 容量を守るため。超えたら保存しない）</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>操作者（Windows のドメイン\ユーザー名。画面から入力させない）</summary>
    public static string Operator => $@"{Environment.UserDomainName}\{Environment.UserName}";
}
