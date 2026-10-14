using System.Text;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Data;
using SimilaritySearchApp.Estimate.Models;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Estimate.Data;

/// <summary>
/// OCR見積の保存と照会（取込・一覧・詳細・物件/業者の紐づけ・明細の確認結果）。
/// OCR の原値の列は取込後に変更しない。人の確認・補正は拡張列（HumanReviewStatus・Corrected* など）へ保存する。
/// 更新は rowversion を照合し、他の人が先に変更していたら保存しない（再読込を促す）。
/// </summary>
public sealed class EstimateRepository
{
    private readonly DbSettings _db;
    private readonly DocumentRepository _docs = new();

    public EstimateRepository(DbSettings db) => _db = db;

    private string Projects => EstimateDb.T(_db.Tables.Projects);

    public Task<SqlConnection> OpenAsync(CancellationToken ct = default) => new SqlConnectionFactory(_db).OpenAsync(ct);

    // ================================================================
    // 取込
    // ================================================================

    /// <summary>同じ取込識別キーの見積があればその見積ヘッダID。</summary>
    public async Task<int?> FindByIdentityAsync(SqlConnection conn, SqlTransaction? tx, string identityKey, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT TOP 1 h.OcrEstimateHeader_ID
            FROM {EstimateDb.T(EstimateDb.ImportBatch)} b
            JOIN {EstimateDb.T(EstimateDb.Header)} h ON h.OcrImportBatch_ID = b.OcrImportBatch_ID
            WHERE b.ImportIdentityKey = @k
            ORDER BY h.OcrEstimateHeader_ID
            """, conn, tx);
        cmd.Parameters.Add(new SqlParameter("@k", System.Data.SqlDbType.Char, 64) { Value = identityKey });
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    public sealed record ImportFiles(
        DocumentRepository.StoredFile StructuredJson, DocumentRepository.StoredFile? StructuredExcel,
        DocumentRepository.StoredFile? RawJson, DocumentRepository.StoredFile? Pdf,
        string StructuredJsonPath, string? StructuredExcelPath, string? RawJsonPath, string? PdfPath);

    /// <summary>
    /// 1 回の取込（取込記録・見積ヘッダ・明細・行監査・資料）を 1 トランザクションで保存する。
    /// 同じ取込識別キーが既にあれば何も作らず既存の見積を返す（元PDFが未登録で今回指定されていれば追加する）。
    /// </summary>
    public async Task<ImportResult> ImportAsync(StructuredPayload p, ImportFiles f, string identityKey, string ruleVersion, bool isTestData, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var guard = await ColumnGuard.LoadAsync(conn, tx,
                new[] { EstimateDb.ImportBatch, EstimateDb.Header, EstimateDb.Line, EstimateDb.Audit, EstimateDb.Document }, ct);
            var warnings = new List<string>();
            var added = new List<string>();

            // 既に取り込み済みなら、その見積を返す
            if (await FindByIdentityAsync(conn, tx, identityKey, ct) is int existing)
            {
                if (f.Pdf is not null)
                {
                    var batchId = await BatchOfAsync(conn, tx, existing, ct);
                    if (await _docs.AddIfMissingAsync(conn, tx, guard, batchId, f.Pdf, ct)) added.Add($"元PDF（{f.Pdf.FileName}）");
                }
                await tx.CommitAsync(ct);
                return new ImportResult(existing, true, added, warnings);
            }

            // 同じ統合OCR JSON から別のルールで構造化した取込があれば、前の取込として記録する
            int? previous = null;
            if (f.RawJson is not null)
            {
                await using var prev = new SqlCommand(
                    $"SELECT TOP 1 OcrImportBatch_ID FROM {EstimateDb.T(EstimateDb.ImportBatch)} WHERE RawJsonHash = @h ORDER BY OcrImportBatch_ID DESC", conn, tx);
                prev.Parameters.Add(new SqlParameter("@h", System.Data.SqlDbType.Char, 64) { Value = DocumentRepository.Hex(f.RawJson.Hash) });
                var v = await prev.ExecuteScalarAsync(ct);
                if (v is not null and not DBNull) previous = Convert.ToInt32(v);
            }

            // ── 取込記録 ──
            const string B = EstimateDb.ImportBatch;
            const string bw = "取込記録";
            await using var batchCmd = new SqlCommand(
                $"""
                INSERT INTO {EstimateDb.T(B)}
                    (ImportIdentityKey, PreviousImportBatch_ID, RuleVersion, SourceExcelPath, SourceExcelHash,
                     StructuredJsonPath, StructuredJsonHash, RawJsonPath, RawJsonHash, PdfPath, PdfHash,
                     ImportedAt, ImportStatus, ImportedBy, CommonRulesFile, SupplierRulesFile)
                OUTPUT INSERTED.OcrImportBatch_ID
                VALUES (@key, @prev, @rule, @xp, @xh, @jp, @jh, @rp, @rh, @pp, @ph, SYSUTCDATETIME(), @st, @by, @cr, @sr)
                """, conn, tx);
            batchCmd.Parameters.Add(guard.Param("@key", B, "ImportIdentityKey", identityKey, bw));
            batchCmd.Parameters.Add(guard.Param("@prev", B, "PreviousImportBatch_ID", previous, bw));
            batchCmd.Parameters.Add(guard.Param("@rule", B, "RuleVersion", ruleVersion, bw));
            batchCmd.Parameters.Add(guard.Param("@xp", B, "SourceExcelPath", f.StructuredExcelPath, bw));
            batchCmd.Parameters.Add(guard.Param("@xh", B, "SourceExcelHash", HexOrNull(f.StructuredExcel), bw));
            batchCmd.Parameters.Add(guard.Param("@jp", B, "StructuredJsonPath", f.StructuredJsonPath, bw));
            batchCmd.Parameters.Add(guard.Param("@jh", B, "StructuredJsonHash", DocumentRepository.Hex(f.StructuredJson.Hash), bw));
            batchCmd.Parameters.Add(guard.Param("@rp", B, "RawJsonPath", f.RawJsonPath, bw));
            batchCmd.Parameters.Add(guard.Param("@rh", B, "RawJsonHash", HexOrNull(f.RawJson), bw));
            batchCmd.Parameters.Add(guard.Param("@pp", B, "PdfPath", f.PdfPath, bw));
            batchCmd.Parameters.Add(guard.Param("@ph", B, "PdfHash", HexOrNull(f.Pdf), bw));
            batchCmd.Parameters.Add(guard.Param("@st", B, "ImportStatus", "Imported", bw));
            batchCmd.Parameters.Add(guard.Param("@by", B, "ImportedBy", EstimateDb.Operator, bw));
            batchCmd.Parameters.Add(guard.Param("@cr", B, "CommonRulesFile", Null(p.Metadata.CommonRulesFile), bw));
            batchCmd.Parameters.Add(guard.Param("@sr", B, "SupplierRulesFile", Null(p.Metadata.SupplierRulesFile), bw));

            // ── 見積ヘッダ ──
            const string H = EstimateDb.Header;
            const string hw = "見積ヘッダ";
            await using var headerCmd = new SqlCommand(
                $"""
                INSERT INTO {EstimateDb.T(H)}
                    (OcrImportBatch_ID, ProjectName_OCR, ProjectName_Normalized, SupplierName_OCR, Estimate_ID_OCR,
                     HeaderEstimateAmount, DetailAmountTotal, DifferenceToHeaderAmount, ReviewStatus, IsTestData, CreatedAt, UpdatedAt)
                OUTPUT INSERTED.OcrEstimateHeader_ID
                VALUES (@b, @pn, @pnn, @sn, @eid, @hea, @dat, @dif, @rs, @test, SYSUTCDATETIME(), SYSUTCDATETIME())
                """, conn, tx);
            headerCmd.Parameters.Add(guard.Param("@b", H, "OcrImportBatch_ID", 0, hw));   // 取込記録を作った後に差し替える
            headerCmd.Parameters.Add(guard.Param("@pn", H, "ProjectName_OCR", Null(p.Metadata.ProjectName), hw));
            headerCmd.Parameters.Add(guard.Param("@pnn", H, "ProjectName_Normalized", Null(NameMatcher.Normalize(p.Metadata.ProjectName)), hw));
            headerCmd.Parameters.Add(guard.Param("@sn", H, "SupplierName_OCR", Null(p.Metadata.SupplierName), hw));
            headerCmd.Parameters.Add(guard.Param("@eid", H, "Estimate_ID_OCR", Null(p.Metadata.EstimateId), hw));
            headerCmd.Parameters.Add(guard.Param("@hea", H, "HeaderEstimateAmount", p.Summary.HeaderEstimateAmount, hw));
            headerCmd.Parameters.Add(guard.Param("@dat", H, "DetailAmountTotal", p.Summary.DetailAmountTotal, hw));
            headerCmd.Parameters.Add(guard.Param("@dif", H, "DifferenceToHeaderAmount", p.Summary.DifferenceToHeaderAmount, hw));
            headerCmd.Parameters.Add(guard.Param("@rs", H, "ReviewStatus", EstimateDb.ReviewPending, hw));
            headerCmd.Parameters.Add(guard.Param("@test", H, "IsTestData", isTestData, hw));

            // ── 明細・行監査（先に全行を検査し、1件でも収まらなければ何も保存しない）──
            var lineCmds = p.Items.Select((it, i) => LineInsert(conn, tx, guard, it, i + 1)).ToList();
            var auditCmds = p.AuditRows.Select((a, i) => AuditInsert(conn, tx, guard, a, i + 1)).ToList();
            guard.ThrowIfIssues("取込");

            var batch = Convert.ToInt32(await batchCmd.ExecuteScalarAsync(ct));
            headerCmd.Parameters["@b"].Value = batch;
            var header = Convert.ToInt32(await headerCmd.ExecuteScalarAsync(ct));
            foreach (var cmd in lineCmds.Concat(auditCmds))
            {
                cmd.Parameters["@h"].Value = header;
                await cmd.ExecuteNonQueryAsync(ct);
                await cmd.DisposeAsync();
            }

            // ── 資料本体 ──
            foreach (var doc in new[] { f.StructuredJson, f.StructuredExcel, f.RawJson, f.Pdf })
            {
                if (doc is null) continue;
                if (await _docs.AddIfMissingAsync(conn, tx, guard, batch, doc, ct)) added.Add($"{DocLabel(doc.Type)}（{doc.FileName}）");
            }
            if (f.Pdf is null) warnings.Add("元PDFが未登録です。下書き・確認はできますが、取決めの確定には元PDFの登録が必要です。");
            if (f.StructuredExcel is null) warnings.Add("構造化Excelが見つからなかったため、Excel は登録していません。");

            await tx.CommitAsync(ct);
            return new ImportResult(header, false, added, warnings);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // 同時に同じファイルを取り込んだ（取込識別キーの一意索引）→ 先に登録された方を返す
            await SafeRollbackAsync(tx);
            await using var again = await OpenAsync(ct);
            if (await FindByIdentityAsync(again, null, identityKey, ct) is int id) return new ImportResult(id, true, new(), new());
            throw;
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    private static SqlCommand LineInsert(SqlConnection conn, SqlTransaction tx, ColumnGuard g, ImportItem it, int n)
    {
        const string L = EstimateDb.Line;
        var w = $"明細 {n} 行目（{(it.ItemName.Length > 20 ? it.ItemName[..20] + "…" : it.ItemName)}）";
        var isTarget = it.RecordType == "金額明細" && it.Amount is not null;
        var exclusion = isTarget ? null : it.RecordType == "金額明細" ? "金額がありません" : $"{(it.RecordType.Length > 0 ? it.RecordType : "参考明細")}（計算対象外）";
        var key = NameMatcher.Normalize($"{it.ItemName}|{it.Material}|{it.Dimension}|{it.Unit}");
        var keyMax = g.Column(L, "Line_Normalized_Key").MaxLength;
        if (keyMax is int max && max > 0 && key.Length > max) key = key[..max];   // 照合用に作る派生キーなので長さに合わせる（OCR原値ではない）

        var cmd = new SqlCommand(
            $"""
            INSERT INTO {EstimateDb.T(L)}
                (OcrEstimateHeader_ID, Record_Type, PdfPage, OcrTableIndex, OcrRowIndex, Trade, Section, ItemName, MaterialSpec, DimensionSpec,
                 Quantity, Unit, UnitPrice, Amount, Remarks_OCR, CalculatedAmount, Difference, AmountCheck, DuplicateCandidate,
                 ReviewRequired, ReviewReason, Line_Normalized_Key, HumanReviewStatus, IsCalculationTarget, ExclusionReason, CreatedAt, UpdatedAt)
            VALUES (@h, @rt, @pg, @ti, @ri, @tr, @se, @nm, @ma, @di, @q, @u, @up, @am, @re, @ca, @df, @ac, @dc, @rq, @rr, @key, @hrs, @tg, @ex,
                    SYSUTCDATETIME(), SYSUTCDATETIME())
            """, conn, tx);
        cmd.Parameters.Add(new SqlParameter("@h", System.Data.SqlDbType.Int) { Value = 0 });
        cmd.Parameters.Add(g.Param("@rt", L, "Record_Type", Null(it.RecordType), w));
        cmd.Parameters.Add(g.Param("@pg", L, "PdfPage", it.PdfPage, w));
        cmd.Parameters.Add(g.Param("@ti", L, "OcrTableIndex", it.OcrTableIndex, w));
        cmd.Parameters.Add(g.Param("@ri", L, "OcrRowIndex", it.OcrRowIndex, w));
        cmd.Parameters.Add(g.Param("@tr", L, "Trade", Null(it.Trade), w));
        cmd.Parameters.Add(g.Param("@se", L, "Section", Null(it.Section), w));
        cmd.Parameters.Add(g.Param("@nm", L, "ItemName", it.ItemName, w));
        cmd.Parameters.Add(g.Param("@ma", L, "MaterialSpec", Null(it.Material), w));
        cmd.Parameters.Add(g.Param("@di", L, "DimensionSpec", Null(it.Dimension), w));
        cmd.Parameters.Add(g.Param("@q", L, "Quantity", it.Quantity, w));
        cmd.Parameters.Add(g.Param("@u", L, "Unit", Null(it.Unit), w));
        cmd.Parameters.Add(g.Param("@up", L, "UnitPrice", it.UnitPrice, w));
        cmd.Parameters.Add(g.Param("@am", L, "Amount", it.Amount, w));
        cmd.Parameters.Add(g.Param("@re", L, "Remarks_OCR", Null(it.RemarksOcr), w));
        cmd.Parameters.Add(g.Param("@ca", L, "CalculatedAmount", it.CalculatedAmount, w));
        cmd.Parameters.Add(g.Param("@df", L, "Difference", it.Difference, w));
        cmd.Parameters.Add(g.Param("@ac", L, "AmountCheck", Null(it.AmountCheck), w));
        cmd.Parameters.Add(g.Param("@dc", L, "DuplicateCandidate", Null(it.DuplicateCandidate), w));
        cmd.Parameters.Add(g.Param("@rq", L, "ReviewRequired", Null(it.ReviewRequired), w));
        cmd.Parameters.Add(g.Param("@rr", L, "ReviewReason", Null(it.ReviewReason), w));
        cmd.Parameters.Add(g.Param("@key", L, "Line_Normalized_Key", Null(key), w));
        cmd.Parameters.Add(g.Param("@hrs", L, "HumanReviewStatus", EstimateDb.ReviewPending, w));
        cmd.Parameters.Add(g.Param("@tg", L, "IsCalculationTarget", isTarget, w));
        cmd.Parameters.Add(g.Param("@ex", L, "ExclusionReason", exclusion, w));
        return cmd;
    }

    private static SqlCommand AuditInsert(SqlConnection conn, SqlTransaction tx, ColumnGuard g, ImportAuditRow a, int n)
    {
        const string A = EstimateDb.Audit;
        var w = $"OCR行監査 {n} 行目";
        var cmd = new SqlCommand(
            $"""
            INSERT INTO {EstimateDb.T(A)} (OcrEstimateHeader_ID, PdfPage, OcrTableIndex, OcrRowIndex, RowStatus, Reason, RowText)
            VALUES (@h, @pg, @ti, @ri, @st, @rs, @tx)
            """, conn, tx);
        cmd.Parameters.Add(new SqlParameter("@h", System.Data.SqlDbType.Int) { Value = 0 });
        cmd.Parameters.Add(g.Param("@pg", A, "PdfPage", a.PdfPage, w));
        cmd.Parameters.Add(g.Param("@ti", A, "OcrTableIndex", a.OcrTableIndex, w));
        cmd.Parameters.Add(g.Param("@ri", A, "OcrRowIndex", a.OcrRowIndex, w));
        cmd.Parameters.Add(g.Param("@st", A, "RowStatus", Null(a.RowStatus), w));
        cmd.Parameters.Add(g.Param("@rs", A, "Reason", Null(a.Reason), w));
        cmd.Parameters.Add(g.Param("@tx", A, "RowText", Null(a.RowText), w));
        return cmd;
    }

    /// <summary>後から元PDFを追加する（同じ見積の取込に属する資料として）。</summary>
    public async Task<bool> AddDocumentAsync(int headerId, DocumentRepository.StoredFile file, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var guard = await ColumnGuard.LoadAsync(conn, tx, new[] { EstimateDb.Document }, ct);
            var batch = await BatchOfAsync(conn, tx, headerId, ct);
            var added = await _docs.AddIfMissingAsync(conn, tx, guard, batch, file, ct);
            await tx.CommitAsync(ct);
            return added;
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    private static async Task<int> BatchOfAsync(SqlConnection conn, SqlTransaction? tx, int headerId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT OcrImportBatch_ID FROM {EstimateDb.T(EstimateDb.Header)} WHERE OcrEstimateHeader_ID = @id", conn, tx);
        cmd.Parameters.AddWithValue("@id", headerId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? throw new InvalidOperationException($"見積 #{headerId} の取込記録が見つかりません。") : Convert.ToInt32(v);
    }

    // ================================================================
    // 一覧・詳細
    // ================================================================

    public async Task<List<EstimateListItem>> ListAsync(EstimateListQuery q, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var stateWhere = q.State switch
        {
            "unlinked" => "AND (h.Project_ID IS NULL OR h.Supplier_ID IS NULL)",
            "draft" => "AND (a.Status IS NULL OR a.Status = N'Draft')",
            "confirmed" => "AND a.Status = N'Confirmed'",
            _ => "",
        };
        await using var cmd = new SqlCommand(
            $"""
            SELECT TOP (@limit) h.OcrEstimateHeader_ID, b.ImportedAt, b.ImportedBy, h.SupplierName_OCR, h.ProjectName_OCR,
                   h.Project_ID, p.ProjectName, h.Supplier_ID, s.Supplier_Code, s.Supplier_Name_Official,
                   (SELECT COUNT(*) FROM {EstimateDb.T(EstimateDb.Line)} l WHERE l.OcrEstimateHeader_ID = h.OcrEstimateHeader_ID),
                   h.DetailAmountTotal, a.Status, a.RevisionNo, h.IsTestData,
                   CASE WHEN EXISTS (SELECT 1 FROM {EstimateDb.T(EstimateDb.Document)} d
                                     WHERE d.OcrImportBatch_ID = h.OcrImportBatch_ID AND d.DocumentType = N'{EstimateDb.DocOriginalPdf}') THEN 1 ELSE 0 END
            FROM {EstimateDb.T(EstimateDb.Header)} h
            LEFT JOIN {EstimateDb.T(EstimateDb.ImportBatch)} b ON b.OcrImportBatch_ID = h.OcrImportBatch_ID
            LEFT JOIN {Projects} p ON p.Project_id = h.Project_ID
            LEFT JOIN {EstimateDb.T(EstimateDb.Supplier)} s ON s.Supplier_ID = h.Supplier_ID
            OUTER APPLY (SELECT TOP 1 x.Status, x.RevisionNo FROM {EstimateDb.T(EstimateDb.Agreement)} x
                         WHERE x.OcrEstimateHeader_ID = h.OcrEstimateHeader_ID ORDER BY x.RevisionNo DESC) a
            WHERE (@kw IS NULL OR h.SupplierName_OCR LIKE @kw OR h.ProjectName_OCR LIKE @kw OR p.ProjectName LIKE @kw
                   OR s.Supplier_Name_Official LIKE @kw OR h.Project_ID LIKE @kw OR s.Supplier_Code LIKE @kw)
              AND (@test = 1 OR h.IsTestData = 0)
              {stateWhere}
            ORDER BY h.OcrEstimateHeader_ID DESC
            """, conn);
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(q.Limit, 1, 2000));
        cmd.Parameters.Add(new SqlParameter("@kw", System.Data.SqlDbType.NVarChar, 400)
        {
            Value = string.IsNullOrWhiteSpace(q.Keyword) ? DBNull.Value : (object)$"%{EscapeLike(q.Keyword.Trim())}%",
        });
        cmd.Parameters.AddWithValue("@test", q.IncludeTest);

        var list = new List<EstimateListItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var status = S(r, 12);
            int? rev = r.IsDBNull(13) ? null : r.GetInt32(13);
            var linked = !r.IsDBNull(5) && !r.IsDBNull(7);
            var state = status switch
            {
                EstimateDb.Confirmed => $"確定 第{rev}版",
                EstimateDb.Draft => $"下書き 第{rev}版",
                _ => linked ? "未確定" : "未紐づけ",
            };
            list.Add(new EstimateListItem(Id(r, 0), Utc(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6),
                I(r, 7), S(r, 8), S(r, 9), Convert.ToInt32(r.GetValue(10)), D(r, 11), state, rev,
                !r.IsDBNull(14) && r.GetBoolean(14), r.GetInt32(15) == 1));
        }
        return list;
    }

    public async Task<EstimateDetail> DetailAsync(int headerId, AgreementRepository agreements, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var header = await HeaderAsync(conn, null, headerId, ct);
        var lines = await LinesAsync(conn, null, headerId, ct);
        var audit = await AuditAsync(conn, headerId, ct);
        var docs = await _docs.ListAsync(conn, headerId, ct);
        var agreementList = await agreements.ListAsync(conn, headerId, ct);
        var locked = agreementList.Count > 0 && agreementList[0].Status == EstimateDb.Confirmed;
        return new EstimateDetail(header, lines, audit, docs, agreementList, locked, EstimateDb.Operator);
    }

    public async Task<HeaderView> HeaderAsync(SqlConnection conn, SqlTransaction? tx, int headerId, CancellationToken ct, bool forUpdate = false)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT h.OcrEstimateHeader_ID, h.OcrImportBatch_ID, h.Project_ID, p.ProjectName,
                   CONCAT(p.SiteAddress_State_Province, p.SiteAddress_City_Town_Village, p.SiteAddress_StreetName),
                   h.Supplier_ID, s.Supplier_Code, s.Supplier_Name_Official, s.HeadOffice_Address,
                   h.ProjectName_OCR, h.SupplierName_OCR, h.Estimate_ID_OCR, h.EstimateDate,
                   h.HeaderEstimateAmount, h.DetailAmountTotal, h.DifferenceToHeaderAmount, h.ReviewStatus, h.IsTestData,
                   b.ImportedAt, b.ImportedBy, b.CommonRulesFile, b.SupplierRulesFile, h.RowVersion
            FROM {EstimateDb.T(EstimateDb.Header)} h {(forUpdate ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
            LEFT JOIN {EstimateDb.T(EstimateDb.ImportBatch)} b ON b.OcrImportBatch_ID = h.OcrImportBatch_ID
            LEFT JOIN {Projects} p ON p.Project_id = h.Project_ID
            LEFT JOIN {EstimateDb.T(EstimateDb.Supplier)} s ON s.Supplier_ID = h.Supplier_ID
            WHERE h.OcrEstimateHeader_ID = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("@id", headerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new InvalidOperationException($"見積 #{headerId} が見つかりません（削除された可能性があります）。");
        return new HeaderView(
            Id(r, 0), I(r, 1), S(r, 2), S(r, 3), Null(S(r, 4)),
            I(r, 5), S(r, 6), S(r, 7), S(r, 8),
            S(r, 9), S(r, 10), S(r, 11), r.IsDBNull(12) ? null : r.GetDateTime(12),
            D(r, 13), D(r, 14), D(r, 15), S(r, 16), !r.IsDBNull(17) && r.GetBoolean(17),
            Utc(r, 18), S(r, 19), S(r, 20), S(r, 21), Convert.ToBase64String((byte[])r[22]));
    }

    public async Task<List<LineView>> LinesAsync(SqlConnection conn, SqlTransaction? tx, int headerId, CancellationToken ct, bool forUpdate = false)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT OcrEstimateLine_ID, Record_Type, PdfPage, OcrTableIndex, OcrRowIndex, Trade, Section, ItemName, MaterialSpec, DimensionSpec,
                   Quantity, Unit, UnitPrice, Amount, Remarks_OCR, CalculatedAmount, Difference, AmountCheck, DuplicateCandidate,
                   ReviewRequired, ReviewReason, HumanReviewStatus, HumanReviewNotes, ReviewedBy, ReviewedAt, IsCalculationTarget, ExclusionReason,
                   CorrectedQuantity, CorrectedUnit, CorrectedUnitPrice, CorrectedAmount, CorrectionReason, RowVersion
            FROM {EstimateDb.T(EstimateDb.Line)} {(forUpdate ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
            WHERE OcrEstimateHeader_ID = @id
            ORDER BY OcrEstimateLine_ID
            """, conn, tx);
        cmd.Parameters.AddWithValue("@id", headerId);
        var list = new List<LineView>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var order = 0;
        while (await r.ReadAsync(ct))
        {
            list.Add(new LineView(
                Id(r, 0), ++order, S(r, 1), I(r, 2), I(r, 3), I(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9),
                D(r, 10), S(r, 11), D(r, 12), D(r, 13), S(r, 14), D(r, 15), D(r, 16), S(r, 17), S(r, 18),
                S(r, 19), S(r, 20), S(r, 21), S(r, 22), S(r, 23), Utc(r, 24), r.IsDBNull(25) ? null : r.GetBoolean(25), S(r, 26),
                D(r, 27), S(r, 28), D(r, 29), D(r, 30), S(r, 31), Convert.ToBase64String((byte[])r[32])));
        }
        return list;
    }

    private static async Task<List<AuditView>> AuditAsync(SqlConnection conn, int headerId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT PdfPage, OcrTableIndex, OcrRowIndex, RowStatus, Reason, RowText
            FROM {EstimateDb.T(EstimateDb.Audit)} WHERE OcrEstimateHeader_ID = @id ORDER BY OcrAuditRow_ID
            """, conn);
        cmd.Parameters.AddWithValue("@id", headerId);
        var list = new List<AuditView>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new AuditView(I(r, 0), I(r, 1), I(r, 2), S(r, 3), S(r, 4), S(r, 5)));
        return list;
    }

    // ================================================================
    // 紐づけ候補
    // ================================================================

    /// <summary>物件の候補（キーワード、無ければ OCR の工事名で探す）。</summary>
    public async Task<List<ProjectCandidate>> ProjectCandidatesAsync(string? keyword, string? ocrName, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"""
            SELECT Project_id, ProjectName,
                   CONCAT(SiteAddress_State_Province, SiteAddress_City_Town_Village, SiteAddress_StreetName), Structure
            FROM {Projects}
            """, conn);
        var all = new List<(string Id, string? Name, string? Address, string? Structure)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) all.Add((Convert.ToString(r.GetValue(0)) ?? "", S(r, 1), Null(S(r, 2)), S(r, 3)));
        }
        var query = string.IsNullOrWhiteSpace(keyword) ? ocrName : keyword;
        return NameMatcher.Rank(all, x => new[] { x.Name, x.Id, x.Address }, query, !string.IsNullOrWhiteSpace(keyword))
            .Take(50)
            .Select(m => new ProjectCandidate(m.Item.Id, m.Item.Name, m.Item.Address, m.Item.Structure, m.Match))
            .ToList();
    }

    /// <summary>業者の候補（取引先CD・正式名・カナで探す。名前だけで同一とは決めない）。</summary>
    public async Task<List<SupplierCandidate>> SupplierCandidatesAsync(string? keyword, string? ocrName, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"""
            SELECT Supplier_ID, Supplier_Code, Supplier_Name_Official, Supplier_Name_Kana, HeadOffice_Address, Phone_Number, Transaction_Department
            FROM {EstimateDb.T(EstimateDb.Supplier)}
            """, conn);
        var all = new List<SupplierCandidate>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                all.Add(new SupplierCandidate(Id(r, 0), S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), ""));
        }
        var query = string.IsNullOrWhiteSpace(keyword) ? ocrName : keyword;
        return NameMatcher.Rank(all, x => new[] { x.Name, x.Code, x.Kana }, query, !string.IsNullOrWhiteSpace(keyword))
            .Take(50)
            .Select(m => m.Item with { Match = m.Match })
            .ToList();
    }

    // ================================================================
    // 紐づけ・確認の保存
    // ================================================================

    /// <summary>最新の取決めが確定済みなら true（確定済みの見積は「新しい版で訂正」してから変更する）。</summary>
    public static async Task<bool> IsLockedAsync(SqlConnection conn, SqlTransaction? tx, int headerId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"SELECT TOP 1 Status FROM {EstimateDb.T(EstimateDb.Agreement)} WHERE OcrEstimateHeader_ID = @id ORDER BY RevisionNo DESC", conn, tx);
        cmd.Parameters.AddWithValue("@id", headerId);
        return (await cmd.ExecuteScalarAsync(ct)) as string == EstimateDb.Confirmed;
    }

    public async Task<string> SaveLinkAsync(LinkRequest req, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (await IsLockedAsync(conn, tx, req.HeaderId, ct))
                throw new InvalidOperationException("取決めが確定済みのため、紐づけは変更できません。変更する場合は「新しい版で訂正」を押してください。");
            var guard = await ColumnGuard.LoadAsync(conn, tx, new[] { EstimateDb.Header }, ct);

            if (req.ProjectId is not null && !await ExistsAsync(conn, tx, $"SELECT COUNT(*) FROM {Projects} WHERE Project_id = @v", req.ProjectId, ct))
                throw new InvalidOperationException($"物件 {req.ProjectId} が物件情報にありません。");
            if (req.SupplierId is not null && !await ExistsAsync(conn, tx, $"SELECT COUNT(*) FROM {EstimateDb.T(EstimateDb.Supplier)} WHERE Supplier_ID = @v", req.SupplierId, ct))
                throw new InvalidOperationException($"業者（ID {req.SupplierId}）が協力業者マスタにありません。");

            const string H = EstimateDb.Header;
            await using var cmd = new SqlCommand(
                $"""
                UPDATE {EstimateDb.T(H)}
                SET Project_ID = @p, Supplier_ID = @s, ReviewedBy = @by, ReviewedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
                OUTPUT INSERTED.RowVersion
                WHERE OcrEstimateHeader_ID = @id AND RowVersion = @rv
                """, conn, tx);
            cmd.Parameters.Add(guard.Param("@p", H, "Project_ID", req.ProjectId, "紐づけ"));
            cmd.Parameters.Add(guard.Param("@s", H, "Supplier_ID", req.SupplierId, "紐づけ"));
            cmd.Parameters.Add(guard.Param("@by", H, "ReviewedBy", EstimateDb.Operator, "紐づけ"));
            cmd.Parameters.AddWithValue("@id", req.HeaderId);
            cmd.Parameters.Add(RowVersionParam("@rv", req.RowVersion));
            guard.ThrowIfIssues("紐づけの保存");
            var rv = await cmd.ExecuteScalarAsync(ct) as byte[] ?? throw Conflict();
            await tx.CommitAsync(ct);
            return Convert.ToBase64String(rv);
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    public async Task<LineView> SaveLineReviewAsync(int headerId, LineReviewRequest req, CancellationToken ct = default)
    {
        if (!EstimateDb.ReviewStatuses.Contains(req.HumanReviewStatus))
            throw new InvalidOperationException($"確認状態が正しくありません: {req.HumanReviewStatus}");
        var excluded = req.HumanReviewStatus == EstimateDb.ReviewExcluded || !req.IsCalculationTarget;
        if (excluded && string.IsNullOrWhiteSpace(req.ExclusionReason))
            throw new InvalidOperationException("計算対象外にする場合は、理由を入力してください。");
        var corrected = req.CorrectedQuantity is not null || !string.IsNullOrWhiteSpace(req.CorrectedUnit) || req.CorrectedUnitPrice is not null || req.CorrectedAmount is not null;
        if (corrected && string.IsNullOrWhiteSpace(req.CorrectionReason))
            throw new InvalidOperationException("補正値を入れた場合は、補正の理由を入力してください。");

        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (await IsLockedAsync(conn, tx, headerId, ct))
                throw new InvalidOperationException("取決めが確定済みのため、明細の確認結果は変更できません。変更する場合は「新しい版で訂正」を押してください。");
            var guard = await ColumnGuard.LoadAsync(conn, tx, new[] { EstimateDb.Line }, ct);
            const string L = EstimateDb.Line;
            const string w = "明細の確認";
            await using var cmd = new SqlCommand(
                $"""
                UPDATE {EstimateDb.T(L)}
                SET HumanReviewStatus = @st, HumanReviewNotes = @nt, ReviewedBy = @by, ReviewedAt = SYSUTCDATETIME(),
                    IsCalculationTarget = @tg, ExclusionReason = @ex,
                    CorrectedQuantity = @cq, CorrectedUnit = @cu, CorrectedUnitPrice = @cp, CorrectedAmount = @ca, CorrectionReason = @cr,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE OcrEstimateLine_ID = @id AND OcrEstimateHeader_ID = @h AND RowVersion = @rv
                """, conn, tx);
            cmd.Parameters.Add(guard.Param("@st", L, "HumanReviewStatus", req.HumanReviewStatus, w));
            cmd.Parameters.Add(guard.Param("@nt", L, "HumanReviewNotes", Null(req.HumanReviewNotes?.Trim()), w));
            cmd.Parameters.Add(guard.Param("@by", L, "ReviewedBy", EstimateDb.Operator, w));
            cmd.Parameters.Add(guard.Param("@tg", L, "IsCalculationTarget", !excluded, w));
            cmd.Parameters.Add(guard.Param("@ex", L, "ExclusionReason", excluded ? req.ExclusionReason!.Trim() : null, w));
            cmd.Parameters.Add(guard.Param("@cq", L, "CorrectedQuantity", req.CorrectedQuantity, w));
            cmd.Parameters.Add(guard.Param("@cu", L, "CorrectedUnit", Null(req.CorrectedUnit?.Trim()), w));
            cmd.Parameters.Add(guard.Param("@cp", L, "CorrectedUnitPrice", req.CorrectedUnitPrice, w));
            cmd.Parameters.Add(guard.Param("@ca", L, "CorrectedAmount", req.CorrectedAmount, w));
            cmd.Parameters.Add(guard.Param("@cr", L, "CorrectionReason", corrected ? req.CorrectionReason!.Trim() : null, w));
            cmd.Parameters.AddWithValue("@id", req.LineId);
            cmd.Parameters.AddWithValue("@h", headerId);
            cmd.Parameters.Add(RowVersionParam("@rv", req.RowVersion));
            guard.ThrowIfIssues("明細の確認の保存");
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw Conflict();
            var line = (await LinesAsync(conn, tx, headerId, ct)).First(l => l.LineId == req.LineId);
            await tx.CommitAsync(ct);
            return line;
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    // ================================================================
    // 共通
    // ================================================================

    public static InvalidOperationException Conflict() =>
        new("他の人（または別の画面）が先に変更したため、保存しませんでした。「⟳ 再読込」で最新の内容を読み込んでから、もう一度操作してください。");

    public static SqlParameter RowVersionParam(string name, string base64)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
        }
        return new SqlParameter(name, System.Data.SqlDbType.Binary, 8) { Value = bytes };
    }

    private static async Task<bool> ExistsAsync(SqlConnection conn, SqlTransaction tx, string sql, object value, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@v", value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public static async Task SafeRollbackAsync(SqlTransaction tx)
    {
        try
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // 接続が切れているなど。サーバー側でロールバックされる
        }
    }

    private static string DocLabel(string type) => type switch
    {
        EstimateDb.DocOriginalPdf => "元PDF",
        EstimateDb.DocStructuredExcel => "構造化Excel",
        EstimateDb.DocStructuredJson => "構造化JSON",
        EstimateDb.DocRawJson => "統合OCR JSON",
        _ => type,
    };

    private static string? HexOrNull(DocumentRepository.StoredFile? f) => f is null ? null : DocumentRepository.Hex(f.Hash);

    private static string EscapeLike(string s) => s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    internal static string? Null(string? s) => string.IsNullOrEmpty(s) ? null : s;

    internal static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));

    internal static decimal? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));

    internal static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

    /// <summary>ID 列（実DB で int でも bigint でも読めるように）</summary>
    internal static int Id(SqlDataReader r, int i) => Convert.ToInt32(r.GetValue(i));

    internal static DateTime? Utc(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
}

/// <summary>
/// 名称の照合（候補の検索用）。NFKC で全角・半角をそろえ、空白を除くだけ。
/// 法人格などは削らない（別会社を同じとみなさないため）。候補が 1 件でも自動では決めず、人が選ぶ。
/// </summary>
public static class NameMatcher
{
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var n = s.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(n.Length);
        foreach (var c in n)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    public sealed record Ranked<T>(T Item, string Match, int Score);

    /// <summary>
    /// 候補を並べる: 正規化して一致（3）→ 一方が他方を含む（2）→ キーワード検索のときはその語を含む（1）。
    /// 照合の語が空なら何も返さない（全件は出さない）。
    /// </summary>
    public static IEnumerable<Ranked<T>> Rank<T>(IEnumerable<T> items, Func<T, IEnumerable<string?>> fields, string? query, bool isKeyword)
    {
        var q = Normalize(query);
        if (q.Length == 0) return Enumerable.Empty<Ranked<T>>();
        var ranked = new List<Ranked<T>>();
        foreach (var item in items)
        {
            var best = 0;
            foreach (var f in fields(item))
            {
                var n = Normalize(f);
                if (n.Length == 0) continue;
                var score = n == q ? 3
                    : (q.Length >= 3 && n.Contains(q, StringComparison.OrdinalIgnoreCase)) || (n.Length >= 3 && q.Contains(n, StringComparison.OrdinalIgnoreCase)) ? 2
                    : isKeyword && n.Contains(q, StringComparison.OrdinalIgnoreCase) ? 1
                    : 0;
                best = Math.Max(best, score);
            }
            if (best > 0) ranked.Add(new Ranked<T>(item, best switch { 3 => "正規化一致", 2 => "部分一致", _ => "検索語を含む" }, best));
        }
        return ranked.OrderByDescending(x => x.Score);
    }
}
