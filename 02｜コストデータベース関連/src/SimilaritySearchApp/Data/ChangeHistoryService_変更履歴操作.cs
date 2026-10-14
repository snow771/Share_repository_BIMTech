using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>
/// 変更履歴の一覧・明細・取り消し・削除。
///
/// 取り消しの考え方（保存1回分＝操作 単位）:
///   追加(INSERT)  → その行を削除する（追加したときと同じ内容のままの場合だけ）
///   更新(UPDATE)  → 変わった列を元の値に戻す（今の値が更新後の値のままの場合だけ）
///   削除(DELETE)  → 元の行を追加し直す（自動採番の列も元の番号で戻す。権限が無ければ新しい番号）
/// 「この時点まで戻す」は、その操作以降のまだ取り消されていない操作を新しい順にすべて取り消す。
/// その後に他の人が同じ行を変更していた場合は「衝突」として何もせずに知らせる（強制すれば上書き）。
/// 取り消しは1トランザクションで行い、取り消したこと自体も「取り消し」の操作として履歴に残す。
/// </summary>
public sealed class ChangeHistoryService
{
    private readonly DbSchemaRepository _schema = new();

    public async Task<HistoryListResult> ListAsync(DbSettings s, HistoryQuery q, CancellationToken ct = default)
    {
        await using var conn = await new SqlConnectionFactory(s).OpenAsync(ct);
        return await new ChangeLogRepository(s.Tables).ListAsync(conn, q, ct);
    }

    public async Task<HistoryDetailResult> DetailAsync(DbSettings s, HistoryDetailRequest req, CancellationToken ct = default)
    {
        await using var conn = await new SqlConnectionFactory(s).OpenAsync(ct);
        var repo = new ChangeLogRepository(s.Tables);
        var batch = await repo.GetBatchAsync(conn, null, req.BatchId, ct)
                    ?? throw new InvalidOperationException($"操作 #{req.BatchId} が見つかりません。");
        var (rows, total) = await repo.ReadRowsAsync(conn, null, req.BatchId, Math.Clamp(req.Limit, 10, 5000), ct);

        var keyCache = new Dictionary<string, List<string>?>();
        var views = new List<ChangeRowView>();
        var seq = 0;
        foreach (var r in rows)
        {
            var key = $"{r.Schema}.{r.Table}";
            if (!keyCache.TryGetValue(key, out var pk))
            {
                try
                {
                    var cols = await _schema.GetColumnsAsync(conn, r.Schema, r.Table, ct);
                    pk = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
                }
                catch (InvalidOperationException)
                {
                    pk = null;   // テーブルが削除されている等
                }
                keyCache[key] = pk;
            }
            views.Add(ChangeLogRepository.ToView(++seq, r, pk));
        }
        return new HistoryDetailResult(batch, views, total);
    }

    public async Task<int> PurgeAsync(DbSettings s, PurgeRequest req, CancellationToken ct = default)
    {
        await using var conn = await new SqlConnectionFactory(s).OpenAsync(ct);
        return await new ChangeLogRepository(s.Tables).PurgeAsync(conn, req.Before, ct);
    }

