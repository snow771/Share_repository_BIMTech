using System.Data;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Data;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Stencil;

public sealed record StencilRegisterResult(
    int ProjectsInserted, int ProjectsUpdated, int AssessRows, int ReviseRows, int AssessDeleted, int ReviseDeleted, List<string> Warnings);

/// <summary>
/// 読込結果の DB 登録。
///   ① 物件情報: 新規は INSERT、既存（同じ project_id）は UPDATE
///   ② 査定・修正単価: その物件の既存行を削除してから一括登録（入れ替え）
/// すべて1トランザクション。1件でも失敗すれば全体を取り消す。
/// テーブル名は設定（DB接続 → テーブル名）のもの。列名は StencilColumnMap で解決する。
/// </summary>
public sealed class StencilDbRegistrar
{
    private readonly DbSettings _settings;
    private readonly DbSchemaRepository _schema = new();

    public StencilDbRegistrar(DbSettings settings) => _settings = settings;

    private TableNames Tables => _settings.Tables;

    /// <summary>各ファイルの project_id が DB に既にあるかを調べ、結果を書き込む。</summary>
    public async Task CheckExistingAsync(StencilScanResult scan, CancellationToken ct = default)
    {
        await using var conn = await new SqlConnectionFactory(_settings).OpenAsync(ct);
        var projects = await OpenTableAsync(conn, Tables.Projects, ct);
        var assess = await OpenTableAsync(conn, Tables.Assess, ct);
        var revise = await OpenTableAsync(conn, Tables.Revise, ct);

        var projectIds = await DistinctIdsAsync(conn, projects, StencilColumnMap.ProjectColumns["project_id"], ct);
        var assessCounts = await CountByIdAsync(conn, assess, ct);
        var reviseCounts = await CountByIdAsync(conn, revise, ct);

        foreach (var f in scan.Files.Where(f => f.ProjectId is not null))
        {
            var id = f.ProjectId!;
            f.ExistingAssessRows = assessCounts.TryGetValue(id, out var a) ? a : 0;
            f.ExistingReviseRows = reviseCounts.TryGetValue(id, out var r) ? r : 0;
            f.ExistsInDb = projectIds.Contains(id) || f.ExistingAssessRows > 0 || f.ExistingReviseRows > 0;
        }
        scan.DbChecked = true;
        scan.DbMessage = $"{_settings.Server} / {_settings.Database}";
    }

