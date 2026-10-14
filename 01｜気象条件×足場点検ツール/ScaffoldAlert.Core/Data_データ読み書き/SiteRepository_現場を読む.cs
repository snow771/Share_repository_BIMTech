using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 現場(M_Site)を読み出す係。
///
/// ★このファイルの決まり
///   ・SQL文はこの Data フォルダの中だけに書く。画面には一切書かない。
///   ・移設先でテーブル名や項目名が変わる可能性があるため、
///     直す場所をここに閉じ込めておく。
///
/// ★使っているテーブルと項目 (移設時はここを見る)
///   dbo.M_Site
///     IsDeleted    削除フラグ (0 = 生きている)
///     SiteStatus   状態 (ACTIVE = 稼働中 / SUSPENDED = 休止中 / CLOSED = 竣工)
///     HasScaffold  足場があるか (1 = ある)
///     Department   部署
/// </summary>
public class SiteRepository
{
    private readonly SqlConnectionFactory _factory;

    public SiteRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 現場の件数をまとめて数える。
    /// 1回の問い合わせで3つ数えているのは、往復を減らすため。
    /// </summary>
    public SiteCount CountSites()
    {
        const string sql = @"
            SELECT
                COUNT(*) AS 全件,
                SUM(CASE WHEN SiteStatus = N'ACTIVE'    THEN 1 ELSE 0 END) AS 稼働中,
                SUM(CASE WHEN SiteStatus = N'SUSPENDED' THEN 1 ELSE 0 END) AS 休止中
            FROM dbo.M_Site
            WHERE IsDeleted = 0;";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return new SiteCount(0, 0, 0);
        }

        // GetInt32 は列の番号で読む。0 = 全件、1 = 稼働中、2 = 休止中。
        // IsDBNull は「値が無い」場合の確認。1件も無いと SUM は空になる。
        int total = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        int active = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        int suspended = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

        return new SiteCount(total, active, suspended);
    }

    /// <summary>
    /// 部署ごとの現場数を返す。接続できていることを目で確かめるために出す。
    /// </summary>
    public List<DepartmentCount> CountByDepartment()
    {
        const string sql = @"
            SELECT ISNULL(Department, N'未設定') AS 部署, COUNT(*) AS 件数
            FROM dbo.M_Site
            WHERE IsDeleted = 0
            GROUP BY ISNULL(Department, N'未設定')
            ORDER BY COUNT(*) DESC;";

        var list = new List<DepartmentCount>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new DepartmentCount(reader.GetString(0), reader.GetInt32(1)));
        }

        return list;
    }
}

/// <summary>現場の件数。record は「値を持つだけの型」を短く書く書き方。</summary>
public record SiteCount(int Total, int Active, int Suspended);

/// <summary>部署ごとの件数。</summary>
public record DepartmentCount(string Department, int Count);