    public async Task<RevertResult> RevertAsync(DbSettings s, RevertRequest req, CancellationToken ct = default)
    {
        await using var conn = await new SqlConnectionFactory(s).OpenAsync(ct);
        var repo = new ChangeLogRepository(s.Tables);
        await repo.EnsureAsync(conn, null, ct);

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 対象の操作（新しい順）
            List<ChangeBatchInfo> targets;
            if (req.Mode == "since")
            {
                targets = await repo.ActiveBatchesSinceAsync(conn, tx, req.BatchId, ct);
            }
            else
            {
                var b = await repo.GetBatchAsync(conn, tx, req.BatchId, ct)
                        ?? throw new InvalidOperationException($"操作 #{req.BatchId} が見つかりません。");
                if (b.RevertedByBatchId is int by) throw new InvalidOperationException($"操作 #{b.BatchId} は、操作 #{by} で既に取り消されています。");
                targets = new List<ChangeBatchInfo> { b };
            }
            if (targets.Count == 0) throw new InvalidOperationException("取り消す操作がありません（すべて取り消し済みです）。");

            var log = new ChangeCollector("取り消し")
            {
                Summary = targets.Count == 1
                    ? $"操作 #{targets[0].BatchId}（{targets[0].Source}・{targets[0].ExecutedAt:yyyy/MM/dd HH:mm}）を取り消し"
                    : $"操作 #{targets[^1].BatchId} 以降の {targets.Count} 件を取り消し（#{string.Join(", #", targets.Select(t => t.BatchId))}）",
            };
            log.RevertsBatchIds.AddRange(targets.Select(t => t.BatchId));

            var conflicts = new List<RevertConflict>();
            var warnings = new List<string>();
            var columnsCache = new Dictionary<string, List<DbColumnInfo>>();

            foreach (var batch in targets)
            {
                var (rows, _) = await repo.ReadRowsAsync(conn, tx, batch.BatchId, null, ct);
                // 後に行った変更から順に戻す
                for (var i = rows.Count - 1; i >= 0; i--)
                {
                    var r = rows[i];
                    var key = $"{r.Schema}.{r.Table}";
                    if (!columnsCache.TryGetValue(key, out var cols))
                    {
                        cols = await _schema.GetColumnsAsync(conn, r.Schema, r.Table, ct, tx);
                        columnsCache[key] = cols;
                    }
                    var conflict = await RevertRowAsync(conn, tx, r, cols, req.Force, log, warnings, s.CommandTimeoutSec, ct);
                    if (conflict is not null) conflicts.Add(conflict with { BatchId = batch.BatchId });
                }
            }

            if (conflicts.Count > 0 && !req.Force)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return new RevertResult(false, null, new List<int>(), 0, conflicts.Take(200).ToList(), warnings);
            }

            var newId = await repo.WriteAsync(conn, tx, log, ct);
            await tx.CommitAsync(ct);
            return new RevertResult(true, newId, targets.Select(t => t.BatchId).ToList(), log.Rows.Count, conflicts.Take(200).ToList(), warnings);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // ------------------------------------------------------------------

