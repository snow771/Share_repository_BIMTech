using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Estimate.Agreement;
using SimilaritySearchApp.Estimate.Models;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Ocr.Pdf;
using static SimilaritySearchApp.Estimate.Data.EstimateRepository;

namespace SimilaritySearchApp.Estimate.Data;

/// <summary>
/// 取決め（Agreement_取決め情報・AgreementLine_取決め明細）の保存。
///   下書き保存 … 入力だけを保存（明細は作らない）。確定済みの版がある間は作れない
///   確定       … C# で再計算・確定条件を再検証し、前の確定版を Superseded に、この版を Confirmed に、全明細の取決め明細を
///                1 トランザクションで保存する。確定要求キー（ConfirmRequestKey）で通信再送・二重クリックを 1 回にまとめる
///   新しい版で訂正 … 確定済みの版はそのまま残し、次の版番号の下書きを作る（確定するまで旧版が有効）
/// </summary>
public sealed class AgreementRepository
{
    private readonly EstimateRepository _estimates;
    private readonly DocumentRepository _docs = new();

    public AgreementRepository(EstimateRepository estimates) => _estimates = estimates;

    // ── 照会 ──

    public async Task<List<AgreementView>> ListAsync(SqlConnection conn, int headerId, CancellationToken ct, SqlTransaction? tx = null)
    {
        var oh = await OverheadColumnsAsync(conn, tx, ct);
        await using var cmd = new SqlCommand(
            $"""
            SELECT Agreement_ID, RevisionNo, Status, PreviousAgreement_ID, BaseEstimateAmount, BaseAmountSource, BaseAmountReason,
                   AgreedAmount, TaxBasis, AppliedRate, AgreementDate, EvidenceDocument_ID, EvidencePage,
                   ConvertedDetailTotal, ResidualAmount, ConfirmedBy, ConfirmedAt, Notes, ChangeReason, RowVersion
                   {(oh.Agreement ? ", OverheadSource, OverheadAmount, OverheadRate, OverheadLineIds, OverheadItemsTotal" : "")}
            FROM {EstimateDb.T(EstimateDb.Agreement)} WHERE OcrEstimateHeader_ID = @id ORDER BY RevisionNo DESC
            """, conn, tx);
        cmd.Parameters.AddWithValue("@id", headerId);
        var list = new List<AgreementView>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                list.Add(new AgreementView(
                    Convert.ToInt64(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), r.GetString(2), r.IsDBNull(3) ? null : Convert.ToInt64(r.GetValue(3)),
                    D(r, 4), S(r, 5), S(r, 6), D(r, 7), S(r, 8), D(r, 9), r.IsDBNull(10) ? null : r.GetDateTime(10),
                    r.IsDBNull(11) ? null : Convert.ToInt64(r.GetValue(11)), I(r, 12), D(r, 13), D(r, 14), S(r, 15), Utc(r, 16),
                    S(r, 17), S(r, 18), Convert.ToBase64String((byte[])r[19]), new List<AgreementLineView>())
                {
                    OverheadSource = oh.Agreement ? S(r, 20) : null,
                    OverheadAmount = oh.Agreement ? D(r, 21) : null,
                    OverheadRate = oh.Agreement ? D(r, 22) : null,
                    OverheadLineIds = oh.Agreement ? AgreementCalculator.SplitIds(S(r, 23)) : new List<int>(),
                    OverheadItemsTotal = oh.Agreement ? D(r, 24) : null,
                });
            }
        }
        foreach (var a in list.Where(a => a.Status != EstimateDb.Draft)) a.Lines.AddRange(await LinesAsync(conn, tx, a.AgreementId, oh.Line, ct));
        return list;
    }

    private static async Task<List<AgreementLineView>> LinesAsync(SqlConnection conn, SqlTransaction? tx, long agreementId, bool overheadColumns, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT AgreementLine_ID, OcrEstimateLine_ID, LineOrder, RecordType, ItemName, MaterialSpec, DimensionSpec, Quantity, Unit,
                   OriginalUnitPrice, OriginalAmount, AppliedRate, ConvertedUnitPrice, ConvertedAmount, IsCalculationTarget, ExclusionReason, CalculationType
                   {(overheadColumns ? ", OverheadIncludedUnitPrice, OverheadIncludedAmount" : "")}
            FROM {EstimateDb.T(EstimateDb.AgreementLine)} WHERE Agreement_ID = @id ORDER BY LineOrder
            """, conn, tx);
        cmd.Parameters.AddWithValue("@id", agreementId);
        var list = new List<AgreementLineView>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new AgreementLineView(Convert.ToInt64(r.GetValue(0)), Id(r, 1), Convert.ToInt32(r.GetValue(2)), r.GetString(3), r.GetString(4), S(r, 5), S(r, 6),
                D(r, 7), S(r, 8), D(r, 9), D(r, 10), D(r, 11), D(r, 12), D(r, 13), r.GetBoolean(14), S(r, 15), r.GetString(16))
            {
                OverheadIncludedUnitPrice = overheadColumns ? D(r, 17) : null,
                OverheadIncludedAmount = overheadColumns ? D(r, 18) : null,
            });
        }
        return list;
    }

    /// <summary>諸経費の列（OCR見積取決め_諸経費列追加.sql で追加）があるか。</summary>
    public static async Task<(bool Agreement, bool Line)> OverheadColumnsAsync(SqlConnection conn, SqlTransaction? tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            """
            SELECT
              (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@a)
                 AND name IN (N'OverheadSource', N'OverheadAmount', N'OverheadRate', N'OverheadLineIds', N'OverheadItemsTotal')),
              (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@l)
                 AND name IN (N'OverheadIncludedUnitPrice', N'OverheadIncludedAmount'))
            """, conn, tx);
        cmd.Parameters.AddWithValue("@a", $"{EstimateDb.Schema}.{EstimateDb.Agreement}");
        cmd.Parameters.AddWithValue("@l", $"{EstimateDb.Schema}.{EstimateDb.AgreementLine}");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetInt32(0) == 5, r.GetInt32(1) == 2);
    }

    public const string MissingOverheadColumns =
        "諸経費を保存する列が DB にありません。docs/2026-10-13_OCR見積DB連携 の「OCR見積取決め_諸経費列追加.sql」を SSMS で実行してください（諸経費なしの取決めはこのままでも保存できます）。";

    private static async Task EnsureOverheadColumnsAsync(SqlConnection conn, SqlTransaction? tx, AgreementInput input, CancellationToken ct)
    {
        if (!AgreementCalculator.UsesOverhead(input)) return;
        var oh = await OverheadColumnsAsync(conn, tx, ct);
        if (!oh.Agreement || !oh.Line) throw new InvalidOperationException(MissingOverheadColumns);
    }

    // ── 計算プレビュー（保存しない）──

    public async Task<AgreementPreview> PreviewAsync(AgreementInput input, CancellationToken ct = default)
    {
        await using var conn = await _estimates.OpenAsync(ct);
        var header = await _estimates.HeaderAsync(conn, null, input.HeaderId, ct);
        var lines = await _estimates.LinesAsync(conn, null, input.HeaderId, ct);
        var evidence = await EvidenceAsync(conn, input, ct);
        var preview = AgreementCalculator.Compute(input, header, lines, evidence);
        if (AgreementCalculator.UsesOverhead(input))
        {
            var oh = await OverheadColumnsAsync(conn, null, ct);
            if (!oh.Agreement || !oh.Line) preview.Blocking.Add(MissingOverheadColumns);
        }
        return preview;
    }

    /// <summary>元PDFの有無・根拠PDFがこの見積のものか・ページ数。</summary>
    private async Task<EvidenceInfo> EvidenceAsync(SqlConnection conn, AgreementInput input, CancellationToken ct)
    {
        var docs = await _docs.ListAsync(conn, input.HeaderId, ct);
        var pdfs = docs.Where(d => d.Type == EstimateDb.DocOriginalPdf).ToList();
        if (pdfs.Count == 0) return new EvidenceInfo(false, false, null);
        var chosen = pdfs.FirstOrDefault(d => d.DocumentId == input.EvidenceDocumentId);
        if (chosen is null) return new EvidenceInfo(true, false, null);
        var file = await _docs.GetAsync(conn, input.HeaderId, chosen.DocumentId, ct);
        return new EvidenceInfo(true, true, PdfSplitter.CountPages(file.Content));
    }

    // ── 下書き保存 ──

    public async Task<AgreementView> SaveDraftAsync(AgreementInput input, CancellationToken ct = default)
    {
        await using var conn = await _estimates.OpenAsync(ct);
        var preview = await PreviewNoThrowAsync(conn, input, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await EnsureOverheadColumnsAsync(conn, tx, input, ct);
            var latest = (await ListAsync(conn, input.HeaderId, ct, tx)).FirstOrDefault();
            if (latest?.Status == EstimateDb.Confirmed)
                throw new InvalidOperationException($"第{latest.RevisionNo}版が確定済みです。変更する場合は「新しい版で訂正」を押してください。");
            var guard = await ColumnGuard.LoadAsync(conn, tx, new[] { EstimateDb.Agreement }, ct);
            var cmd = latest is null
                ? InsertCommand(conn, tx, guard, input, revision: 1, previous: null, EstimateDb.Draft, preview, changeReason: null)
                : UpdateCommand(conn, tx, guard, input, latest, EstimateDb.Draft, preview);
            guard.ThrowIfIssues("取決めの下書き保存");
            await using (cmd)
            {
                if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw Conflict();
            }
            var saved = (await ListAsync(conn, input.HeaderId, ct, tx)).First();
            await tx.CommitAsync(ct);
            return saved;
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    // ── 確定 ──

    public async Task<AgreementView> ConfirmAsync(AgreementInput input, CancellationToken ct = default)
    {
        if (input.RequestKey is not Guid key) throw new InvalidOperationException("確定要求の識別キーがありません。画面を開き直してください。");

        await using var conn = await _estimates.OpenAsync(ct);
        // 同じ確定要求（再送・二重クリック）なら、既に保存した結果を返す
        if (await FindByRequestAsync(conn, null, input.HeaderId, key, ct) is AgreementView done) return done;

        var evidence = await EvidenceAsync(conn, input, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            // 見積ヘッダ・明細をロックして読み、画面が見ていた版と同じか確かめる
            await EnsureOverheadColumnsAsync(conn, tx, input, ct);
            var header = await _estimates.HeaderAsync(conn, tx, input.HeaderId, ct, forUpdate: true);
            if (header.RowVersion != input.HeaderRowVersion) throw Conflict();
            var lines = await _estimates.LinesAsync(conn, tx, input.HeaderId, ct, forUpdate: true);
            var agreements = await ListAsync(conn, input.HeaderId, ct, tx);
            var latest = agreements.FirstOrDefault();
            if (latest?.Status == EstimateDb.Confirmed)
                throw new InvalidOperationException($"第{latest.RevisionNo}版が既に確定されています（{latest.ConfirmedBy}）。「⟳ 再読込」で確認してください。訂正は「新しい版で訂正」から行います。");

            // C# で再計算・再検証（画面の計算結果は使わない）
            var preview = AgreementCalculator.Compute(input, header, lines, evidence);
            if (!preview.CanConfirm)
                throw new InvalidOperationException("確定できません。\n・" + string.Join("\n・", preview.Blocking));

            var guard = await ColumnGuard.LoadAsync(conn, tx, new[] { EstimateDb.Agreement, EstimateDb.AgreementLine }, ct);
            var previousConfirmed = agreements.FirstOrDefault(a => a.Status == EstimateDb.Confirmed);

            // 1) 前の確定版を Superseded に（同じ見積で Confirmed は 1 件だけ）
            if (previousConfirmed is not null)
            {
                await using var sup = new SqlCommand(
                    $"UPDATE {EstimateDb.T(EstimateDb.Agreement)} SET Status = N'{EstimateDb.Superseded}' WHERE Agreement_ID = @id AND Status = N'{EstimateDb.Confirmed}'",
                    conn, tx);
                sup.Parameters.AddWithValue("@id", previousConfirmed.AgreementId);
                if (await sup.ExecuteNonQueryAsync(ct) != 1) throw Conflict();
            }

            // 2) この版を Confirmed で保存（下書きがあればそれを確定、無ければ新しい版）
            long agreementId;
            var cmd = latest?.Status == EstimateDb.Draft
                ? UpdateCommand(conn, tx, guard, input, latest, EstimateDb.Confirmed, preview, key)
                : InsertCommand(conn, tx, guard, input, (latest?.RevisionNo ?? 0) + 1, previousConfirmed?.AgreementId, EstimateDb.Confirmed, preview, null, key);
            await using (cmd)
            {
                // 明細も先に検査してから書き込む
                var lineCmds = preview.Lines.Select(p => LineCommand(conn, tx, guard, p, preview.Rate!.Value)).ToList();
                guard.ThrowIfIssues("取決めの確定");
                if (latest?.Status == EstimateDb.Draft)
                {
                    if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw Conflict();
                    agreementId = latest.AgreementId;
                }
                else
                {
                    agreementId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
                }

                // 3) 全明細の取決め明細
                foreach (var lc in lineCmds)
                {
                    lc.Parameters["@a"].Value = agreementId;
                    await lc.ExecuteNonQueryAsync(ct);
                    await lc.DisposeAsync();
                }
            }

            var saved = (await ListAsync(conn, input.HeaderId, ct, tx)).First(a => a.AgreementId == agreementId);
            await tx.CommitAsync(ct);
            return saved;
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            await SafeRollbackAsync(tx);
            // 同じ確定要求が別の接続で先に保存された → その結果を返す
            await using var again = await _estimates.OpenAsync(ct);
            if (await FindByRequestAsync(again, null, input.HeaderId, key, ct) is AgreementView same) return same;
            throw new InvalidOperationException("ほかの人が同時に確定したため、保存しませんでした。「⟳ 再読込」で最新の状態を確認してください。", e);
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    // ── 新しい版で訂正 ──

    public async Task<AgreementView> NewRevisionAsync(NewRevisionRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new InvalidOperationException("訂正の理由を入力してください。");
        await using var conn = await _estimates.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await _estimates.HeaderAsync(conn, tx, req.HeaderId, ct, forUpdate: true);   // 同じ見積への同時操作を順番にする
            var latest = (await ListAsync(conn, req.HeaderId, ct, tx)).FirstOrDefault();
            if (latest?.Status != EstimateDb.Confirmed)
                throw new InvalidOperationException("確定済みの取決めがないため、訂正の版は作れません（下書きをそのまま編集してください）。");
            var guard = await ColumnGuard.LoadAsync(conn, tx, new[] { EstimateDb.Agreement }, ct);
            var (notes, residual, increase) = AgreementCalculator.SplitNotes(latest.Notes);
            var input = new AgreementInput
            {
                HeaderId = req.HeaderId, BaseSource = latest.BaseAmountSource, BaseAmount = latest.BaseEstimateAmount, BaseReason = latest.BaseAmountReason,
                TaxBasis = latest.TaxBasis, AgreedAmount = latest.AgreedAmount, AgreementDate = latest.AgreementDate,
                EvidenceDocumentId = latest.EvidenceDocumentId, EvidencePage = latest.EvidencePage, Notes = notes,
                ResidualReason = residual, IncreaseReason = increase,
                OverheadSource = latest.OverheadSource,
                OverheadAmount = latest.OverheadSource == AgreementCalculator.OverheadInput ? latest.OverheadAmount : null,
                OverheadLineIds = latest.OverheadLineIds,
                ItemsTotalOverride = latest.OverheadItemsTotal,
            };
            await using (var cmd = InsertCommand(conn, tx, guard, input, latest.RevisionNo + 1, latest.AgreementId, EstimateDb.Draft, null, req.Reason.Trim()))
            {
                guard.ThrowIfIssues("訂正の版の作成");
                await cmd.ExecuteScalarAsync(ct);
            }
            var saved = (await ListAsync(conn, req.HeaderId, ct, tx)).First();
            await tx.CommitAsync(ct);
            return saved;
        }
        catch
        {
            await SafeRollbackAsync(tx);
            throw;
        }
    }

    // ── SQL ──

    private async Task<AgreementPreview?> PreviewNoThrowAsync(SqlConnection conn, AgreementInput input, CancellationToken ct)
    {
        try
        {
            var header = await _estimates.HeaderAsync(conn, null, input.HeaderId, ct);
            var lines = await _estimates.LinesAsync(conn, null, input.HeaderId, ct);
            return AgreementCalculator.Compute(input, header, lines, await EvidenceAsync(conn, input, ct));
        }
        catch (InvalidOperationException)
        {
            return null;   // 下書きは計算できなくても保存する
        }
    }

    private async Task<AgreementView?> FindByRequestAsync(SqlConnection conn, SqlTransaction? tx, int headerId, Guid key, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"SELECT Agreement_ID FROM {EstimateDb.T(EstimateDb.Agreement)} WHERE ConfirmRequestKey = @k AND OcrEstimateHeader_ID = @h", conn, tx);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@h", headerId);
        var v = await cmd.ExecuteScalarAsync(ct);
        if (v is null or DBNull) return null;
        var id = Convert.ToInt64(v);
        return (await ListAsync(conn, headerId, ct, tx)).FirstOrDefault(a => a.AgreementId == id);
    }

    private static void AddValues(SqlCommand cmd, ColumnGuard g, AgreementInput i, string status, AgreementPreview? p, Guid? key)
    {
        const string A = EstimateDb.Agreement;
        const string w = "取決め";
        var confirmed = status == EstimateDb.Confirmed;
        cmd.Parameters.Add(g.Param("@status", A, "Status", status, w));
        cmd.Parameters.Add(g.Param("@base", A, "BaseEstimateAmount", i.BaseAmount, w));
        cmd.Parameters.Add(g.Param("@bsrc", A, "BaseAmountSource", Null(i.BaseSource), w));
        cmd.Parameters.Add(g.Param("@brsn", A, "BaseAmountReason", Null(i.BaseReason?.Trim()), w));
        cmd.Parameters.Add(g.Param("@agreed", A, "AgreedAmount", i.AgreedAmount, w));
        cmd.Parameters.Add(g.Param("@tax", A, "TaxBasis", Null(i.TaxBasis), w));
        cmd.Parameters.Add(g.Param("@rate", A, "AppliedRate", p?.Rate, w));
        cmd.Parameters.Add(g.Param("@date", A, "AgreementDate", i.AgreementDate?.Date, w));
        cmd.Parameters.Add(g.Param("@doc", A, "EvidenceDocument_ID", i.EvidenceDocumentId, w));
        cmd.Parameters.Add(g.Param("@page", A, "EvidencePage", i.EvidencePage, w));
        cmd.Parameters.Add(g.Param("@rule", A, "CalculationRuleVersion", AgreementCalculator.RuleVersion(i), w));
        cmd.Parameters.Add(g.Param("@total", A, "ConvertedDetailTotal", p?.ConvertedTotal, w));
        cmd.Parameters.Add(g.Param("@resid", A, "ResidualAmount", p?.Residual, w));
        cmd.Parameters.Add(g.Param("@by", A, "ConfirmedBy", confirmed ? EstimateDb.Operator : null, w));
        cmd.Parameters.Add(g.Param("@notes", A, "Notes", AgreementCalculator.ComposeNotes(i.Notes, i.ResidualReason, i.IncreaseReason), w));
        cmd.Parameters.Add(g.Param("@key", A, "ConfirmRequestKey", key, w));
        if (HasOverhead(g))
        {
            var uses = AgreementCalculator.UsesOverhead(i);
            cmd.Parameters.Add(g.Param("@ohsrc", A, "OverheadSource", uses ? i.OverheadSource : AgreementCalculator.OverheadNone, w));
            cmd.Parameters.Add(g.Param("@ohamt", A, "OverheadAmount", uses ? p?.OverheadAmount ?? i.OverheadAmount : null, w));
            cmd.Parameters.Add(g.Param("@ohrate", A, "OverheadRate", uses ? p?.OverheadRate : null, w));
            cmd.Parameters.Add(g.Param("@ohids", A, "OverheadLineIds",
                i.OverheadSource == AgreementCalculator.OverheadLines ? AgreementCalculator.JoinIds(i.OverheadLineIds) : null, w));
            cmd.Parameters.Add(g.Param("@ohitems", A, "OverheadItemsTotal", uses ? p?.ItemsTotal ?? i.ItemsTotalOverride : null, w));
        }
    }

    private static bool HasOverhead(ColumnGuard g) => g.HasColumn(EstimateDb.Agreement, "OverheadSource");

    private static bool HasLineOverhead(ColumnGuard g) => g.HasColumn(EstimateDb.AgreementLine, "OverheadIncludedUnitPrice");

    private static SqlCommand InsertCommand(SqlConnection conn, SqlTransaction tx, ColumnGuard g, AgreementInput i, int revision, long? previous,
        string status, AgreementPreview? p, string? changeReason, Guid? key = null)
    {
        var cmd = new SqlCommand(
            $"""
            INSERT INTO {EstimateDb.T(EstimateDb.Agreement)}
                (OcrEstimateHeader_ID, RevisionNo, PreviousAgreement_ID, Status, BaseEstimateAmount, BaseAmountSource, BaseAmountReason,
                 AgreedAmount, TaxBasis, AppliedRate, AgreementDate, EvidenceDocument_ID, EvidencePage, CalculationRuleVersion,
                 ConvertedDetailTotal, ResidualAmount, ConfirmedBy, ConfirmedAt, Notes, ChangeReason, ConfirmRequestKey
                 {(HasOverhead(g) ? ", OverheadSource, OverheadAmount, OverheadRate, OverheadLineIds, OverheadItemsTotal" : "")})
            OUTPUT INSERTED.Agreement_ID
            VALUES (@h, @rev, @prev, @status, @base, @bsrc, @brsn, @agreed, @tax, @rate, @date, @doc, @page, @rule,
                    @total, @resid, @by, {(status == EstimateDb.Confirmed ? "SYSUTCDATETIME()" : "NULL")}, @notes, @chg, @key
                    {(HasOverhead(g) ? ", @ohsrc, @ohamt, @ohrate, @ohids, @ohitems" : "")})
            """, conn, tx);
        cmd.Parameters.Add(g.Param("@h", EstimateDb.Agreement, "OcrEstimateHeader_ID", i.HeaderId, "取決め"));
        cmd.Parameters.Add(g.Param("@rev", EstimateDb.Agreement, "RevisionNo", revision, "取決め"));
        cmd.Parameters.Add(g.Param("@prev", EstimateDb.Agreement, "PreviousAgreement_ID", previous, "取決め"));
        cmd.Parameters.Add(g.Param("@chg", EstimateDb.Agreement, "ChangeReason", changeReason, "取決め"));
        AddValues(cmd, g, i, status, p, key);
        return cmd;
    }

    private static SqlCommand UpdateCommand(SqlConnection conn, SqlTransaction tx, ColumnGuard g, AgreementInput i, AgreementView draft,
        string status, AgreementPreview? p, Guid? key = null)
    {
        var cmd = new SqlCommand(
            $"""
            UPDATE {EstimateDb.T(EstimateDb.Agreement)}
            SET Status = @status, BaseEstimateAmount = @base, BaseAmountSource = @bsrc, BaseAmountReason = @brsn, AgreedAmount = @agreed,
                TaxBasis = @tax, AppliedRate = @rate, AgreementDate = @date, EvidenceDocument_ID = @doc, EvidencePage = @page,
                CalculationRuleVersion = @rule, ConvertedDetailTotal = @total, ResidualAmount = @resid, ConfirmedBy = @by,
                ConfirmedAt = {(status == EstimateDb.Confirmed ? "SYSUTCDATETIME()" : "NULL")}, Notes = @notes, ConfirmRequestKey = @key
                {(HasOverhead(g) ? ", OverheadSource = @ohsrc, OverheadAmount = @ohamt, OverheadRate = @ohrate, OverheadLineIds = @ohids, OverheadItemsTotal = @ohitems" : "")}
            WHERE Agreement_ID = @id AND Status = N'{EstimateDb.Draft}' AND RowVersion = @rv
            """, conn, tx);
        cmd.Parameters.AddWithValue("@id", draft.AgreementId);
        cmd.Parameters.Add(RowVersionParam("@rv", draft.RowVersion));
        AddValues(cmd, g, i, status, p, key);
        return cmd;
    }

    private static SqlCommand LineCommand(SqlConnection conn, SqlTransaction tx, ColumnGuard g, PreviewLine l, decimal rate)
    {
        const string L = EstimateDb.AgreementLine;
        var w = $"取決め明細 {l.Order} 行目";
        var cmd = new SqlCommand(
            $"""
            INSERT INTO {EstimateDb.T(L)}
                (Agreement_ID, OcrEstimateLine_ID, LineOrder, RecordType, ItemName, MaterialSpec, DimensionSpec, Quantity, Unit,
                 OriginalUnitPrice, OriginalAmount, AppliedRate, ConvertedUnitPrice, ConvertedAmount, IsCalculationTarget, ExclusionReason, CalculationType
                 {(HasLineOverhead(g) ? ", OverheadIncludedUnitPrice, OverheadIncludedAmount" : "")})
            VALUES (@a, @line, @ord, @rt, @nm, @ma, @di, @q, @u, @up, @am, @rate, @cup, @cam, @tg, @ex, @ctype
                    {(HasLineOverhead(g) ? ", @ohup, @oham" : "")})
            """, conn, tx);
        cmd.Parameters.Add(new SqlParameter("@a", System.Data.SqlDbType.BigInt) { Value = 0L });
        cmd.Parameters.Add(g.Param("@line", L, "OcrEstimateLine_ID", l.LineId, w));
        cmd.Parameters.Add(g.Param("@ord", L, "LineOrder", l.Order, w));
        cmd.Parameters.Add(g.Param("@rt", L, "RecordType", l.RecordType ?? "", w));
        cmd.Parameters.Add(g.Param("@nm", L, "ItemName", l.ItemName ?? "", w));
        cmd.Parameters.Add(g.Param("@ma", L, "MaterialSpec", Null(l.MaterialSpec), w));
        cmd.Parameters.Add(g.Param("@di", L, "DimensionSpec", Null(l.DimensionSpec), w));
        cmd.Parameters.Add(g.Param("@q", L, "Quantity", l.Quantity, w));
        cmd.Parameters.Add(g.Param("@u", L, "Unit", Null(l.Unit), w));
        cmd.Parameters.Add(g.Param("@up", L, "OriginalUnitPrice", l.UnitPrice, w));
        cmd.Parameters.Add(g.Param("@am", L, "OriginalAmount", l.Amount, w));
        cmd.Parameters.Add(g.Param("@rate", L, "AppliedRate", l.IsTarget ? rate : null, w));
        cmd.Parameters.Add(g.Param("@cup", L, "ConvertedUnitPrice", l.IsTarget ? l.ConvertedUnitPrice : null, w));
        cmd.Parameters.Add(g.Param("@cam", L, "ConvertedAmount", l.IsTarget ? l.ConvertedAmount : null, w));
        cmd.Parameters.Add(g.Param("@tg", L, "IsCalculationTarget", l.IsTarget, w));
        cmd.Parameters.Add(g.Param("@ex", L, "ExclusionReason", l.IsTarget ? null : (string.IsNullOrWhiteSpace(l.ExclusionReason) ? "計算対象外" : l.ExclusionReason), w));
        cmd.Parameters.Add(g.Param("@ctype", L, "CalculationType",
            l.IsTarget ? (l.OverheadIncludedAmount is not null || l.OverheadIncludedUnitPrice is not null ? "TotalRateWithOverhead" : EstimateDb.CalcTotalRate)
            : l.IsOverhead ? "Overhead"
            : l.RecordType == "参考明細" ? "Reference" : EstimateDb.CalcExcluded, w));
        if (HasLineOverhead(g))
        {
            cmd.Parameters.Add(g.Param("@ohup", L, "OverheadIncludedUnitPrice", l.IsTarget ? l.OverheadIncludedUnitPrice : null, w));
            cmd.Parameters.Add(g.Param("@oham", L, "OverheadIncludedAmount", l.IsTarget ? l.OverheadIncludedAmount : null, w));
        }
        return cmd;
    }
}
