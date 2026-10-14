using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Data;

/// <summary>観測値の保存の結果。</summary>
public class SaveObservationResult
{
    /// <summary>保存できた件数。</summary>
    public int Saved { get; set; }

    /// <summary>観測時刻が決められず保存しなかった観測所の数。</summary>
    public int SkippedNoTime { get; set; }

    /// <summary>うまくいかなかったときの理由。</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 気象の観測値を保存する係。
///
/// 同じ観測所・同じ観測時刻のデータが既にあれば上書きし、
/// なければ追加する。何度実行しても重複せず、
/// 気象庁が後日値を修正した場合も追従できる。
/// </summary>
public class ObservationRepository
{
    private readonly SqlConnectionFactory _factory;

    public ObservationRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    // ------------------------------------------------------------
    // このバッチが書き込む列。取得元のCSV1本につき1つの組に分けてある。
    //
    // 組は取得元と1対1にすること。取得は別々のHTTPなので、
    // 片側だけ失敗するのは普通に起こる。例えば最大風速と最大瞬間風速を
    // 1つの組にまとめると、瞬間風速だけ取れた回に MaxWindSpeed が
    // NULL で上書きされ、保存済みの風速が消えて 10m/s の判定が空振りする。
    // ------------------------------------------------------------
    private static readonly string[] PrecipColumns =
        { "Precip1h", "Precip12h", "Precip24h", "QualityFlag" };

    private static readonly string[] WindColumns =
        { "MaxWindSpeed", "MaxWindAt" };

    private static readonly string[] GustColumns =
        { "MaxGustSpeed", "MaxGustAt" };

    private static readonly string[] SnowColumns =
        { "Snow24h", "SnowDepth" };

    private static readonly string[] AllColumns =
        PrecipColumns.Concat(WindColumns)
                     .Concat(GustColumns)
                     .Concat(SnowColumns)
                     .ToArray();