    /// <summary>1行を元に戻す。戻せない（衝突）場合はその理由を返す（force なら可能な範囲で戻して null）。</summary>
    private static async Task<RevertConflict?> RevertRowAsync(
        SqlConnection conn, SqlTransaction tx, ChangeRowRecord r, List<DbColumnInfo> cols, bool force,
        ChangeCollector log, List<string> warnings, int timeout, CancellationToken ct)
    {
        var table = $"{TableEditRepository.Q(r.Schema)}.{TableEditRepository.Q(r.Table)}";
        var pk = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
        string keyText = ChangeLogRepository.KeyText(r, pk);
        RevertConflict Conflict(string reason) => new(0, r.Table, r.Operation, keyText, reason);

        switch (r.Operation)
        {
            case "INSERT":
            {
                // 追加した行を削除する
                var after = r.After!;
                var current = await ReadCurrentAsync(conn, tx, table, cols, after, timeout, ct);
                if (current is null) return force ? null : Conflict("追加した行が既にありません（その後に削除されています）。");
                var diff = Differences(cols, after, current);
                if (diff.Count > 0 && !force) return Conflict($"追加した後に変更されています（{string.Join("、", diff)}）。");
                await using var cmd = new SqlCommand { Connection = conn, Transaction = tx, CommandTimeout = timeout };
                cmd.CommandText = $"DELETE FROM {table} OUTPUT DELETED.* WHERE {KeyWhere(cmd, cols, after)}";
                var n = await log.ExecuteAsync(cmd, r.Schema, r.Table, "DELETE", ct);
                if (n > 1) throw new InvalidOperationException($"{r.Table}: 同じ内容の行が複数あり、1行に特定できません（{keyText}）。");
                return null;
            }
            case "UPDATE":
            {
                // 変わった列を元の値に戻す
                var before = r.Before!;
                var after = r.After!;
                var current = await ReadCurrentAsync(conn, tx, table, cols, after, timeout, ct);
                if (current is null) return force ? null : Conflict("更新した行が見つかりません（その後に削除・キーの変更がされています）。");
                var changed = cols.Where(c => c.IsEditable && before.TryGetValue(c.Name, out var b) && after.TryGetValue(c.Name, out var a) && b != a).ToList();
                if (changed.Count == 0) return null;
                var moved = changed.Where(c => current.TryGetValue(c.Name, out var cur) && cur != after[c.Name]).Select(c => c.Name).ToList();
                if (moved.Count > 0 && !force) return Conflict($"その後に別の変更がされています（{string.Join("、", moved)}）。");

                await using var cmd = new SqlCommand { Connection = conn, Transaction = tx, CommandTimeout = timeout };
                var sets = new List<string>();
                for (var i = 0; i < changed.Count; i++)
                {
                    sets.Add($"{TableEditRepository.Q(changed[i].Name)} = @s{i}");
                    cmd.Parameters.Add(Param($"@s{i}", changed[i], Parse(before[changed[i].Name], changed[i])));
                }
                cmd.CommandText = $"UPDATE {table} SET {string.Join(", ", sets)} OUTPUT DELETED.*, INSERTED.* WHERE {KeyWhere(cmd, cols, current)}";
                await log.ExecuteAsync(cmd, r.Schema, r.Table, "UPDATE", ct);
                return null;
            }
            case "DELETE":
            {
                // 削除した行を追加し直す
                var before = r.Before!;
                if (pk.Count > 0 && await ReadCurrentAsync(conn, tx, table, cols, before, timeout, ct) is not null)
                    return force ? null : Conflict("同じキーの行が既にあります（その後に追加されています）。");

                var identity = cols.FirstOrDefault(c => c.IsIdentity);
                var insertable = cols.Where(c => (c.IsEditable || c.IsIdentity) && before.ContainsKey(c.Name)).ToList();
                var skipped = cols.Where(c => !c.IsEditable && !c.IsIdentity && !c.IsComputed && before.ContainsKey(c.Name)).Select(c => c.Name).ToList();
                if (skipped.Count > 0) warnings.Add($"{r.Table}: 列 {string.Join(", ", skipped)} は形式上戻せないため、既定値で戻しました。");

                if (identity is not null)
                {
                    // 自動採番の列も元の番号で戻す（IDENTITY_INSERT の権限が無ければ新しい番号で戻す）
                    try
                    {
                        await InsertAsync(conn, tx, table, insertable, before, identityInsert: true, r, log, timeout, ct);
                        return null;
                    }
                    catch (SqlException)
                    {
                        warnings.Add($"{r.Table}: 自動採番の番号を元に戻す権限が無いため、新しい番号で戻しました（{keyText}）。");
                    }
                }
                await InsertAsync(conn, tx, table, insertable.Where(c => !c.IsIdentity).ToList(), before, identityInsert: false, r, log, timeout, ct);
                return null;
            }
            default:
                return Conflict($"不明な操作です: {r.Operation}");
        }
    }

