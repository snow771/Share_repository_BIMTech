using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>
/// DB編集画面の窓口。テーブル一覧・列・外部キーをまとめて読み込んでキャッシュし、
/// 表示（ページ取得）・保存・関連図の材料を提供する。接続設定が変わったらキャッシュを捨てる。
/// </summary>
public sealed class DatabaseEditor
{
    private sealed record SchemaCache(string ConnectionKey, List<DbTableInfo> Tables, List<ColumnRef> Columns, List<ForeignKeyRef> ForeignKeys);

    private readonly DbSchemaRepository _schema = new();
    private readonly TableEditRepository _edit = new();
    private SchemaCache? _cache;

    public async Task<DbTablesResult> ListTablesAsync(DbSettings settings, bool refresh, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh, ct);
        return new DbTablesResult(settings.Server, settings.Database, cache.Tables);
    }

    public async Task<DbTableDetail> GetTableAsync(DbSettings settings, string schema, string name, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh: false, ct);
        var table = Find(cache, schema, name);
        await using var conn = await new SqlConnectionFactory(settings).OpenAsync(ct);
        var columns = await _schema.GetColumnsAsync(conn, table.Schema, table.Name, ct);
        var hood = RelationInferenceService.Build(table, cache.Tables, cache.Columns, cache.ForeignKeys);
        return new DbTableDetail(table, columns, columns.Any(c => c.IsPrimaryKey), hood);
    }

    /// <summary>AI アシスタント用: テーブル一覧と全列名（キャッシュ）。</summary>
    public async Task<(List<DbTableInfo> Tables, List<ColumnRef> Columns)> CatalogAsync(DbSettings settings, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh: false, ct);
        return (cache.Tables, cache.Columns);
    }

    /// <summary>
    /// AI アシスタント用の読み取り専用検索。テーブル・列は実在するものだけに絞ってから SELECT する。
    /// すべての語を含む行が無ければ、どれかの語を含む行で探し直す。
    /// </summary>
    public async Task<LookupRows> LookupAsync(DbSettings settings, LookupPlan plan, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh: false, ct);
        var table = Find(cache, plan.Schema, plan.Table);
        await using var conn = await new SqlConnectionFactory(settings).OpenAsync(ct);
        var columns = await _schema.GetColumnsAsync(conn, table.Schema, table.Name, ct);
        var show = DbLookupService.ShowColumns(columns, plan.ShowColumns);
        var search = DbLookupService.SearchColumns(columns, plan.SearchColumns);
        var repo = new TableLookupRepository();

        var (rows, total) = await repo.SearchAsync(conn, table.Schema, table.Name, show, search, plan.Keywords, matchAll: true,
            DbLookupService.MaxRows, settings.CommandTimeoutSec, ct);
        var matchedAll = true;
        if (total == 0 && plan.Keywords.Count > 1)
        {
            (rows, total) = await repo.SearchAsync(conn, table.Schema, table.Name, show, search, plan.Keywords, matchAll: false,
                DbLookupService.MaxRows, settings.CommandTimeoutSec, ct);
            matchedAll = false;
        }
        return new LookupRows(table, show.Select(c => c.Name).ToList(), rows, total, plan.Keywords, matchedAll);
    }

    public async Task<DbPageResult> QueryAsync(DbSettings settings, DbQueryRequest req, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh: false, ct);
        var table = Find(cache, req.Schema, req.Name);
        req.Schema = table.Schema;
        req.Name = table.Name;
        await using var conn = await new SqlConnectionFactory(settings).OpenAsync(ct);
        var columns = await _schema.GetColumnsAsync(conn, table.Schema, table.Name, ct);
        return await _edit.QueryPageAsync(conn, req, columns, settings.CommandTimeoutSec, ct);
    }

    public async Task<DbSaveResult> SaveAsync(DbSettings settings, DbChangeSet changes, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh: false, ct);
        var table = Find(cache, changes.Schema, changes.Name);
        changes.Schema = table.Schema;
        changes.Name = table.Name;
        await using var conn = await new SqlConnectionFactory(settings).OpenAsync(ct);
        var columns = await _schema.GetColumnsAsync(conn, table.Schema, table.Name, ct);

        // 変更履歴（だれが・いつ・何を）。履歴テーブルが作れない場合は保存前にわかるよう先に確認する
        var changeLog = new ChangeLogRepository(settings.Tables);
        await changeLog.EnsureAsync(conn, null, ct);
        var log = new ChangeCollector(string.IsNullOrWhiteSpace(changes.Source) ? "DB編集" : changes.Source!);

        // 物件情報の行を削除するときは、同じ project_id を持つ関連テーブルの行も同じトランザクションで先に削除する
        var related = new Dictionary<DbTableInfo, int>();
        Func<SqlConnection, SqlTransaction, DbRowKey, Task>? cascade = null;
        var pidCol = ProjectIdColumn(settings, table, columns);
        if (pidCol is not null && changes.Deletes.Count > 0)
        {
            // 見積DB（取決め実績）が紐づいている物件は消さない（取決めの履歴を連鎖削除しないため）
            await EnsureNoLinkedEstimatesAsync(conn, cache,
                changes.Deletes.Select(d => d.Key.TryGetValue(pidCol.Name, out var v) ? v : null), ct);
            var targets = RelatedProjectTables(cache, table);
            cascade = async (c, tx, key) =>
            {
                if (!key.Key.TryGetValue(pidCol.Name, out var pid) || string.IsNullOrWhiteSpace(pid)) return;
                foreach (var (t, col) in targets)
                {
                    await using var cmd = new SqlCommand(
                        $"DELETE FROM {TableEditRepository.Q(t.Schema)}.{TableEditRepository.Q(t.Name)} OUTPUT DELETED.* WHERE {TableEditRepository.Q(col)} = @pid", c, tx)
                    { CommandTimeout = settings.CommandTimeoutSec };
                    cmd.Parameters.AddWithValue("@pid", pid);
                    var n = await log.ExecuteAsync(cmd, t.Schema, t.Name, "DELETE", ct);
                    if (n > 0) related[t] = related.TryGetValue(t, out var cur) ? cur + n : n;
                }
            };
        }

        var result = await _edit.SaveAsync(conn, changes, columns, settings.CommandTimeoutSec, ct, cascade, log,
            beforeCommit: (c, tx) => changeLog.WriteAsync(c, tx, log, ct));
        _cache = null;   // 行数が変わるので一覧を取り直す
        return result with { Related = related.Select(kv => new DbRelatedDelete(kv.Key.Name, kv.Key.Label, kv.Value)).ToList() };
    }

    /// <summary>
    /// 物件情報の削除前の確認: 削除予定の project_id ごとに、関連テーブルで一緒に消える行数を数える。
    /// 物件情報テーブル以外なら空を返す。
    /// </summary>
    public async Task<List<DbRelatedDelete>> PreviewRelatedDeletesAsync(DbSettings settings, DbDeletePreviewRequest req, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(settings, refresh: false, ct);
        var table = Find(cache, req.Schema, req.Name);
        await using var conn = await new SqlConnectionFactory(settings).OpenAsync(ct);
        var columns = await _schema.GetColumnsAsync(conn, table.Schema, table.Name, ct);
        var pidCol = ProjectIdColumn(settings, table, columns);
        if (pidCol is null) return new List<DbRelatedDelete>();

        var ids = req.Deletes.Select(d => d.Key.TryGetValue(pidCol.Name, out var v) ? v : null)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        var list = new List<DbRelatedDelete>();
        if (ids.Count == 0) return list;
        await EnsureNoLinkedEstimatesAsync(conn, cache, ids, ct);

        foreach (var (t, col) in RelatedProjectTables(cache, table))
        {
            var ps = ids.Select((_, i) => $"@p{i}").ToList();
            await using var cmd = new SqlCommand(
                $"SELECT COUNT(*) FROM {TableEditRepository.Q(t.Schema)}.{TableEditRepository.Q(t.Name)} WHERE {TableEditRepository.Q(col)} IN ({string.Join(", ", ps)})", conn)
            { CommandTimeout = settings.CommandTimeoutSec };
            for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue(ps[i], ids[i]);
            var n = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (n > 0) list.Add(new DbRelatedDelete(t.Name, t.Label, n));
        }
        return list;
    }

    /// <summary>このテーブルが「物件情報」（設定のテーブル名）なら、その project_id 列。違えば null。</summary>
    private static DbColumnInfo? ProjectIdColumn(DbSettings settings, DbTableInfo table, List<DbColumnInfo> columns) =>
        string.Equals(table.Name, settings.Tables.Projects, StringComparison.OrdinalIgnoreCase)
            ? columns.FirstOrDefault(c => c.KeyName == "project_id")
            : null;

    /// <summary>
    /// 削除しようとしている物件に、見積DB の見積（OCR見積ヘッダ）が紐づいていれば止める。
    /// 見積には取決めの確定履歴があるため、物件の削除に合わせて連鎖削除はしない。
    /// </summary>
    private static async Task EnsureNoLinkedEstimatesAsync(SqlConnection conn, SchemaCache cache, IEnumerable<string?> projectIds, CancellationToken ct)
    {
        var ids = projectIds.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        var header = cache.Tables.FirstOrDefault(t => string.Equals(t.Name, Estimate.EstimateDb.Header, StringComparison.OrdinalIgnoreCase));
        if (ids.Count == 0 || header is null) return;
        var ps = ids.Select((_, i) => $"@p{i}").ToList();
        await using var cmd = new SqlCommand(
            $"SELECT Project_ID, COUNT(*) FROM {TableEditRepository.Q(header.Schema)}.{TableEditRepository.Q(header.Name)} WHERE Project_ID IN ({string.Join(", ", ps)}) GROUP BY Project_ID", conn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue(ps[i], ids[i]!);
        var linked = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) linked.Add($"{r.GetValue(0)}（見積 {r.GetInt32(1)} 件）");
        }
        if (linked.Count > 0)
            throw new InvalidOperationException(
                $"見積DB の見積が紐づいている物件は削除できません: {string.Join("、", linked)}\n" +
                "AI-OCR 画面の「🗂 保存済み見積を開く」で、その見積を削除するか、物件の紐づけを外してから削除してください。");
    }

    /// <summary>project_id 列を持つ、物件情報以外のテーブル（とその列名）。見積DB の見積ヘッダは除く（上で止める）。</summary>
    private static List<(DbTableInfo Table, string Column)> RelatedProjectTables(SchemaCache cache, DbTableInfo projects) =>
        cache.Columns
            .Where(c => RelationInferenceService.KeyOf(c.Column) == "project_id")
            .Where(c => !(c.Schema == projects.Schema && c.Table == projects.Name))
            .Where(c => !string.Equals(c.Table, Estimate.EstimateDb.Header, StringComparison.OrdinalIgnoreCase))
            .Select(c => (Table: cache.Tables.FirstOrDefault(t => t.Schema == c.Schema && t.Name == c.Table), c.Column))
            .Where(x => x.Table is not null)
            .Select(x => (x.Table!, x.Column))
            .ToList();

    public void Invalidate() => _cache = null;

    // ------------------------------------------------------------------

    private async Task<SchemaCache> GetCacheAsync(DbSettings settings, bool refresh, CancellationToken ct)
    {
        var key = $"{settings.Server}|{settings.Database}|{settings.UseWindowsAuth}|{settings.User}";
        if (!refresh && _cache is not null && _cache.ConnectionKey == key) return _cache;

        await using var conn = await new SqlConnectionFactory(settings).OpenAsync(ct);
        var roles = AppRoles(settings.Tables);
        var tables = (await _schema.ListTablesAsync(conn, ct))
            .Select(t =>
            {
                var label = RelationInferenceService.JapaneseLabel(t.Name);
                return new DbTableInfo(t.Schema, t.Name, RelationInferenceService.BaseName(t.Name),
                    label.Length > 0 ? label : t.Name, t.Rows,
                    roles.TryGetValue(t.Name, out var role) ? role : null);
            })
            .ToList();
        var columns = await _schema.ListAllColumnsAsync(conn, ct);
        var fks = await _schema.ListForeignKeysAsync(conn, ct);
        _cache = new SchemaCache(key, tables, columns, fks);
        return _cache;
    }

    /// <summary>画面から来たテーブル名は必ず実在するものと照合する（SQL に埋め込む前の安全確認）。</summary>
    private static DbTableInfo Find(SchemaCache cache, string schema, string name) =>
        cache.Tables.FirstOrDefault(t => t.Schema == schema && t.Name == name)
        ?? throw new InvalidOperationException($"テーブル {schema}.{name} が見つかりません。一覧を更新してください。");

    /// <summary>このアプリが読んでいるテーブル → 役割名</summary>
    private static Dictionary<string, string> AppRoles(TableNames t)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string table, string role)
        {
            if (!string.IsNullOrWhiteSpace(table)) map.TryAdd(table, role);
        }
        Add(t.Projects, "物件情報");
        Add(t.Assess, "査定単価");
        Add(t.Revise, "修正単価");
        Add(t.MaterialMaster, "標準単価");
        Add(t.Category, "カテゴリ");
        Add(t.IndexAllocation, "指数割当");
        Add(t.Deflator, "補正係数");
        Add(t.Term, "期間");
        Add(t.Index, "指数");
        return map;
    }
}
