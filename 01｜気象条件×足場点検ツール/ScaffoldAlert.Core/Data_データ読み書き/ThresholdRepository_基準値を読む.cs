using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Judgement;

namespace ScaffoldAlert.Core.Data;

/// <summary>
/// 判定のしきい値 (M_Threshold) を読む係。
///
/// ★使っているテーブルと項目 (移設時はここを見る)
///   dbo.M_Threshold
///     EventType       事象の種類 (RAIN / WIND / GUST / SNOW / EARTHQUAKE)
///     ThresholdName   名前
///     ThresholdValue  数値のしきい値 (50 / 10 / 30 / 25)
///     ThresholdText   文字のしきい値 (震度の "4")
///     WindowHours     積算の時間
///     LegalBasis      法令の根拠
///     IsEnabled       使うかどうか (1 = 使う)
///
/// 数字をプログラムに書かずデータベースから読むのは、
/// 基準を変えるときにデータベースだけで済ませるため。
/// </summary>
public class ThresholdRepository
{
    private readonly SqlConnectionFactory _factory;

    public ThresholdRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 使うことになっているしきい値を、事象の種類をキーにして返す。
    /// IsEnabled = 0 のものは読まない。運用を止めたい基準は
    /// データベース側でフラグを落とす。
    /// </summary>
    public Dictionary<string, Threshold> LoadEnabled()
    {
        const string sql = @"
            SELECT EventType, ThresholdName, ThresholdValue, ThresholdText,
                   WindowHours, LegalBasis
            FROM dbo.M_Threshold
            WHERE IsEnabled = 1;";

        var result = new Dictionary<string, Threshold>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var eventType = reader.GetString(0);
            result[eventType] = new Threshold(
                EventType: eventType,
                Name: reader.GetString(1),
                Value: reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                Text: reader.IsDBNull(3) ? null : reader.GetString(3),
                WindowHours: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                LegalBasis: reader.IsDBNull(5) ? null : reader.GetString(5));
        }

        return result;
    }
}
