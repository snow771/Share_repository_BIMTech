using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Services;
using static SimilaritySearchApp.Data.SqlConnectionFactory;

namespace SimilaritySearchApp.Data;

/// <summary>
/// 建築材料マスタ（MaterialMaster_建築材料マスタ）の取得。
/// ER 図の列名は論理名（例: Material_Code_建材コード）のため、実テーブルの列名が
/// 「Material_Code」「Material_Code_建材コード」「建材コード」のいずれでも読めるよう、列名を前方一致で解決する。
/// </summary>
public sealed class MaterialRepository
{
    public async Task<List<Models.MaterialRecord>> LoadAsync(
        SqlConnection conn, string table, int commandTimeoutSec, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand($"SELECT * FROM {Quote(table)}", conn) { CommandTimeout = commandTimeoutSec };
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var cols = new ColumnMap(r);
        var id = cols.Find("MaterialMaster_ID", "ID");
        var code = cols.Find("Work_Type_Code", "工種コード");
        var material = cols.Find("Material_Code", "建材コード");
        var name = cols.Find("Material_Name", "名称");
        var spec = cols.Find("Specification_Dimensions", "Specification", "規格寸法", "規格・寸法");
        var notes = cols.Find("Notes", "備考メモ", "備考(メモ)", "備考");
        var unit = cols.Find("Unit", "単位");
        var assess = cols.Find("Appraisal_Unit_Price", "査定単価");
        var revise = cols.Find("Revised_Unit_Price", "修正単価");
        var sheet = cols.Find("Source_Sheet", "元シート");
        var row = cols.Find("Source_Row", "元行番号");
        var status = cols.Find("ReviewStatus", "確認状態");

        if (code < 0 || name < 0)
            throw new InvalidOperationException($"{table} に Work_Type_Code / Material_Name 列が見つかりません。列: {string.Join(", ", cols.Names)}");

        var list = new List<Models.MaterialRecord>();
        while (await r.ReadAsync(ct))
        {
            list.Add(new Models.MaterialRecord
            {
                Id = id >= 0 ? ToInt(Value(r, id)) : null,
                WorkTypeCode = TextNormalizer.Clean(Value(r, code)),
                MaterialCode = material >= 0 ? TextNormalizer.Clean(Value(r, material)) : "",
                Name = TextNormalizer.Clean(Value(r, name)),
                Specification = Text(r, spec),
                Notes = Text(r, notes),
                Unit = Text(r, unit),
                AssessPrice = assess >= 0 ? ToDouble(Value(r, assess)) : null,   // 0 と null は区別する
                RevisePrice = revise >= 0 ? ToDouble(Value(r, revise)) : null,
                SourceSheet = Text(r, sheet),
                SourceRow = row >= 0 ? ToInt(Value(r, row)) : null,
                ReviewStatus = Text(r, status),
            });
        }

        // 主キー順（Excel の並びに近い）で返す
        return list.OrderBy(m => m.Id ?? int.MaxValue).ToList();
    }

    private static string? Text(SqlDataReader r, int i) =>
        i < 0 ? null : TextNormalizer.CleanOrNull(Value(r, i));

    /// <summary>列名の解決（大文字小文字・前方一致「名前_」を許容）。</summary>
    private sealed class ColumnMap
    {
        public List<string> Names { get; }

        public ColumnMap(SqlDataReader r) =>
            Names = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();

        /// <summary>候補名のいずれかに一致する列の位置。完全一致を優先し、次に「候補名_」で始まる列。無ければ -1。</summary>
        public int Find(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var i = Names.FindIndex(n => string.Equals(n, c, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            foreach (var c in candidates)
            {
                var i = Names.FindIndex(n => n.StartsWith(c + "_", StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            return -1;
        }
    }
}
