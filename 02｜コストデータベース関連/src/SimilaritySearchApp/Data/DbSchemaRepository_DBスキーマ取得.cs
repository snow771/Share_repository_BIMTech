using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>DB の構造（テーブル一覧・列・外部キー）の取得。システムカタログ（sys.*）を読むだけで、データは変更しない。</summary>
public sealed class DbSchemaRepository
{
    public async Task<List<(string Schema, string Name, long Rows)>> ListTablesAsync(SqlConnection conn, CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.name, t.name, SUM(p.rows)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            WHERE t.is_ms_shipped = 0 AND t.name <> 'sysdiagrams'
            GROUP BY s.name, t.name
            ORDER BY t.name
            """;
        var list = new List<(string, string, long)>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add((r.GetString(0), r.GetString(1), Convert.ToInt64(r.GetValue(2))));
        return list;
    }

    /// <summary>1テーブルの列定義（主キー・IDENTITY・計算列の情報付き）。</summary>
    public async Task<List<DbColumnInfo>> GetColumnsAsync(SqlConnection conn, string schema, string table, CancellationToken ct = default, SqlTransaction? tx = null)
    {
        const string sql = """
            SELECT c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed,
                   CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(QUOTENAME(@s) + '.' + QUOTENAME(@t))
            ORDER BY c.column_id
            """;
        await using var cmd = new SqlCommand(sql, conn, tx);  // 取り消し中（トランザクション内）でも読めるように
        cmd.Parameters.AddWithValue("@s", schema);
        cmd.Parameters.AddWithValue("@t", table);

        var list = new List<DbColumnInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var type = r.GetString(1);
            var bytes = (int)r.GetInt16(2);
            int? maxLen = type.ToLowerInvariant() switch
            {
                "nchar" or "nvarchar" => bytes == -1 ? -1 : bytes / 2,
                "char" or "varchar" => bytes,
                _ => null,
            };
            var isIdentity = r.GetBoolean(6);
            var isComputed = r.GetBoolean(7);
            list.Add(new DbColumnInfo(
                Name: r.GetString(0), TypeName: type, MaxLength: maxLen,
                Precision: r.GetByte(3), Scale: r.GetByte(4),
                IsNullable: r.GetBoolean(5), IsIdentity: isIdentity, IsComputed: isComputed,
                IsPrimaryKey: r.GetInt32(8) == 1,
                IsEditable: !isIdentity && !isComputed && DbValueConverter.IsSupported(type),
                KeyName: RelationInferenceService.KeyOf(r.GetString(0))));
        }
        if (list.Count == 0) throw new InvalidOperationException($"テーブル {schema}.{table} が見つかりません。");
        return list;
    }

    /// <summary>全テーブルの列名（関連推定用）。</summary>
    public async Task<List<ColumnRef>> ListAllColumnsAsync(SqlConnection conn, CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.name, t.name, c.name,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM sys.indexes i
                       JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                       WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id
                   ) THEN 1 ELSE 0 END
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            """;
        var list = new List<ColumnRef>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new ColumnRef(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1));
        return list;
    }

    public async Task<List<ForeignKeyRef>> ListForeignKeysAsync(SqlConnection conn, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ps.name, pt.name, pc.name, rs.name, rt.name, rc.name
            FROM sys.foreign_key_columns fkc
            JOIN sys.tables pt ON pt.object_id = fkc.parent_object_id
            JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            """;
        var list = new List<ForeignKeyRef>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ForeignKeyRef(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        return list;
    }
}
