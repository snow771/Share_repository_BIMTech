using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 暴風の判定：最大瞬間風速が基準値 (30m/s) 以上か。
///
/// 強風 (10分間平均10m/s) とは別の基準。
/// どちらかに当たれば点検が必要という運用のため、別の事象として登録する。
/// </summary>
public class GustJudge : WindJudgeBase
{
    public GustJudge(SqlConnectionFactory factory, AlertRepository alerts)
        : base(factory, alerts) { }

    protected override string EventType => "GUST";
    protected override string SpeedColumn => "MaxGustSpeed";
    protected override string TimeColumn => "MaxGustAt";

    protected override string BuildDetail(decimal value, decimal limit)
        => $"最大瞬間風速 {value:F1}m/s (基準 {limit:F0}m/s)";
}
