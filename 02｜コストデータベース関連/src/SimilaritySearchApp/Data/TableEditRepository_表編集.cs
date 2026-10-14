using System.Text;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>
/// 任意テーブルの表示（ページ単位）と、追加・更新・削除の保存（DB編集画面用）。
///
/// 安全のための約束:
///   - テーブル名・列名は必ず DB から取得した定義と照合してから SQL に埋め込む（[ ] でエスケープ）。値はすべてパラメータ。
///   - 保存は1トランザクション。1件でも失敗すれば全体を取り消す。
///   - 更新・削除は「ちょうど1行」に当たったときだけ成功とする。主キーが無い表は全列の元の値で行を特定し、
///     同じ内容の行が複数ある・他の人が先に変更した、などで1行に絞れなければ取り消す。
/// </summary>
public sealed class TableEditRepository
{
    public async Task<DbPageResult> QueryPageAsync(
        SqlConnection conn, DbQueryRequest req, List<DbColumnInfo> columns, int commandTimeoutSec, CancellationToken ct = default)
    {
        var table = Q(req.Schema) + "." + Q(req.Name);
        var pageSize = Math.Clamp(req.PageSize, 10, 1000);
        var page = Math.Max(1, req.Page);
        var hasPk = columns.Any(c => c.IsPrimaryKey);

        // 表示列（バイナリは中身を読まず長さだけ）
        var select = string.Join(", ", columns.Select(c => DbValueConverter.IsBinary(c.TypeName)
            ? $"DATALENGTH({Q(c.Name)}) AS {Q(c.Name)}"
            : Q(c.Name)));

        // 全列の部分一致検索
        var where = "";
        var parameters = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var searchable = columns.Where(c => !DbValueConverter.IsBinary(c.TypeName) && c.TypeName.ToLowerInvariant() is not ("xml" or "geography" or "geometry" or "hierarchyid" or "sql_variant")).ToList();
            if (searchable.Count > 0)
            {
                // 全角カナ・半角カナ（ボード / ﾎﾞｰﾄﾞ）のどちらで入力しても見つかるよう、表記ゆれもまとめて探す
                var variants = DbLookupService.KeywordVariants(req.Search.Trim());
                for (var i = 0; i < variants.Count; i++) parameters.Add(new SqlParameter($"@q{i}", "%" + EscapeLike(variants[i]) + "%"));
                where = "WHERE " + string.Join(" OR ", searchable.SelectMany(c =>
                    Enumerable.Range(0, variants.Count).Select(i => $"{AsText(c)} LIKE @q{i} ESCAPE '\\'")));
            }
        }

        // 並び順: 指定列 → 主キー → 先頭列
        var orderParts = new List<string>();
        var sortCol = columns.FirstOrDefault(c => c.Name == req.SortColumn && DbValueConverter.IsComparable(c.TypeName));
        if (sortCol is not null) orderParts.Add($"{Q(sortCol.Name)} {(req.SortDesc ? "DESC" : "ASC")}");
        orderParts.AddRange(columns.Where(c => c.IsPrimaryKey && c != sortCol).Select(c => Q(c.Name)));
        if (orderParts.Count == 0)
        {
            var first = columns.FirstOrDefault(c => DbValueConverter.IsComparable(c.TypeName));
            orderParts.Add(first is null ? "(SELECT NULL)" : Q(first.Name));
        }

        int total;
        await using (var count = new SqlCommand($"SELECT COUNT(*) FROM {table} {where}", conn) { CommandTimeout = commandTimeoutSec })
        {
            count.Parameters.AddRange(parameters.Select(Clone).ToArray());
            total = (int)(await count.ExecuteScalarAsync(ct) ?? 0);
        }

