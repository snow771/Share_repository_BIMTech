using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;
using static SimilaritySearchApp.Data.SqlConnectionFactory;

namespace SimilaritySearchApp.Data;

/// <summary>物価指数補正に使うマスタ一式。</summary>
public sealed record MasterSet(
    List<CategoryRow> Categories,
    List<IndexAllocationRow> IndexAllocations,
    List<DeflatorRow> Deflators);

/// <summary>カテゴリ・指数割当・補正係数・期間マスタの取得（Python の load_masters_from_db）。</summary>
public sealed class MasterRepository
{
    public async Task<MasterSet> LoadDeflatorMastersAsync(
        SqlConnection conn, TableNames t, int commandTimeoutSec, CancellationToken ct = default)
    {
        var cat = await QueryAsync(conn, commandTimeoutSec, ct,
            $"SELECT Work_Type_Code, Category_Code FROM {Quote(t.Category)}",
            r => new CategoryRow(TextNormalizer.Clean(Value(r, 0)), ToInt(Value(r, 1))));

        var ia = await QueryAsync(conn, commandTimeoutSec, ct,
            $"SELECT Category_Code, Index_Allocation FROM {Quote(t.IndexAllocation)}",
            r => new IndexAllocationRow(ToInt(Value(r, 0)), TextNormalizer.Clean(Value(r, 1))));

        var de = await QueryAsync(conn, commandTimeoutSec, ct,
            $"SELECT d.term, d.Index_Allocation, d.deflator FROM {Quote(t.Deflator)} d",
            r => new DeflatorRow(TextNormalizer.Clean(Value(r, 0)), TextNormalizer.Clean(Value(r, 1)), ToDouble(Value(r, 2))));

        return new MasterSet(cat, ia, de);
    }

    public Task<List<TermRow>> LoadTermsAsync(
        SqlConnection conn, TableNames t, int commandTimeoutSec, CancellationToken ct = default) =>
        QueryAsync(conn, commandTimeoutSec, ct,
            $"SELECT term, start_date, end_date FROM {Quote(t.Term)}",
            r => new TermRow(TextNormalizer.Clean(Value(r, 0)), ToDate(Value(r, 1)), ToDate(Value(r, 2))));

    private static async Task<List<T>> QueryAsync<T>(
        SqlConnection conn, int timeout, CancellationToken ct, string sql, Func<SqlDataReader, T> map)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeout };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await r.ReadAsync(ct)) list.Add(map(r));
        return list;
    }
}