    public async Task<StencilRegisterResult> RegisterAsync(
        StencilScanResult scan, IReadOnlyCollection<int> fileIndexes, bool doProjectInfo, bool doUnitPrice, CancellationToken ct = default)
    {
        var files = scan.Files.Where(f => fileIndexes.Contains(f.Index) && f.IsValid).ToList();
        if (files.Count == 0) throw new InvalidOperationException("登録する物件が選ばれていません。");
        var dup = files.GroupBy(f => f.ProjectId).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidOperationException($"同じ project_id（{dup.Key}）の物件が複数選ばれています。どれか1件にしてください。");

        await using var conn = await new SqlConnectionFactory(_settings).OpenAsync(ct);
        var projects = await OpenTableAsync(conn, Tables.Projects, ct);
        var assess = doUnitPrice ? await OpenTableAsync(conn, Tables.Assess, ct) : null;
        var revise = doUnitPrice ? await OpenTableAsync(conn, Tables.Revise, ct) : null;

        var pidCol = StencilColumnMap.Resolve(projects.Columns, StencilColumnMap.ProjectColumns["project_id"])
                     ?? throw new InvalidOperationException($"{Tables.Projects} に project_id の列が見つかりません。");
        var existing = await DistinctIdsAsync(conn, projects, pidCol.Name, ct);

        var warnings = new List<string>();
        var missing = StencilColumnMap.ProjectColumns.Values.Where(n => StencilColumnMap.Resolve(projects.Columns, n) is null).ToList();
        if (doProjectInfo && missing.Count > 0) warnings.Add($"{Tables.Projects} に無い列は登録しませんでした: {string.Join(", ", missing)}");

        // 変更履歴（だれが・いつ・何を）。履歴テーブルが作れない場合は登録前にわかるよう先に確認する
        var changeLog = new ChangeLogRepository(Tables);
        await changeLog.EnsureAsync(conn, null, ct);
        var log = new ChangeCollector("Stencile登録");

        int inserted = 0, updated = 0, assessRows = 0, reviseRows = 0, assessDeleted = 0, reviseDeleted = 0;
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        var current = "";
        try
        {
            foreach (var f in files)
            {
                current = $"{f.FileName}（{f.ProjectId}）";
                if (doProjectInfo && f.ProjectInfo is not null)
                {
                    var values = ProjectValues(projects.Columns, f, warnings);
                    if (existing.Contains(f.ProjectId!))
                    {
                        await UpdateProjectAsync(conn, tx, projects, pidCol, f.ProjectId!, values, log, ct);
                        updated++;
                    }
                    else
                    {
                        await InsertProjectAsync(conn, tx, projects, values, log, ct);
                        inserted++;
                    }
                }
                if (doUnitPrice && f.UnitPrices is not null)
                {
                    assessDeleted += await DeleteByProjectAsync(conn, tx, assess!, f.ProjectId!, log, ct);
                    reviseDeleted += await DeleteByProjectAsync(conn, tx, revise!, f.ProjectId!, log, ct);
                    assessRows += await BulkInsertPricesAsync(conn, tx, assess!, StencilColumnMap.AssessPrice, f.UnitPrices, u => u.AssessPrice, ct);
                    reviseRows += await BulkInsertPricesAsync(conn, tx, revise!, StencilColumnMap.RevisePrice, f.UnitPrices, u => u.RevisePrice, ct);
                    // 一括登録した行も履歴に残す（取り消しで削除できるよう、自動採番の番号も含めて読み直す）
                    await LogInsertedByProjectAsync(conn, tx, assess!, f.ProjectId!, log, ct);
                    await LogInsertedByProjectAsync(conn, tx, revise!, f.ProjectId!, log, ct);
                }
            }
            current = "変更履歴の記録";
            log.Summary = $"Stencile登録: 物件 {files.Count} 件（新規 {inserted} / 更新 {updated}）・査定単価 {assessRows} 行・修正単価 {reviseRows} 行";
            await changeLog.WriteAsync(conn, tx, log, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException($"{current} の登録で失敗したため、すべて取り消しました。\n{e.Message}", e);
        }
        return new StencilRegisterResult(inserted, updated, assessRows, reviseRows, assessDeleted, reviseDeleted, warnings);
    }

    // ── テーブル・列 ────────────────────────────────────────

    private sealed record TableRef(string Schema, string Name, List<DbColumnInfo> Columns)
    {
        public string Quoted => $"{TableEditRepository.Q(Schema)}.{TableEditRepository.Q(Name)}";
    }

    /// <summary>テーブル名からスキーマと列定義を取得（見つからなければエラー）。</summary>
    private async Task<TableRef> OpenTableAsync(SqlConnection conn, string name, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT TOP 1 s.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = @t ORDER BY CASE WHEN s.name = 'dbo' THEN 0 ELSE 1 END",
            conn);
        cmd.Parameters.AddWithValue("@t", name);
        var schema = await cmd.ExecuteScalarAsync(ct) as string
                     ?? throw new InvalidOperationException($"テーブル {name} が見つかりません（設定 → DB接続 → テーブル名 を確認してください）。");
        return new TableRef(schema, name, await _schema.GetColumnsAsync(conn, schema, name, ct));
    }

    private static DbColumnInfo Require(TableRef t, string baseName) =>
        StencilColumnMap.Resolve(t.Columns, baseName)
        ?? throw new InvalidOperationException($"{t.Name} に列 {baseName} が見つかりません。");

    // ── 照合 ──────────────────────────────────────────────