        var sql = $"SELECT {select} FROM {table} {where} ORDER BY {string.Join(", ", orderParts)} OFFSET @off ROWS FETCH NEXT @n ROWS ONLY";
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = commandTimeoutSec };
        cmd.Parameters.AddRange(parameters.Select(Clone).ToArray());
        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@n", pageSize);

        var rows = new List<string?[]>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var row = new string?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var v = r.IsDBNull(i) ? null : r.GetValue(i);
                row[i] = DbValueConverter.IsBinary(columns[i].TypeName) && v is not null
                    ? $"(バイナリ {Convert.ToInt64(v):N0} bytes)"
                    : DbValueConverter.Format(v);
            }
            rows.Add(row);
        }
        return new DbPageResult(columns, rows, total, page, pageSize, hasPk);
    }

    /// <param name="beforeDelete">各行を削除する直前に、同じトランザクションで呼ばれる（関連テーブルの一括削除用）</param>
    /// <param name="log">変更前後の値を集める（変更履歴用）。OUTPUT 句で変更と同時に取得する</param>
    /// <param name="beforeCommit">コミット直前に同じトランザクションで呼ばれる（変更履歴の書き込み用）</param>
    public async Task<DbSaveResult> SaveAsync(
        SqlConnection conn, DbChangeSet changes, List<DbColumnInfo> columns, int commandTimeoutSec, CancellationToken ct = default,
        Func<SqlConnection, SqlTransaction, DbRowKey, Task>? beforeDelete = null,
        ChangeCollector? log = null,
        Func<SqlConnection, SqlTransaction, Task>? beforeCommit = null)
    {
        log ??= new ChangeCollector("DB編集");
        var table = Q(changes.Schema) + "." + Q(changes.Name);
        var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var keyColumns = columns.Any(c => c.IsPrimaryKey)
            ? columns.Where(c => c.IsPrimaryKey).ToList()
            : columns.Where(c => DbValueConverter.IsComparable(c.TypeName)).ToList();

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        int deleted = 0, updated = 0, inserted = 0;
        var current = "";
        try
        {
            // 削除 → 更新 → 追加 の順（主キーを入れ替えるような操作でも衝突しにくい）
            foreach (var d in changes.Deletes)
            {
                current = $"{d.RowNo}行目の削除";
                if (beforeDelete is not null) await beforeDelete(conn, tx, d);
                await using var cmd = NewCommand(conn, tx, commandTimeoutSec);
                var cond = KeyCondition(cmd, d.Key, keyColumns);
                cmd.CommandText = $"DELETE FROM {table} OUTPUT DELETED.* WHERE {cond}";
                ExpectOne(await log.ExecuteAsync(cmd, changes.Schema, changes.Name, "DELETE", ct), current);
                deleted++;
            }

            foreach (var u in changes.Updates)
            {
                current = $"{u.RowNo}行目の更新";
                if (u.Values.Count == 0) continue;
                await using var cmd = NewCommand(conn, tx, commandTimeoutSec);
                var sets = new List<string>();
                var i = 0;
                foreach (var (name, text) in u.Values)
                {
                    var col = EditableColumn(byName, name);
                    var p = $"@v{i++}";
                    sets.Add($"{Q(col.Name)} = {p}");
                    cmd.Parameters.Add(Param(p, col, DbValueConverter.Parse(text, col)));
                }
                var cond = KeyCondition(cmd, u.Key, keyColumns);
                cmd.CommandText = $"UPDATE {table} SET {string.Join(", ", sets)} OUTPUT DELETED.*, INSERTED.* WHERE {cond}";
                ExpectOne(await log.ExecuteAsync(cmd, changes.Schema, changes.Name, "UPDATE", ct), current);
                updated++;
            }

            foreach (var ins in changes.Inserts)
            {
                current = $"{ins.RowNo}行目の追加";
                await using var cmd = NewCommand(conn, tx, commandTimeoutSec);
                var names = new List<string>();
                var values = new List<string>();
                var i = 0;
                foreach (var (name, text) in ins.Values)
                {
                    var col = EditableColumn(byName, name);
                    if (string.IsNullOrEmpty(text)) continue;   // 空欄は列の既定値に任せる（既定値の無い必須列は DB のエラーで知らせる）
                    var value = DbValueConverter.Parse(text, col);
                    var p = $"@v{i++}";
                    names.Add(Q(col.Name));
                    values.Add(p);
                    cmd.Parameters.Add(Param(p, col, value));
                }
                // 入力が無い NOT NULL 列（既定値なし）は DB 側のエラーで知らせる
                cmd.CommandText = names.Count == 0
                    ? $"INSERT INTO {table} OUTPUT INSERTED.* DEFAULT VALUES"
                    : $"INSERT INTO {table} ({string.Join(", ", names)}) OUTPUT INSERTED.* VALUES ({string.Join(", ", values)})";
                await log.ExecuteAsync(cmd, changes.Schema, changes.Name, "INSERT", ct);
                inserted++;
            }

            current = "変更履歴の記録";
            if (beforeCommit is not null) await beforeCommit(conn, tx);
            await tx.CommitAsync(ct);
            return new DbSaveResult(inserted, updated, deleted);
        }
        catch (Exception e)
        {
            await tx.RollbackAsync(CancellationToken.None);
            var reason = e is SqlException se ? Explain(se) : e.Message;
            throw new InvalidOperationException($"{current}で失敗したため、すべての変更を取り消しました。\n{reason}", e);
        }
    }

    // ------------------------------------------------------------------

    public static string Q(string name) => "[" + (name ?? "").Replace("]", "]]") + "]";

    private static SqlCommand NewCommand(SqlConnection conn, SqlTransaction tx, int timeout) =>
        new() { Connection = conn, Transaction = tx, CommandTimeout = timeout };

    private static DbColumnInfo EditableColumn(Dictionary<string, DbColumnInfo> byName, string name)
    {
        if (!byName.TryGetValue(name, out var col)) throw new InvalidOperationException($"列「{name}」はこのテーブルにありません。");
        if (!col.IsEditable) throw new InvalidOperationException($"列「{name}」は編集できません（自動採番・計算列など）。");
        return col;
    }

    /// <summary>行を特定する WHERE 句。NULL は IS NULL で比較する。</summary>
    private static string KeyCondition(SqlCommand cmd, Dictionary<string, string?> key, List<DbColumnInfo> keyColumns)
    {
        if (keyColumns.Count == 0) throw new InvalidOperationException("行を特定できる列がないため、更新・削除できません。");
        var parts = new List<string>();
        var i = 0;
        foreach (var col in keyColumns)
        {
            if (!key.TryGetValue(col.Name, out var text))
                throw new InvalidOperationException($"行を特定する列「{col.Name}」の値がありません。画面を再読込してください。");
            if (text is null)
            {
                parts.Add($"{Q(col.Name)} IS NULL");
                continue;
            }
            var p = $"@k{i++}";
            parts.Add($"{Q(col.Name)} = {p}");
            // キーは読込時の値なので、NULL 不可・文字数などの入力チェックは通さずに型変換だけ行う
            var loose = col with { IsNullable = true, MaxLength = null };
            cmd.Parameters.Add(Param(p, col, DbValueConverter.Parse(text, loose)));
        }
        return string.Join(" AND ", parts);
    }

    private static SqlParameter Param(string name, DbColumnInfo col, object value)
    {
        var p = new SqlParameter(name, DbValueConverter.SqlType(col.TypeName)) { Value = value };
        if (col.MaxLength is int len) p.Size = len;                   // -1 = max
        if (p.SqlDbType == System.Data.SqlDbType.Decimal)
        {
            p.Precision = (byte)col.Precision;
            p.Scale = (byte)col.Scale;
        }
        return p;
    }

    private static void ExpectOne(int affected, string what)
    {
        if (affected == 1) return;
        throw new InvalidOperationException(affected == 0
            ? "対象の行が見つかりません（他の人が先に変更・削除した可能性があります）。再読込してからやり直してください。"
            : $"同じ内容の行が {affected} 行あり、1行に特定できません（主キーの無い表です）。");
    }

    /// <summary>検索用に列を文字列化する式。日時は画面と同じ yyyy-mm-dd 形式（style 121）、それ以外は既定の変換。</summary>
    private static string AsText(DbColumnInfo c) =>
        c.TypeName.ToLowerInvariant() is "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset"
            ? $"CONVERT(nvarchar(40), {Q(c.Name)}, 121)"
            : $"CONVERT(nvarchar(4000), {Q(c.Name)})";

    private static SqlParameter Clone(SqlParameter p) => new(p.ParameterName, p.Value);

    private static string EscapeLike(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch is '%' or '_' or '[' or '\\') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>よくある SQL Server エラーを日本語で説明する。</summary>
    private static string Explain(SqlException e) => e.Number switch
    {
        547 => "関連する他のテーブルとの整合性（外部キー制約）に反します。参照されている行は削除できず、存在しないコードは登録できません。\n" + e.Message,
        2627 or 2601 => "主キーまたは一意の列が、既存の行と重複しています。\n" + e.Message,
        515 => "必須の列（NULL 不可）が空です。\n" + e.Message,
        8152 or 2628 => "文字数が列の上限を超えています。\n" + e.Message,
        229 or 230 => "このテーブルを変更する権限がありません。\n" + e.Message,
        _ => e.Message,
    };
}