    private static async Task InsertAsync(
        SqlConnection conn, SqlTransaction tx, string table, List<DbColumnInfo> cols, Dictionary<string, string?> values,
        bool identityInsert, ChangeRowRecord r, ChangeCollector log, int timeout, CancellationToken ct)
    {
        await using var cmd = new SqlCommand { Connection = conn, Transaction = tx, CommandTimeout = timeout };
        var names = new List<string>();
        var ps = new List<string>();
        for (var i = 0; i < cols.Count; i++)
        {
            names.Add(TableEditRepository.Q(cols[i].Name));
            ps.Add($"@v{i}");
            cmd.Parameters.Add(Param($"@v{i}", cols[i], Parse(values[cols[i].Name], cols[i])));
        }
        var insert = $"INSERT INTO {table} ({string.Join(", ", names)}) OUTPUT INSERTED.* VALUES ({string.Join(", ", ps)})";
        cmd.CommandText = identityInsert
            ? $"SET IDENTITY_INSERT {table} ON; {insert}; SET IDENTITY_INSERT {table} OFF;"
            : insert;
        await log.ExecuteAsync(cmd, r.Schema, r.Table, "INSERT", ct);
    }

    /// <summary>行を特定して今の値を読む（主キー、無ければ比較できる全列）。無ければ null。</summary>
    private static async Task<Dictionary<string, string?>?> ReadCurrentAsync(
        SqlConnection conn, SqlTransaction tx, string table, List<DbColumnInfo> cols, Dictionary<string, string?> row, int timeout, CancellationToken ct)
    {
        await using var cmd = new SqlCommand { Connection = conn, Transaction = tx, CommandTimeout = timeout };
        cmd.CommandText = $"SELECT TOP 2 * FROM {table} WHERE {KeyWhere(cmd, cols, row)}";
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return null;
        return ChangeCollector.ReadRow(rd, 0, rd.FieldCount);
    }

    /// <summary>行を特定する WHERE 句（主キー、無ければ比較できる全列。NULL は IS NULL）。</summary>
    private static string KeyWhere(SqlCommand cmd, List<DbColumnInfo> cols, Dictionary<string, string?> row)
    {
        var keys = cols.Where(c => c.IsPrimaryKey).ToList();
        if (keys.Count == 0) keys = cols.Where(c => DbValueConverter.IsComparable(c.TypeName) && DbValueConverter.IsSupported(c.TypeName)).ToList();
        var parts = new List<string>();
        foreach (var c in keys)
        {
            if (!row.TryGetValue(c.Name, out var v)) continue;
            if (v is null)
            {
                parts.Add($"{TableEditRepository.Q(c.Name)} IS NULL");
                continue;
            }
            var p = $"@k{cmd.Parameters.Count}";
            parts.Add($"{TableEditRepository.Q(c.Name)} = {p}");
            cmd.Parameters.Add(Param(p, c, Parse(v, c with { IsNullable = true, MaxLength = null })));
        }
        if (parts.Count == 0) throw new InvalidOperationException("行を特定できる列がありません。");
        return string.Join(" AND ", parts);
    }

    private static List<string> Differences(List<DbColumnInfo> cols, Dictionary<string, string?> expected, Dictionary<string, string?> current) =>
        cols.Where(c => DbValueConverter.IsComparable(c.TypeName) && expected.ContainsKey(c.Name) && current.ContainsKey(c.Name) && expected[c.Name] != current[c.Name])
            .Select(c => c.Name).ToList();

    /// <summary>履歴の文字列 → DB の値。null は NULL、文字列列の空文字は空文字のまま（NULL にしない）。</summary>
    private static object Parse(string? text, DbColumnInfo col)
    {
        if (text is null) return DBNull.Value;
        var isText = col.TypeName.ToLowerInvariant() is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";
        if (text.Length == 0 && isText) return "";
        return DbValueConverter.Parse(text, col with { IsNullable = true, MaxLength = null });
    }

    private static SqlParameter Param(string name, DbColumnInfo col, object value)
    {
        var p = new SqlParameter(name, DbValueConverter.SqlType(col.TypeName)) { Value = value };
        if (col.MaxLength is int len) p.Size = len;
        if (p.SqlDbType == System.Data.SqlDbType.Decimal)
        {
            p.Precision = (byte)col.Precision;
            p.Scale = (byte)col.Scale;
        }
        return p;
    }
}