    private static async Task<HashSet<string>> DistinctIdsAsync(SqlConnection conn, TableRef t, string pidBaseName, CancellationToken ct)
    {
        var col = Require(t, pidBaseName);
        await using var cmd = new SqlCommand($"SELECT DISTINCT {TableEditRepository.Q(col.Name)} FROM {t.Quoted} WHERE {TableEditRepository.Q(col.Name)} IS NOT NULL", conn);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) set.Add(Convert.ToString(r.GetValue(0))!.Trim());
        return set;
    }

    private static async Task<Dictionary<string, int>> CountByIdAsync(SqlConnection conn, TableRef t, CancellationToken ct)
    {
        var col = Require(t, StencilColumnMap.CostProjectId);
        var q = TableEditRepository.Q(col.Name);
        await using var cmd = new SqlCommand($"SELECT {q}, COUNT(*) FROM {t.Quoted} WHERE {q} IS NOT NULL GROUP BY {q}", conn);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) map[Convert.ToString(r.GetValue(0))!.Trim()] = r.GetInt32(1);
        return map;
    }

    // ── ① 物件情報 ──────────────────────────────────────────

    /// <summary>
    /// 出力列 → (DB列, DB に渡す値)。値は列の型に合わせて変換する（空欄は NULL）。
    /// 型に合わない値（例: 数値列に「有」）は NULL にして警告に残す（1項目のせいで全体が止まらないように）。
    /// NULL にできない列で変換できない場合だけエラーにする。
    /// </summary>
    private static List<(DbColumnInfo Col, object Value)> ProjectValues(List<DbColumnInfo> cols, StencilFileResult f, List<string> warnings)
    {
        var list = new List<(DbColumnInfo, object)>();
        foreach (var (outName, baseName) in StencilColumnMap.ProjectColumns)
        {
            var col = StencilColumnMap.Resolve(cols, baseName);
            if (col is null || !col.IsEditable) continue;
            f.ProjectInfo!.TryGetValue(outName, out var raw);
            try
            {
                list.Add((col, DbValueConverter.Parse(DbValueConverter.Format(raw), col)));
            }
            catch (FormatException e)
            {
                if (!col.IsNullable) throw new InvalidOperationException($"「{outName}」（列 {col.Name}）: {e.Message}");
                warnings.Add($"{f.ProjectId}: 「{outName}」の値「{raw}」は列 {col.Name}（{col.TypeName}）に入らないため空欄で登録しました。");
                list.Add((col, DBNull.Value));
            }
        }
        return list;
    }

    private static async Task InsertProjectAsync(
        SqlConnection conn, SqlTransaction tx, TableRef t, List<(DbColumnInfo Col, object Value)> values, ChangeCollector log, CancellationToken ct)
    {
        await using var cmd = new SqlCommand { Connection = conn, Transaction = tx };
        var names = new List<string>();
        var ps = new List<string>();
        for (var i = 0; i < values.Count; i++)
        {
            names.Add(TableEditRepository.Q(values[i].Col.Name));
            ps.Add($"@p{i}");
            cmd.Parameters.Add(Param($"@p{i}", values[i].Col, values[i].Value));
        }
        cmd.CommandText = $"INSERT INTO {t.Quoted} ({string.Join(", ", names)}) OUTPUT INSERTED.* VALUES ({string.Join(", ", ps)})";
        await log.ExecuteAsync(cmd, t.Schema, t.Name, "INSERT", ct);
    }

    private static async Task UpdateProjectAsync(
        SqlConnection conn, SqlTransaction tx, TableRef t, DbColumnInfo pidCol, string projectId,
        List<(DbColumnInfo Col, object Value)> values, ChangeCollector log, CancellationToken ct)
    {
        await using var cmd = new SqlCommand { Connection = conn, Transaction = tx };
        var sets = new List<string>();
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i].Col.Name == pidCol.Name) continue;
            sets.Add($"{TableEditRepository.Q(values[i].Col.Name)} = @p{i}");
            cmd.Parameters.Add(Param($"@p{i}", values[i].Col, values[i].Value));
        }
        if (sets.Count == 0) return;
        cmd.Parameters.Add(Param("@pid", pidCol, DbValueConverter.Parse(projectId, pidCol)));
        cmd.CommandText = $"UPDATE {t.Quoted} SET {string.Join(", ", sets)} OUTPUT DELETED.*, INSERTED.* WHERE {TableEditRepository.Q(pidCol.Name)} = @pid";
        await log.ExecuteAsync(cmd, t.Schema, t.Name, "UPDATE", ct);
    }

    // ── ② 単価 ────────────────────────────────────────────

    private static async Task<int> DeleteByProjectAsync(SqlConnection conn, SqlTransaction tx, TableRef t, string projectId, ChangeCollector log, CancellationToken ct)
    {
        var col = Require(t, StencilColumnMap.CostProjectId);
        await using var cmd = new SqlCommand($"DELETE FROM {t.Quoted} OUTPUT DELETED.* WHERE {TableEditRepository.Q(col.Name)} = @pid", conn, tx) { CommandTimeout = 600 };
        cmd.Parameters.Add(Param("@pid", col, DbValueConverter.Parse(projectId, col)));
        return await log.ExecuteAsync(cmd, t.Schema, t.Name, "DELETE", ct);
    }

    /// <summary>その物件の行（いま一括登録したもの）を読み、追加として履歴に記録する。</summary>
    private static async Task LogInsertedByProjectAsync(SqlConnection conn, SqlTransaction tx, TableRef t, string projectId, ChangeCollector log, CancellationToken ct)
    {
        var col = Require(t, StencilColumnMap.CostProjectId);
        await using var cmd = new SqlCommand($"SELECT * FROM {t.Quoted} WHERE {TableEditRepository.Q(col.Name)} = @pid", conn, tx) { CommandTimeout = 600 };
        cmd.Parameters.Add(Param("@pid", col, DbValueConverter.Parse(projectId, col)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) log.Add(t.Schema, t.Name, "INSERT", null, ChangeCollector.ReadRow(r, 0, r.FieldCount));
    }

    /// <summary>単価行を SqlBulkCopy で一括登録（行数が多いため1行ずつの INSERT より大幅に速い）。</summary>
    private static async Task<int> BulkInsertPricesAsync(
        SqlConnection conn, SqlTransaction tx, TableRef t, string priceBaseName, List<UnitPriceRow> rows,
        Func<UnitPriceRow, double> price, CancellationToken ct)
    {
        var pid = Require(t, StencilColumnMap.CostProjectId);
        var code = Require(t, StencilColumnMap.CostWorkTypeCode);
        var trade = StencilColumnMap.Resolve(t.Columns, StencilColumnMap.CostTrade);
        var work = StencilColumnMap.Resolve(t.Columns, StencilColumnMap.CostWork);
        var item = StencilColumnMap.Resolve(t.Columns, StencilColumnMap.CostItem);
        var priceCol = Require(t, priceBaseName);

        var table = new DataTable();
        table.Columns.Add(pid.Name, typeof(string));
        table.Columns.Add(code.Name, typeof(string));
        if (trade is not null) table.Columns.Add(trade.Name, typeof(string));
        if (work is not null) table.Columns.Add(work.Name, typeof(string));
        if (item is not null) table.Columns.Add(item.Name, typeof(string));
        table.Columns.Add(priceCol.Name, typeof(decimal));

        foreach (var r in rows)
        {
            var dr = table.NewRow();
            dr[pid.Name] = r.ProjectId;
            dr[code.Name] = r.WorkTypeCode;
            if (trade is not null) dr[trade.Name] = (object?)r.Trade ?? DBNull.Value;
            if (work is not null) dr[work.Name] = (object?)r.Work ?? DBNull.Value;
            if (item is not null) dr[item.Name] = (object?)r.Item ?? DBNull.Value;
            dr[priceCol.Name] = (decimal)price(r);
            table.Rows.Add(dr);
        }

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = t.Quoted,
            BulkCopyTimeout = 600,
        };
        foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(table, ct);
        return table.Rows.Count;
    }

    private static SqlParameter Param(string name, DbColumnInfo col, object value)
    {
        var p = new SqlParameter(name, DbValueConverter.SqlType(col.TypeName)) { Value = value };
        if (col.MaxLength is int len) p.Size = len;
        if (p.SqlDbType == SqlDbType.Decimal)
        {
            p.Precision = (byte)col.Precision;
            p.Scale = (byte)col.Scale;
        }
        return p;
    }
}
