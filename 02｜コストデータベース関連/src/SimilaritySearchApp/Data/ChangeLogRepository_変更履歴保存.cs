using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>
/// 変更履歴（だれが・いつ・どのテーブルのどの行を・どう変えたか）の保存と読み出し。
///   ChangeBatch_変更操作 … 保存1回分（日時・ユーザー・PC・種類・要約・取り消し状態）
///   ChangeRow_変更明細   … 行ごとの変更前・変更後（JSON）
/// テーブルが無ければ初回に作成する（CREATE TABLE の権限が必要。無い場合は sql/ChangeLog_変更履歴.sql を管理者に実行してもらう）。
/// 履歴は変更と同じトランザクションで書くので、データと履歴が食い違うことはない。
/// </summary>
public sealed class ChangeLogRepository
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly TableNames _tables;

    public ChangeLogRepository(TableNames tables) => _tables = tables;

    private string BatchTable => $"[dbo].{TableEditRepository.Q(_tables.ChangeBatch)}";
    private string RowTable => $"[dbo].{TableEditRepository.Q(_tables.ChangeRow)}";

    /// <summary>履歴テーブルが無ければ作る。</summary>
    public async Task EnsureAsync(SqlConnection conn, SqlTransaction? tx, CancellationToken ct)
    {
        var sql = CreateSql(_tables);
        await using var cmd = new SqlCommand(sql, conn, tx);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException e)
        {
            throw new InvalidOperationException(
                "変更履歴のテーブルを作成できませんでした（権限が無い可能性があります）。" +
                "管理者に SimilaritySearchApp\\sql\\ChangeLog_変更履歴.sql を実行してもらってください。\n" + e.Message, e);
        }
    }

    /// <summary>履歴テーブルの作成 SQL（存在すれば何もしない）。</summary>
    public static string CreateSql(TableNames t)
    {
        var batch = $"[dbo].{TableEditRepository.Q(t.ChangeBatch)}";
        var row = $"[dbo].{TableEditRepository.Q(t.ChangeRow)}";
        var batchName = t.ChangeBatch.Replace("'", "''");
        var rowName = t.ChangeRow.Replace("'", "''");
        return $"""
            IF OBJECT_ID(N'[dbo].[{batchName}]', N'U') IS NULL
            BEGIN
                CREATE TABLE {batch} (
                    BatchId           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ExecutedAt        DATETIME2(3)   NOT NULL,
                    UserName          NVARCHAR(256)  NOT NULL,
                    MachineName       NVARCHAR(128)  NULL,
                    DbLogin           NVARCHAR(256)  NULL,
                    Source            NVARCHAR(50)   NOT NULL,
                    Summary           NVARCHAR(1000) NULL,
                    Tables            NVARCHAR(1000) NULL,
                    RowTotal          INT            NOT NULL,
                    RevertsBatchIds   NVARCHAR(1000) NULL,
                    RevertedByBatchId INT            NULL
                );
                CREATE INDEX IX_ChangeBatch_ExecutedAt ON {batch} (ExecutedAt);
            END;
            IF OBJECT_ID(N'[dbo].[{rowName}]', N'U') IS NULL
            BEGIN
                CREATE TABLE {row} (
                    RowId       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    BatchId     INT           NOT NULL,
                    SeqNo       INT           NOT NULL,
                    TableSchema NVARCHAR(128) NOT NULL,
                    TableName   NVARCHAR(256) NOT NULL,
                    Operation   NVARCHAR(10)  NOT NULL,
                    BeforeJson  NVARCHAR(MAX) NULL,
                    AfterJson   NVARCHAR(MAX) NULL
                );
                CREATE INDEX IX_ChangeRow_Batch ON {row} (BatchId, SeqNo);
            END;
            """;
    }

    /// <summary>集めた変更を履歴に書く（変更と同じトランザクションで呼ぶ）。変更が無ければ何もしない。</summary>
    public async Task<int?> WriteAsync(SqlConnection conn, SqlTransaction tx, ChangeCollector c, CancellationToken ct)
    {
        if (c.Rows.Count == 0) return null;
        await EnsureAsync(conn, tx, ct);

        var tables = c.Rows.Select(r => r.Table).Distinct().ToList();
        var summary = c.Summary ?? AutoSummary(c.Rows);
        int batchId;
        await using (var cmd = new SqlCommand($"""
            INSERT INTO {BatchTable} (ExecutedAt, UserName, MachineName, DbLogin, Source, Summary, Tables, RowTotal, RevertsBatchIds)
            OUTPUT INSERTED.BatchId
            VALUES (SYSDATETIME(), @user, @machine, SUSER_SNAME(), @source, @summary, @tables, @rows, @reverts)
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("@user", $@"{Environment.UserDomainName}\{Environment.UserName}");
            cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
            cmd.Parameters.AddWithValue("@source", c.Source);
            cmd.Parameters.AddWithValue("@summary", Truncate(summary, 1000));
            cmd.Parameters.AddWithValue("@tables", Truncate(string.Join(", ", tables), 1000));
            cmd.Parameters.AddWithValue("@rows", c.Rows.Count);
            cmd.Parameters.AddWithValue("@reverts", c.RevertsBatchIds.Count == 0 ? (object)DBNull.Value : string.Join(",", c.RevertsBatchIds));
            batchId = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }

        // 明細は数千行になることがあるので一括で書く
        var dt = new DataTable();
        dt.Columns.Add("BatchId", typeof(int));
        dt.Columns.Add("SeqNo", typeof(int));
        dt.Columns.Add("TableSchema", typeof(string));
        dt.Columns.Add("TableName", typeof(string));
        dt.Columns.Add("Operation", typeof(string));
        dt.Columns.Add("BeforeJson", typeof(string));
        dt.Columns.Add("AfterJson", typeof(string));
        var seq = 0;
        foreach (var r in c.Rows)
        {
            dt.Rows.Add(batchId, ++seq, r.Schema, r.Table, r.Operation,
                r.Before is null ? (object)DBNull.Value : JsonSerializer.Serialize(r.Before, Json),
                r.After is null ? (object)DBNull.Value : JsonSerializer.Serialize(r.After, Json));
        }
        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = RowTable, BulkCopyTimeout = 600 };
        foreach (DataColumn col in dt.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(dt, ct);

        if (c.RevertsBatchIds.Count > 0)
        {
            var ps = c.RevertsBatchIds.Select((_, i) => $"@b{i}").ToList();
            await using var mark = new SqlCommand($"UPDATE {BatchTable} SET RevertedByBatchId = @by WHERE BatchId IN ({string.Join(", ", ps)})", conn, tx);
            mark.Parameters.AddWithValue("@by", batchId);
            for (var i = 0; i < ps.Count; i++) mark.Parameters.AddWithValue(ps[i], c.RevertsBatchIds[i]);
            await mark.ExecuteNonQueryAsync(ct);
        }
        return batchId;
    }

    // ── 読み出し ────────────────────────────────────────────

    public async Task<HistoryListResult> ListAsync(SqlConnection conn, HistoryQuery q, CancellationToken ct)
    {
        await EnsureAsync(conn, null, ct);
        var where = new List<string>();
        await using var cmd = new SqlCommand { Connection = conn };
        if (q.From is DateTime from) { where.Add("ExecutedAt >= @from"); cmd.Parameters.AddWithValue("@from", from); }
        if (q.To is DateTime to) { where.Add("ExecutedAt < @to"); cmd.Parameters.AddWithValue("@to", to.Date.AddDays(1)); }
        if (!string.IsNullOrWhiteSpace(q.User)) { where.Add("(UserName LIKE @user OR MachineName LIKE @user OR DbLogin LIKE @user)"); cmd.Parameters.AddWithValue("@user", $"%{q.User.Trim()}%"); }
        if (!string.IsNullOrWhiteSpace(q.Table)) { where.Add("Tables LIKE @table"); cmd.Parameters.AddWithValue("@table", $"%{q.Table.Trim()}%"); }
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            // 要約・テーブル名に加え、明細の値（project_id など）でも探す
            where.Add($"(Summary LIKE @kw OR Tables LIKE @kw OR EXISTS (SELECT 1 FROM {RowTable} r WHERE r.BatchId = b.BatchId AND (r.BeforeJson LIKE @kw OR r.AfterJson LIKE @kw)))");
            cmd.Parameters.AddWithValue("@kw", $"%{q.Keyword.Trim()}%");
        }
        var limit = Math.Clamp(q.Limit, 10, 2000);
        cmd.CommandText = $"SELECT TOP ({limit + 1}) * FROM {BatchTable} b {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY BatchId DESC";

        var list = new List<ChangeBatchInfo>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) list.Add(ReadBatch(r));
        }
        var truncated = list.Count > limit;
        if (truncated) list.RemoveAt(list.Count - 1);
        return new HistoryListResult(list, truncated, _tables.ChangeBatch, _tables.ChangeRow);
    }

    public async Task<ChangeBatchInfo?> GetBatchAsync(SqlConnection conn, SqlTransaction? tx, int batchId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT * FROM {BatchTable} WHERE BatchId = @id", conn, tx);
        cmd.Parameters.AddWithValue("@id", batchId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadBatch(r) : null;
    }

    /// <summary>指定の操作以降で、まだ取り消されていない操作（新しい順）。</summary>
    public async Task<List<ChangeBatchInfo>> ActiveBatchesSinceAsync(SqlConnection conn, SqlTransaction tx, int batchId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT * FROM {BatchTable} WHERE BatchId >= @id AND RevertedByBatchId IS NULL ORDER BY BatchId DESC", conn, tx);
        cmd.Parameters.AddWithValue("@id", batchId);
        var list = new List<ChangeBatchInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(ReadBatch(r));
        return list;
    }

    /// <summary>操作の明細（SeqNo の昇順）。limit 件まで。</summary>
    public async Task<(List<ChangeRowRecord> Rows, int Total)> ReadRowsAsync(SqlConnection conn, SqlTransaction? tx, int batchId, int? limit, CancellationToken ct)
    {
        int total;
        await using (var count = new SqlCommand($"SELECT COUNT(*) FROM {RowTable} WHERE BatchId = @id", conn, tx))
        {
            count.Parameters.AddWithValue("@id", batchId);
            total = (int)(await count.ExecuteScalarAsync(ct))!;
        }
        var top = limit is int n ? $"TOP ({n})" : "";
        await using var cmd = new SqlCommand($"SELECT {top} TableSchema, TableName, Operation, BeforeJson, AfterJson FROM {RowTable} WHERE BatchId = @id ORDER BY SeqNo", conn, tx);
        cmd.Parameters.AddWithValue("@id", batchId);
        var rows = new List<ChangeRowRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new ChangeRowRecord(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(r.GetString(3)),
                r.IsDBNull(4) ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(r.GetString(4))));
        }
        return (rows, total);
    }

    /// <summary>指定日時より前の履歴を削除する。削除した操作の数を返す。</summary>
    public async Task<int> PurgeAsync(SqlConnection conn, DateTime before, CancellationToken ct)
    {
        await EnsureAsync(conn, null, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        await using (var rows = new SqlCommand($"DELETE r FROM {RowTable} r JOIN {BatchTable} b ON b.BatchId = r.BatchId WHERE b.ExecutedAt < @before", conn, tx) { CommandTimeout = 600 })
        {
            rows.Parameters.AddWithValue("@before", before);
            await rows.ExecuteNonQueryAsync(ct);
        }
        int n;
        await using (var batches = new SqlCommand($"DELETE FROM {BatchTable} WHERE ExecutedAt < @before", conn, tx))
        {
            batches.Parameters.AddWithValue("@before", before);
            n = await batches.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return n;
    }

    // ── 表示用の差分 ─────────────────────────────────────────

    /// <summary>行の差分（UPDATE は変わった列だけ、INSERT/DELETE は値のある列）。</summary>
    public static ChangeRowView ToView(int seq, ChangeRowRecord r, IReadOnlyCollection<string>? keyColumns)
    {
        var changes = new List<ChangeDiff>();
        var cols = (r.After ?? r.Before ?? new Dictionary<string, string?>()).Keys;
        foreach (var c in cols)
        {
            string? b = null, a = null;
            r.Before?.TryGetValue(c, out b);
            r.After?.TryGetValue(c, out a);
            if (r.Operation == "UPDATE" && b == a) continue;
            if (r.Operation != "UPDATE" && (r.Before is null ? a : b) is null) continue;
            changes.Add(new ChangeDiff(c, b, a));
        }
        return new ChangeRowView(seq, r.Table, r.Operation, KeyText(r, keyColumns), changes);
    }

    /// <summary>行の見出し（主キー、無ければ先頭の数列）。</summary>
    public static string KeyText(ChangeRowRecord r, IReadOnlyCollection<string>? keyColumns)
    {
        var row = r.After ?? r.Before;
        if (row is null) return "";
        var keys = keyColumns is { Count: > 0 } ? keyColumns.Where(row.ContainsKey).ToList() : row.Keys.Take(3).ToList();
        return string.Join(" / ", keys.Select(k => $"{k}={row[k] ?? "NULL"}"));
    }

    // ------------------------------------------------------------------

    private static ChangeBatchInfo ReadBatch(SqlDataReader r) => new(
        BatchId: r.GetInt32(r.GetOrdinal("BatchId")),
        ExecutedAt: r.GetDateTime(r.GetOrdinal("ExecutedAt")),
        UserName: r.GetString(r.GetOrdinal("UserName")),
        MachineName: Str(r, "MachineName"),
        DbLogin: Str(r, "DbLogin"),
        Source: r.GetString(r.GetOrdinal("Source")),
        Summary: Str(r, "Summary"),
        Tables: Str(r, "Tables"),
        RowTotal: r.GetInt32(r.GetOrdinal("RowTotal")),
        RevertsBatchIds: Str(r, "RevertsBatchIds"),
        RevertedByBatchId: r.IsDBNull(r.GetOrdinal("RevertedByBatchId")) ? null : r.GetInt32(r.GetOrdinal("RevertedByBatchId")));

    private static string? Str(SqlDataReader r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static string AutoSummary(List<ChangeRowRecord> rows) =>
        string.Join(" / ", rows.GroupBy(r => r.Table).Select(g =>
            $"{(RelationInferenceService.JapaneseLabel(g.Key) is { Length: > 0 } label ? label : g.Key)}: " +
            string.Join("・", g.GroupBy(x => x.Operation).Select(o => $"{OpLabel(o.Key)}{o.Count()}"))));

    public static string OpLabel(string op) => op switch { "INSERT" => "追加", "UPDATE" => "更新", "DELETE" => "削除", _ => op };

    private static string Truncate(string s, int n) => s.Length > n ? s[..n] : s;
}
