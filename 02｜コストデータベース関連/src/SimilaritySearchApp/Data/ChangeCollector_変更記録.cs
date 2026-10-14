using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Data;

/// <summary>
/// 1回の保存（トランザクション）の中で行った変更を集める。最後に ChangeLogRepository.WriteAsync で履歴に書く。
/// 変更前後の値は、DELETE / UPDATE / INSERT の OUTPUT 句で、変更と同時に取得したものを使う。
/// </summary>
public sealed class ChangeCollector
{
    public ChangeCollector(string source) => Source = source;

    /// <summary>DB編集 / Excel取込 / Stencile登録 / 取り消し</summary>
    public string Source { get; set; }

    /// <summary>一覧に出す要約（省略時は件数から自動で作る）</summary>
    public string? Summary { get; set; }

    /// <summary>取り消しの操作のとき、取り消した操作の番号</summary>
    public List<int> RevertsBatchIds { get; } = new();

    public List<ChangeRowRecord> Rows { get; } = new();

    public void Add(string schema, string table, string op, Dictionary<string, string?>? before, Dictionary<string, string?>? after) =>
        Rows.Add(new ChangeRowRecord(schema, table, op, before, after));

    /// <summary>
    /// OUTPUT 句付きの SQL を実行し、返ってきた行を記録する。
    /// DELETE: OUTPUT DELETED.* ／ INSERT: OUTPUT INSERTED.* ／ UPDATE: OUTPUT DELETED.*, INSERTED.*（前半が変更前、後半が変更後）
    /// </summary>
    /// <returns>影響した行数</returns>
    public async Task<int> ExecuteAsync(SqlCommand cmd, string schema, string table, string op, CancellationToken ct)
    {
        var n = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            n++;
            if (op == "UPDATE")
            {
                var half = r.FieldCount / 2;
                Add(schema, table, op, ReadRow(r, 0, half), ReadRow(r, half, half));
            }
            else
            {
                var row = ReadRow(r, 0, r.FieldCount);
                Add(schema, table, op, op == "DELETE" ? row : null, op == "INSERT" ? row : null);
            }
        }
        return n;
    }

    /// <summary>reader の現在行を「列名 → 文字列」に（値は DbValueConverter.Format。戻すときにそのまま Parse できる）。</summary>
    public static Dictionary<string, string?> ReadRow(SqlDataReader r, int start, int count)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = start; i < start + count; i++)
            row[r.GetName(i)] = r.IsDBNull(i) ? null : DbValueConverter.Format(r.GetValue(i));
        return row;
    }
}
