using System.Data;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Data;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Estimate.Data;

/// <summary>
/// 実DBの列定義（型・長さ・精度・小数桁・NULL可否）に合わせて値を検査し、SQL パラメータを作る。
///
/// 既存の列は旧型（金額 decimal(18,2)・数量 decimal(18,4)・名称 nvarchar(500) など）のこともあれば、
/// 新しい型のこともあるため、型は決め打ちせず実DBから読む。
/// 収まらない値は<b>丸めず・切り詰めず</b>、どこの・どの列の・どんな値かを Issues に記録する
/// （呼び出し側は Issues があれば保存を止めて、内容を画面に表示する）。
/// </summary>
public sealed class ColumnGuard
{
    private readonly Dictionary<string, Dictionary<string, DbColumnInfo>> _tables;

    public List<string> Issues { get; } = new();

    public ColumnGuard(Dictionary<string, List<DbColumnInfo>> tables)
    {
        _tables = tables.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>実DBから表の列定義を読む。表が無ければ、何が足りないかを示して止める。</summary>
    public static async Task<ColumnGuard> LoadAsync(SqlConnection conn, SqlTransaction? tx, IEnumerable<string> tables, CancellationToken ct = default)
    {
        var repo = new DbSchemaRepository();
        var found = new Dictionary<string, List<DbColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var t in tables.Distinct())
        {
            try
            {
                found[t] = await repo.GetColumnsAsync(conn, EstimateDb.Schema, t, ct, tx);
            }
            catch (InvalidOperationException)
            {
                missing.Add(t);
            }
        }
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"見積DB連携に必要なテーブルがありません: {string.Join("、", missing)}\n" +
                "docs/2026-10-13_OCR見積DB連携 の「DB構造SQL_実行手順.md」で構造の反映を確認してください（接続先のDBが正しいかも確認してください）。");
        return new ColumnGuard(found);
    }

    public bool HasColumn(string table, string column) =>
        _tables.TryGetValue(table, out var cols) && cols.ContainsKey(column);

    public DbColumnInfo Column(string table, string column)
    {
        if (_tables.TryGetValue(table, out var cols) && cols.TryGetValue(column, out var c)) return c;
        throw new InvalidOperationException(
            $"テーブル {table} に列 {column} がありません。DB構造SQL（OCR見積取決め_DB構造作成拡張.sql）の適用状況を確認してください。");
    }

    /// <summary>値を検査してパラメータにする（問題があれば Issues に記録し、値は DBNull にしておく）。</summary>
    public SqlParameter Param(string name, string table, string column, object? value, string where)
    {
        var col = Column(table, column);
        var issue = Check(col, value);
        if (issue is not null)
        {
            Issues.Add($"{where}：{column} {issue}");
            value = null;
        }
        return MakeParam(name, col, value);
    }

    /// <summary>列の定義に収まるか。収まれば null、収まらなければ理由。</summary>
    public static string? Check(DbColumnInfo col, object? value)
    {
        if (value is null or DBNull)
            return col.IsNullable || col.IsIdentity ? null : "は必須ですが値がありません。";

        var type = col.TypeName.ToLowerInvariant();
        switch (value)
        {
            case string s when type is "nvarchar" or "nchar" or "varchar" or "char":
                if (col.MaxLength is int max && max >= 0 && s.Length > max)
                    return $"は最大 {max} 文字ですが {s.Length} 文字あります（先頭: 「{(s.Length > 30 ? s[..30] + "…" : s)}」）。";
                return null;
            case decimal d when type is "decimal" or "numeric":
                return CheckDecimal(d, col.Precision, col.Scale);
            case decimal d when type is "int" or "bigint" or "smallint" or "tinyint":
                return d == decimal.Truncate(d) ? null : $"は整数の列ですが小数 {d} です。";
            case decimal d when type is "money":
                return CheckDecimal(d, 19, 4);
            default:
                return null;
        }
    }

    /// <summary>decimal(p,s) に丸めずに入るか（小数桁・整数桁）。</summary>
    public static string? CheckDecimal(decimal d, int precision, int scale)
    {
        var normalized = d;   // 末尾の 0 を除く（1.50 → 1.5）
        while (normalized.Scale > 0 && normalized == decimal.Round(normalized, normalized.Scale - 1))
            normalized = decimal.Round(normalized, normalized.Scale - 1);
        var digitsAfter = normalized.Scale;
        if (digitsAfter > scale)
            return $"は小数 {scale} 桁までですが {d} です（丸めずに止めました）。";
        var integer = decimal.Truncate(Math.Abs(normalized));
        var digitsBefore = integer == 0 ? 0 : integer.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        if (digitsBefore > precision - scale)
            return $"は整数部 {precision - scale} 桁までですが {d} です。";
        return null;
    }

    public static SqlParameter MakeParam(string name, DbColumnInfo col, object? value)
    {
        var type = col.TypeName.ToLowerInvariant();
        var p = new SqlParameter(name, SqlType(type)) { Value = value ?? DBNull.Value };
        switch (type)
        {
            case "nvarchar" or "nchar" or "varchar" or "char":
                p.Size = col.MaxLength is int max && max >= 0 ? max : -1;
                break;
            case "varbinary":
                p.Size = -1;
                break;
            case "binary":
                p.Size = value is byte[] b ? b.Length : 32;
                break;
            case "decimal" or "numeric":
                p.Precision = (byte)col.Precision;
                p.Scale = (byte)col.Scale;
                break;
        }
        return p;
    }

    private static SqlDbType SqlType(string type) => type switch
    {
        "bit" => SqlDbType.Bit,
        "tinyint" => SqlDbType.TinyInt,
        "smallint" => SqlDbType.SmallInt,
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "float" => SqlDbType.Float,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "char" => SqlDbType.Char,
        "varchar" => SqlDbType.VarChar,
        "nchar" => SqlDbType.NChar,
        "binary" => SqlDbType.Binary,
        "varbinary" => SqlDbType.VarBinary,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        _ => SqlDbType.NVarChar,
    };

    /// <summary>Issues があれば、まとめて例外にする（保存を止める）。</summary>
    public void ThrowIfIssues(string action)
    {
        if (Issues.Count == 0) return;
        var head = Issues.Take(30).ToList();
        var more = Issues.Count > head.Count ? $"\n…ほか {Issues.Count - head.Count} 件" : "";
        throw new InvalidOperationException(
            $"{action}を中止しました。DB の列に収まらない値があります（丸め・切り詰めはしていません）。\n・{string.Join("\n・", head)}{more}");
    }
}
