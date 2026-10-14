using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 地震 (T_Earthquake) を読む係。
///
/// ★使っているテーブルと項目
///   dbo.T_Earthquake
///     JmaEventId    気象庁が付けている地震のID
///     OccurredAt    発生日時
///     Hypocenter    震源地
///     Magnitude     マグニチュード
///     MaxIntensity  全国の最大震度
/// </summary>
public class EarthquakeRepository
{
    private readonly SqlConnectionFactory _factory;

    public EarthquakeRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 既に取り込んである地震のIDを返す。
    ///
    /// 毎回すべてを入れ直すと無駄が大きいため、
    /// 新しい地震と、続報で震度が変わりうる直近の地震だけを対象にする。
    /// その判断にこの一覧を使う。
    /// </summary>
    public HashSet<string> LoadKnownEventIds()
    {
        const string sql = "SELECT JmaEventId FROM dbo.T_Earthquake;";

        var set = new HashSet<string>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    /// <summary>
    /// 直近の地震を返す。バッチが動いているかを目で確かめるために使う。
    /// </summary>
    public List<StoredQuake> LoadRecent(int count)
    {
        string sql = $@"
            SELECT TOP {count} JmaEventId, OccurredAt, Hypocenter,
                   Magnitude, MaxIntensity
            FROM dbo.T_Earthquake
            ORDER BY OccurredAt DESC;";

        var list = new List<StoredQuake>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new StoredQuake(
                EventId: reader.GetString(0),
                OccurredAt: reader.GetDateTime(1),
                Hypocenter: reader.IsDBNull(2) ? null : reader.GetString(2),
                Magnitude: reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                MaxIntensity: reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return list;
    }

    /// <summary>
    /// 地震1件を保存し、市区町村ごとの震度を入れ直す。
    ///
    /// 同じ地震は続報で何度も配信されるため、気象庁のIDで上書きする。
    /// 震度も続報で更新されるので、一度消してから入れ直す。
    /// 足し込む形にすると、取り下げられた地域が残ってしまう。
    /// </summary>
    /// <returns>保存した地域別震度の件数。保存できなければ -1。</returns>
    public int SaveEarthquake(QuakeEntry entry,
                              Dictionary<string, string> areaNames)
    {
        if (entry.OccurredAt is null)
        {
            // 発生日時が分からないものは保存しない。
            // 判定は発生日時を基準にしているため、
            // 推測で入れると判定の結果まで狂う。
            return -1;
        }

        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            const string mergeSql = @"
                MERGE dbo.T_Earthquake AS tgt
                USING (SELECT @eid AS JmaEventId) AS src
                ON tgt.JmaEventId = src.JmaEventId
                WHEN MATCHED THEN UPDATE SET
                    OccurredAt = @at, Hypocenter = @place,
                    Magnitude = @mag, Depth = @depth,
                    MaxIntensity = @maxi
                WHEN NOT MATCHED THEN INSERT
                    (JmaEventId, OccurredAt, Hypocenter,
                     Magnitude, Depth, MaxIntensity)
                    VALUES (@eid, @at, @place, @mag, @depth, @maxi);";

            using (var merge = new SqlCommand(mergeSql, connection, tran))
            {
                merge.Parameters.AddWithValue("@eid", entry.EventId);
                merge.Parameters.AddWithValue("@at", entry.OccurredAt.Value);
                merge.Parameters.AddWithValue("@place",
                    (object?)entry.Hypocenter ?? DBNull.Value);
                merge.Parameters.AddWithValue("@mag",
                    (object?)entry.Magnitude ?? DBNull.Value);
                merge.Parameters.AddWithValue("@depth",
                    (object?)entry.DepthKm ?? DBNull.Value);
                merge.Parameters.AddWithValue("@maxi",
                    (object?)entry.MaxIntensity ?? DBNull.Value);
                merge.ExecuteNonQuery();
            }

            int earthquakeId;

            using (var find = new SqlCommand(
                "SELECT EarthquakeId FROM dbo.T_Earthquake WHERE JmaEventId = @eid;",
                connection, tran))
            {
                find.Parameters.AddWithValue("@eid", entry.EventId);
                var found = find.ExecuteScalar();
                if (found is null || found == DBNull.Value)
                {
                    tran.Rollback();
                    return -1;
                }
                earthquakeId = Convert.ToInt32(found);
            }

            using (var clear = new SqlCommand(
                "DELETE FROM dbo.T_EarthquakeIntensity WHERE EarthquakeId = @id;",
                connection, tran))
            {
                clear.Parameters.AddWithValue("@id", earthquakeId);
                clear.ExecuteNonQuery();
            }

            const string insertSql = @"
                INSERT INTO dbo.T_EarthquakeIntensity
                    (EarthquakeId, AreaCode, AreaName, Intensity)
                VALUES (@id, @code, @name, @level);";

            int saved = 0;

            foreach (var pair in entry.CityIntensities)
            {
                using var insert = new SqlCommand(insertSql, connection, tran);
                insert.Parameters.AddWithValue("@id", earthquakeId);
                insert.Parameters.AddWithValue("@code", pair.Key);
                insert.Parameters.AddWithValue("@name",
                    areaNames.TryGetValue(pair.Key, out var name)
                        ? name : (object)DBNull.Value);
                insert.Parameters.AddWithValue("@level", pair.Value);
                insert.ExecuteNonQuery();
                saved++;
            }

            tran.Commit();
            return saved;
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }
}

/// <summary>データベースに入っている地震1件。</summary>
public record StoredQuake(
    string EventId,
    DateTime OccurredAt,
    string? Hypocenter,
    decimal? Magnitude,
    string? MaxIntensity);
