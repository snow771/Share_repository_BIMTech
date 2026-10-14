using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Data;
using SimilaritySearchApp.Estimate.Models;
using SimilaritySearchApp.Models;
using static SimilaritySearchApp.Estimate.Data.EstimateRepository;

namespace SimilaritySearchApp.Estimate.Data;

/// <summary>
/// 見積（1回の取込分）をまとめて削除する。取決めの後に別の業者へ依頼し直す場合などに使う。
/// 外部キーの順番どおりに、1 トランザクションで消す:
///   取決め明細 → 取決め情報 → 見積明細と建築材料の対応 → 見積明細・OCR行監査 → 見積ヘッダ → 資料ファイル → OCR取込
/// 確定済み（旧版を含む）の取決めがある見積は、見積ID の入力と理由を必須にする（画面でも 2 回確認する）。
/// 削除した行は「📜 変更履歴」に「見積削除」として記録する（だれが・いつ・理由・削除した行）。
/// 資料ファイルの本体（PDF・Excel）は履歴に残らないため、変更履歴の「取り消し」では戻せない。
/// </summary>
public sealed class EstimateDeleteRepository
{
    private const string MaterialMatch = "OcrLineMaterialMasterMatch_見積明細建築材料対応";

    private readonly DbSettings _db;
    private readonly EstimateRepository _estimates;

    public EstimateDeleteRepository(DbSettings db)
    {
        _db = db;
        _estimates = new EstimateRepository(db);
    }

    public async Task<DeleteEstimateResult> DeleteAsync(DeleteEstimateRequest req, CancellationToken ct = default)
    {
        await using var conn = await _estimates.OpenAsync(ct);
        var changeLog = new ChangeLogRepository(_db.Tables);
        await changeLog.EnsureAsync(conn, null, ct);

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var header = await _estimates.HeaderAsync(conn, tx, req.HeaderId, ct, forUpdate: true);
            if (header.RowVersion != req.RowVersion) throw Conflict();
            var agreements = await new AgreementRepository(_estimates).ListAsync(conn, req.HeaderId, ct, tx);
            var confirmed = agreements.Where(a => a.Status != EstimateDb.Draft).ToList();
            if (confirmed.Count > 0)
            {
                if (req.ConfirmText?.Trim() != req.HeaderId.ToString())
                    throw new InvalidOperationException($"確定済みの取決めがある見積です。削除するには、確認のため見積ID「{req.HeaderId}」を入力してください。");
                if (string.IsNullOrWhiteSpace(req.Reason))
                    throw new InvalidOperationException("確定済みの取決めがある見積を削除する理由を入力してください。");
            }

            var log = new ChangeCollector("見積削除")
            {
                Summary = $"見積 #{req.HeaderId}（{header.SupplierNameOcr ?? "業者名なし"}／{header.ProjectNameOcr ?? "工事名なし"}）を削除"
                          + (confirmed.Count > 0 ? $"・確定済みの取決め {confirmed.Count} 版を含む" : "")
                          + (string.IsNullOrWhiteSpace(req.Reason) ? "" : $"。理由: {req.Reason.Trim()}"),
            };
            var batch = header.BatchId;
            var rows = 0;

            async Task Run(string table, string sql, string op = "DELETE")
            {
                await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = Math.Max(60, _db.CommandTimeoutSec) };
                cmd.Parameters.AddWithValue("@h", req.HeaderId);
                cmd.Parameters.AddWithValue("@b", (object?)batch ?? DBNull.Value);
                rows += await log.ExecuteAsync(cmd, EstimateDb.Schema, table, op, ct);
            }

            var lineIds = $"SELECT OcrEstimateLine_ID FROM {EstimateDb.T(EstimateDb.Line)} WHERE OcrEstimateHeader_ID = @h";
            var agreementIds = $"SELECT Agreement_ID FROM {EstimateDb.T(EstimateDb.Agreement)} WHERE OcrEstimateHeader_ID = @h";

            await Run(EstimateDb.AgreementLine, $"DELETE FROM {EstimateDb.T(EstimateDb.AgreementLine)} OUTPUT DELETED.* WHERE Agreement_ID IN ({agreementIds})");
            await Run(EstimateDb.Agreement, $"DELETE FROM {EstimateDb.T(EstimateDb.Agreement)} OUTPUT DELETED.* WHERE OcrEstimateHeader_ID = @h");
            if (await TableExistsAsync(conn, tx, MaterialMatch, ct))
                await Run(MaterialMatch, $"DELETE FROM {EstimateDb.T(MaterialMatch)} OUTPUT DELETED.* WHERE OcrEstimateLine_ID IN ({lineIds})");
            await Run(EstimateDb.Line, $"DELETE FROM {EstimateDb.T(EstimateDb.Line)} OUTPUT DELETED.* WHERE OcrEstimateHeader_ID = @h");
            await Run(EstimateDb.Audit, $"DELETE FROM {EstimateDb.T(EstimateDb.Audit)} OUTPUT DELETED.* WHERE OcrEstimateHeader_ID = @h");
            await Run(EstimateDb.Header, $"DELETE FROM {EstimateDb.T(EstimateDb.Header)} OUTPUT DELETED.* WHERE OcrEstimateHeader_ID = @h");

            // 取込記録と資料は、同じ取込を使う見積がほかに無いときだけ消す
            if (batch is not null && !await OtherHeaderUsesBatchAsync(conn, tx, batch.Value, ct))
            {
                // 資料の本体（FileContent）は履歴に入れない（大きいため。取り消しでは戻せない）
                await Run(EstimateDb.Document,
                    $"""
                    DELETE FROM {EstimateDb.T(EstimateDb.Document)}
                    OUTPUT DELETED.DocumentFile_ID, DELETED.OcrImportBatch_ID, DELETED.DocumentType, DELETED.OriginalFileName,
                           DELETED.ContentType, DELETED.FileSize, DELETED.FileHash, DELETED.StoredAt, DELETED.StoredBy
                    WHERE OcrImportBatch_ID = @b
                    """);
                // この取込を「前の取込」としている別の取込があれば、参照を外す
                await Run(EstimateDb.ImportBatch,
                    $"UPDATE {EstimateDb.T(EstimateDb.ImportBatch)} SET PreviousImportBatch_ID = NULL OUTPUT DELETED.*, INSERTED.* WHERE PreviousImportBatch_ID = @b",
                    "UPDATE");
                await Run(EstimateDb.ImportBatch, $"DELETE FROM {EstimateDb.T(EstimateDb.ImportBatch)} OUTPUT DELETED.* WHERE OcrImportBatch_ID = @b");
            }

            var changeId = await changeLog.WriteAsync(conn, tx, log, ct);
            await tx.CommitAsync(ct);
            return new DeleteEstimateResult(req.HeaderId, rows, changeId ?? 0);
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection conn, SqlTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT OBJECT_ID(@t, N'U')", conn, tx);
        cmd.Parameters.AddWithValue("@t", $"{EstimateDb.Schema}.{table}");
        return await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
    }

    private static async Task<bool> OtherHeaderUsesBatchAsync(SqlConnection conn, SqlTransaction tx, int batchId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {EstimateDb.T(EstimateDb.Header)} WHERE OcrImportBatch_ID = @b", conn, tx);
        cmd.Parameters.AddWithValue("@b", batchId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }
}