    /// <summary>
    /// 取得した観測値を保存する。
    ///
    /// 「その回に取れなかった値」で既存の記録を消してはいけないため、
    /// 観測所ごとに、取れた組の列だけを更新する。
    ///   ・全部取れた       → 全10列を更新
    ///   ・取れた組がある    → その組の列だけ更新 (残りは既存値を残す)
    ///   ・1つも取れていない → 保存しない (全列 NULL の行を作らない)
    ///
    /// 取れなかった組は「保存しない」ではなく「更新対象から外す」を選んだ。
    /// 降水量は取れているのに風速が無いというだけで行ごと捨てると、
    /// 大雨判定に使う12時間積算雨量まで保存されなくなり、
    /// 見逃しにつながるため。取れた組が1つでもあれば、その値は必ず残す。
    /// </summary>
    /// <param name="fallbackAt">
    /// 降水量が取れなかった観測所に使う観測時刻 (風速CSVの現在時刻)。
    /// </param>
    public SaveObservationResult Save(WeatherFetchResult weather,
                                      DateTime? fallbackAt)
    {
        var result = new SaveObservationResult();

        // 保存対象は「降水量が取れた観測所」ではなく、
        // 降水量・風速・最大瞬間風速・雪のいずれかが取れた観測所すべて。
        var targets = weather.Precip.Keys
            .Union(weather.Wind.Keys)
            .Union(weather.Gust.Keys)
            .Union(weather.Snow.Keys)
            .OrderBy(x => x)
            .ToList();

        // 取れた組の組み合わせは最大15通り。同じ組み合わせの文は使い回す。
        var sqlCache = new Dictionary<string, string>();

        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            foreach (var id in targets)
            {
                weather.Precip.TryGetValue(id, out var p);
                weather.Wind.TryGetValue(id, out var w);
                weather.Gust.TryGetValue(id, out var g);
                weather.Snow.TryGetValue(id, out var s);

                // 降水量CSVがその観測所の行を返していれば
                // 「降水量が取れた」と扱う。値が空でも、それは
                // 気象庁が欠測と示したという情報なのでそのまま記録する
                // (0 で埋めない。「雨が降らなかった」という
                //  誤った記録になり、証跡として不正確になるため)。
                bool hasPrecip = p is not null;

                // 風速側は値そのものが無ければ「取れていない」と扱う。
                // 風速の値は「その日の最大値」で毎回同じ行に上書きされて
                // いくため、値の無い回で既存の最大値を消すと復元できない。
                bool hasWind = w?.Speed is not null;
                bool hasGust = g?.Speed is not null;

                // 雪も同じ考え方。雪の降らない地域・季節は値が入らない。
                bool hasSnow = s?.Snow24h is not null || s?.SnowDepth is not null;

                if (!hasPrecip && !hasWind && !hasGust && !hasSnow)
                {
                    // 全列 NULL の行になるだけで、意味のある観測値が無い。
                    // 毎時これを入れると空行が積み上がり、
                    // 画面の一覧にも全欄空白の行が並ぶ。
                    continue;
                }

                // 観測時刻は風速CSVに書かれている「現在時刻」を使う。
                // 起時(実際に吹いた時刻)から日付を作ると、その日の
                // 最大値が出た時刻を観測レコードの時刻にしてしまい、
                // 実態と食い違う。
                DateTime? observedAt = hasPrecip ? p!.ObservedAt : fallbackAt;

                if (observedAt is null)
                {
                    // 観測時刻が決められないものは保存しない。
                    // 時刻を推測で作らない。
                    result.SkippedNoTime++;
                    continue;
                }

                var updateColumns = new List<string>();
                if (hasPrecip) { updateColumns.AddRange(PrecipColumns); }
                if (hasWind) { updateColumns.AddRange(WindColumns); }
                if (hasGust) { updateColumns.AddRange(GustColumns); }
                if (hasSnow) { updateColumns.AddRange(SnowColumns); }

                string key = $"{hasPrecip}{hasWind}{hasGust}{hasSnow}";
                if (!sqlCache.TryGetValue(key, out var sql))
                {
                    sql = BuildMergeSql(updateColumns);
                    sqlCache[key] = sql;
                }

                using var command = new SqlCommand(sql, connection, tran);

                command.Parameters.AddWithValue("@stationId", id);
                command.Parameters.AddWithValue("@observedAt", observedAt.Value);

                Add(command, "@Precip1h", hasPrecip ? p!.Precip1h : null);
                Add(command, "@Precip12h", hasPrecip ? p!.Precip12h : null);
                Add(command, "@Precip24h", hasPrecip ? p!.Precip24h : null);
                Add(command, "@QualityFlag", hasPrecip ? p!.QualityFlag : null);
                Add(command, "@MaxWindSpeed", w?.Speed);
                Add(command, "@MaxWindAt", w?.At);
                Add(command, "@MaxGustSpeed", g?.Speed);
                Add(command, "@MaxGustAt", g?.At);
                Add(command, "@Snow24h", s?.Snow24h);
                Add(command, "@SnowDepth", s?.SnowDepth);

                command.ExecuteNonQuery();
                result.Saved++;
            }

            tran.Commit();
        }
        catch (Exception ex)
        {
            tran.Rollback();
            result.Saved = 0;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// MERGE 文を組み立てる。
    ///
    /// updateColumns は「既存行が見つかったときに上書きする列」。
    /// ここに入れなかった列は既存の値がそのまま残る。
    ///
    /// 取れなかった値を空で上書きしないために、この作り分けが要る。
    /// 例えば降水量だけが失敗した回に降水量の列まで更新してしまうと、
    /// 先に保存してあった12時間積算雨量が消える。
    /// 臨検で示す証跡が無言で失われ、大雨の見逃しにも直結する。
    ///
    /// COALESCE で済ませる手もあるが採らない。気象庁が事後に値を
    /// 「欠測」へ訂正した場合、その訂正を取り込めなくなり、
    /// 記録が実態とずれるため。
    /// 「取れなかった」と「欠測と訂正された」は違う。
    ///
    /// 行が無いときは上書きする既存値が無いので、常に全列を入れる。
    /// 取れなかった列は空で入る。
    ///
    /// 列名はこのファイル内の決まった文字だけから組み立てており、
    /// 外から来た文字は混ざらない。
    /// </summary>
    public static string BuildMergeSql(List<string> updateColumns)
    {
        string setClause = string.Join(",\n            ",
            updateColumns.Select(c => $"{c} = @{c}"));

        string insertColumns = string.Join(", ", AllColumns);
        string insertValues = string.Join(", ", AllColumns.Select(c => "@" + c));

        return $@"
            MERGE dbo.T_Observation AS tgt
            USING (SELECT @stationId AS StationId,
                          @observedAt AS ObservedAt) AS src
            ON tgt.StationId = src.StationId
               AND tgt.ObservedAt = src.ObservedAt
            WHEN MATCHED THEN UPDATE SET
            {setClause},
            FetchedAt = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (StationId, ObservedAt, {insertColumns})
                VALUES (@stationId, @observedAt, {insertValues});";
    }

    /// <summary>値が空のときは、空のまま渡す。</summary>
    private static void Add(SqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
