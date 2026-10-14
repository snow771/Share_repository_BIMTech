using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;
using static SimilaritySearchApp.Data.SqlConnectionFactory;

namespace SimilaritySearchApp.Data;

/// <summary>査定単価・修正単価の取得（DISTINCT で重複除去。Python 版と同じ）。</summary>
public sealed class CostRepository
{
    /// <summary>査定単価テーブルの金額列</summary>
    public const string AssessPriceColumn = "Appraisal_Unit_Price";

    /// <summary>修正単価テーブルの金額列</summary>
    public const string RevisePriceColumn = "Revised_Unit_Price";

    public async Task<List<CostRecord>> LoadAsync(
        SqlConnection conn, string table, string priceColumn, StructureType structure,
        int commandTimeoutSec, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT DISTINCT
                a.Project_id          AS project_id,
                a.Work_Type_Code      AS 工種コード,
                a.Work_Type_Item      AS 工種項目,
                a.Construction_Item   AS 工事項目,
                a.Project             AS 項目,
                a.{Quote(priceColumn)} AS amount_yen
            FROM {Quote(table)} a
            WHERE LEFT(CAST(a.Project_id AS NVARCHAR(50)), 1) = @prefix
            """;

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = commandTimeoutSec };
        cmd.Parameters.AddWithValue("@prefix", structure.ProjectIdPrefix());

        var list = new List<CostRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var amount = ToDouble(Value(r, 5)) ?? 0;   // 欠損は合計に寄与しない（pandas の sum と同じ）
            list.Add(new CostRecord
            {
                ProjectId = TextNormalizer.Clean(Value(r, 0)),
                WorkTypeCode = TextNormalizer.Clean(Value(r, 1)),
                Trade = TextNormalizer.Clean(Value(r, 2)),
                Work = TextNormalizer.Clean(Value(r, 3)),
                Item = TextNormalizer.Clean(Value(r, 4)),
                Amount = amount,
                AmountAdj = amount,
            });
        }
        return list;
    }
}
