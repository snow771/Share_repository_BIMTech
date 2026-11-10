// このファイルの目的:
//   Products表へのUPSERT(MERGE)文とパラメータを組み立てます。
//   Microsoft.Data.SqlClientへは依存しません（パラメータは
//   IReadOnlyDictionary<string,object>として返すだけです）。
//   実際の接続・実行はBui.Curation側のCurationDbWriterが行います。
//
// 修正時の影響:
//   product_id列は常にofficialIdを使います。valuesの中に仮ID時代の
//   product_idが残っていても、それでは上書きしません。
//
// なぜ列名を許可リストで絞るのか（2026-09-16、レビュー指摘で追加）:
//   valuesのキーは、現場フォルダのProducts.csvの「生のヘッダー行」が
//   そのまま渡ってきます（ProvisionalRecordScanner → GoldStandardCsvReader
//   経由）。固定のC#定数リストではないため、現場のCSVヘッダーが壊れている・
//   不正なものだった場合、そのままSQL列名として文へ連結されてしまいます。
//   値(value)側はパラメータ化されていますが、列名(key)側はSQL識別子として
//   文字列連結する以上、パラメータ化できません。そのためGoldStandardTables.
//   Columnsに実在する既知のProducts表の列名だけを許可リストとして使い、
//   それ以外のキーは無視します。加えて、埋め込む列名は角括弧で囲みます。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>Products表へのUPSERT文を組み立てます。</summary>
public static class CurationSqlCommandBuilder
{
    /// <summary>
    /// Products表として書き込みを許可する列名です（product_id列は別途固定で扱うため含みません）。
    /// ER図（GoldStandard_ER_v1.dbml）のProducts表定義と一致させます。
    /// </summary>
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.Ordinal)
    {
        GoldStandardTables.Columns.ProductName,
        GoldStandardTables.Columns.ManufacturerId,
        GoldStandardTables.Columns.MaterialCategoryId,
        GoldStandardTables.Columns.PrimarySourceId,
        GoldStandardTables.Columns.ProductUrl,
        GoldStandardTables.Columns.StatusId,
        GoldStandardTables.Columns.Note,
    };

    /// <summary>
    /// Product_Specs表として書き込みを許可する列名です（gold_id列は別途固定で扱うため含みません）。
    /// ER図の全列のうち、移行監査用の original_* 等は対象外とし、実データ列だけに絞ります。
    /// </summary>
    private static readonly HashSet<string> AllowedSpecColumns = new(StringComparer.Ordinal)
    {
        GoldStandardTables.Columns.ProductId,
        GoldStandardTables.Columns.SpecKey,
        GoldStandardTables.Columns.ValueType,
        GoldStandardTables.Columns.ValueNumber,
        GoldStandardTables.Columns.ValueString,
        GoldStandardTables.Columns.Unit,
        GoldStandardTables.Columns.SourceId,
        GoldStandardTables.Columns.ReliabilityRankId,
    };

    /// <summary>null・空白のみの値はDBNull.Valueへ変換します。SQL Server側のNULL許容FK列（primary_source_id等）へ
    /// 空文字列を書き込むと制約違反になるため、CSVの空欄はNULLとして扱います。</summary>
    private static object ToSqlValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    /// <summary>
    /// officialIdをキーとしたMERGE文と、対応するパラメータを返します。
    /// valuesのうち、Products表の許可リストに無い列（不正・未知のCSVヘッダー等）は無視します。
    /// </summary>
    public static (string Sql, IReadOnlyDictionary<string, object> Parameters) BuildProductsUpsert(
        string officialId, IReadOnlyDictionary<string, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officialId);
        ArgumentNullException.ThrowIfNull(values);

        var parameters = new Dictionary<string, object> { [$"@{GoldStandardTables.Columns.ProductId}"] = officialId.Trim() };
        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, GoldStandardTables.Columns.ProductId, StringComparison.Ordinal)) continue;
            if (!AllowedColumns.Contains(pair.Key)) continue;
            parameters[$"@{pair.Key}"] = ToSqlValue(pair.Value);
        }

        var columns = parameters.Keys.Select(key => key.TrimStart('@')).ToArray();
        var insertColumns = string.Join(", ", columns.Select(column => $"[{column}]"));
        var insertValues = string.Join(", ", parameters.Keys);
        var updateAssignments = string.Join(", ",
            columns.Where(column => column != GoldStandardTables.Columns.ProductId)
                .Select(column => $"target.[{column}] = source.[{column}]"));

        var sql = $"""
            MERGE dbo.Products AS target
            USING (SELECT {string.Join(", ", parameters.Keys.Select(key => $"{key} AS [{key.TrimStart('@')}]"))}) AS source
            ON target.[{GoldStandardTables.Columns.ProductId}] = source.[{GoldStandardTables.Columns.ProductId}]
            WHEN MATCHED THEN UPDATE SET {updateAssignments}
            WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// specRowsの各行から、gold_idをキーとしたMERGE文と、対応するパラメータを組み立てます。
    /// gold_id・spec_key・value_type はER図上NOT NULLのため、いずれかが無い行は登録できず
    /// スキップし、警告文をWarningsへ積みます（ここで事前にはじかないと、SQL Server側の
    /// NOT NULL制約違反でINSERT/MERGEが失敗し、CurationDbWriterが同一トランザクションに
    /// 含めているProducts本体の登録まで巻き込んでロールバックしてしまいます）。
    /// product_id列は常にofficialIdで上書きします（この時点で製品は既に正式IDへ移行済みのため、
    /// 行に残っている仮ID時代のproduct_idはそのまま使いません）。
    /// </summary>
    public static (IReadOnlyList<(string Sql, IReadOnlyDictionary<string, object> Parameters)> Commands, IReadOnlyList<string> Warnings) BuildProductSpecsUpserts(
        string officialId, IReadOnlyList<IReadOnlyDictionary<string, string>> specRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officialId);
        ArgumentNullException.ThrowIfNull(specRows);

        var trimmedOfficialId = officialId.Trim();
        var commands = new List<(string Sql, IReadOnlyDictionary<string, object> Parameters)>();
        var warnings = new List<string>();

        foreach (var row in specRows)
        {
            var goldId = row.TryGetValue(GoldStandardTables.Columns.GoldId, out var goldIdValue) ? goldIdValue.Trim() : string.Empty;
            var specKey = row.TryGetValue(GoldStandardTables.Columns.SpecKey, out var specKeyValue) ? specKeyValue.Trim() : string.Empty;
            var valueType = row.TryGetValue(GoldStandardTables.Columns.ValueType, out var valueTypeValue) ? valueTypeValue.Trim() : string.Empty;

            if (goldId.Length == 0)
            {
                warnings.Add($"Product_Specsの1行にgold_idが無いため登録をスキップしました（spec_key: {specKey}）");
                continue;
            }
            if (specKey.Length == 0)
            {
                warnings.Add($"Product_Specsの1行にspec_keyが無いため登録をスキップしました（gold_id: {goldId}）");
                continue;
            }
            if (valueType.Length == 0)
            {
                warnings.Add($"Product_Specsの1行にvalue_typeが無いため登録をスキップしました（gold_id: {goldId}, spec_key: {specKey}）");
                continue;
            }

            var parameters = new Dictionary<string, object>
            {
                [$"@{GoldStandardTables.Columns.GoldId}"] = goldId,
                [$"@{GoldStandardTables.Columns.ProductId}"] = trimmedOfficialId,
            };
            foreach (var pair in row)
            {
                if (string.Equals(pair.Key, GoldStandardTables.Columns.GoldId, StringComparison.Ordinal)) continue;
                if (string.Equals(pair.Key, GoldStandardTables.Columns.ProductId, StringComparison.Ordinal)) continue;
                if (!AllowedSpecColumns.Contains(pair.Key)) continue;
                parameters[$"@{pair.Key}"] = ToSqlValue(pair.Value);
            }

            var columns = parameters.Keys.Select(key => key.TrimStart('@')).ToArray();
            var insertColumns = string.Join(", ", columns.Select(column => $"[{column}]"));
            var insertValues = string.Join(", ", parameters.Keys);
            var updateAssignments = string.Join(", ",
                columns.Where(column => column != GoldStandardTables.Columns.GoldId)
                    .Select(column => $"target.[{column}] = source.[{column}]"));

            var sql = $"""
                MERGE dbo.Product_Specs AS target
                USING (SELECT {string.Join(", ", parameters.Keys.Select(key => $"{key} AS [{key.TrimStart('@')}]"))}) AS source
                ON target.[{GoldStandardTables.Columns.GoldId}] = source.[{GoldStandardTables.Columns.GoldId}]
                WHEN MATCHED THEN UPDATE SET {updateAssignments}
                WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});
                """;

            commands.Add((sql, parameters));
        }

        return (commands, warnings);
    }

    /// <summary>
    /// メーカー・建材分類マスタへの登録文です（2026-09-18、実運用で発見した不具合を修正）。
    /// 現場フォルダのローカルマスタ（Manufacturers.csv等）へ「新しく登録する」で追加された名前は、
    /// そのフォルダの中だけに存在し、SQL Server側のマスタ表にはまだ無いことがあります。
    /// Products側はそのIDを外部キーとして参照するため、先にこちらを実行しないと
    /// FOREIGN KEY制約違反になります（実際にこの順序違反でエラーになったことを確認済みです）。
    /// 既にSQL Server側にマスタ行が実在する場合は、名称を上書きしません
    /// （WHEN NOT MATCHEDのみ。管理者がSQL Server側で名称を修正済みかもしれないため）。
    /// table・idColumn・nameColumnは呼び出し側の固定値のみを渡してください（許可リストが無いため）。
    /// </summary>
    public static (string Sql, IReadOnlyDictionary<string, object> Parameters) BuildMasterUpsert(
        string table, string idColumn, string id, string nameColumn, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(idColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var parameters = new Dictionary<string, object>
        {
            ["@id"] = id.Trim(),
            ["@name"] = name.Trim(),
        };

        var sql = $"""
            MERGE dbo.[{table}] AS target
            USING (SELECT @id AS [{idColumn}], @name AS [{nameColumn}]) AS source
            ON target.[{idColumn}] = source.[{idColumn}]
            WHEN NOT MATCHED THEN INSERT ([{idColumn}], [{nameColumn}]) VALUES (source.[{idColumn}], source.[{nameColumn}]);
            """;

        return (sql, parameters);
    }
}
