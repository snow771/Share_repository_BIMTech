using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>
/// AI アシスタント用の読み取り専用の検索（SELECT のみ）。
/// テーブル・列は呼び出し側で実在を確認済みのものだけを受け取り、キーワードはすべてパラメータで渡す。
///   WHERE ( 列1 LIKE %語1% OR 列2 LIKE %語1% … ) AND ( …語2… )   ← matchAll=false なら語ごとの条件を OR でつなぐ
/// 語ごとに全角カナ・半角カナなどの表記ゆれも探す。
/// </summary>
public sealed class TableLookupRepository
{
    public async Task<(List<List<string?>> Rows, int Total)> SearchAsync(
        SqlConnection conn, string schema, string table, List<DbColumnInfo> show, List<DbColumnInfo> search,
        List<string> keywords, bool matchAll, int limit, int timeoutSec, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand { Connection = conn, CommandTimeout = timeoutSec };
        var where = BuildWhere(cmd, search, keywords, matchAll);
        var from = $"{TableEditRepository.Q(schema)}.{TableEditRepository.Q(table)}";

        cmd.CommandText = $"SELECT COUNT(*) FROM {from}{where}";
        var total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));

        cmd.CommandText = $"SELECT TOP ({limit}) {string.Join(", ", show.Select(c => TableEditRepository.Q(c.Name)))} FROM {from}{where}";
        var rows = new List<List<string?>>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var row = new List<string?>(r.FieldCount);
            for (var i = 0; i < r.FieldCount; i++) row.Add(r.IsDBNull(i) ? null : DbValueConverter.Format(r.GetValue(i)));
            rows.Add(row);
        }
        return (rows, total);
    }

    private static string BuildWhere(SqlCommand cmd, List<DbColumnInfo> search, List<string> keywords, bool matchAll)
    {
        if (keywords.Count == 0 || search.Count == 0) return "";
        var groups = new List<string>();
        foreach (var keyword in keywords)
        {
            var parts = new List<string>();
            foreach (var variant in DbLookupService.KeywordVariants(keyword))
            {
                var p = $"@k{cmd.Parameters.Count}";
                cmd.Parameters.Add(new SqlParameter(p, System.Data.SqlDbType.NVarChar, 4000) { Value = $"%{EscapeLike(variant)}%" });
                parts.AddRange(search.Select(c => $"{TableEditRepository.Q(c.Name)} LIKE {p}"));
            }
            groups.Add("(" + string.Join(" OR ", parts) + ")");
        }
        return " WHERE " + string.Join(matchAll ? " AND " : " OR ", groups);
    }

    /// <summary>LIKE の特殊文字（% _ [）を文字として扱う。</summary>
    private static string EscapeLike(string s) => s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
